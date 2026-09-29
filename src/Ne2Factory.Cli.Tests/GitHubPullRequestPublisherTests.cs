using Microsoft.Extensions.Logging.Abstractions;
using Ne2Factory.Cli.FactoryWorker.Publishing;

namespace Ne2Factory.Cli.Tests;

public class GitHubPullRequestPublisherTests
{
    private static readonly ChangeProposal Proposal = new(42, "factory/issue-42", "main", "Arreglar login", "cambié X");

    private readonly FakeProcessRunner _proc = new();
    private readonly FakeGit _git = new();
    private readonly GitHubPullRequestPublisher _sut;

    public GitHubPullRequestPublisherTests()
    {
        _sut = new GitHubPullRequestPublisher(_git, new GitHub(_proc, NullLogger<GitHub>.Instance));
    }

    [Fact]
    public void Publish_PushesBranchAndCreatesPullRequestThatClosesTheIssue()
    {
        _proc.OnCapture = (_, args, _) => args[1] == "list"
            ? ("[]", "", 0)
            : ("https://github.com/o/r/pull/7\n", "", 0);

        var result = _sut.Publish(Proposal);

        Assert.Equal("Pull request: https://github.com/o/r/pull/7", result.Value);
        Assert.Contains("push factory/issue-42", _git.Calls);
        var create = _proc.CaptureCalls.Single(c => c.Args[1] == "create").Args;
        Assert.Equal(
            ["pr", "create", "--head", "factory/issue-42", "--base", "main", "--title", "Arreglar login", "--body", "cambié X\n\nCloses #42"],
            create);
    }

    [Fact]
    public void Publish_ReusesOpenPullRequest_WhenTicketBranchAlreadyHasOne()
    {
        _proc.OnCapture = (_, _, _) => ("""[{"url":"https://github.com/o/r/pull/7"}]""", "", 0);

        var result = _sut.Publish(Proposal);

        Assert.Equal("Pull request: https://github.com/o/r/pull/7", result.Value);
        Assert.DoesNotContain(_proc.CaptureCalls, c => c.Args[1] == "create");
    }

    [Fact]
    public void Publish_Fails_WhenPullRequestCreationFails()
    {
        _proc.OnCapture = (_, args, _) => args[1] == "list" ? ("[]", "", 0) : ("", "no permission", 1);

        var result = _sut.Publish(Proposal);

        Assert.True(result.IsFailure);
        Assert.Equal("no permission", result.Error.Message);
    }
}
