using FrostBot.Logic.Services;
using Quartz;

namespace FrostBot.Logic.Jobs;

// Upload notifications are driven entirely by polling YouTube on a fixed schedule - the Data
// API's uploads playlist, with the channel's RSS feed as fallback and Shorts marker (see
// NotificationService). PollForRecentUploadsAsync handles and logs its own
// errors; the next tick (2 minutes away) retries naturally.
//
// Non-concurrent so a slow tick (HTTP timeouts, Discord rate limits) can never overlap the next
// one on the 1 GB instance - the poll's checkpoint makes a skipped tick harmless.
[DisallowConcurrentExecution]
public class YoutubeUploadPollJob : IJob
{
    private readonly NotificationService _notificationService;

    public YoutubeUploadPollJob(NotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public Task Execute(IJobExecutionContext context)
        => _notificationService.PollForRecentUploadsAsync(context.CancellationToken);
}
