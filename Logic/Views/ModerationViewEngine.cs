using FrostBot.Data.Interfaces;
using FrostBot.Data.Models;
using FrostBot.Logic.DTOs;
using FrostBot.Logic.Library;
using FrostBot.Logic.Services;
using NetCord;
using NetCord.Rest;

namespace FrostBot.Logic.Views;

public class ModerationViewEngine
{
    private readonly ModerationService _modService;

    public ModerationViewEngine(ModerationService modService)
    {
        _modService = modService;
    }

    private static string FlattenModEntriesToString(IReadOnlyCollection<IModItem> entries)
    {
        return PageHelper
            .FlattenEnumerableIntoString(entries, e => $"`Reason`: {e.Reason}\n`Moderator`: <@{e.ModeratorUserId}>\n{e.Timestamp:G}\n")
            .Trim();
    }

    private static string FlattenBanEntriesToString(IReadOnlyCollection<Ban> entries)
    {
        return PageHelper
            .FlattenEnumerableIntoString(entries, e => $"`Reason`: {e.Reason}\n`Moderator`: <@{e.ModeratorUserId}>\n`Until`:{e.ExpiresAt?.ToString() ?? """Permanent"""}\n{e.Timestamp:G}\n")
            .Trim();
    }

    private static string FlattenMuteEntriesToString(IReadOnlyCollection<Mute> entries)
    {
        return PageHelper
            .FlattenEnumerableIntoString(entries, e => $"`Reason`: {e.Reason}\n`Moderator`: <@{e.ModeratorUserId}>\n`Until`:{e.ExpiresAt.ToString() ?? """Permanent"""}\n{e.Timestamp:G}\n")
            .Trim();
    }

    public async Task<InteractionMessageProperties> CreateModLogEmbed<T>(User user, int pageNumber) where T : class, IModItem
    {
        Page<T> page = await _modService.FetchModItemPage<T>(user.Id, pageNumber);
        string modName = typeof(T).Name;
        string body = page.Items.Count > 0 ? modName switch
        {
            nameof(Ban) => FlattenBanEntriesToString((IReadOnlyCollection<Ban>)page.Items),
            nameof(Mute) => FlattenMuteEntriesToString((IReadOnlyCollection<Mute>)page.Items),
            _ => FlattenModEntriesToString(page.Items)
        } : $"This user has no {modName}s";

        InteractionMessageProperties message = DiscordInteractions.CreatePagedEmbed($"{modName}s", body, pageNumber, $"{modName}s".ToLower(), page.HasNextPage, [user.Id.ToString()]);
        EmbedProperties embed = message.Embeds.First().WithColor(BotColours.Moderation);
        DiscordInteractions.ApplyAuthor(embed, user);
        embed.WithFooter(new() { Text = $"Id: {user.Id.ToString()}" });

        return message;
    }
}
