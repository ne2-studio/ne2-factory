using Ne2Factory.Cli.Common;

namespace Ne2Factory.Cli.Backlog;

// Thrown by IBacklog when the backing store fails (network down, `gh` not
// authenticated, rate-limited, ...).
public sealed class BacklogException(ApplicationError error) : Exception(error.Message)
{
    public ApplicationError Error { get; } = error;
}
