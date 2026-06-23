using FrostBot.Data;
using FrostBot.Data.Models;
using FrostBot.Logic.Library;
using FrostBot.Logic.Services;
using FrostBot.Logic.Views;
using Microsoft.EntityFrameworkCore;
using NetCord;
using NetCord.Rest;
using NetCord.Services;
using NetCord.Services.ApplicationCommands;
using Quartz;
using System.Text.RegularExpressions;

namespace FrostBot.Commands;

[RequireContext<ApplicationCommandContext>(RequiredContext.Guild)]
public class ModerationModule : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly IDbContextFactory<BotDbContext> _dbContextFactory;
    private readonly IScheduler _scheduler;
    private readonly LogService _logService;
    private readonly ModerationService _modService;
    private readonly ModerationViewEngine _modViewEngine;

    public ModerationModule(
        IDbContextFactory<BotDbContext> dbContextFactory,
        IScheduler scheduler,
        LogService logService,
        ModerationService modService,
        ModerationViewEngine modViewEngine)
    {
        _dbContextFactory = dbContextFactory;
        _scheduler = scheduler;
        _logService = logService;
        _modService = modService;
        _modViewEngine = modViewEngine;
    }

    private async Task LogError(Exception ex, string action)
    {
        EmbedProperties embed = DiscordInteractions.CreateFailEmbed($"Error occured while {action}");
        await DiscordInteractions.SendDeferredResponse(Context, new() { Embeds = [embed] });
        await _logService.SendLogErrorEmbed(Context.Guild.Id, ex.Message, "Error occured while " + action);
    }

    private async Task RespondToModeration(string status)
    {
        EmbedProperties embed = DiscordInteractions.CreateSuccessEmbed(status);
        await DiscordInteractions.SendDeferredResponse(Context, new() { Embeds = [embed] });
    }

    private int Parse(string input, string unit)
    {
        var match = Regex.Match(input, $@"(\d+){unit}");
        return match.Success ? int.Parse(match.Groups[1].Value) : 0;
    }

    private async Task<DateTimeOffset> ParseTime(string time)
    {
        DateTimeOffset result = DateTimeOffset.UtcNow;

        try
        {
            int years = Parse(time, "y");
            int months = Parse(time, "M");
            int days = Parse(time, "d");
            int hours = Parse(time, "h");
            int minutes = Parse(time, "m");
            int seconds = Parse(time, "s");

            result = result.AddYears(years)
                .AddMonths(months)
                .AddDays(days)
                .AddHours(hours)
                .AddMinutes(minutes)
                .AddSeconds(seconds);
        }
        catch (Exception ex)
        {
            await LogError(ex, "parsing time");
        }

        return result;
    }


    [SlashCommand("warn", "Warn a user.",
        DefaultGuildPermissions = Permissions.ModerateUsers)]
    public async Task WarnAsync(
        [SlashCommandParameter(Description = "The user to warn.")] GuildUser user,
        [SlashCommandParameter(Description = "The reason for the warning.")] string reason)
    {
        // Handle user warning themselves
        if (user.Id == Context.User.Id) { await DiscordInteractions.RespondEphemeral(Context, "You cannot warn yourself."); return; }

        try
        {
            await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage());
            string status = await _modService.WarnAsync(user, Context.User.Id, reason);
            await RespondToModeration(status);
        }
        catch (Exception ex)
        {
            await LogError(ex, "warning");
        }
    }


    [SlashCommand("kick", "Kick a user.",
        DefaultGuildPermissions = Permissions.ModerateUsers)]
    public async Task KickAsync(
        [SlashCommandParameter(Description = "The user to kick.")] GuildUser user,
        [SlashCommandParameter(Description = "The reason for the kick.")] string reason)
    {
        // Handle user kicking themselves
        if (user.Id == Context.User.Id) { await DiscordInteractions.RespondEphemeral(Context, "You cannot kick yourself."); return; }

        try
        {
            await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage());
            string status = await _modService.KickAsync(user.GuildId, user, Context.User.Id, reason);
            await user.KickAsync();
            await RespondToModeration(status);
        } catch (Exception ex)
        {
            await LogError(ex, "kicking");
        }
    }

    [SlashCommand("mute", "Mute a user.",
            DefaultGuildPermissions = Permissions.ModerateUsers)]
    public async Task MuteAsync(
        [SlashCommandParameter(Description = "The user to mute.")] GuildUser user,
        [SlashCommandParameter(Description = "The reason for the mute.")] string reason,
        [SlashCommandParameter(Description = "The duration of the mute (Eg. 1d 1h 1m 1s). Default = 28d (max).")] string duration = "28d")
    {
        // Handle user muting themselves
        if (user.Id == Context.User.Id) { await DiscordInteractions.RespondEphemeral(Context, "You cannot mute yourself."); return; }
        // Handle if user is already muted
        if (user.TimeOutUntil > DateTimeOffset.UtcNow) { await DiscordInteractions.RespondEphemeral(Context, "User is already muted."); return; }

        try
        {
            await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage());
            DateTimeOffset until = await ParseTime(duration);
            DateTimeOffset maximumUntil = DateTimeOffset.UtcNow.AddSeconds(2_419_190);
            until = maximumUntil < until ? maximumUntil : until;

            string status = await _modService.MuteAsync(user, Context.User.Id, reason, until);
            await user.TimeOutAsync(until, new() { AuditLogReason = reason });
            await RespondToModeration(status);
    } catch (Exception ex)
        {
            await LogError(ex, "muting");
        }
    }

    [SlashCommand("ban", "Ban a user.",
            DefaultGuildPermissions = Permissions.BanUsers)]
    public async Task BanAsync(
        [SlashCommandParameter(Description = "The user to ban.")] GuildUser user,
        [SlashCommandParameter(Description = "The reason for the ban.")] string reason,
        [SlashCommandParameter(Description = "The duration of the ban (Eg. '1y 1M 1d 1h 1m 1s')")] string? duration,
        [SlashCommandParameter(Description = "Whether to delete messages from the user within the past day.")] bool deleteMessages = false)
    {
        // Handle user banning themselves
        if (user.Id == Context.User.Id) { await DiscordInteractions.RespondEphemeral(Context, "You cannot ban yourself."); return; }

        try
        {
            await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage());
            DateTimeOffset? until = duration != null ? await ParseTime(duration) : null;
            string status = await _modService.BanAsync(user, Context.User.Id, reason, until);
            await user.BanAsync(deleteMessages ? 86400 : 0, new() { AuditLogReason = reason });
            await RespondToModeration(status);
        }
        catch (Exception ex)
        {
            await LogError(ex, "banning");
        }
    }

    [SlashCommand("unmute", "Unmute a user.",
            DefaultGuildPermissions = Permissions.ModerateUsers)]
    public async Task UnmuteAsync(
        [SlashCommandParameter(Description = "The user to unmute.")] GuildUser user)
    {
        try
        {
            await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage());
            bool success = await _modService.UnmuteAsync(user, Context.User.Id);
            await user.ModifyAsync(u => u.TimeOutUntil = default(DateTimeOffset));
            await RespondToModeration(success ? $"User {user} unmuted." : "User is not muted.");
        } catch (Exception ex)
        {
            await LogError(ex, "unmuting");
        }
    }

    [SlashCommand("unban", "Unban a user.",
            DefaultGuildPermissions = Permissions.BanUsers)]
    public async Task UnbanAsync(
        [SlashCommandParameter(Description = "The user to unban.")] User user,
        [SlashCommandParameter(Description = "The reason for the unban.")] string? reason)
    {
        // Handle user unbanning themselves
        if (user.Id == Context.User.Id) { await DiscordInteractions.RespondEphemeral(Context, "You cannot unban yourself."); return; }

        try
        {
            await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage());
            bool success = await _modService.UnbanAsync(Context.Guild.Id, user, Context.User.Id, reason);      
            if (success) await Context.Client.Rest.UnbanGuildUserAsync(Context.Guild.Id, user.Id);
            await RespondToModeration(success ? $"User {user} unbanned" : "User is not banned.");
        }
        catch (Exception ex)
        {
            await LogError(ex, "unbanning");
        }
    }

    [SlashCommand("warnings", "View warnings of a user", DefaultGuildPermissions = Permissions.ModerateUsers)]
    public async Task Warnings(User? user = null)
    {
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage());
        User target = user ?? Context.User;
        InteractionMessageProperties message = await _modViewEngine.CreateModLogEmbed<Warning>(target, 1);
        await DiscordInteractions.SendDeferredResponse(Context, message);
    }

    [SlashCommand("kicks", "View kicks of a user", DefaultGuildPermissions = Permissions.ModerateUsers)]
    public async Task Kicks(User? user = null)
    {
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage());
        User target = user ?? Context.User;
        InteractionMessageProperties message = await _modViewEngine.CreateModLogEmbed<Kick>(target, 1);
        await DiscordInteractions.SendDeferredResponse(Context, message);
    }

    [SlashCommand("bans", "View bans of a user", DefaultGuildPermissions = Permissions.ModerateUsers)]
    public async Task Bans(User? user = null)
    {
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage());
        User target = user ?? Context.User;
        InteractionMessageProperties message = await _modViewEngine.CreateModLogEmbed<Ban>(target, 1);
        await DiscordInteractions.SendDeferredResponse(Context, message);
    }

    [SlashCommand("mutes", "View mutes of a user", DefaultGuildPermissions = Permissions.ModerateUsers)]
    public async Task Mutes(User? user = null)
    {
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage());
        User target = user ?? Context.User;
        InteractionMessageProperties message = await _modViewEngine.CreateModLogEmbed<Mute>(target, 1);
        await DiscordInteractions.SendDeferredResponse(Context, message);
    }
}

