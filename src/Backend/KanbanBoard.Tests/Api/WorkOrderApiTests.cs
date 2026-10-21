using System.Net;
using System.Net.Http.Json;
using KanbanBoard.Api.Models;
using KanbanBoard.Tests.Infrastructure;

namespace KanbanBoard.Tests.Api;

/// <summary>
/// 1.22.0: people order each column by dragging cards; agents see the order as
/// 'rank' and get the work item list in that order.
/// </summary>
/// <remarks>Each test boots its own host: ranks are positions among every card in a column.</remarks>
public sealed class WorkOrderApiTests
{
    private static async Task<WorkItemDto> Create(KanbanApiFactory factory, string title, int priority = 2, string state = "New")
    {
        var response = await factory.CreateUiClient("Robert").PostAsJsonAsync("/api/ui/workitems",
            new { title, type = "Bug", priority, state });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkItemDto>(KanbanApiFactory.Json))!;
    }

    private static async Task<List<WorkItemDto>> List(KanbanApiFactory factory, string query = "") =>
        (await factory.CreateAgentClient().GetFromJsonAsync<List<WorkItemDto>>($"/api/v1/workitems{query}", KanbanApiFactory.Json))!;

    private static async Task<WorkItemDto> Get(KanbanApiFactory factory, int id) =>
        (await factory.CreateAgentClient().GetFromJsonAsync<WorkItemDto>($"/api/v1/workitems/{id}", KanbanApiFactory.Json))!;

    private static Task<HttpResponseMessage> Place(KanbanApiFactory factory, int id, object body) =>
        factory.CreateUiClient("Robert").PutAsJsonAsync($"/api/ui/workitems/{id}/position", body);

    private static async Task<string[]> Titles(KanbanApiFactory factory, string state = "New") =>
        (await List(factory, $"?state={state}")).Select(i => i.Title).ToArray();

    [Fact]
    public async Task NewCards_GoToTheBottomOfTheirPriority_AndRankIsTheirPosition()
    {
        await using var factory = new KanbanApiFactory();
        await Create(factory, "A");
        var b = await Create(factory, "B");
        await Create(factory, "Urgent", priority: 1);

        var items = await List(factory, "?state=New");
        Assert.Equal(new[] { "Urgent", "A", "B" }, items.Select(i => i.Title));
        Assert.Equal(new int?[] { 1, 2, 3 }, items.Select(i => i.Rank));
        Assert.Equal(3, (await Get(factory, b.Id)).Rank);

        var board = (await factory.CreateAgentClient().GetFromJsonAsync<BoardDto>("/api/v1/board", KanbanApiFactory.Json))!;
        var column = board.Columns.Single(c => c.State == WorkItemState.New);
        Assert.Equal(new[] { "Urgent", "A", "B" }, column.Items.Select(i => i.Title));
        Assert.Equal(new int?[] { 1, 2, 3 }, column.Items.Select(i => i.Rank));
    }

    [Fact]
    public async Task TheList_IsInBoardOrder_ColumnByColumn_AndSortIdKeepsTheOldOrder()
    {
        await using var factory = new KanbanApiFactory();
        await Create(factory, "Low in To Do", priority: 4);
        await Create(factory, "Being worked", priority: 1, state: "Active");
        await Create(factory, "Critical in To Do", priority: 1);

        Assert.Equal(new[] { "Critical in To Do", "Low in To Do", "Being worked" }, (await List(factory)).Select(i => i.Title));
        Assert.Equal(new[] { "Low in To Do", "Being worked", "Critical in To Do" }, (await List(factory, "?sort=id")).Select(i => i.Title));
        // Ranks count within each column.
        Assert.Equal(new int?[] { 1, 2, 1 }, (await List(factory)).Select(i => i.Rank));
    }

    [Fact]
    public async Task Reordering_WithinAPriority_ChangesOnlyTheOrder()
    {
        await using var factory = new KanbanApiFactory();
        var a = await Create(factory, "A");
        var b = await Create(factory, "B");
        var c = await Create(factory, "C");
        var before = await Get(factory, c.Id);

        // C to the top.
        Assert.Equal(HttpStatusCode.OK, (await Place(factory, c.Id, new { afterId = (int?)null, beforeId = a.Id })).StatusCode);
        Assert.Equal(new[] { "C", "A", "B" }, await Titles(factory));

        // A to the bottom.
        await Place(factory, a.Id, new { afterId = b.Id });
        Assert.Equal(new[] { "C", "B", "A" }, await Titles(factory));

        // A reorder isn't an edit: no history row, updatedAt kept.
        var after = await Get(factory, c.Id);
        Assert.Equal(before.History!.Count, after.History!.Count);
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);
        Assert.Equal(1, after.Rank);
    }

    [Fact]
    public async Task DroppedAmongOtherPriorities_ItTakesTheirPriority_RecordedInHistory()
    {
        await using var factory = new KanbanApiFactory();
        var first = await Create(factory, "P1 first", priority: 1);
        var second = await Create(factory, "P1 second", priority: 1);
        var low = await Create(factory, "P4", priority: 4);
        var card = await Create(factory, "Was P3", priority: 3);

        // Between two priority 1 cards: becomes priority 1, in that spot.
        var response = await Place(factory, card.Id, new { afterId = first.Id, beforeId = second.Id });
        var placed = (await response.Content.ReadFromJsonAsync<WorkItemDto>(KanbanApiFactory.Json))!;
        Assert.Equal(1, placed.Priority);
        Assert.Equal(2, placed.Rank);
        Assert.Equal(new[] { "P1 first", "Was P3", "P1 second", "P4" }, await Titles(factory));
        var entry = (await Get(factory, card.Id)).History!.Last();
        Assert.Equal(new FieldChange("3", "1"), entry.ChangedFields["Priority"]);
        Assert.False(entry.IsAiAction);

        // Below the priority 4 card: becomes priority 4.
        await Place(factory, card.Id, new { afterId = low.Id });
        Assert.Equal(4, (await Get(factory, card.Id)).Priority);
        Assert.Equal(new[] { "P1 first", "P1 second", "P4", "Was P3" }, await Titles(factory));

        // Between priority 1 and 4 it keeps a priority that already fits.
        await Place(factory, low.Id, new { afterId = second.Id, beforeId = card.Id });
        Assert.Equal(4, (await Get(factory, low.Id)).Priority);
    }

    [Fact]
    public async Task MovingToAnotherColumn_AtASpot_IsOneHistoryEntry()
    {
        await using var factory = new KanbanApiFactory();
        var x = await Create(factory, "X", state: "Active");
        var y = await Create(factory, "Y", state: "Active");
        var card = await Create(factory, "Card");

        await Place(factory, card.Id, new { state = "Active", afterId = x.Id, beforeId = y.Id });

        Assert.Equal(new[] { "X", "Card", "Y" }, await Titles(factory, "Active"));
        var item = await Get(factory, card.Id);
        Assert.Equal(WorkItemState.Active, item.State);
        Assert.Equal(2, item.History!.Count);
        Assert.Equal(new FieldChange("New", "Active"), item.History.Last().ChangedFields["State"]);
    }

    [Fact]
    public async Task Neighbours_MustBeOtherCardsInThatColumn()
    {
        await using var factory = new KanbanApiFactory();
        var card = await Create(factory, "Card");
        var active = await Create(factory, "Active one", state: "Active");

        Assert.Equal(HttpStatusCode.BadRequest, (await Place(factory, card.Id, new { afterId = card.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Place(factory, card.Id, new { afterId = active.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Place(factory, card.Id, new { beforeId = 999_999 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Place(factory, card.Id, new { state = "Removed" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Place(factory, 999_999, new { })).StatusCode);
        Assert.Equal(WorkItemState.New, (await Get(factory, card.Id)).State);
    }

    [Fact]
    public async Task SplittingTheSameGapAgainAndAgain_KeepsTheOrderRight()
    {
        await using var factory = new KanbanApiFactory();
        var a = await Create(factory, "A");
        var b = await Create(factory, "B");
        var c = await Create(factory, "C");

        // Each move halves the gap after A; well past what a double can split.
        var (moving, other) = (c, b);
        for (var i = 0; i < 70; i++)
        {
            await Place(factory, moving.Id, new { afterId = a.Id, beforeId = other.Id });
            Assert.Equal(new[] { "A", moving.Title, other.Title }, await Titles(factory));
            (moving, other) = (other, moving);
        }
    }

    [Fact]
    public async Task AgentsCannotReorder_ThroughTheBoardEndpoint()
    {
        await using var factory = new KanbanApiFactory();
        var card = await Create(factory, "Card");
        var response = await factory.CreateAgentClient().PutAsJsonAsync($"/api/ui/workitems/{card.Id}/position", new { });
        Assert.False(response.IsSuccessStatusCode);
    }
}
