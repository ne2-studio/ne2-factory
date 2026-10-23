using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Ne2Factory.Cli.Backlog;

namespace Ne2Factory.Cli.ControlPanel.Endpoints;

public sealed record BacklogEntry(int Number, string Title);

// Error is set when that list couldn't be read; the other sections still are.
public sealed record BacklogSection(TicketState State, IReadOnlyList<BacklogEntry> Items, string? Error);

internal static class BacklogEndpoints
{
    public static void MapBacklogEndpoints(this IEndpointRouteBuilder api)
    {
        // Same lists as `backlog list`, fetched in parallel: each one is a `gh` call
        // with the GitHub backlog.
        api.MapGet("/backlog", (IBacklog backlog) => Task.WhenAll(
            Section(TicketState.Unrefined, backlog.ListUnrefined),
            Section(TicketState.Refined, backlog.ListRefined),
            Section(TicketState.MissingData, backlog.ListMissingData),
            Section(TicketState.InReview, backlog.ListInReview),
            Section(TicketState.Done, backlog.ListDone),
            Section(TicketState.Failed, backlog.ListFailed)));

        api.MapPost("/backlog/{number:int}/requeue", (int number, IBacklog backlog) =>
        {
            try
            {
                backlog.Requeue(number);
                return Results.NoContent();
            }
            catch (BacklogException ex)
            {
                return ControlPanelApp.Error($"No se pudo reencolar #{number}: {ex.Message}", StatusCodes.Status502BadGateway);
            }
        });
    }

    private static Task<BacklogSection> Section(TicketState state, Func<IReadOnlyList<BacklogItem>> list) =>
        Task.Run(() =>
        {
            try
            {
                return new BacklogSection(state, list().Select(i => new BacklogEntry(i.Number, i.Title)).ToArray(), null);
            }
            catch (BacklogException ex)
            {
                return new BacklogSection(state, [], ex.Message);
            }
        });
}
