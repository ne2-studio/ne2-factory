namespace Ne2Factory.Cli.Services;

public interface IProcessRunner
{
    // Captures stdout (trimmed callers decide); stderr is inherited so real
    // failures are still visible on the console, matching bash `$(cmd)` which
    // only captures stdout.
    (string Stdout, string Stderr, int ExitCode) Capture(string exe, IReadOnlyList<string> args, string? stdin = null);

    // Runs a process with all standard streams inherited from this one, e.g.
    // `claude ...` or `git pull` where output should show up live.
    int RunInherited(string exe, IReadOnlyList<string> args, string? workingDirectory = null);
}
