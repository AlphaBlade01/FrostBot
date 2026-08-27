using FrostBot.Data;
using FrostBot.Data.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;

namespace FrostBot.Logic.Services;

public class ChannelConfigService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    private readonly IDbContextFactory<BotDbContext> _dbContextFactory;
    private readonly ConcurrentDictionary<ulong, CacheEntry> _cache = new();
    private readonly ConcurrentDictionary<ulong, Lazy<Task<ChannelConfig>>> _loads = new();

    public ChannelConfigService(IDbContextFactory<BotDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<ChannelConfig> GetChannelConfigAsync(ulong channelId, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(channelId, out CacheEntry? cached) &&
            cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return cached.Config;
        }

        Lazy<Task<ChannelConfig>> load = _loads.GetOrAdd(
            channelId,
            id => new Lazy<Task<ChannelConfig>>(
                () => LoadChannelConfigAsync(id),
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return await load.Value.WaitAsync(cancellationToken);
        }
        finally
        {
            _loads.TryRemove(new KeyValuePair<ulong, Lazy<Task<ChannelConfig>>>(channelId, load));
        }
    }

    public async Task<ChannelConfig> UpdateChannelConfigAsync(
        ulong channelId,
        ulong guildId,
        bool? isXpDisabled = null,
        bool? isMessageLoggingDisabled = null,
        CancellationToken cancellationToken = default)
    {
        await using BotDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        ChannelConfig? config = await dbContext.ChannelConfigs
            .SingleOrDefaultAsync(c => c.ChannelId == channelId, cancellationToken);

        if (config is null)
        {
            config = new ChannelConfig
            {
                ChannelId = channelId,
                GuildId = guildId
            };
            await dbContext.ChannelConfigs.AddAsync(config, cancellationToken);
        }

        if (isXpDisabled.HasValue)
            config.IsXpDisabled = isXpDisabled.Value;

        if (isMessageLoggingDisabled.HasValue)
            config.IsMessageLoggingDisabled = isMessageLoggingDisabled.Value;

        await dbContext.SaveChangesAsync(cancellationToken);
        SetCache(config);
        return config;
    }

    public void Invalidate(ulong channelId)
    {
        _cache.TryRemove(channelId, out _);
    }

    private async Task<ChannelConfig> LoadChannelConfigAsync(ulong channelId)
    {
        await using BotDbContext dbContext = await _dbContextFactory.CreateDbContextAsync();
        ChannelConfig? config = await dbContext.ChannelConfigs
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.ChannelId == channelId);

        config ??= new ChannelConfig { ChannelId = channelId };
        SetCache(config);
        return config;
    }

    private void SetCache(ChannelConfig config)
    {
        _cache[config.ChannelId] = new CacheEntry(config, DateTimeOffset.UtcNow.Add(CacheTtl));
    }

    private sealed record CacheEntry(ChannelConfig Config, DateTimeOffset ExpiresAt);
}
