using Ne2Factory.Cli.Backlog;

namespace Ne2Factory.Cli.FactoryWorker;

internal static class PromptTemplates
{
    public static string WorkTicket(BacklogItem item) => Fill(Load("work-ticket"), item);

    public static string RefineTicket(BacklogItem item) => Fill(Load("refine-ticket"), item);

    internal static string Load(string name)
    {
        var resourceName = $"{name}.md";
        using var stream = typeof(PromptTemplates).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Recurso de prompt embebido no encontrado: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string Fill(string template, BacklogItem item)
    {
        var comments = string.Join("\n", item.Comments);
        var reference = item.Url is null ? $"#{item.Number}" : $"#{item.Number} ({item.Url})";

        return template
            .Replace("{{TICKET_REFERENCE}}", reference)
            .Replace("{{TITLE}}", item.Title)
            .Replace("{{BODY}}", item.Body ?? "")
            .Replace("{{COMMENTS}}", comments);
    }
}
