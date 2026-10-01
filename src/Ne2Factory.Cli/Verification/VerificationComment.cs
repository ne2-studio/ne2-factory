using Ne2Factory.Cli.Agents;

namespace Ne2Factory.Cli.Verification;

// The comment the factory leaves on a pull request after verifying it. Each one
// carries a hidden marker with the commit it verified, so the pull request
// itself records which heads are already verified — no local state needed.
internal static class VerificationComment
{
    private static string Marker(string commit) => $"<!-- ne2-factory:verification head={commit} -->";

    public static bool Covers(PullRequest pr) => pr.Comments.Any(c => c.Contains(Marker(pr.HeadCommit), StringComparison.Ordinal));

    public static string ForVerdict(string commit, VerifierResult result, bool sessionLeftChanges) =>
        Format(
            $"## Verification: {result.Status.Trim().ToUpperInvariant()}",
            commit,
            string.IsNullOrWhiteSpace(result.Report) ? "(sin informe)" : result.Report.Trim(),
            sessionLeftChanges);

    public static string ForNoVerdict(string commit, bool sessionLeftChanges) =>
        Format(
            "## Verification: NO VERDICT",
            commit,
            "The verifier session ended without a result the factory could interpret. It won't be retried until the pull request gets new commits.",
            sessionLeftChanges);

    private static string Format(string heading, string commit, string content, bool sessionLeftChanges)
    {
        var warning = sessionLeftChanges
            ? "\n\n> **Warning:** the verifier left uncommitted changes in the working tree, which it must not do. The factory stashed them."
            : "";

        return $"""
            {heading}

            Commit `{commit}`, verified by the `verifier` agent. For audit only: nothing is decided from this result.{warning}

            {content}

            {Marker(commit)}
            """;
    }
}
