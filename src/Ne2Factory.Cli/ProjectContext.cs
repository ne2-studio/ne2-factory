using System.Text.RegularExpressions;

namespace Ne2Factory.Cli;

// Resolves the repo being worked on (not the factory bundle) and its shell-only
// config file, mirroring what bin/backlog and bin/gap-scout used to do at the
// top of the bash scripts.
internal sealed class ProjectContext
{
    public string RootDir { get; }
    public string BacklogDir { get; }
    public string SignalFile { get; }
    public string TmuxSession { get; } // kept only as the label for GAP_SCOUT job dedup logging; tmux itself is gone
    public int BacklogPollIntervalSeconds { get; }
    public string[] GapScoutScopes { get; }

    private ProjectContext(string rootDir, Dictionary<string, string> env)
    {
        RootDir = rootDir;
        BacklogDir = Path.Combine(rootDir, ".backlog");
        SignalFile = Path.Combine(BacklogDir, ".signal");
        TmuxSession = Get(env, "TMUX_SESSION", "backlog");
        BacklogPollIntervalSeconds = int.TryParse(Get(env, "BACKLOG_POLL_INTERVAL", "30"), out var v) ? v : 30;
        GapScoutScopes = Get(env, "GAP_SCOUT_SCOPES", "app api admin")
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    }

    public static ProjectContext Resolve()
    {
        var rootDir = Environment.GetEnvironmentVariable("NE2_FACTORY_ROOT");
        if (string.IsNullOrWhiteSpace(rootDir))
        {
            var (stdout, _, exit) = Proc.Capture("git", ["rev-parse", "--show-toplevel"]);
            if (exit != 0)
            {
                Console.Error.WriteLine("No es un repositorio git (o git no está disponible).");
                Environment.Exit(1);
            }
            rootDir = stdout.Trim();
        }

        Directory.SetCurrentDirectory(rootDir);

        var env = new Dictionary<string, string>();
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            env[(string)entry.Key] = (string?)entry.Value ?? "";

        var envFile = Path.Combine(rootDir, ".ne2-factory.env");
        if (File.Exists(envFile))
        {
            foreach (var (key, value) in ParseEnvFile(envFile))
                env[key] = value; // file overrides process environment, same as bash `source`
        }

        return new ProjectContext(rootDir, env);
    }

    private static string Get(Dictionary<string, string> env, string key, string fallback)
        => env.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value) ? value : fallback;

    private static IEnumerable<(string Key, string Value)> ParseEnvFile(string path)
    {
        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var match = Regex.Match(line, @"^([A-Za-z_][A-Za-z0-9_]*)=(.*)$");
            if (!match.Success) continue;

            var key = match.Groups[1].Value;
            var value = match.Groups[2].Value.Trim();
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                value = value[1..^1];
            else if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
                value = value[1..^1];

            yield return (key, value);
        }
    }
}
