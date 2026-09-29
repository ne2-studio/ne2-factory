namespace Ne2Factory.Cli.Backlog;

// A ticket's state as the factory understands it. Computed from whatever the
// concrete IBacklog implementation uses internally (GitHub labels, for
// GithubIssuesBacklog) — the factory domain never sees labels directly.
public enum TicketState
{
    Unrefined,
    Refined,
    MissingData,
    Failed,
    Done,
    Unknown,
}
