using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Ne2Factory.Cli.Backlog;
using Ne2Factory.Cli.Common;
using Ne2Factory.Cli.ControlPanel;
using Ne2Factory.Cli.FactoryWorker;
using Ne2Factory.Cli.Services;

namespace Ne2Factory.Cli.Tests;

// The control panel's API, mounted on a test server over the real factory
// composition, with the backlog, AgentRuns and external processes faked.
public sealed class ControlPanelApiTests : IAsyncLifetime
{
    private readonly string _rootDir = Directory.CreateTempSubdirectory("ne2-factory-panel-").FullName;
    private readonly InMemoryBacklog _backlog = new();
    private readonly InMemoryAgentRunRepository _runs = new();
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        File.WriteAllText(Path.Combine(_rootDir, ".ne2-factory.env"), "GAP_SCOUT_SCOPES=\"app api\"\nBACKLOG_POLL_INTERVAL=1\n");

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = _rootDir });
        builder.WebHost.UseTestServer();
        builder.AddFactory(_rootDir);
        builder.Services.AddSingleton<IBacklog>(_backlog);
        builder.Services.AddSingleton<IAgentRunRepository>(_runs);
        builder.Services.AddSingleton<IProcessRunner>(new FakeProcessRunner());
        builder.Services.AddSingleton(new LogBuffer());
        builder.Services.AddControlPanel();

        _app = builder.Build();
        _app.UseControlPanel();
        await _app.StartAsync();

        _client = _app.GetTestClient();
        _client.DefaultRequestHeaders.Add(ControlPanelApp.RequestHeader, "1");
    }

    public async Task DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        Directory.Delete(_rootDir, recursive: true);
    }

    private static BacklogItem Item(int number, TicketState state) =>
        new(number, $"Ticket {number}", null, null, state, []);

    [Fact]
    public async Task Backlog_ListsEverySectionWithItsTickets()
    {
        _backlog.Add(Item(1, TicketState.Unrefined));
        _backlog.Add(Item(2, TicketState.Failed));
        _backlog.Add(Item(3, TicketState.Failed));

        var sections = await _client.GetFromJsonAsync<JsonElement>("/api/backlog");

        Assert.Equal(
            ["Unrefined", "Refined", "MissingData", "InReview", "Done", "Failed"],
            sections.EnumerateArray().Select(s => s.GetProperty("state").GetString()));
        var failed = sections.EnumerateArray().Single(s => s.GetProperty("state").GetString() == "Failed");
        Assert.Equal([2, 3], failed.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("number").GetInt32()));
    }

    [Fact]
    public async Task Backlog_ReportsASectionThatFailed_WithoutFailingTheOthers()
    {
        _backlog.Add(Item(1, TicketState.Unrefined));
        _backlog.ListFailedFailure = ApplicationError.ExternalDependencyUnavailable("gh no disponible");

        var sections = (await _client.GetFromJsonAsync<JsonElement>("/api/backlog")).EnumerateArray().ToArray();

        var failed = sections.Single(s => s.GetProperty("state").GetString() == "Failed");
        Assert.Equal("gh no disponible", failed.GetProperty("error").GetString());
        var unrefined = sections.Single(s => s.GetProperty("state").GetString() == "Unrefined");
        Assert.Equal(1, unrefined.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Requeue_MovesTheTicketBackIntoTheQueue()
    {
        _backlog.Add(Item(7, TicketState.Failed));

        var response = await _client.PostAsync("/api/backlog/7/requeue", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(TicketState.Unrefined, _backlog.Get(7)!.State);
    }

    [Fact]
    public async Task Requeue_ReportsTheBacklogError()
    {
        _backlog.Add(Item(7, TicketState.Failed));
        _backlog.RequeueFailure = ApplicationError.ExternalDependencyUnavailable("gh no disponible");

        var response = await _client.PostAsync("/api/backlog/7/requeue", null);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("gh no disponible", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Writes_AreForbidden_WithoutThePanelHeader()
    {
        _backlog.Add(Item(7, TicketState.Failed));
        using var client = _app.GetTestClient();

        var response = await client.PostAsync("/api/backlog/7/requeue", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(TicketState.Failed, _backlog.Get(7)!.State);
    }

    [Fact]
    public async Task Runs_ListsTheMostRecentFirst_UpToTheLimit()
    {
        var now = DateTime.UtcNow;
        foreach (var (issue, minutesAgo) in new[] { (1, 30), (2, 10), (3, 20) })
            _runs.Add(new AgentRun { Id = Guid.NewGuid(), IssueNumber = issue, AgentName = "implementer", QueuedAt = now.AddMinutes(-minutesAgo) });

        var runs = await _client.GetFromJsonAsync<JsonElement>("/api/runs?limit=2");

        Assert.Equal([2, 3], runs.EnumerateArray().Select(r => r.GetProperty("issueNumber").GetInt32()));
        Assert.Equal("Queued", runs[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task Runs_RejectsAnOutOfRangeLimit()
    {
        var response = await _client.GetAsync("/api/runs?limit=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GapScout_ListsTheConfiguredScopes_AndRejectsAnyOther()
    {
        Assert.Equal(["app", "api"], (await _client.GetFromJsonAsync<string[]>("/api/gap-scout/scopes"))!);

        var response = await _client.PostAsJsonAsync("/api/gap-scout/scans", new { target = "admin", yolo = false });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("admin", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Worker_StartsAndStopsOnDemand()
    {
        var initial = await _client.GetFromJsonAsync<JsonElement>("/api/worker");
        Assert.Equal("Stopped", initial.GetProperty("state").GetString());

        var started = await _client.PostAsync("/api/worker/start", null);
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        Assert.Equal("Running", (await started.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("state").GetString());

        var again = await _client.PostAsync("/api/worker/start", null);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        await _client.PostAsync("/api/worker/stop", null);
        await _app.Services.GetRequiredService<WorkerSupervisor>().StopAsync();
        var stopped = await _client.GetFromJsonAsync<JsonElement>("/api/worker");
        Assert.Equal("Stopped", stopped.GetProperty("state").GetString());
    }
}
