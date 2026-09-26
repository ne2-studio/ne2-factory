using Microsoft.Extensions.Configuration;

namespace Ne2Factory.Cli;

// Project-scoped settings, read from IConfiguration (process env vars, then
// `.ne2-factory.env`, which overrides them — see Configuration/EnvFileConfigurationSource).
internal sealed class ProjectContext(RootDirectory rootDirectory, IConfiguration configuration)
{
    public string RootDir { get; } = rootDirectory.Path;
    public string BacklogDir { get; } = Path.Combine(rootDirectory.Path, ".backlog");
    // Shared SQLite file: Hangfire's own tables (job/state/server) plus our
    // AgentRuns table live side by side in it. Filename kept from when it only
    // backed gap-scout scans.
    public string FactoryDbPath { get; } = Path.Combine(rootDirectory.Path, ".backlog", "gap-scout.db");

    // Kept only as the label for GAP_SCOUT job dedup logging; tmux itself is gone.
    public string TmuxSession { get; } = configuration["TMUX_SESSION"] ?? "backlog";

    public int BacklogPollIntervalSeconds { get; } =
        int.TryParse(configuration["BACKLOG_POLL_INTERVAL"], out var v) ? v : 30;

    public string[] GapScoutScopes { get; } =
        (configuration["GAP_SCOUT_SCOPES"] ?? "app api admin")
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
}
