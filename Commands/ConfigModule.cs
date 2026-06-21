using FrostBot.Data;
using FrostBot.Data.Models;
using FrostBot.Logic.Library;
using FrostBot.Logic.Services;
using Microsoft.EntityFrameworkCore;
using NetCord;
using NetCord.Rest;
using NetCord.Services;
using NetCord.Services.ApplicationCommands;
using System.Reflection;

namespace FrostBot.Commands;


[RequireContext<ApplicationCommandContext>(RequiredContext.Guild)]
public class ConfigModule : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly IDbContextFactory<BotDbContext> _dbContextFactory;
    private readonly LogService _logService;

    public ConfigModule(IDbContextFactory<BotDbContext> dbContextFactory, LogService logService)
    {
        _dbContextFactory = dbContextFactory;
        _logService = logService;
    }

    private async Task LogError(string title, string exMessage)
    {
        await _logService.SendLogErrorEmbed(Context.Guild.Id, exMessage, title);
        var embed = DiscordInteractions.CreateFailEmbed("Error occured.");
        await Context.Interaction.SendResponseAsync(InteractionCallback.Message(new() { Embeds = [embed] }));
    }

    private async Task LogAction(string description, string? footer = null)
    {
        var embed = new EmbedProperties()
            .WithDescription(description)
            .WithColor(BotColours.Utility)
            .WithTimestamp(DateTimeOffset.UtcNow)
            .WithFooter(new() { Text = footer });
        await _logService.SendLogEmbed(Context.Guild.Id, embed);
    }

    [SlashCommand("setlogchannel", "Set the log channel", DefaultGuildPermissions = Permissions.Administrator)]
    public async Task SetLogChannel(Channel? channel = null)
    {
        try
        {
            await using BotDbContext db = await _dbContextFactory.CreateDbContextAsync();
            GuildConfig? config = await db.GuildConfigs.FirstOrDefaultAsync(c => c.GuildId == Context.Guild.Id);

            if (config != null)
            {
                config.LogChannelId = channel?.Id;
                db.GuildConfigs.Update(config);
            }
            else
            {
                config = new GuildConfig
                {
                    GuildId = Context.Guild.Id,
                    LogChannelId = channel?.Id
                };
                await db.GuildConfigs.AddAsync(config);
            }

            await db.SaveChangesAsync();
            EmbedProperties embed = DiscordInteractions.CreateSuccessEmbed($"Log channel set to {channel}");
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(new() { Embeds = [embed] }));
            await LogAction($"Log channel set to {channel}", $"Id: {channel.Id.ToString()}");
        }
        catch (Exception ex)
        {
            await LogError("Error while setting log channel", ex.Message);
        }
    }

    [SlashCommand("setautorole", "Set the role which is automatically given to a user upon joining.", DefaultGuildPermissions = Permissions.Administrator)]
    public async Task SetAutoRole(Role? role = null)
    {
        try
        {
            await using BotDbContext dbContext = await _dbContextFactory.CreateDbContextAsync();
            GuildConfig? config = await dbContext.GuildConfigs.FirstOrDefaultAsync(c => c.GuildId == Context.Guild.Id);

            if (config != null)
            {
                config.AutoRoleId = role?.Id;
                dbContext.GuildConfigs.Update(config);
            } else
            {
                config = new GuildConfig()
                {
                    GuildId = Context.Guild.Id,
                    AutoRoleId = role?.Id
                };
                await dbContext.GuildConfigs.AddAsync(config);
            }

            await dbContext.SaveChangesAsync();
            EmbedProperties embed = DiscordInteractions.CreateSuccessEmbed($"Auto role set to {role}");
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(new() { Embeds = [embed] }));
            await LogAction($"Auto role set to {role}", role != null ? $"Id: {role.Id}" : null);

        } catch (Exception ex)
        {
            await LogError("Error while setting auto role", ex.Message);
        }
    }

    [SlashCommand("setwelcomechannel", "Set channel where welcome messages will be sent.", DefaultGuildPermissions = Permissions.Administrator)]
    public async Task SetWelcomeChannel(Channel? channel = null)
    {
        try
        {
            await using BotDbContext dbContext = await _dbContextFactory.CreateDbContextAsync();
            GuildConfig? config = await dbContext.GuildConfigs.FirstOrDefaultAsync(c => c.GuildId == Context.Guild.Id);

            if (config != null)
            {
                config.WelcomeChannelId = channel?.Id;
                dbContext.GuildConfigs.Update(config);
            } else
            {
                config = new GuildConfig()
                {
                    GuildId = Context.Guild.Id,
                    WelcomeChannelId = channel?.Id
                };
                await dbContext.GuildConfigs.AddAsync(config);
            }

            await dbContext.SaveChangesAsync();
            EmbedProperties embed = DiscordInteractions.CreateSuccessEmbed($"Welcome channel set to {channel}");
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(new() { Embeds = [embed] }));
            await LogAction($"Welcome channel set to {channel}", channel != null ? $"Id: {channel.Id}" : null);
        }
        catch (Exception ex)
        {
            await LogError("Error while setting welcome channel", ex.Message);
        }
    }

    [SlashCommand("setwelcomemessage", "Set welcome message. Use $user as the variable to refer to the user.", DefaultGuildPermissions = Permissions.Administrator)]
    public async Task SetWelcomeMessage(string? message = null)
    {
        try
        {
            await using BotDbContext dbContext = await _dbContextFactory.CreateDbContextAsync();
            GuildConfig? config = await dbContext.GuildConfigs.FirstOrDefaultAsync(c => c.GuildId == Context.Guild.Id);

            if (config != null)
            {
                config.WelcomeMessage = message;
                dbContext.GuildConfigs.Update(config);
            }
            else
            {
                config = new GuildConfig()
                {
                    GuildId = Context.Guild.Id,
                    WelcomeMessage = message
                };
                await dbContext.GuildConfigs.AddAsync(config);
            }

            await dbContext.SaveChangesAsync();
            EmbedProperties embed = DiscordInteractions.CreateSuccessEmbed($"Welcome message set to `{message ?? "default"}`");
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(new() { Embeds = [embed] }));
            await LogAction($"Welcome message set to `{message ?? "default"}`");
        }
        catch (Exception ex)
        {
            await LogError("Error while setting welcome message", ex.Message);
        }
    }

    [SlashCommand("setlevelupmessage", "Set the message sent when a user levels up. Variables: $user, $level", DefaultGuildPermissions = Permissions.Administrator)]
    public async Task SetLevelUpMessage(string? message = null)
    {
        try
        {
            await using BotDbContext dbContext = await _dbContextFactory.CreateDbContextAsync();
            GuildConfig? config = await dbContext.GuildConfigs.FirstOrDefaultAsync(c => c.GuildId == Context.Guild.Id);
            if (config == null)
            {
                config = new()
                {
                    GuildId = Context.Guild.Id,
                    LevelUpMessage = message
                };
                await dbContext.GuildConfigs.AddAsync(config);
            } else
            {
                config.LevelUpMessage = message;
                dbContext.GuildConfigs.Update(config);
            }

            await dbContext.SaveChangesAsync();
            var embed = DiscordInteractions.CreateSuccessEmbed("Level up message set to: " + message ?? "default");
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(new() { Embeds = [embed] }));
            await LogAction($"Set level up message: {message ?? "default"}");
        } catch (Exception ex)
        {
            await LogError("Error while setting level up message", ex.Message);
        }
    }

    [SlashCommand("setlevelchannel", "Set the channel to which level up messages are sent. (Empty = same channel as message)",
        DefaultGuildPermissions = Permissions.Administrator)]
    public async Task SetLevelChannel(Channel? channel = null)
    {
        try
        {
            await using BotDbContext dbContext = await _dbContextFactory.CreateDbContextAsync();
            GuildConfig? config = await dbContext.GuildConfigs.FirstOrDefaultAsync(c => c.GuildId == Context.Guild.Id);

            if (config == null)
            {
                config = new()
                {
                    GuildId = Context.Guild.Id,
                    LevelUpChannelId = channel?.Id
                };
                await dbContext.GuildConfigs.AddAsync(config);
            }
            else
            {
                config.LevelUpChannelId = channel?.Id;
                dbContext.GuildConfigs.Update(config);
            }

            await dbContext.SaveChangesAsync();
            var embed = DiscordInteractions.CreateSuccessEmbed($"Level channel set to {channel}");
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(new() { Embeds = [embed] }));
            await LogAction($"Set level channel to {channel}", channel != null ? $"Id: {channel.Id}" : null);
        } catch (Exception ex)
        {
            await LogError("Error while setting level channel", ex.Message);
        }
    }

    [SlashCommand("configurechannel", "Configure channels with certain bot-specific settings",  DefaultGuildPermissions = Permissions.Administrator)]
    public async Task ConfigureChannel(Channel channel,
        [SlashCommandParameter(Description = "Whether xp should be disabled for this channel.")] bool? disableXp = null)
    {
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
            ChannelConfig? retrievedConfig = await dbContext.ChannelConfigs.FirstOrDefaultAsync(c => c.ChannelId == channel.Id);
            bool exists = retrievedConfig != null;

            ChannelConfig config = retrievedConfig ?? new ChannelConfig() { ChannelId = channel.Id, GuildId = Context.Guild.Id };
            if (disableXp != null) config.IsXpDisabled = (bool)disableXp;

            if (!exists) await dbContext.ChannelConfigs.AddAsync(config);
            await dbContext.SaveChangesAsync();

            var embed = DiscordInteractions.CreateSuccessEmbed($"Successfully updated channel configurations for {channel}.");
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(new() { Embeds = [embed] }));
            await LogAction($"Successfully updated channel configurations for {channel}", $"Id: {channel.Id}");
        } catch (Exception ex)
        {
            await LogError("Error while configuring channel", ex.Message);
        }
    }

    [SlashCommand("viewchannelconfig", "View channel configurations", DefaultGuildPermissions = Permissions.Administrator)]
    public async Task ViewChannelConfig(Channel channel)
    {
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
            ChannelConfig? retrievedConfig = await dbContext.ChannelConfigs.FirstOrDefaultAsync(c => c.ChannelId == channel.Id);
            string body = "";

            // Add each field to body
            if (retrievedConfig != null)
            {
                PropertyInfo[] properties = retrievedConfig.GetType().GetProperties();
                foreach (PropertyInfo property in properties)
                {
                    string name = property.Name;
                    if (name == "ChannelId" || name == "GuildId") continue;
                    body += $"`{name}`: {property.GetValue(retrievedConfig)}\n";
                }
                body = body.Trim();
            } else
            {
                body = "This channel has not been configured yet.";
            }

            var embed = new EmbedProperties()
                .WithTitle($"Channel config for {channel}")
                .WithDescription(body)
                .WithFooter(new() { Text = $"Channel Id: {channel.Id}" });
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(new() { Embeds = [embed] }));

        } catch (Exception ex)
        {
            await LogError("Error while viewing channel configuration", ex.Message);
        }
    }

    [SlashCommand("setlevelreward", "Set the role rewarded upon reaching a certain level (no role = remove role reward)")]
    public async Task SetLevelReward(int level, Role? role = null)
    {
        try
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
            EmbedProperties embed;

            // Remove role reward if no role 
            if (role == null)
            {
                await dbContext.RoleRewards.Where(r => r.LevelRequired == level).ExecuteDeleteAsync();
                embed = DiscordInteractions.CreateSuccessEmbed($"Removed role reward for level **{level}**");
                await Context.Interaction.SendResponseAsync(InteractionCallback.Message(new() { Embeds = [embed] }));
                return;
            }

            // Add / Modify role reward if role specified
            RoleReward? retrievedRecord = await dbContext.RoleRewards.FirstOrDefaultAsync(r => r.RoleId == role.Id);

            if (retrievedRecord == null)
            {
                RoleReward newReward = new() { RoleId = role.Id, LevelRequired = level };
                await dbContext.RoleRewards.AddAsync(newReward);
            } else
            {
                retrievedRecord.LevelRequired = level;
            }
            await dbContext.SaveChangesAsync();

            embed = DiscordInteractions.CreateSuccessEmbed($"Role {role} set to be rewarded at level **{level}**.");
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(new() { Embeds = [embed] }));
            await LogAction($"Set role {role} to be rewarded at level {level}", $"Id: {role.Id}");
        } catch (Exception ex)
        {
            await LogError("Error while setting level reward", ex.Message);
        }
    }
}
