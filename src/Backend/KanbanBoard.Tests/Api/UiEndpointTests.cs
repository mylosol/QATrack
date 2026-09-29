using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KanbanBoard.Api.Models;
using KanbanBoard.Tests.Infrastructure;

namespace KanbanBoard.Tests.Api;

/// <summary>
/// Integration tests for the unauthenticated browser API (/api/ui), the SPA
/// fallback, OpenAPI endpoints and the hardening middleware.
/// </summary>
public sealed class UiEndpointTests : IClassFixture<KanbanApiFactory>
{
    private readonly KanbanApiFactory _factory;

    public UiEndpointTests(KanbanApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Host_UsesTheConfiguredTestDatabase()
    {
        var response = await _factory.CreateUiClient().GetAsync("/api/ui/board");
        response.EnsureSuccessStatusCode();
        Assert.True(File.Exists(_factory.DatabasePath));
    }

    [Fact]
    public async Task GetBoard_ReturnsColumnsWithWipLimits()
    {
        var board = await _factory.CreateUiClient().GetFromJsonAsync<BoardDto>("/api/ui/board", KanbanApiFactory.Json);

        Assert.NotNull(board);
        Assert.Equal(new int?[] { null, 5, 5, null }, board!.Columns.Select(c => c.WipLimit));
    }

    [Fact]
    public async Task Create_WithoutAntiForgeryHeader_IsForbidden()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/ui/workitems",
            new { title = "csrf", type = "Bug" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Create_WithAgentCredentials_IsRedirectedToV1()
    {
        var client = _factory.CreateUiClient();
        client.DefaultRequestHeaders.Add("X-Agent-Identity", "Sneaky-Bot");

        var response = await client.PostAsJsonAsync("/api/ui/workitems", new { title = "bypass", type = "Bug" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("/api/v1", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CreateMoveAndComment_AsHuman_RecordsHumanHistory()
    {
        var client = _factory.CreateUiClient("Quinn QA");

        var create = await client.PostAsJsonAsync("/api/ui/workitems",
            new { title = "UI created", type = "Feature", severity = "2 - High" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<WorkItemDto>(KanbanApiFactory.Json);
        Assert.NotNull(create.Headers.Location);

        var move = await client.PatchAsJsonAsync($"/api/ui/workitems/{created!.Id}", new { state = "Active" });
        move.EnsureSuccessStatusCode();

        var comment = await client.PostAsJsonAsync($"/api/ui/workitems/{created.Id}/comments", new { text = "looks good" });
        Assert.Equal(HttpStatusCode.Created, comment.StatusCode);

        var item = await client.GetFromJsonAsync<WorkItemDto>($"/api/ui/workitems/{created.Id}", KanbanApiFactory.Json);
        Assert.Equal(WorkItemState.Active, item!.State);
        Assert.False(item.AiModified);
        Assert.Equal("Quinn QA", item.LastModifiedBy);
        Assert.Equal(3, item.History!.Count);
        Assert.All(item.History, h => Assert.False(h.IsAiAction));
    }

    [Fact]
    public async Task InvalidPayloads_Return400ProblemDetails()
    {
        var client = _factory.CreateUiClient();

        var missingType = await client.PostAsJsonAsync("/api/ui/workitems", new { title = "no type" });
        var badEnum = await client.PostAsJsonAsync("/api/ui/workitems", new { title = "x", type = "Spaceship" });
        var badSeverity = await client.PostAsJsonAsync("/api/ui/workitems", new { title = "x", type = "Bug", severity = "Huge" });
        var tooLong = await client.PostAsJsonAsync("/api/ui/workitems", new { title = new string('t', 256), type = "Bug" });

        foreach (var response in new[] { missingType, badEnum, badSeverity, tooLong })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }
    }

    [Fact]
    public async Task UnknownWorkItem_Returns404ProblemDetails()
    {
        var response = await _factory.CreateUiClient().GetAsync("/api/ui/workitems/424242");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UnknownApiRoute_Returns404_NotSpaShell()
    {
        var response = await _factory.CreateClient().GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("<html", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Responses_CarrySecurityHeaders()
    {
        var response = await _factory.CreateClient().GetAsync("/api/ui/board");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.DoesNotContain("unsafe-inline", response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task OpenApiJson_IsServed_AndOnlyDescribesV1()
    {
        var response = await _factory.CreateClient().GetAsync("/api/openapi.json");
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.StartsWith("3.0", doc.RootElement.GetProperty("openapi").GetString());
        var paths = doc.RootElement.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Contains("/api/v1/workitems", paths);
        Assert.Contains("/api/v1/workitems/{id}", paths);
        Assert.Contains("/api/v1/workitems/{id}/comments", paths);
        Assert.Contains("/api/v1/board", paths);
        Assert.DoesNotContain(paths, p => p.StartsWith("/api/ui", StringComparison.Ordinal));

        var operationIds = doc.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(p => p.Value.EnumerateObject())
            .Select(op => op.Value.GetProperty("operationId").GetString())
            .ToList();
        Assert.Contains("listWorkItems", operationIds);
        Assert.Contains("createWorkItem", operationIds);
        Assert.Contains("updateWorkItem", operationIds);
        Assert.Contains("addWorkItemComment", operationIds);
        Assert.Contains("getBoard", operationIds);
    }

    [Fact]
    public async Task SwaggerUi_IsServedAtApiDocs()
    {
        var response = await _factory.CreateClient().GetAsync("/api/docs/index.html");
        response.EnsureSuccessStatusCode();
        Assert.Contains("swagger", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }
}
