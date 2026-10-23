using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Ne2Factory.Cli.ControlPanel.Endpoints;

public sealed record ProjectInfo(string Root, string BacklogProvider, int PollIntervalSeconds);

internal static class ProjectEndpoints
{
    public static void MapProjectEndpoints(this IEndpointRouteBuilder api) =>
        api.MapGet("/project", (RootDirectory root, ProjectContext ctx) =>
            new ProjectInfo(root.Path, ctx.BacklogProvider, ctx.BacklogPollIntervalSeconds));
}
