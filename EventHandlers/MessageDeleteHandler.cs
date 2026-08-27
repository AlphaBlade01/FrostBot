using FrostBot.Data;
using FrostBot.Data.Models;
using FrostBot.Logic.Library;
using FrostBot.Logic.Services;
using Microsoft.EntityFrameworkCore;
using NetCord;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;

namespace FrostBot.EventHandlers;

public class MessageDeleteHandler : IMessageDeleteGatewayHandler
{
    private readonly IDbContextFactory<BotDbContext> _dbContextFactory;
    private readonly LogService _logService;
    private readonly ChannelConfigService _channelConfigService;
    private readonly GatewayClient _client;

    public MessageDeleteHandler(IDbContextFactory<BotDbContext> dbContextFactory, LogService logService, ChannelConfigService channelConfigService, GatewayClient client)
    {
        _dbContextFactory = dbContextFactory;
        _logService = logService;
        _channelConfigService = channelConfigService;
        _client = client;
    }

    public async ValueTask HandleAsync(MessageDeleteEventArgs arg)
    {
        if (arg.GuildId is null) return; // Ensure the message is in a guild

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        LogMessage? log = await dbContext.Messages.FirstOrDefaultAsync(m => m.MessageId == arg.MessageId);

        if (log is null) return; // Message expired (not logged)

        // Update database
        log.IsDeleted = true;
        await dbContext.SaveChangesAsync();

        // Send log embed to the log channel
        var embed = DiscordInteractions.CreateLogEmbed($"Message Deleted in <#{arg.ChannelId}>", log.Content, footerParts: [$"Id: {log.AuthorId}", $"Message Id: {arg.MessageId}"], colour: BotColours.Messages);
        GuildUser author = await _client.Rest.GetGuildUserAsync((ulong)arg.GuildId, log.AuthorId);
        DiscordInteractions.ApplyAuthor(embed, author);

        // Ensure message logging is enabled
        if ((await _channelConfigService.GetChannelConfigAsync(arg.ChannelId)).IsMessageLoggingDisabled) return;
        await _logService.SendLogEmbed((ulong)arg.GuildId, embed);
    }
}
