using Quartz;

namespace FrostBot.Logic.Jobs.Registrations;

public class YoutubeSubscriptionJobRegistration : IJobRegistration
{
    public Task RegisterAsync(IScheduler scheduler, CancellationToken cancellationToken = default)
    {
        // Renew the YouTube hub subscription daily. The hub can grant a lease as
        // short as 5 days, so renewing every 24h guarantees the subscription never
        // lapses between renewals.
        var job = JobBuilder.Create<YoutubeSubscriptionJob>()
            .WithIdentity("youtube-subscription", "youtube")
            .StoreDurably()
            .Build();

        var trigger = TriggerBuilder.Create()
            .WithIdentity("youtube-subscription-trigger", "youtube")
            .StartNow()
            .WithSimpleSchedule(x => x.WithIntervalInHours(24).RepeatForever())
            .Build();

        // ScheduleJobs with replace: true upserts both the job and its trigger
        // (RescheduleJob alone silently does nothing when the trigger does not
        // already exist in the store).
        return scheduler.ScheduleJobs(new Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>>
        {
            [job] = [trigger]
        }, replace: true, cancellationToken);
    }
}
