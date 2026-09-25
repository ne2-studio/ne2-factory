using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Ne2Factory.Cli.Services;

// Thin wrapper around external process execution (git, gh, claude) — the .NET
// equivalent of the bash scripts shelling out directly.
internal sealed class ProcessRunner(ILogger<ProcessRunner> logger) : IProcessRunner
{
    public (string Stdout, string Stderr, int ExitCode) Capture(string exe, IReadOnlyList<string> args, string? stdin = null)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        logger.LogDebug("Ejecutando (captura): {Exe} {Args}", exe, string.Join(' ', args));

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

    public int RunInherited(string exe, IReadOnlyList<string> args, string? workingDirectory = null)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            WorkingDirectory = workingDirectory ?? "",
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        logger.LogDebug("Ejecutando (heredado): {Exe} {Args}", exe, string.Join(' ', args));

        using var process = Process.Start(psi)!;
        process.WaitForExit();
        return process.ExitCode;
    }
}
