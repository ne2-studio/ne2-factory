namespace Ne2Factory.Cli.Backlog;

internal sealed record BacklogItem(
    int Number,
    string Title,
    string? Body,
    string? Url,
    TicketState State,
    IReadOnlyList<string> Comments);
