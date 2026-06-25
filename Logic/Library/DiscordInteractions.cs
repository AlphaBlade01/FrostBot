using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace FrostBot.Logic.Library;

public static class DiscordInteractions
{
    public static async Task RespondEphemeral(ApplicationCommandContext context, string content)
    {
        await context.Interaction.SendResponseAsync(InteractionCallback.Message(new InteractionMessageProperties()
        {
            Content = content,
            Flags = MessageFlags.Ephemeral
        }));
    }

    public static async Task SendDeferredResponse(ApplicationCommandContext context, InteractionMessageProperties message)
    {
        await context.Interaction.ModifyResponseAsync(x =>
        {
            x.Content = message.Content;
            x.Embeds = message.Embeds;
            x.Components = message.Components;
            x.AllowedMentions = message.AllowedMentions;
            x.Flags = message.Flags;
        });
    }

    public static EmbedProperties CreateLogEmbed(string title, string? content = null, string? authorName = null,
        string? iconUrl = null, string[]? footerParts = null, Color colour = default)
    {
        string footerText = string.Join(" • ", footerParts ?? []);
        var embed = new EmbedProperties()
            .WithTitle(title)
            .WithDescription(content)
            .WithAuthor(new()
            {
                Name = authorName,
                IconUrl = iconUrl
            })
            .WithFooter(new() { Text = footerText })
            .WithTimestamp(DateTimeOffset.UtcNow)
            .WithColor(colour);
        return embed;
    }

    public static EmbedProperties CreateSuccessEmbed(string message)
    {

        EmbedProperties embed = new EmbedProperties()
            .WithDescription($"✅ {message}")
            .WithColor(BotColours.Success);
        return embed;
    }

    public static EmbedProperties CreateFailEmbed(string message)
    {
        EmbedProperties embed = new EmbedProperties()
            .WithDescription($"❌ {message}")
            .WithColor(BotColours.Fail);
        return embed;
    }

    public static InteractionMessageProperties CreatePagedEmbed(string title, string description, int page, string id, bool hasNextPage = true, string[]? argsList = null)
    {
        string? args = argsList != null ? string.Join(':', argsList) : "0";
        var embed = new EmbedProperties().WithTitle(title).WithDescription(description);
        var messageProperties = new InteractionMessageProperties().WithEmbeds([embed]);
        var actionRow = new ActionRowProperties();

        if (page > 1) actionRow.AddComponents([new ButtonProperties($"paged_embed:{id}:{page - 1}:{args}", EmojiProperties.Standard("⬅️"), ButtonStyle.Primary)]);
        if (hasNextPage) actionRow.AddComponents([new ButtonProperties($"paged_embed:{id}:{page + 1}:{args}", EmojiProperties.Standard("➡️"), ButtonStyle.Primary)]);

        return messageProperties.WithComponents(actionRow.Any() ? [actionRow] : null);
    }

    public static EmbedProperties ApplyAuthor(EmbedProperties embed, User user)
    {
        return embed.WithAuthor(new() { Name = user.Username, IconUrl = (user.GetAvatarUrl() ?? user.DefaultAvatarUrl).ToString() });
    }
}
