using Microsoft.Extensions.Logging;
using Quartz;
using Quartz.Impl.Matchers;

namespace FrostBot.Logic.Jobs;

// Quartz persists job definitions, so renaming or deleting a job class leaves definitions behind
// that point at a type the assembly no longer contains. Quartz's trigger scan loads those types to
// fire them: the load throws, the scan aborts, and every other job - including the YouTube poll -
// silently stops firing until the scheduler recovers. Purge such definitions once at startup.
public static class QuartzJobStoreCleanup
{
    public static async Task RemoveUnloadableJobsAsync(IScheduler scheduler, ILogger logger, CancellationToken cancellationToken = default)
    {
        foreach (JobKey jobKey in await scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup(), cancellationToken))
        {
            try
            {
                await scheduler.GetJobDetail(jobKey, cancellationToken);
            }
            catch (Exception ex) when (IsMissingJobType(ex))
            {
                logger.LogWarning("Removing stale Quartz job {JobKey}: {Reason}", jobKey, ex.GetBaseException().Message);
                await scheduler.DeleteJob(jobKey, cancellationToken);
            }
        }

        // A trigger whose job definition is gone fails the same way. Deleting a job removes its
        // triggers, so this only catches leftovers from manual store edits.
        foreach (TriggerKey triggerKey in await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.AnyGroup(), cancellationToken))
        {
            try
            {
                await scheduler.GetTrigger(triggerKey, cancellationToken);
            }
            catch (Exception ex) when (IsMissingJobType(ex))
            {
                logger.LogWarning("Removing stale Quartz trigger {TriggerKey}: {Reason}", triggerKey, ex.GetBaseException().Message);
                await scheduler.UnscheduleJob(triggerKey, cancellationToken);
            }
        }
    }

    // Only a failed type resolution means the definition is dead. A database error must surface
    // instead of deleting a perfectly healthy job.
    private static bool IsMissingJobType(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is TypeLoadException or FileNotFoundException or FileLoadException or BadImageFormatException)
                return true;
        }

        return false;
    }
}
