using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Ne2Factory.Cli.ControlPanel;

internal static class ControlPanelCommand
{
    private const int DefaultPort = 5180;

    private const string Usage = """
        Usage: ne2-factory ui [--port <n>]   (from the root of the repo being worked on)

        Serves the factory's web control panel on http://localhost:<port> (default
        5180), reachable from this machine only. Everything the other subcommands
        do, from the browser:
          Worker      start/stop the worker (what `ne2-factory run` runs) and follow
                        its log.
          Backlog     the lists of `backlog list`; requeue a ticket.
          Runs        recent AgentRuns, as `runs`.
          Gap-scout   queue scans, as `gap-scout scan`.

        The worker starts stopped. It refuses to start while another one is alive
        for this repo (e.g. `ne2-factory run` in a terminal), since both would share
        the working tree. Stopping it lets a job already running (an agent session)
        finish first. Ctrl-C stops the panel and the worker with it.

        Same caveat as `ne2-factory run`: every ticket runs headless via
        `claude --print --dangerously-skip-permissions`.
        """;

    public static async Task<int> RunAsync(string rootDir, string[] args)
    {
        var port = DefaultPort;
        switch (args)
        {
            case ["-h"] or ["--help"]:
                Console.WriteLine(Usage);
                return 0;
            case []:
                break;
            case ["--port", var value] when int.TryParse(value, out port) && port is > 0 and <= 65535:
                break;
            default:
                Console.Error.WriteLine("Uso: ne2-factory ui [--port <n>]");
                return 1;
        }

        var logs = new LogBuffer();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = rootDir,
            // Ships next to the binary, not in the repo being worked on (the content root).
            WebRootPath = Path.Combine(AppContext.BaseDirectory, "ControlPanel", "wwwroot"),
        });
        builder.AddFactory(rootDir, logging => logging
            // Per-request logs would bury the worker's own under the panel's polling.
            .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
            .WriteTo.Sink(logs));
        builder.Services.AddSingleton(logs);
        builder.Services.AddControlPanel();

        builder.WebHost.UseUrls($"http://localhost:{port}");
        // Rejects any other Host header, so a DNS-rebound site can't reach the panel either.
        builder.Configuration.AddInMemoryCollection([new("AllowedHosts", "localhost;127.0.0.1;[::1]")]);

        await using var app = builder.Build();
        app.UseControlPanel();

        await app.StartAsync();
        Console.WriteLine($"Panel de control en http://localhost:{port} — Ctrl-C para parar.");
        await app.WaitForShutdownAsync();
        return 0;
    }
}
