using Microsoft.Extensions.Logging.Abstractions;
using Ne2Factory.Cli.Verification;

namespace Ne2Factory.Cli.Tests;

public class GitHubPullRequestsTests
{
    private readonly FakeProcessRunner _proc = new();
    private readonly GitHubPullRequests _sut;

    public GitHubPullRequestsTests()
    {
        _sut = new GitHubPullRequests(new GitHub(_proc, NullLogger<GitHub>.Instance));
    }

    [Fact]
    public void Get_MapsGhPullRequestOntoDomain()
    {
        _proc.OnCapture = (_, _, _) => ("""
            {"number":57,"title":"Arreglar login","body":"cambié X","url":"https://github.com/o/r/pull/57",
             "state":"OPEN","isDraft":false,"headRefName":"factory/issue-42","headRefOid":"abc123",
             "baseRefName":"main","comments":[{"author":{"login":"pedro"},"createdAt":"2026-09-29T10:00:00Z","body":"hola"}]}
            """, "", 0);

        var pr = _sut.Get(57).Value;

        Assert.Equal(
            new PullRequest(57, "Arreglar login", "cambié X", "https://github.com/o/r/pull/57", true, false, "factory/issue-42", "abc123", "main", pr.Comments),
            pr);
        Assert.Equal(["hola"], pr.Comments);
        Assert.Equal(["pr", "view", "57", "--json", "number,title,body,url,state,isDraft,headRefName,headRefOid,baseRefName,comments"], _proc.CaptureCalls.Single().Args);
    }

    [Fact]
    public void Get_IsNotOpen_WhenMerged()
    {
        _proc.OnCapture = (_, _, _) => ("""
            {"number":57,"title":"t","body":null,"url":"u","state":"MERGED","isDraft":false,
             "headRefName":"factory/issue-42","headRefOid":"abc123","baseRefName":"main","comments":[]}
            """, "", 0);

        Assert.False(_sut.Get(57).Value.IsOpen);
    }

    [Fact]
    public void ListOpen_Fails_WhenGhFails()
    {
        _proc.OnCapture = (_, _, _) => ("", "not authenticated", 1);

        var result = _sut.ListOpen();

        Assert.True(result.IsFailure);
        Assert.Equal("not authenticated", result.Error.Message);
    }

    [Fact]
    public void Comment_CommentsOnThePullRequest()
    {
        _sut.Comment(57, "veredicto");

        Assert.Equal(["pr", "comment", "57", "--body", "veredicto"], _proc.CaptureCalls.Single().Args);
    }
}
