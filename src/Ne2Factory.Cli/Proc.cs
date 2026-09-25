using System.Diagnostics;

namespace Ne2Factory.Cli;

// Thin wrapper around external process execution (git, gh, claude) — the .NET
// equivalent of the bash scripts shelling out directly.
internal static class Proc
{
    // Captures stdout (trimmed callers decide); stderr is inherited so real
    // failures are still visible on the console, matching bash `$(cmd)` which
    // only captures stdout.
    public static (string Stdout, string Stderr, int ExitCode) Capture(string exe, IReadOnlyList<string> args, string? stdin = null)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var process = Process.Start(psi)!;
        if (stdin is not null)
        {
            process.StandardInput.Write(stdin);
            process.StandardInput.Close();
        }
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (stdout, stderr, process.ExitCode);
    }

    // Runs a process with all standard streams inherited from this one, e.g.
    // `claude ...` or `git pull` where output should show up live.
    public static int RunInherited(string exe, IReadOnlyList<string> args, string? workingDirectory = null)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            WorkingDirectory = workingDirectory ?? "",
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var process = Process.Start(psi)!;
        process.WaitForExit();
        return process.ExitCode;
    }
}
