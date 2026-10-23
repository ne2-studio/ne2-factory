using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Ne2Factory.Cli.GapScout;

namespace Ne2Factory.Cli.ControlPanel.Endpoints;

// Target: one of the configured scopes, or "all".
public sealed record ScanRequest(string Target, bool Yolo);

internal static class GapScoutEndpoints
{
    public static void MapGapScoutEndpoints(this IEndpointRouteBuilder api)
    {
        var gapScout = api.MapGroup("/gap-scout");

        gapScout.MapGet("/scopes", (GapScoutScans scans) => scans.Scopes);

        gapScout.MapPost("/scans", (ScanRequest request, GapScoutScans scans) =>
        {
            var queued = scans.Enqueue(request.Target, request.Yolo);
            return queued.IsSuccess
                ? Results.Ok(queued.Value)
                : ControlPanelApp.Error(queued.Error.Message, StatusCodes.Status400BadRequest);
        });
    }
}
