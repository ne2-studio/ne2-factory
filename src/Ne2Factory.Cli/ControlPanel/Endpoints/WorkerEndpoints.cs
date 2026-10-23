using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Ne2Factory.Cli.ControlPanel.Endpoints;

public sealed record OtherWorker(string Name, DateTime? Heartbeat);

public sealed record WorkerStatus(WorkerState State, DateTime? StartedAt, IReadOnlyList<OtherWorker> OtherWorkers);

internal static class WorkerEndpoints
{
    public static void MapWorkerEndpoints(this IEndpointRouteBuilder api)
    {
        var worker = api.MapGroup("/worker");

        worker.MapGet("/", Status);

        worker.MapPost("/start", (WorkerSupervisor supervisor) =>
        {
            if (supervisor.State != WorkerState.Stopped)
                return ControlPanelApp.Error("El worker ya está en marcha o parándose.", StatusCodes.Status409Conflict);

            var others = supervisor.OtherWorkers();
            if (others.Count > 0)
                return ControlPanelApp.Error(
                    $"Ya hay otro worker vivo para este repo ({string.Join(", ", others.Select(o => o.Name))}), p. ej. `ne2-factory run` en una terminal. Páralo antes.",
                    StatusCodes.Status409Conflict);

            supervisor.Start();
            return Results.Ok(Status(supervisor));
        });

        // Returns at once: the worker stays Stopping until a job already running finishes.
        worker.MapPost("/stop", (WorkerSupervisor supervisor) =>
        {
            _ = supervisor.StopAsync();
            return Status(supervisor);
        });

        worker.MapGet("/logs", (LogBuffer logs, long after = 0) => logs.After(after));
    }

    private static WorkerStatus Status(WorkerSupervisor supervisor) =>
        new(supervisor.State,
            supervisor.StartedAt,
            supervisor.OtherWorkers().Select(s => new OtherWorker(s.Name, s.Heartbeat)).ToArray());
}
