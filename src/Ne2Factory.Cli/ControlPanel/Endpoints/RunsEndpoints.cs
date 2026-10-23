using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Ne2Factory.Cli.FactoryWorker;

namespace Ne2Factory.Cli.ControlPanel.Endpoints;

internal static class RunsEndpoints
{
    private const int MaxLimit = 500;

    public static void MapRunsEndpoints(this IEndpointRouteBuilder api) =>
        api.MapGet("/runs", (IAgentRunRepository runs, int limit = 20) =>
            limit is > 0 and <= MaxLimit
                ? Results.Ok(runs.ListRecent(limit))
                : ControlPanelApp.Error($"limit debe estar entre 1 y {MaxLimit}.", StatusCodes.Status400BadRequest));
}
