namespace Ne2Factory.Cli.Backlog;

// A ticket's state as the factory understands it. Computed from whatever the
// concrete IBacklog implementation uses internally (GitHub labels, for
// GithubIssuesBacklog) — the factory domain never sees labels directly.
internal enum TicketState
{
    Unrefined,
    Refined,
    Failed,
    Done,
    Unknown,
}
