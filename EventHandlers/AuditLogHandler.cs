using FrostBot.Data;
using FrostBot.Logic.Services;
using Microsoft.EntityFrameworkCore;
using NetCord;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using NetCord.JsonModels;

namespace FrostBot.EventHandlers;

public class AuditLogHandler : IGuildAuditLogEntryCreateGatewayHandler
{
    private const ulong BOT_USER_ID = 816242708804009996;
    private readonly IDbContextFactory<BotDbContext> _dbContextFactory;
    private readonly ModerationService _modService;
    private readonly GatewayClient _client;
    private readonly LogService _logService;

    public AuditLogHandler(IDbContextFactory<BotDbContext> dbContextFactory, ModerationService modService, GatewayClient client, LogService logService)
    {
        _dbContextFactory = dbContextFactory;
        _modService = modService;
        _client = client;
        _logService = logService;
    }

    private async Task BanEventHandler(AuditLogEntry logEntry)
    {
        if (logEntry.UserId == BOT_USER_ID) return; // Ensure the event was not handled by the bot

        await using BotDbContext dbContext = await _dbContextFactory.CreateDbContextAsync();
        GuildUser user = await _client.Rest.GetGuildUserAsync(logEntry.GuildId, (ulong)logEntry.TargetId);
        await _modService.BanAsync(user, (ulong)logEntry.UserId, logEntry.Reason);
    }

    private async Task UnbanEventHandler(AuditLogEntry logEntry)
    {
        if (logEntry.UserId == BOT_USER_ID) return; // Ensure the event was not handled by the bot

        await using BotDbContext dbContext = await _dbContextFactory.CreateDbContextAsync();
        User user = await _client.Rest.GetUserAsync((ulong)logEntry.TargetId);
        await _modService.UnbanAsync(logEntry.GuildId, user, logEntry.UserId, logEntry.Reason);
    }

    private async Task MuteEventHandler(AuditLogEntry logEntry, DateTimeOffset until)
    {
        if (logEntry.UserId == BOT_USER_ID) return; // Ensure the event was not handled by the bot


        await using BotDbContext dbContext = await _dbContextFactory.CreateDbContextAsync();
        GuildUser user = await _client.Rest.GetGuildUserAsync(logEntry.GuildId, (ulong)logEntry.TargetId);
        await _modService.MuteAsync(user, (ulong)logEntry.UserId, logEntry.Reason, until);
        
    }

    private async Task UnmuteEventHandler(AuditLogEntry logEntry)
    {
        if (logEntry.UserId == BOT_USER_ID) return; // Ensure the event was not handled by the bot

        await using BotDbContext dbContext = await _dbContextFactory.CreateDbContextAsync();
        GuildUser user = await _client.Rest.GetGuildUserAsync(logEntry.GuildId, (ulong)logEntry.TargetId);
        await _modService.UnmuteAsync(user, logEntry.UserId);
    }

    private async Task KickEventHandler(AuditLogEntry logEntry)
    {
        if (logEntry.UserId == BOT_USER_ID) return; // Ensure the event was not handled by the bot
     
        await using BotDbContext dbContext = await _dbContextFactory.CreateDbContextAsync();
        User user = await _client.Rest.GetUserAsync((ulong)logEntry.TargetId);
        await _modService.KickAsync(logEntry.GuildId, user, (ulong)logEntry.UserId, logEntry.Reason);
    }

    public async ValueTask HandleAsync(AuditLogEntry arg)
    {
        switch (arg.ActionType)
        {
            case AuditLogEvent.GuildUserBanAdd:
                await BanEventHandler(arg);
                break;
            case AuditLogEvent.GuildUserBanRemove:
                await UnbanEventHandler(arg);
                break;
            case AuditLogEvent.GuildUserUpdate:
                try
                {
                    arg.TryGetChange<JsonGuildUser, DateTimeOffset?>(u => u.TimeOutUntil, out AuditLogChange<DateTimeOffset?>? change);
                    if (change == null) return; // Not a timeout event

                    // Call right handler depending on whether timeout applied or removed
                    if (change.HasNewValue) await MuteEventHandler(arg, change.NewValue.Value);
                    else await UnmuteEventHandler(arg);
                }
                catch (Exception ex)
                {
                    await _logService.SendLogErrorEmbed(arg.GuildId, ex.Message, "Error while handling manual timeout");
                }

                break;
            case AuditLogEvent.GuildUserKick:
                await KickEventHandler(arg);
                break;

        }
    }
}
