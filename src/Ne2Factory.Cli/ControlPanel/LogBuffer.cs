using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace Ne2Factory.Cli.ControlPanel;

public sealed record LogEntry(long Seq, DateTimeOffset Timestamp, string Level, string Message);

// The last log lines, so the control panel can show what `ne2-factory run` would
// print to its terminal. Clients poll with the last Seq they saw.
internal sealed class LogBuffer : ILogEventSink
{
    private const int Capacity = 1000;

    private static readonly MessageTemplateTextFormatter Formatter = new("{Message:lj}{NewLine}{Exception}");

    private readonly object gate = new();
    private readonly Queue<LogEntry> entries = new();
    private long lastSeq;

    public void Emit(LogEvent logEvent)
    {
        using var writer = new StringWriter();
        Formatter.Format(logEvent, writer);
        var message = writer.ToString().TrimEnd();

        lock (gate)
        {
            entries.Enqueue(new LogEntry(++lastSeq, logEvent.Timestamp, logEvent.Level.ToString(), message));
            if (entries.Count > Capacity) entries.Dequeue();
        }
    }

    public IReadOnlyList<LogEntry> After(long seq)
    {
        lock (gate) return entries.Where(e => e.Seq > seq).ToArray();
    }
}
