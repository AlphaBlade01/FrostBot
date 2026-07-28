using FrostBot.Data;
using FrostBot.Data.Models;
using FrostBot.Logic.Library;
using FrostBot.Logic.Services;
using Microsoft.EntityFrameworkCore;
using NetCord;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using NetCord.Rest;

namespace FrostBot.EventHandlers;

public class MessageUpdateHandler : IMessageUpdateGatewayHandler
{
    private readonly IDbContextFactory<BotDbContext> _dbContextFactory;
    private readonly LogService _logService;
    
    public MessageUpdateHandler(IDbContextFactory<BotDbContext> dbContextFactory, LogService logService)
    {
        _dbContextFactory = dbContextFactory;
        _logService = logService;
    }

    public async ValueTask HandleAsync(Message arg)
    {
        // Ignore bot messages, messages outside of guilds, and messages that have not been edited
        if (arg.Author.IsBot || arg.Guild is null || arg.EditedAt is null) return;

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        LogMessage? log = await dbContext.Messages.FirstOrDefaultAsync(m => m.MessageId == arg.Id);

        if (log is null) return; // Message entry expired

        // Create log embed
        GuildUser user = await arg.Guild.GetUserAsync(arg.Author.Id);
        var embed = DiscordInteractions.CreateLogEmbed("Message Updated", footerParts: [$"Id: {user.Id}", $"Message Id: {arg.Id}"], colour: BotColours.Messages)
            .WithFields([
                new EmbedFieldProperties() { Name = "Old Content", Value = log.Content },
                new EmbedFieldProperties() { Name = "New Content", Value = arg.Content }
            ])
            .WithUrl($"https://discord.com/channels/{arg.GuildId}/{arg.ChannelId}/{arg.Id}");
        DiscordInteractions.ApplyAuthor(embed, user);

        // Update database entry
        log.Content = arg.Content;
        await dbContext.SaveChangesAsync();

        // Send log embed to logging channel
        await _logService.SendLogEmbed((ulong)arg.GuildId, embed);
    }
}
