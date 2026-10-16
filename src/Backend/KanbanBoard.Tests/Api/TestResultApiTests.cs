using System.Net;
using System.Net.Http.Json;
using KanbanBoard.Api.Models;
using KanbanBoard.Tests.Infrastructure;

namespace KanbanBoard.Tests.Api;

/// <summary>1.19.0: a person records a retest as "Passed in" / "Failed in" a version.</summary>
public sealed class TestResultApiTests : IClassFixture<KanbanApiFactory>
{
    private readonly KanbanApiFactory _factory;

    public TestResultApiTests(KanbanApiFactory factory)
    {
        _factory = factory;
    }

    private async Task<WorkItemDto> Resolved()
    {
        var ui = _factory.CreateUiClient("Robert");
        var created = (await (await ui.PostAsJsonAsync("/api/ui/workitems", new { title = "Retest me", type = "Bug" }))
            .Content.ReadFromJsonAsync<WorkItemDto>(KanbanApiFactory.Json))!;
        await _factory.CreateAgentClient("Fixer-Bot").PatchAsJsonAsync($"/api/v1/workitems/{created.Id}",
            new { state = "Resolved", fixedInVersion = "2.4.3", comment = "Fixed." });
        return created;
    }

    private async Task<WorkItemDto> Get(int id) =>
        (await _factory.CreateAgentClient().GetFromJsonAsync<WorkItemDto>($"/api/v1/workitems/{id}", KanbanApiFactory.Json))!;

    [Fact]
    public async Task AFailedRetest_IsRecorded_WithTheVersion_AndTheMoveBackToNew()
    {
        var card = await Resolved();

        // What the board sends for "Failed in 2.4.3" + Save changes.
        var response = await _factory.CreateUiClient("Robert").PatchAsJsonAsync($"/api/ui/workitems/{card.Id}",
            new { testResult = "failed", testedInVersion = "2.4.3", state = "New" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var after = await Get(card.Id);
        Assert.Equal("Failed", after.TestResult);
        Assert.Equal("2.4.3", after.TestedInVersion);
        Assert.Equal(WorkItemState.New, after.State);
        var entry = after.History!.Last();
        Assert.False(entry.IsAiAction);
        Assert.Equal(new FieldChange(null, "Failed"), entry.ChangedFields["TestResult"]);
        Assert.Equal(new FieldChange(null, "2.4.3"), entry.ChangedFields["TestedInVersion"]);
        Assert.Equal(new FieldChange("Resolved", "New"), entry.ChangedFields["State"]);
    }

    [Fact]
    public async Task APassingRetest_Replaces_TheEarlierFailure()
    {
        var card = await Resolved();
        var ui = _factory.CreateUiClient("Robert");
        await ui.PatchAsJsonAsync($"/api/ui/workitems/{card.Id}", new { testResult = "Failed", testedInVersion = "2.4.3", state = "New" });
        await ui.PatchAsJsonAsync($"/api/ui/workitems/{card.Id}", new { testResult = "Passed", testedInVersion = "2.4.4", state = "Closed" });

        var after = await Get(card.Id);
        Assert.Equal("Passed", after.TestResult);
        Assert.Equal("2.4.4", after.TestedInVersion);
        Assert.Equal(WorkItemState.Closed, after.State);
        Assert.Equal(new FieldChange("Failed", "Passed"), after.History!.Last().ChangedFields["TestResult"]);
    }

    [Fact]
    public async Task ThroughTheApi_ItDoesNotMoveTheCardByItself_AndEmptyClears()
    {
        var card = await Resolved();
        var agent = _factory.CreateAgentClient();
        await agent.PatchAsJsonAsync($"/api/v1/workitems/{card.Id}", new { testResult = "Passed", testedInVersion = "2.4.3" });
        var tested = await Get(card.Id);
        Assert.Equal("Passed", tested.TestResult);
        Assert.Equal(WorkItemState.Resolved, tested.State);

        await agent.PatchAsJsonAsync($"/api/v1/workitems/{card.Id}", new { testResult = "", testedInVersion = "" });
        var cleared = await Get(card.Id);
        Assert.Null(cleared.TestResult);
        Assert.Null(cleared.TestedInVersion);
    }

    [Fact]
    public async Task AnythingButPassedOrFailed_Is400()
    {
        var card = await Resolved();
        var response = await _factory.CreateUiClient().PatchAsJsonAsync($"/api/ui/workitems/{card.Id}", new { testResult = "Maybe" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Passed or Failed", await response.Content.ReadAsStringAsync());
    }
}
