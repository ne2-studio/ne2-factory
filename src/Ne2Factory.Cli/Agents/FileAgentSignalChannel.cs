namespace Ne2Factory.Cli.Agents;

// Backs the signal with a plain-text file (`status=...` / `reason=...` lines)
// at ProjectContext.SignalFile — the file a skill like work-ticket writes to
// report how it ended.
internal sealed class FileAgentSignalChannel(ProjectContext ctx) : IAgentSignalChannel
{
    public void Reset()
    {
        if (File.Exists(ctx.SignalFile)) File.Delete(ctx.SignalFile);
    }

    public AgentSignal? Read()
    {
        if (!File.Exists(ctx.SignalFile)) return null;

        var lines = File.ReadAllLines(ctx.SignalFile);
        var status = lines.FirstOrDefault(l => l.StartsWith("status="))?["status=".Length..];
        var reason = lines.FirstOrDefault(l => l.StartsWith("reason="))?["reason=".Length..];

        File.Delete(ctx.SignalFile);
        return new AgentSignal(status, reason);
    }
}
