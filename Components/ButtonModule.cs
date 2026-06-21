using FrostBot.Data.Models;
using FrostBot.Logic.Library;
using FrostBot.Logic.Services;
using FrostBot.Logic.Views;
using NetCord;
using NetCord.Rest;
using NetCord.Services;
using NetCord.Services.ComponentInteractions;

namespace FrostBot.Components;

[RequireContext<ButtonInteractionContext>(RequiredContext.Guild)]
public class ButtonModule : ComponentInteractionModule<ButtonInteractionContext>
{
    private readonly LevelService _levelService;
    private readonly LogService _logService;
    private readonly ModerationService _modService;
    private readonly ModerationViewEngine _modViewEngine;

    public ButtonModule(LevelService levelService, LogService logService, ModerationService modService, ModerationViewEngine modViewEngine)
    {
        _levelService = levelService;
        _logService = logService;
        _modService = modService;
        _modViewEngine = modViewEngine;
    }

    private async Task<InteractionMessageProperties> HandleLeaderboardAsync(int targetPage)
    {
        try
        {
            string body = await _levelService.GetLeaderboardPage(targetPage);
            bool hasNextPage = _levelService.hasNextPage(targetPage);
            var message = DiscordInteractions.CreatePagedEmbed("Level Leaderboard", body, targetPage, $"level", hasNextPage);
            return message;
        }
        catch (Exception ex)
        {
            await _logService.SendLogErrorEmbed(Context.Guild.Id, ex.Message, "Error while switching between leaderboard pages");
            var embed = DiscordInteractions.CreateFailEmbed("Error while fetching leaderboard page.");
            return new InteractionMessageProperties().WithEmbeds([embed]);
        }
    }


    [ComponentInteraction("paged_embed")]
    public async Task HandlePagedEmbed(string embedType, int targetPage, ulong? targetUserId = null)
    {
        try
        {
            User? user = targetUserId != null ? await Context.Client.Rest.GetUserAsync((ulong)targetUserId) : null;
            InteractionMessageProperties response = embedType switch
            {
                "level" => await HandleLeaderboardAsync(targetPage),
                "warnings" => await _modViewEngine.CreateModLogEmbed<Warning>(user, targetPage),
                "kicks" => await _modViewEngine.CreateModLogEmbed<Kick>(user, targetPage),
                "bans" => await _modViewEngine.CreateModLogEmbed<Ban>(user, targetPage),
                "mutes" => await _modViewEngine.CreateModLogEmbed<Mute>(user, targetPage),
                _ => new InteractionMessageProperties().WithContent("This button doesn't exist how did you click it?? ||<@735159945519169576>||")
            };

            await Context.Interaction.SendResponseAsync(InteractionCallback.ModifyMessage(m =>
            {
                m.WithContent(response.Content);
                m.WithEmbeds(response.Embeds);
                m.WithComponents(response.Components);
            }));
        } catch (Exception ex)
        {
            await _logService.SendLogErrorEmbed(Context.Guild.Id, ex.Message, $"Error navigating paged embed with id `{embedType}`");
        }
    }
}
