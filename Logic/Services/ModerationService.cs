using FrostBot.Data;
using FrostBot.Data.Interfaces;
using FrostBot.Data.Models;
using FrostBot.Logic.DTOs;
using FrostBot.Logic.Jobs;
using FrostBot.Logic.Library;
using Microsoft.EntityFrameworkCore;
using NetCord;
using NetCord.Rest;
using Quartz;

namespace FrostBot.Logic.Services;

public class ModerationService
{
    private const int PAGE_SIZE = 5;
    private readonly IDbContextFactory<BotDbContext> _dbContextFactory;
    private readonly IScheduler _scheduler;
    private readonly LogService _logService;

    public ModerationService(IDbContextFactory<BotDbContext> dbContextFactory, IScheduler scheduler, LogService logService)
    {
        _dbContextFactory = dbContextFactory;
        _scheduler = scheduler;
        _logService = logService;
    }

    private async Task ScheduleUnbanAsync(ulong userId, ulong guildId, DateTimeOffset triggerTime)
    {
        var job = JobBuilder.Create<UnbanJob>()
            .WithIdentity($"unban-{userId}", "unbans")
            .UsingJobData("UserId", userId.ToString())
            .UsingJobData("GuildId", guildId.ToString())
            .Build();
        var trigger = TriggerBuilder.Create()
            .StartAt(triggerTime)
            .Build();
        await _scheduler.ScheduleJob(job, trigger);
    }

    private async Task ScheduleUnmuteAsync(ulong userId, ulong guildId, DateTimeOffset triggerTime)
    {
        var job = JobBuilder.Create<UnmuteJob>()
            .WithIdentity($"unmute-{userId}", "unmutes")
            .UsingJobData("UserId", userId.ToString())
            .UsingJobData("GuildId", guildId.ToString())
            .Build();
        var trigger = TriggerBuilder.Create()
            .StartAt(triggerTime)
            .Build();
        await _scheduler.ScheduleJob(job, trigger);
    }

    public async Task<string> BanAsync(GuildUser user, ulong moderatorId, string? reason = null, DateTimeOffset? until = null)
    {
        // Update database
        await using BotDbContext dbContext = await _dbContextFactory.CreateDbContextAsync();
        Ban ban = new()
        {
            UserId = user.Id,
            ModeratorUserId = moderatorId,
            Reason = reason ?? "",
            Timestamp = DateTimeOffset.UtcNow,
            ExpiresAt = until,
            IsActive = true
        };
        await dbContext.Bans.AddAsync(ban);
        await dbContext.SaveChangesAsync();

        // Schedule unban 
        if (until != null)
            await ScheduleUnbanAsync(user.Id, user.GuildId, (DateTimeOffset)until);

        // Log ban
        EmbedProperties embed = DiscordInteractions.CreateLogEmbed("Ban", reason, user.Username,
            (user.GetAvatarUrl() ?? user.DefaultAvatarUrl).ToString(), [$"Id: {user.Id}"], BotColours.Moderation)
            .WithFields([
                new() { Name = "Moderator", Value = $"<@{moderatorId}>", Inline = true },
                new() { Name = "User", Value = user.ToString(), Inline = true },
                new() { Name = "Until", Value = until?.UtcDateTime.ToString() ?? "Permanent", Inline = true }
            ]);
        await _logService.SendLogEmbed(user.GuildId, embed);
        return $"Banned user {user}.";
    }

    public async Task<bool> UnbanAsync(ulong guildId, User user, ulong? moderatorId = null, string? reason = null)
    {
        // Update database
        await using BotDbContext dbContext = await _dbContextFactory.CreateDbContextAsync();
        Ban? ban = await dbContext.Bans.FirstOrDefaultAsync(b => b.UserId == user.Id && b.IsActive == true);
        if (ban == null) return false;

        ban.UnbannedAt = DateTimeOffset.UtcNow;
        ban.IsActive = false;
        await dbContext.SaveChangesAsync();

        // Delete scheduled unban job if exists
        var jobKey = new JobKey($"unban-{user.Id}", "unbans");
        bool exists = await _scheduler.CheckExists(jobKey);
        if (exists) await _scheduler.DeleteJob(jobKey);

        // Log unban
        EmbedProperties embed = DiscordInteractions.CreateLogEmbed("Unban", reason, user.Username,
            (user.GetAvatarUrl() ?? user.DefaultAvatarUrl).ToString(), [$"Id: {user.Id}"], BotColours.Moderation)
            .AddFields([new() { Name = "User", Value = user.ToString(), Inline = true }]);
        if (moderatorId != null) embed.AddFields([new() { Name = "Moderator", Value = $"<@{moderatorId}>", Inline = true }]);
        await _logService.SendLogEmbed(guildId, embed);
        return true;
    }

