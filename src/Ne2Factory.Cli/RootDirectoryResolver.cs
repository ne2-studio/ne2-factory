using System.Diagnostics;

namespace Ne2Factory.Cli;

// Resolves the repo being worked on (not the factory bundle) before the host is
// built: the working directory and the `.ne2-factory.env` path both have to be
// known ahead of `IConfiguration`, so this step can't live behind DI.
internal static class RootDirectoryResolver
{
    public static string Resolve()
    {
        var rootDir = Environment.GetEnvironmentVariable("NE2_FACTORY_ROOT");
        if (!string.IsNullOrWhiteSpace(rootDir))
            return rootDir;

        var psi = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("rev-parse");
        psi.ArgumentList.Add("--show-toplevel");

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            Console.Error.WriteLine("No es un repositorio git (o git no está disponible).");
            Environment.Exit(1);
        }

        return stdout.Trim();
    }
}
