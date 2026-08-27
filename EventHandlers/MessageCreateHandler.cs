using FrostBot.Data;
using FrostBot.Data.Models;
using FrostBot.Logic.Services;
using Microsoft.EntityFrameworkCore;
using NetCord;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using NetCord.Rest;

namespace FrostBot.EventHandlers;

public class MessageCreateHandler : IMessageCreateGatewayHandler
{
    private const string DEFAULT_LEVEL_MESSAGE_TEMPLATE = "Congrats $user, you have reached level $level";
    private readonly IDbContextFactory<BotDbContext> _dbContextFactory;
    private readonly ChannelConfigService _channelConfigService;
    private readonly LevelService _levelService;
    private readonly GatewayClient _client;

    public MessageCreateHandler(IDbContextFactory<BotDbContext> dbContextFactory, ChannelConfigService channelConfigService, LevelService levelService, GatewayClient client)
    {
        _dbContextFactory = dbContextFactory;
        _channelConfigService = channelConfigService;
        _levelService = levelService;
        _client = client;
    }

    private async Task HandleRoleReward(int level, int levelIncrease, GuildUser user)
    {
        // Fetch role reward 
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        RoleReward? reward = await dbContext.RoleRewards
            .Where(r => r.LevelRequired <= level && r.LevelRequired > level - levelIncrease)
            .OrderByDescending(r => r.LevelRequired)
            .FirstOrDefaultAsync();

        // Add role reward if exists
        if (reward == null) return;
        await user.AddRoleAsync(reward.RoleId);
    }

    private async Task SendLevelUpNotification(Message arg, UserInfo user)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        GuildConfig? storedGuildConfig = await dbContext.GuildConfigs.FirstOrDefaultAsync(c => c.GuildId == arg.GuildId) ?? new()
        {
            GuildId = (ulong)arg.GuildId
        };
        string message = (storedGuildConfig?.LevelUpMessage ?? DEFAULT_LEVEL_MESSAGE_TEMPLATE)
            .Replace("$user", arg.Author.ToString())
            .Replace("$level", user.Level.ToString());

        if (storedGuildConfig == null || storedGuildConfig.LevelUpChannelId == null)
        {
            await arg.SendAsync(new() { Content = message, AllowedMentions = AllowedMentionsProperties.All });
        }
        else
        {
            await _client.Rest.SendMessageAsync((ulong)storedGuildConfig.LevelUpChannelId, new() { Content = message, AllowedMentions = AllowedMentionsProperties.All });
        }
    }

    private async Task HandleLevelLogic(Message arg)
    {
        var userId = arg.Author.Id;

        if (_levelService.IsUserOnCooldown(userId) || arg.Author.IsBot || arg.GuildId == null) return;
        _levelService.AddUserCooldown(userId);

        // Update level in database
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        ChannelConfig channelConfig = await _channelConfigService.GetChannelConfigAsync(arg.ChannelId);
        if (channelConfig.IsXpDisabled) return;

        UserInfo? storedUser = await dbContext.UserInfo.FirstOrDefaultAsync(u => u.UserId == userId);
        UserInfo user = storedUser ?? new() { UserId = userId };

        bool userExists = storedUser != null;
        int levelIncrease = _levelService.UpdateLevel(user);

        if (!userExists) await dbContext.UserInfo.AddAsync(user);
        await dbContext.SaveChangesAsync();
        if (levelIncrease == 0) return;

        await Task.WhenAll(
            HandleRoleReward(user.Level, levelIncrease, arg.Author as GuildUser),
            SendLevelUpNotification(arg, user)
        );
    }

    private async Task HandleMessageLogging(Message arg)
    {
        if (arg.Author.IsBot || arg.GuildId is null) return; // Only log messages from users in guilds

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        LogMessage newLog = new()
        {
            MessageId = arg.Id,
            AuthorId = arg.Author.Id,
            ChannelId = arg.ChannelId,
            Content = arg.Content,
            CreatedAt = arg.CreatedAt
        };
        await dbContext.Messages.AddAsync(newLog);
        await dbContext.SaveChangesAsync();
    }

    public async ValueTask HandleAsync(Message arg)
    {
        await Task.WhenAll(
            HandleLevelLogic(arg),
            HandleMessageLogging(arg)
        );
    }
}
