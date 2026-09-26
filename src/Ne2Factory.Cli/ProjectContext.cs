using Microsoft.Extensions.Configuration;

namespace Ne2Factory.Cli;

// Project-scoped settings, read from IConfiguration (process env vars, then
// `.ne2-factory.env`, which overrides them, then `appsettings.json` — see
// Configuration/EnvFileConfigurationSource and Program.cs).
internal sealed class ProjectContext(RootDirectory rootDirectory, IConfiguration configuration)
{
    public string RootDir { get; } = rootDirectory.Path;
    public string DataDir { get; } = DataDirFor(rootDirectory.Path);
    public string BacklogDir { get; } = Path.Combine(DataDirFor(rootDirectory.Path), "backlog");
    // Shared SQLite file: Hangfire's own tables (job/state/server) plus our
    // AgentRuns table live side by side in it.
    public string FactoryDbPath { get; } = Path.Combine(DataDirFor(rootDirectory.Path), "database.db");

    // Which IBacklog implementation to use: "GitHub" (default) or "File".
    // Set via the "Backlog:Provider" key in appsettings.json.
    public string BacklogProvider { get; } = configuration["Backlog:Provider"] ?? "GitHub";

    // Kept only as the label for GAP_SCOUT job dedup logging; tmux itself is gone.
    public string TmuxSession { get; } = configuration["TMUX_SESSION"] ?? "backlog";

    public int BacklogPollIntervalSeconds { get; } =
        int.TryParse(configuration["BACKLOG_POLL_INTERVAL"], out var v) ? v : 30;

    public string[] GapScoutScopes { get; } =
        (configuration["GAP_SCOUT_SCOPES"] ?? "app api admin")
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    // Static so Program.cs can compute the same path before the DI container
    // (and thus this class) exists, e.g. to wire up Hangfire's storage.
    public static string DataDirFor(string rootDir) => Path.Combine(rootDir, ".ne2-factory");
}