    public async Task<string> MuteAsync(GuildUser user, ulong moderatorId, string reason, DateTimeOffset until)
    {
        // Update database
        await using BotDbContext dbContext = await _dbContextFactory.CreateDbContextAsync();
        Mute mute = new()
        {
            UserId = user.Id,
            ModeratorUserId = moderatorId,
            Reason = reason,
            Timestamp = DateTimeOffset.UtcNow,
            ExpiresAt = until,
            IsActive = true
        };
        await dbContext.Mutes.AddAsync(mute);
        await dbContext.SaveChangesAsync();
        await ScheduleUnmuteAsync(user.Id, user.GuildId, until);

        // Log mute
        EmbedProperties embed = DiscordInteractions.CreateLogEmbed("Mute", reason, user.Username,
            (user.GetAvatarUrl() ?? user.DefaultAvatarUrl).ToString(), [$"Id: {user.Id}"], BotColours.Moderation)
            .WithFields([
                new() { Name = "Moderator", Value = $"<@{moderatorId}>", Inline = true },
                new() { Name = "User", Value = user.ToString(), Inline = true },
                new() { Name = "Until", Value = until.ToString(), Inline = true }
            ]);
        await _logService.SendLogEmbed(user.GuildId, embed);
        return $"Muted user {user}.";
    }

    public async Task<bool> UnmuteAsync(GuildUser user, ulong? moderatorId = null)
    {
        // Update database
        await using BotDbContext dbContext = await _dbContextFactory.CreateDbContextAsync();
        Mute? mute = await dbContext.Mutes.FirstOrDefaultAsync(m => m.UserId == user.Id && m.IsActive);
        if (mute == null) return false;

        mute.UnmutedAt = DateTimeOffset.UtcNow;
        mute.IsActive = false;
        await dbContext.SaveChangesAsync();

        // Delete Quartz job if exists
        var jobkey = new JobKey($"unmute-{user.Id}", "unmutes");
        bool exists = await _scheduler.CheckExists(jobkey);
        if (exists) await _scheduler.DeleteJob(jobkey);

        // Log unmute
        EmbedProperties embed = DiscordInteractions.CreateLogEmbed("Unmute", null, user.Username,
            (user.GetAvatarUrl() ?? user.DefaultAvatarUrl).ToString(), [$"Id: {user.Id}"], BotColours.Moderation)
            .AddFields([ new() { Name = "User", Value = user.ToString(), Inline = true } ]);
        if (moderatorId != null) embed.AddFields([new() { Name = "Moderator", Value = $"<@{moderatorId}>", Inline = true }]);
        await _logService.SendLogEmbed(user.GuildId, embed);
        return true;
    }

    public async Task<string> KickAsync(ulong guildId, User user, ulong moderatorId, string reason)
    {
        // Update database
        await using BotDbContext dbContext = await _dbContextFactory.CreateDbContextAsync();
        Kick kick = new()
        {
            UserId = user.Id,
            ModeratorUserId = moderatorId,
            Reason = reason,
            Timestamp = DateTimeOffset.UtcNow
        };
        await dbContext.Kicks.AddAsync(kick);
        await dbContext.SaveChangesAsync();

        // Log kick
        var embed = DiscordInteractions.CreateLogEmbed("Kick", reason, user.Username,
            (user.GetAvatarUrl() ?? user.DefaultAvatarUrl).ToString(), [$"Id: {user.Id}"], BotColours.Moderation)
            .WithFields([
                new() { Name = "Moderator", Value = $"<@{moderatorId}>", Inline = true },
                new() { Name = "User", Value = user.ToString(), Inline = true }
            ]);

        await _logService.SendLogEmbed(guildId, embed);
        return $"Kicked user {user}.";
    }

    public async Task<string> WarnAsync(GuildUser user, ulong moderatorId, string reason)
    {
        await using BotDbContext dbContext = await _dbContextFactory.CreateDbContextAsync();
        Warning warn = new()
        {
            UserId = user.Id,
            ModeratorUserId = moderatorId,
            Reason = reason,
            Timestamp = DateTimeOffset.UtcNow
        };

        await dbContext.Warnings.AddAsync(warn);
        await dbContext.SaveChangesAsync();

        // Send log to log channel
        var embed = DiscordInteractions.CreateLogEmbed("Warning", reason, user.Username,
            (user.GetAvatarUrl() ?? user.DefaultAvatarUrl).ToString(), [$"Id: {user.Id}"], BotColours.Moderation)
            .AddFields([
                new() { Name = "Moderator", Value = $"<@{moderatorId}>", Inline = true },
                new() { Name = "User", Value = user.ToString(), Inline = true }
            ]);

        await _logService.SendLogEmbed(user.GuildId, embed);
        return $"Warned user {user}.";
    }

    public async Task<Page<T>> FetchModItemPage<T>(ulong userId, int page) where T : class, IModItem
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        T[] entry = await dbContext.Set<T>()
            .Where(x => x.UserId == userId)
            .Skip((page - 1) * PAGE_SIZE)
            .Take(PAGE_SIZE + 1)
            .ToArrayAsync();
        return new Page<T>([.. entry.Take(PAGE_SIZE)], entry.Length == PAGE_SIZE + 1);
    }
}
