using FrostBot.Logic.Services;
using NetCord;
using NetCord.Gateway;
using Quartz;

namespace FrostBot.Logic.Jobs;

public class UnbanJob : IJob
{
    private readonly GatewayClient _gatewayClient;
    private readonly ModerationService _modService;
    private readonly LogService _logService;

    public UnbanJob(GatewayClient gatewayClient, ModerationService modService, LogService logService)
    {
        _gatewayClient = gatewayClient;
        _modService = modService;
        _logService = logService;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var data = context.MergedJobDataMap;
        var s1 = ulong.TryParse(data.GetString("UserId"), out ulong userId);
        var s2 = ulong.TryParse(data.GetString("GuildId"), out ulong guildId);

        if (s1 == false || s2 == false) return;

        try
        {
            User user = await _gatewayClient.Rest.GetUserAsync(userId);
            await _modService.UnbanAsync(guildId, user, reason: "Ban duration ended.");
            await _gatewayClient.Rest.UnbanGuildUserAsync(guildId, userId);
        }
        catch (Exception ex)
        {
            await _logService.SendLogErrorEmbed(guildId, ex.Message, "Error while carrying out unban job");
        }
    }
}
