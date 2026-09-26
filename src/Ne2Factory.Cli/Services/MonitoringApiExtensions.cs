using Hangfire.Common;
using Hangfire.Storage;

namespace Ne2Factory.Cli.Services;

internal static class MonitoringApiExtensions
{
    // Shared by any job type that needs to avoid double-enqueuing the same
    // logical work (GapScoutJobs by scope, AgentRunJobs by issue number).
    public static bool HasJob(this IMonitoringApi monitoring, Func<Job, bool> matches)
    {
        if (monitoring.EnqueuedJobs("default", 0, 1000).Any(j => matches(j.Value.Job))) return true;
        if (monitoring.ProcessingJobs(0, 1000).Any(j => matches(j.Value.Job))) return true;
        if (monitoring.ScheduledJobs(0, 1000).Any(j => matches(j.Value.Job))) return true;
        return false;
    }
}
