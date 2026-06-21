using FrostBot.Logic.Library;
using FrostBot.Logic.Services;
using NetCord.Gateway;
using NetCord.Hosting.Gateway;
using NetCord.Rest;

namespace FrostBot.EventHandlers;

public class UserLeaveHandler : IGuildUserRemoveGatewayHandler
{
    private readonly LogService _logService;

    public UserLeaveHandler(LogService logService)
    {
        _logService = logService;
    }

    public async ValueTask HandleAsync(GuildUserRemoveEventArgs arg)
    {
        var logEmbed = new EmbedProperties()
            .WithTitle("User left")
            .WithDescription(arg.User.ToString())
            .WithFooter(new() { Text = arg.User.ToString() })
            .WithTimestamp(DateTimeOffset.UtcNow)
            .WithColor(BotColours.User);
        await _logService.SendLogEmbed(arg.GuildId, logEmbed);
    }
}
