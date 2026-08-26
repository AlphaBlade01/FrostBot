using FrostBot.Logic.Services;
using Microsoft.Extensions.Logging;
using Quartz;

namespace FrostBot.Logic.Jobs;

public class YoutubeSubscriptionJob : IJob
{
    private const int RETRY_MINUTES = 5;

    private readonly NotificationService _notificationService;
    private readonly ILogger<YoutubeSubscriptionJob> _logger;

    public YoutubeSubscriptionJob(NotificationService notificationService, ILogger<YoutubeSubscriptionJob> logger)
    {
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            await _notificationService.SubscribeToYoutubeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "YouTube subscription failed; retrying in {RetryMinutes} minutes.", RETRY_MINUTES);

            // Reschedule this trigger to retry soon, then keep the normal daily renewal cadence
            var retryTrigger = TriggerBuilder.Create()
                .WithIdentity(context.Trigger.Key)
                .ForJob(context.JobDetail)
                .StartAt(DateTimeOffset.UtcNow.AddMinutes(RETRY_MINUTES))
                .WithSimpleSchedule(x => x.WithIntervalInHours(24).RepeatForever())
                .Build();
            await context.Scheduler.RescheduleJob(context.Trigger.Key, retryTrigger);
            throw;
        }
    }
}
