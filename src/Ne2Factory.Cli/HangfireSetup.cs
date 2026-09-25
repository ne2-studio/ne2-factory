using Hangfire;
using Hangfire.Storage.SQLite;

namespace Ne2Factory.Cli;

// Shared Hangfire wiring: `backlog run --yolo` hosts the server, `gap-scout scan`
// only needs the storage configured to enqueue jobs into the same SQLite file.
internal static class HangfireSetup
{
    public static string ConfigureStorage(ProjectContext ctx)
    {
        Directory.CreateDirectory(ctx.BacklogDir);
        var dbPath = Path.Combine(ctx.BacklogDir, "gap-scout.db");
        GlobalConfiguration.Configuration.UseSQLiteStorage(dbPath);
        return dbPath;
    }
}
