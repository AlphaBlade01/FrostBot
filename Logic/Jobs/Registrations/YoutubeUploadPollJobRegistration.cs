using Quartz;

namespace FrostBot.Logic.Jobs.Registrations;

public class YoutubeUploadPollJobRegistration : IJobRegistration
{
    public Task RegisterAsync(IScheduler scheduler, CancellationToken cancellationToken = default)
    {
        // Poll every 2 minutes for new uploads: the Data API's uploads playlist (1 quota unit)
        // plus the channel feed, which is the fallback and the Shorts marker.
        var job = JobBuilder.Create<YoutubeUploadPollJob>()
            .WithIdentity("youtube-upload-poll", "youtube")
            .StoreDurably()
            .Build();

        var trigger = TriggerBuilder.Create()
            .WithIdentity("youtube-upload-poll-trigger", "youtube")
            .StartNow()
            .WithSimpleSchedule(x => x.WithIntervalInMinutes(2).RepeatForever())
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
