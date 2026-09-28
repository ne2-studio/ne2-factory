using Ne2Factory.Cli.Services;

namespace Ne2Factory.Cli.Tests;

internal sealed class FakeProcessRunner : IProcessRunner
{
    public List<(string Exe, IReadOnlyList<string> Args, string? Stdin)> CaptureCalls { get; } = [];

    public Func<string, IReadOnlyList<string>, string?, (string Stdout, string Stderr, int ExitCode)>? OnCapture { get; set; }

    public (string Stdout, string Stderr, int ExitCode) Capture(string exe, IReadOnlyList<string> args, string? stdin = null)
    {
        CaptureCalls.Add((exe, args, stdin));
        return OnCapture is null ? ("", "", 0) : OnCapture(exe, args, stdin);
    }

    public int RunInherited(string exe, IReadOnlyList<string> args, string? workingDirectory = null) =>
        throw new NotSupportedException($"{nameof(FakeProcessRunner)} no soporta {nameof(RunInherited)}.");
}
