using FrostBot.Data;
using FrostBot.Data.Models;
using FrostBot.Logic.Library;
using FrostBot.Logic.Services;
using Microsoft.EntityFrameworkCore;
using NetCord;
using NetCord.Rest;
using NetCord.Services;
using NetCord.Services.ApplicationCommands;

namespace FrostBot.Commands;

[RequireContext<ApplicationCommandContext>(RequiredContext.Guild)]
public class LevelModule : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly IDbContextFactory<BotDbContext> _dbContextFactory;
    private readonly LevelService _levelService;
    private readonly LogService _logService;

    public LevelModule(IDbContextFactory<BotDbContext> dbContextFactory, LevelService levelService, LogService logService)
    {
        _dbContextFactory = dbContextFactory;
        _levelService = levelService;
        _logService = logService;
    }


    [SlashCommand("level", "See level of specified user (defaults to self)")]
    public async Task Level(GuildUser? user = null)
    {
        try
        {
            await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage());
            const char filledChar = '▰';
            const char emptyChar = '▱';
            user = user ?? (GuildUser)Context.User;

            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
            UserInfo? info = await dbContext.UserInfo.FirstOrDefaultAsync(u => u.UserId == user.Id);

            double xp = info?.TotalXp ?? 0;
            int level = info?.Level ?? 0;
            int baseXp = _levelService.CalculateXp(level);
            int maxXp = _levelService.CalculateXp(level + 1);
            int progress = (int)((xp - baseXp) / (maxXp - baseXp) * 20);
            string progressBar = $"[ {new string(filledChar, progress) + new string(emptyChar, 20 - progress)} ]";

            await Context.Interaction.ModifyResponseAsync(x =>
            {
                x.Content = $"{user} is level **{level}** with total XP **{xp}**\n{progressBar}\n{xp - baseXp} / {maxXp - baseXp} XP";
                x.AllowedMentions = AllowedMentionsProperties.None;
            });
        }
        catch (Exception ex)
        {
            await _logService.SendLogErrorEmbed(Context.Guild.Id, ex.Message, "Error while executing level command");
            var embed = DiscordInteractions.CreateFailEmbed("Error while executing level command.");
            await DiscordInteractions.SendDeferredResponse(Context, new() { Embeds = [embed] });
        }
    }

    [SlashCommand("leaderboard", "View the level leaderboard")]
    public async Task Leaderboard()
    {
        try
        {
            await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage());
            string levelList = await _levelService.GetLeaderboardPage(1);
            bool hasNextPage = _levelService.hasNextPage(1);
            
            InteractionMessageProperties message = DiscordInteractions.CreatePagedEmbed("Level Leaderboard", levelList, 1, "level", hasNextPage);
            message.Embeds?.First().WithColor(new Color(61, 119, 206));
            await DiscordInteractions.SendDeferredResponse(Context, message);
        }
        catch (Exception ex)
        {
            await _logService.SendLogErrorEmbed(Context.Guild.Id, ex.Message, "Error while fetching leaderboard.");
            var embed = DiscordInteractions.CreateFailEmbed("Error while fetching leaderboard.");
            await DiscordInteractions.SendDeferredResponse(Context, new() { Embeds = [embed] });
        }
    }
}
