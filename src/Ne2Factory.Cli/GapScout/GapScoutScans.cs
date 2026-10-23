using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.Logging;
using Ne2Factory.Cli.Common;
using Ne2Factory.Cli.Services;

namespace Ne2Factory.Cli.GapScout;

// JobId is null when the scan was skipped: one for that scope was already queued or running.
public sealed record QueuedScan(string Scope, string? JobId);

// Queues gap-scout scans as background jobs, for `gap-scout scan` and the control panel alike.
internal sealed class GapScoutScans(
    IGitHub gitHub,
    ProjectContext ctx,
    IBackgroundJobClient backgroundJobs,
    JobStorage jobStorage,
    ILogger<GapScoutScans> logger)
{
    private const string Label = "gap-scout";

    public IReadOnlyList<string> Scopes => ctx.GapScoutScopes;

    // `target` is one of Scopes, or "all" for every one of them.
    public Result<IReadOnlyList<QueuedScan>> Enqueue(string target, bool yolo)
    {
        string[] scopes;
        if (target == "all")
            scopes = ctx.GapScoutScopes;
        else if (target.Length > 0 && ctx.GapScoutScopes.Contains(target))
            scopes = [target];
        else
            return Result.Failure<IReadOnlyList<QueuedScan>>(ApplicationError.Validation(
                $"Scope desconocido: '{target}' (scopes: {string.Join(' ', ctx.GapScoutScopes)})"));

        if (!gitHub.TryCreateLabel(Label, "5319E7", "Architecture initiative proposed by gap-scout, pending approval", out var error) && error is not null)
            logger.LogWarning("aviso: no se pudo crear la label '{Label}': {Error}", Label, error);

        var monitoring = jobStorage.GetMonitoringApi();
        var queued = new List<QueuedScan>();
        foreach (var scope in scopes)
        {
            var normalizedScope = scope.TrimEnd('/') + "/";
            var jobId = IsAlreadyQueued(monitoring, normalizedScope)
                ? null
                : backgroundJobs.Enqueue<GapScoutJobs>(job => job.RunScan(normalizedScope, yolo));
            queued.Add(new QueuedScan(normalizedScope, jobId));
        }

        return queued;
    }

    private static bool IsAlreadyQueued(IMonitoringApi monitoring, string scope) =>
        monitoring.HasJob(job => job.Type == typeof(GapScoutJobs) && job.Args.Count > 0 && (job.Args[0] as string) == scope);
}
