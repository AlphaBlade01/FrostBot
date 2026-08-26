using Quartz;

namespace FrostBot.Logic.Jobs.Registrations;

/// <summary>
/// Describes a scheduled job and its trigger(s). Program.cs discovers every
/// implementation automatically, so adding a new recurring job requires no
/// changes to Program.cs — just implement this interface.
/// </summary>
public interface IJobRegistration
{
    Task RegisterAsync(IScheduler scheduler, CancellationToken cancellationToken = default);
}
