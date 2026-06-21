using FrostBot.Data;
using FrostBot.Data.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;

namespace FrostBot.Logic.Services;

public class LevelService
{
    private const int COOLDOWN_MINUTES = 1;
    private const int MAX_MESSAGES = 100;
    private const int XP_INCREASE_LB = 15;
    private const int XP_INCREASE_UB = 25;
    private const int LB_PAGE_SIZE = 10;
    // Key = user id, Value = time at which cooldown ends
    private readonly ConcurrentDictionary<ulong, DateTimeOffset> userCooldowns = new();
    private int messageCount = 0;
    private int loadedLeaderboardPages = 0;

    private readonly IDbContextFactory<BotDbContext> _dbContextFactory;
    private readonly Random _random = new();

    public LevelService(IDbContextFactory<BotDbContext> dbContextFactory) 
    {
        _dbContextFactory = dbContextFactory;
    }

    private void CleanExpiredCache()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in userCooldowns)
        {
            if (entry.Value < now) userCooldowns.TryRemove(entry);
        }
    }

    public bool IsUserOnCooldown(ulong userId)
    {
        bool exists = userCooldowns.TryGetValue(userId, out DateTimeOffset until);

        // Increment messages sent & clean cache if passed threshold
        Interlocked.Increment(ref messageCount);
        if (messageCount > MAX_MESSAGES)
        {
            messageCount = 0;
            CleanExpiredCache();
        }

        return exists ? until > DateTimeOffset.UtcNow : false;
    }

    public void AddUserCooldown(ulong userId)
    {
        userCooldowns[userId] = DateTimeOffset.UtcNow.AddMinutes(COOLDOWN_MINUTES);
    }

    public int UpdateLevel(UserInfo user)
    {
        int xpIncrease = _random.Next(XP_INCREASE_LB, XP_INCREASE_UB + 1);
        int newLevel = CalculateLevel(user.TotalXp + xpIncrease);
        int increase = newLevel - user.Level;

        user.TotalXp += xpIncrease;
        user.Level = newLevel;

        return increase;
    }

    public int CalculateLevel(int xp)
    {
        if (xp <= 0) return 0;

        double x = xp;

        double a = (0.3 * x) + 41.25;
        double pChunk = Math.Pow(-61.0 / 12.0, 3);

        double discriminant = Math.Sqrt(Math.Pow(a, 2) + pChunk);

        double root1 = Math.Cbrt(a + discriminant);
        double root2 = Math.Cbrt(a - discriminant);

        double calculatedLevel = root1 + root2 - 5.5;

        return (int)Math.Floor(Math.Round(calculatedLevel, 5));
    }

    public int CalculateXp(int level)
    {
        return (int)((5d / 3d) * (int)Math.Pow(level, 3) + (55d / 2d) * (int)Math.Pow(level, 2) + (755d / 6d) * level);
    }

    public async Task<string> GetLeaderboardPage(int page)
    {
        // Retrieve appropriate ordered users
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        UserInfo[] retrievedPage = await dbContext.UserInfo.OrderByDescending(u => u.TotalXp)
            .Skip((page - 1) * LB_PAGE_SIZE)
            .Take(LB_PAGE_SIZE + 1)
            .ToArrayAsync();

        // Update loadedLeaderboardPages for future evaluations of how many pages can be loaded
        if (retrievedPage.Length != 0)
        {
            bool loadedExtraElement = retrievedPage.Length > LB_PAGE_SIZE;
            if (page > loadedLeaderboardPages) loadedLeaderboardPages += loadedExtraElement ? 2 : 1;
            else if (page == loadedLeaderboardPages) loadedLeaderboardPages += loadedExtraElement ? 1 : 0;
        }

        // Build leaderboard body string
        string leaderboard = "";

        for (int i = 0; i < Math.Min(retrievedPage.Length, LB_PAGE_SIZE); i++)
        {
            UserInfo user = retrievedPage[i];
            int placeVal = (page - 1) * LB_PAGE_SIZE + (i + 1);
            string place = placeVal switch
            {
                1 => "1. 🥇",
                2 => "2. 🥈",
                3 => "3. 🥉",
                _ => placeVal.ToString() + "."
            };
            leaderboard += $"{place} <@{user.UserId}> - Level {user.Level} - XP {user.TotalXp}\n";
        }

        return leaderboard.Trim();
    }

    public bool hasNextPage(int page)
    {
        return loadedLeaderboardPages > page;
    }
}
