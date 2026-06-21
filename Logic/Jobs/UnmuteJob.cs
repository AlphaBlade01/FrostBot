using FrostBot.Logic.Services;
using NetCord;
using NetCord.Gateway;
using NetCord.Rest;
using Quartz;

namespace FrostBot.Logic.Jobs;

public class UnmuteJob : IJob
{
    private readonly ModerationService _modService;
    private readonly GatewayClient _client;
    private readonly LogService _logService;

    public UnmuteJob(ModerationService modService, GatewayClient client, LogService logService)
    {
        _modService = modService;
        _client = client;
        _logService = logService;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var data = context.MergedJobDataMap;
        bool s1 = ulong.TryParse(data.GetString("UserId"), out ulong userId);
        bool s2 = ulong.TryParse(data.GetString("GuildId"), out ulong guildId);

        if (s1 == false || s2 == false) return;
        
        try
        {
            RestGuild guild = await _client.Rest.GetGuildAsync(guildId);
            GuildUser user = await guild.GetUserAsync(userId);
            await _modService.UnmuteAsync(user);
            await user.ModifyAsync(u => u.TimeOutUntil = null);
        } catch (Exception ex)
        {
            await _logService.SendLogErrorEmbed(guildId, ex.Message, "Error while carrying out unmute job");
        }
    }
}

