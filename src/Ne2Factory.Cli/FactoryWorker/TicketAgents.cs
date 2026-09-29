using Ne2Factory.Cli.Backlog;

namespace Ne2Factory.Cli.FactoryWorker;

// Single source of truth for which agent handles a ticket in each state.
internal static class TicketAgents
{
    public const string Work = "implementer";
    public const string Refine = "refiner";

    public static string? ForState(TicketState state) => state switch
    {
        TicketState.Refined => Work,
        TicketState.Unrefined => Refine,
        _ => null,
    };

    public static TicketState? ExpectedState(string agentName) => agentName switch
    {
        Work => TicketState.Refined,
        Refine => TicketState.Unrefined,
        _ => null,
    };
}
