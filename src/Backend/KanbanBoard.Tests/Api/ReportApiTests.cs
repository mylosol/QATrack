using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using KanbanBoard.Api.Models;
using KanbanBoard.Api.Services;
using KanbanBoard.Tests.Infrastructure;
using KanbanBoard.Tests.Services;
using Microsoft.AspNetCore.Hosting;

namespace KanbanBoard.Tests.Api;

/// <summary>1.12.0: "Report an issue" from the programs under test (X-Reporter-Key).</summary>
public sealed class ReportApiTests : IClassFixture<KanbanApiFactory>
{
    private readonly KanbanApiFactory _factory;

    public ReportApiTests(KanbanApiFactory factory)
    {
        _factory = factory;
    }

    private async Task<WorkItemDto> GetCard(int id) =>
        (await _factory.CreateAgentClient().GetFromJsonAsync<WorkItemDto>($"/api/v1/workitems/{id}", KanbanApiFactory.Json))!;

    [Fact]
    public async Task Report_LandsInNew_AsAHumanReport_NotAI()
    {
        var response = await _factory.CreateReporterClient().PostAsJsonAsync("/api/report/issues", new
        {
            title = "Export button does nothing",
            description = "Click **Export** on the results page.",
            severity = "High",
            program = "proveout",
            programVersion = "2.4.1",
            reporter = "Jane Doe",
            tags = new[] { "export" },
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var receipt = (await response.Content.ReadFromJsonAsync<ReportReceiptDto>(KanbanApiFactory.Json))!;
        Assert.Equal("Jane Doe (in-app report)", receipt.ReportedBy);
        Assert.Equal("ProveOut", receipt.Program);
        Assert.Equal(new[] { "export", "in-app-report" }, receipt.Tags);

        var card = await GetCard(receipt.Id);
        Assert.Equal(WorkItemType.Bug, card.Type);
        Assert.Equal(WorkItemState.New, card.State);
        Assert.Equal("2 - High", card.Severity);
        Assert.Equal("2.4.1", card.ProgramVersion);
        Assert.False(card.AiModified);
        Assert.Null(card.AiAgentIdentity);
        Assert.Equal("Jane Doe (in-app report)", card.LastModifiedBy);
        var entry = Assert.Single(card.History!);
        Assert.False(entry.IsAiAction);
        Assert.Null(entry.AgentName);
        Assert.Equal("Jane Doe (in-app report)", entry.Author);
        Assert.Null(card.DiscussionStatus); // a report is not a comment awaiting an agent
    }

    [Fact]
    public async Task Report_WithoutAName_IsAuthoredByInAppReport_AndCanBeASuggestion()
    {
        var response = await _factory.CreateReporterClient().PostAsJsonAsync("/api/report/issues",
            new { title = "Please add dark mode", type = "Feature" });
        var receipt = (await response.Content.ReadFromJsonAsync<ReportReceiptDto>(KanbanApiFactory.Json))!;

        Assert.Equal(WorkItemType.Feature, receipt.Type);
        Assert.Equal("In-app report", receipt.ReportedBy);
    }

    [Fact]
    public async Task Report_WithEnvironment_AddsItAsACodeBlock()
    {
        var response = await _factory.CreateReporterClient().PostAsJsonAsync("/api/report/issues", new
        {
            title = "Crash on start",
            description = "It crashed.",
            environment = "OS: Windows 11 23H2\nBuild: 2.4.1+abc\n```\n**not markdown**",
        });
        var receipt = (await response.Content.ReadFromJsonAsync<ReportReceiptDto>(KanbanApiFactory.Json))!;

        var description = (await GetCard(receipt.Id)).Description!;
        Assert.StartsWith("It crashed.\n\n**Environment**\n\n````text\nOS: Windows 11 23H2", description);
        Assert.EndsWith("**not markdown**\n````", description);
    }

    [Fact]
    public async Task ScreenshotUpload_ThenReport_ShowsTheImage()
    {
        var reporter = _factory.CreateReporterClient();
        var bytes = ProgramTagAttachmentServiceTests.Png.Concat(Guid.NewGuid().ToByteArray()).ToArray();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        var upload = await reporter.PostAsync("/api/report/attachments", new MultipartFormDataContent { { file, "file", "screen.png" } });
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var image = (await upload.Content.ReadFromJsonAsync<AttachmentDto>(KanbanApiFactory.Json))!;

        var response = await reporter.PostAsJsonAsync("/api/report/issues", new { title = "Layout broken", description = $"See:\n\n{image.Markdown}" });
        var receipt = (await response.Content.ReadFromJsonAsync<ReportReceiptDto>(KanbanApiFactory.Json))!;

        Assert.Contains(image.Url, (await GetCard(receipt.Id)).Description);
    }

    [Theory]
    [InlineData("""{ "title": "x", "type": "Task" }""", "Bug or a Feature")]
    [InlineData("""{ "title": "x", "program": "NoSuchProgram" }""", "NoSuchProgram")]
    [InlineData("""{ "title": "x", "tags": ["1","2","3","4","5","6","7","8","9","10","11"] }""", "Tags")]
    [InlineData("""{ "description": "no title" }""", "Title")]
    public async Task InvalidReports_Are400(string json, string mentions)
    {
        var response = await _factory.CreateReporterClient().PostAsync("/api/report/issues",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(mentions, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-reporter-key-0123456789")]
    [InlineData(KanbanApiFactory.TestApiKey)] // the AI agent key is not a reporter key
    public async Task MissingOrWrongKey_Is401(string? key)
    {
        var client = key is null ? _factory.CreateClient() : _factory.CreateReporterClient(key);
        var response = await client.PostAsJsonAsync("/api/report/issues", new { title = "x" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AgentHeaders_AreRefused_SoAgentsCannotFileAsHumans()
    {
        var client = _factory.CreateReporterClient();
        client.DefaultRequestHeaders.Add("X-Agent-Identity", "Sneaky-Bot");
        var response = await client.PostAsJsonAsync("/api/report/issues", new { title = "x" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("/api/v1", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TheReporterKey_CannotReadOrChangeTheBoard()
    {
        var reporter = _factory.CreateReporterClient();
        Assert.NotEqual(HttpStatusCode.OK, (await reporter.GetAsync("/api/report/issues")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await reporter.GetAsync("/api/v1/workitems")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await reporter.PatchAsJsonAsync("/api/v1/workitems/1", new { state = "Closed" })).StatusCode);
    }

    [Fact]
    public async Task ReportingDocs_AreSeparate_AndUseOnlyTheReporterKey()
    {
        var client = _factory.CreateClient();
        using var report = JsonDocument.Parse(await client.GetStringAsync("/api/openapi-report.json"));
        var paths = report.RootElement.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(new[] { "/api/report/issues", "/api/report/attachments" }, paths);
        var schemes = report.RootElement.GetProperty("components").GetProperty("securitySchemes").EnumerateObject().Select(s => s.Name);
        Assert.Equal(new[] { "ReporterKey" }, schemes);

        using var agent = JsonDocument.Parse(await client.GetStringAsync("/api/openapi.json"));
        Assert.DoesNotContain(agent.RootElement.GetProperty("paths").EnumerateObject(), p => p.Name.StartsWith("/api/report"));
    }

    [Fact]
    public void ComposeDescription_FencesEnvironmentSafely()
    {
        Assert.Null(IssueReportService.ComposeDescription(null, null));
        Assert.Equal("only text", IssueReportService.ComposeDescription("only text", "  "));
        Assert.Equal("**Environment**\n\n```text\nOS: macOS 15\n```", IssueReportService.ComposeDescription(null, "OS: macOS 15\n"));
        Assert.Contains("`````text", IssueReportService.ComposeDescription("x", "a ```` b"));
    }
}

/// <summary>Reporting off, or misconfigured with the agent key: fail closed.</summary>
public sealed class ReportNotConfiguredTests
{
    private sealed class Factory : KanbanApiFactory
    {
        private readonly string _key;

        public Factory(string key) => _key = key;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("IssueReporting:ApiKey", _key);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(KanbanApiFactory.TestApiKey)]
    public async Task Reporting_Is503(string key)
    {
        using var factory = new Factory(key);
        var response = await factory.CreateReporterClient(key.Length > 0 ? key : "anything-0123456789").PostAsJsonAsync("/api/report/issues", new { title = "x" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}

/// <summary>A leaked reporter key can't flood the board.</summary>
public sealed class ReportRateLimitTests : IClassFixture<ReportRateLimitTests.Factory>
{
    public sealed class Factory : KanbanApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("IssueReporting:RequestsPerMinute", "3");
        }
    }

    private readonly Factory _factory;

    public ReportRateLimitTests(Factory factory) => _factory = factory;

    [Fact]
    public async Task TooManyReports_Are429_WithRetryAfter()
    {
        var reporter = _factory.CreateReporterClient();
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.Created, (await reporter.PostAsJsonAsync("/api/report/issues", new { title = $"r{i}" })).StatusCode);
        }

        var limited = await reporter.PostAsJsonAsync("/api/report/issues", new { title = "one too many" });
        Assert.Equal((HttpStatusCode)429, limited.StatusCode);
        Assert.Equal("60", limited.Headers.GetValues("Retry-After").Single());
        Assert.Contains("Too many reports", await limited.Content.ReadAsStringAsync());

        // The AI agent API is not affected by the reporter limit.
        Assert.Equal(HttpStatusCode.OK, (await _factory.CreateAgentClient().GetAsync("/api/v1/workitems")).StatusCode);
    }
}
