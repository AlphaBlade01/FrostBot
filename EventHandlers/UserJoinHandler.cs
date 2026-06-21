using FrostBot.Data;
using FrostBot.Data.Models;
using FrostBot.Logic.Library;
using FrostBot.Logic.Services;
using Microsoft.EntityFrameworkCore;
using NetCord;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using NetCord.Rest;

namespace FrostBot.Events;

public class UserJoinHandler : IGuildUserAddGatewayHandler
{
    private const string DEFAULT_WELCOME_MESSAGE_TEMPLATE = "Welcome to **$servername** $user\nWe hope you enjoy your stay here!";
    private const string WELCOME_MESSAGE_IMAGE_URL = "https://media.discordapp.net/attachments/1507788439045668994/1517671543088480326/New_Project.png?ex=6a391b84&is=6a37ca04&hm=960adb6d83ca3a012578e6b26fc0fb4be530503050bd24a2cff516d7ed51a3c8&=&format=webp&quality=lossless&width=967&height=967";
    private readonly IDbContextFactory<BotDbContext> _dbContextFactory;
    private readonly GatewayClient _client;
    private readonly LogService _logService;

    public UserJoinHandler(IDbContextFactory<BotDbContext> dbContextFactory, GatewayClient client, LogService logService)
    {
        _dbContextFactory = dbContextFactory;
        _client = client;
        _logService = logService;
    }

    private async Task ApplyAutoRole(GuildUser user)
    {
        await using BotDbContext dbContext = await _dbContextFactory.CreateDbContextAsync();
        ulong? autoRoleId = await dbContext.GuildConfigs
            .Where(gc => gc.GuildId == user.GuildId)
            .Select(gc => gc.AutoRoleId)
            .FirstOrDefaultAsync();

        if (autoRoleId.HasValue)
            await user.AddRoleAsync(autoRoleId.Value);
    }

    private async Task SendWelcomeMessage(GuildUser user)
    {
        await using BotDbContext dbContext = await _dbContextFactory.CreateDbContextAsync();
        GuildConfig? config = await dbContext.GuildConfigs.FirstOrDefaultAsync(c => c.GuildId == user.GuildId);
        if (config == null) return;

        // Send welcome message to welcome channel
        RestGuild guild = await _client.Rest.GetGuildAsync(user.GuildId);
        var welcomeMessage = (config.WelcomeMessage ?? DEFAULT_WELCOME_MESSAGE_TEMPLATE)
            .Replace("$user", user.ToString())
            .Replace("$servername", guild.Name.ToString());
        
        if (config.WelcomeChannelId.HasValue)
        {
            var welcomeEmbed = new EmbedProperties()
                .WithDescription(welcomeMessage)
                .WithThumbnail(new EmbedThumbnailProperties((user.GetAvatarUrl() ?? user.DefaultAvatarUrl).ToString()))
                .WithImage(new EmbedImageProperties(WELCOME_MESSAGE_IMAGE_URL))
                .WithColor(BotColours.Primary);
            await _client.Rest.SendMessageAsync((ulong)config.WelcomeChannelId, new()
            {
                Embeds = [welcomeEmbed]
            });
        }

        // Log user joining
        EmbedProperties logEmbed = DiscordInteractions.CreateLogEmbed("User joined", user.ToString(), user.Username,
            (user.GetAvatarUrl() ?? user.DefaultAvatarUrl).ToString(), [$"Id: {user.Id}"], BotColours.User);
        await _logService.SendLogEmbed(user.GuildId, logEmbed);
    }

    public async ValueTask HandleAsync(GuildUser user)
    {
        try
        {
            await Task.WhenAll(
                ApplyAutoRole(user),
                SendWelcomeMessage(user)
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to assign auto role to user {user.Id}: {ex.Message}");
        }
    }
}
