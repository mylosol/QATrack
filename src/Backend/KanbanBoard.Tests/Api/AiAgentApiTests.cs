using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KanbanBoard.Api.Models;
using KanbanBoard.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;

namespace KanbanBoard.Tests.Api;

/// <summary>
/// End-to-end tests of the secured AI agent API (spec 4.1 / 4.2): key
/// enforcement, identity validation and automatic AI tagging.
/// </summary>
public sealed class AiAgentApiTests : IClassFixture<KanbanApiFactory>
{
    private readonly KanbanApiFactory _factory;

    public AiAgentApiTests(KanbanApiFactory factory)
    {
        _factory = factory;
    }

    private async Task<WorkItemDto> CreateAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/v1/workitems", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<WorkItemDto>(KanbanApiFactory.Json))!;
    }

    [Theory]
    [InlineData("/api/v1/workitems")]
    [InlineData("/api/v1/board")]
    [InlineData("/api/v1/workitems/1")]
    public async Task MissingApiKey_Returns401WithChallenge(string path)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Agent-Identity", "Bot");

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("X-API-Key", response.Headers.WwwAuthenticate.ToString());
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("wrong")]
    [InlineData("TEST-API-KEY-0123456789ABCDEF")]
    [InlineData("test-api-key-0123456789abcdef ")]
    public async Task WrongApiKey_Returns401(string key)
    {
        var client = _factory.CreateAgentClient(apiKey: key);
        var response = await client.GetAsync("/api/v1/workitems");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WrongKey_OnWrite_DoesNotPersistAnything()
    {
        var bad = _factory.CreateAgentClient(identity: "Intruder", apiKey: "nope-nope-nope-nope");
        var response = await bad.PostAsJsonAsync("/api/v1/workitems", new { title = "should not exist", type = "Bug" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var items = await _factory.CreateAgentClient()
            .GetFromJsonAsync<List<WorkItemDto>>("/api/v1/workitems", KanbanApiFactory.Json);
        Assert.DoesNotContain(items!, i => i.Title == "should not exist");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("-leading-dash")]
    public async Task InvalidAgentIdentity_Returns400(string? identity)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", KanbanApiFactory.TestApiKey);
        if (identity is not null)
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Agent-Identity", identity);
        }

        var response = await client.GetAsync("/api/v1/workitems");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("X-Agent-Identity", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OverlongAgentIdentity_Returns400()
    {
        var client = _factory.CreateAgentClient(identity: new string('a', 101));
        var response = await client.GetAsync("/api/v1/workitems");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_TagsItemAsAiModified_WithAgentIdentity()
    {
        var created = await CreateAsync(_factory.CreateAgentClient("Claude-Code-Agent-v1"),
            new { title = "Agent found a bug", type = "Bug", severity = "1 - Critical", priority = 1 });

        Assert.True(created.AiModified);
        Assert.Equal("Claude-Code-Agent-v1", created.AiAgentIdentity);
        Assert.Equal("Claude-Code-Agent-v1", created.LastModifiedBy);
        var entry = Assert.Single(created.History!);
        Assert.True(entry.IsAiAction);
        Assert.Equal("Claude-Code-Agent-v1", entry.AgentName);
    }

    [Fact]
    public async Task Patch_ByDifferentAgent_UpdatesIdentity_AndAppendsAiHistory()
    {
        var created = await CreateAsync(_factory.CreateAgentClient("Agent-A"),
            new { title = "Reassign me", type = "Task" });

        var patch = await _factory.CreateAgentClient("Codex-Fixer").PatchAsJsonAsync(
            $"/api/v1/workitems/{created.Id}",
            new { state = "Resolved", assignedTo = "Codex-Fixer", comment = "Fixed in commit abc123" });
        patch.EnsureSuccessStatusCode();
        var updated = await patch.Content.ReadFromJsonAsync<WorkItemDto>(KanbanApiFactory.Json);

        Assert.Equal(WorkItemState.Resolved, updated!.State);
        Assert.Equal("Codex-Fixer", updated.AiAgentIdentity);
        var last = updated.History![^1];
        Assert.True(last.IsAiAction);
        Assert.Equal("Codex-Fixer", last.AgentName);
        Assert.Equal("Fixed in commit abc123", last.Comment);
        Assert.Equal("Resolved", last.ChangedFields["State"].New);
    }

    [Fact]
    public async Task Comment_ByAgent_OnHumanItem_FlagsItAsAiModified()
    {
        var ui = _factory.CreateUiClient("Human Tester");
        var create = await ui.PostAsJsonAsync("/api/ui/workitems", new { title = "Human made", type = "Bug" });
        var human = (await create.Content.ReadFromJsonAsync<WorkItemDto>(KanbanApiFactory.Json))!;
        Assert.False(human.AiModified);

        var agent = _factory.CreateAgentClient("Test-Runner-Bot");
        var comment = await agent.PostAsJsonAsync($"/api/v1/workitems/{human.Id}/comments",
            new { text = "```\n42 passed, 0 failed\n```" });
        Assert.Equal(HttpStatusCode.Created, comment.StatusCode);

        var item = await agent.GetFromJsonAsync<WorkItemDto>($"/api/v1/workitems/{human.Id}", KanbanApiFactory.Json);
        Assert.True(item!.AiModified);
        Assert.Equal("Test-Runner-Bot", item.AiAgentIdentity);
        Assert.Equal(2, item.History!.Count);
        Assert.False(item.History[0].IsAiAction);
        Assert.True(item.History[1].IsAiAction);
    }

    [Fact]
    public async Task List_SupportsAllSpecFilters()
    {
        var agent = _factory.CreateAgentClient("Filter-Bot");
        await CreateAsync(agent, new { title = "filter-bug", type = "Bug", assignedTo = "filter-owner" });

        var byAll = await agent.GetFromJsonAsync<List<WorkItemDto>>(
            "/api/v1/workitems?type=Bug&state=New&aiModified=true&assignedTo=filter-owner", KanbanApiFactory.Json);

        var item = Assert.Single(byAll!);
        Assert.Equal("filter-bug", item.Title);
        Assert.Null(item.History);
    }

    [Fact]
    public async Task InvalidFilterValue_Returns400()
    {
        var response = await _factory.CreateAgentClient().GetAsync("/api/v1/workitems?type=Spaceship");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Board_ReturnsColumnsAndGroupedItems()
    {
        var agent = _factory.CreateAgentClient("Board-Bot");
        await CreateAsync(agent, new { title = "on the board", type = "Feature", state = "Active" });

        var board = await agent.GetFromJsonAsync<BoardDto>("/api/v1/board", KanbanApiFactory.Json);

        var active = board!.Columns.Single(c => c.State == WorkItemState.Active);
        Assert.Equal(5, active.WipLimit);
        Assert.Contains(active.Items, i => i.Title == "on the board" && i.AiModified);
    }

    [Fact]
    public async Task OpenApi_DeclaresBothRequiredHeaders()
    {
        var json = await _factory.CreateClient().GetStringAsync("/api/openapi.json");
        using var doc = JsonDocument.Parse(json);

        var schemes = doc.RootElement.GetProperty("components").GetProperty("securitySchemes");
        Assert.Equal("X-API-Key", schemes.GetProperty("ApiKey").GetProperty("name").GetString());
        Assert.Equal("X-Agent-Identity", schemes.GetProperty("AgentIdentity").GetProperty("name").GetString());
        var requirement = doc.RootElement.GetProperty("security")[0];
        Assert.True(requirement.TryGetProperty("ApiKey", out _));
        Assert.True(requirement.TryGetProperty("AgentIdentity", out _));
    }

    [Fact]
    public async Task OpenApiAndDocs_DoNotRequireApiKey()
    {
        var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/openapi.json")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/docs/index.html")).StatusCode);
    }
}

/// <summary>When no API key is configured, /api/v1 must fail closed while the UI keeps working.</summary>
public sealed class UnconfiguredApiKeyTests : IClassFixture<UnconfiguredApiKeyTests.NoKeyFactory>
{
    public sealed class NoKeyFactory : KanbanApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("AiAgentApi:ApiKey", "short");
        }
    }

    private readonly NoKeyFactory _factory;

    public UnconfiguredApiKeyTests(NoKeyFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task V1_Returns503_EvenWithMatchingShortKey()
    {
        var response = await _factory.CreateAgentClient(apiKey: "short").GetAsync("/api/v1/workitems");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task UiBoard_StillWorks()
    {
        var response = await _factory.CreateUiClient().GetAsync("/api/ui/board");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
