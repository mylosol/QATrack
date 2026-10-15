using System.Net;
using System.Net.Http.Json;
using KanbanBoard.Api.Models;
using KanbanBoard.Tests.Infrastructure;

namespace KanbanBoard.Tests.Api;

/// <summary>1.17.0: the version a bug was fixed in.</summary>
public sealed class FixedInVersionApiTests : IClassFixture<KanbanApiFactory>
{
    private readonly KanbanApiFactory _factory;

    public FixedInVersionApiTests(KanbanApiFactory factory)
    {
        _factory = factory;
    }

    private async Task<WorkItemDto> Get(int id) =>
        (await _factory.CreateAgentClient().GetFromJsonAsync<WorkItemDto>($"/api/v1/workitems/{id}", KanbanApiFactory.Json))!;

    [Fact]
    public async Task AnAgent_ResolvesABug_WithTheVersionItIsFixedIn()
    {
        var created = (await (await _factory.CreateUiClient("Robert").PostAsJsonAsync("/api/ui/workitems",
                new { title = "Export does nothing", type = "Bug", programVersion = "2.4.1" }))
            .Content.ReadFromJsonAsync<WorkItemDto>(KanbanApiFactory.Json))!;
        Assert.Null(created.FixedInVersion);

        var response = await _factory.CreateAgentClient("Fixer-Bot").PatchAsJsonAsync($"/api/v1/workitems/{created.Id}",
            new { state = "Resolved", fixedInVersion = " 2.4.3 ", comment = "Fixed the export handler; ships in 2.4.3." });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var card = await Get(created.Id);
        Assert.Equal("2.4.3", card.FixedInVersion);
        Assert.Equal("2.4.1", card.ProgramVersion);
        Assert.Equal(WorkItemState.Resolved, card.State);
        var entry = card.History!.Last();
        Assert.True(entry.IsAiAction);
        Assert.Equal(new FieldChange(null, "2.4.3"), entry.ChangedFields["FixedInVersion"]);
        Assert.Equal("Fixed the export handler; ships in 2.4.3.", entry.Comment);

        // The board and list carry it.
        var listed = await _factory.CreateAgentClient().GetFromJsonAsync<List<WorkItemDto>>("/api/v1/workitems", KanbanApiFactory.Json);
        Assert.Equal("2.4.3", listed!.Single(w => w.Id == created.Id).FixedInVersion);
    }

    [Fact]
    public async Task EmptyStringClearsIt_AndOmittingItLeavesItAlone()
    {
        var agent = _factory.CreateAgentClient();
        var created = (await (await agent.PostAsJsonAsync("/api/v1/workitems",
                new { title = "Already fixed", type = "Bug", fixedInVersion = "3.0.0" }))
            .Content.ReadFromJsonAsync<WorkItemDto>(KanbanApiFactory.Json))!;
        Assert.Equal("3.0.0", created.FixedInVersion);

        await agent.PatchAsJsonAsync($"/api/v1/workitems/{created.Id}", new { priority = 1 });
        Assert.Equal("3.0.0", (await Get(created.Id)).FixedInVersion);

        await agent.PatchAsJsonAsync($"/api/v1/workitems/{created.Id}", new { fixedInVersion = "" });
        var cleared = await Get(created.Id);
        Assert.Null(cleared.FixedInVersion);
        Assert.Equal(new FieldChange("3.0.0", null), cleared.History!.Last().ChangedFields["FixedInVersion"]);
    }

    [Fact]
    public async Task TooLong_Is400()
    {
        var response = await _factory.CreateAgentClient().PostAsJsonAsync("/api/v1/workitems",
            new { title = "x", type = "Bug", fixedInVersion = new string('9', 65) });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
