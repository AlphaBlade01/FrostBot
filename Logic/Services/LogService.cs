using FrostBot.Data;
using FrostBot.Logic.Library;
using Microsoft.EntityFrameworkCore;
using NetCord.Gateway;
using NetCord.Rest;

namespace FrostBot.Logic.Services;

public class LogService
{
    private readonly IDbContextFactory<BotDbContext> _dbContextFactory;
    private readonly GatewayClient _client;

    public LogService(IDbContextFactory<BotDbContext> dbContextFactory, GatewayClient client)
    {
        _dbContextFactory = dbContextFactory;
        _client = client;
    }

    public async Task SendLogEmbed(ulong guildId, EmbedProperties embed, string? content = null)
    {
        BotDbContext dbContext = await _dbContextFactory.CreateDbContextAsync();
        ulong? channelId = (await dbContext.GuildConfigs.FirstOrDefaultAsync(g => g.GuildId == guildId))?.LogChannelId;
        if (channelId == null) return;
        await _client.Rest.SendMessageAsync(channelId.Value, new MessageProperties { Embeds = [embed], Content = content });
    }

    public async Task SendLogErrorEmbed(ulong guildId, string exMessage, string? title = "Error")
    {
        EmbedProperties embed = new EmbedProperties()
            .WithTitle("Error")
            .WithFields([new() { Name = title, Value = exMessage }])
            .WithColor(BotColours.Fail);

        await SendLogEmbed(guildId, embed, "<@735159945519169576>");
    }
}
