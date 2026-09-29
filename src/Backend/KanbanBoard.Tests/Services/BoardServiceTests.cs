using KanbanBoard.Api.Models;
using KanbanBoard.Api.Services;
using KanbanBoard.Tests.Infrastructure;

namespace KanbanBoard.Tests.Services;

public sealed class BoardServiceTests : IAsyncLifetime, IDisposable
{
    private readonly TempSqliteDatabase _temp = new();

    public Task InitializeAsync() => _temp.InitializeAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _temp.Dispose();

    private async Task AddAsync(string title, WorkItemState state, WorkItemType type = WorkItemType.Task, string? assignee = null)
    {
        var actor = new ActorContext();
        actor.SetHuman("seed");
        await new WorkItemService(_temp.CreateContext(), actor, TimeProvider.System)
            .CreateAsync(new CreateWorkItemRequest { Title = title, Type = type, State = state, AssignedTo = assignee });
    }

    private Task<BoardDto> GetBoard(WorkItemQuery? filter = null) =>
        new BoardService(_temp.CreateContext()).GetBoardAsync(filter ?? new WorkItemQuery());

    [Fact]
    public async Task EmptyBoard_HasFourSpecColumns_AndMetadata()
    {
        var board = await GetBoard();

        Assert.Equal(new[] { "New", "Active", "Resolved", "Closed" }, board.Columns.Select(c => c.Name));
        Assert.All(board.Columns, c => Assert.False(c.IsOverWipLimit));
        Assert.Single(board.Swimlanes);
        Assert.Contains("UserStory", board.Metadata.Types);
        Assert.Equal(4, board.Metadata.Severities.Count);
        Assert.Equal("Critical", board.Metadata.Priorities[1]);
    }

    [Fact]
    public async Task ActiveColumn_Over5Items_IsFlaggedOverWip()
    {
        for (var i = 0; i < 6; i++)
        {
            await AddAsync($"a{i}", WorkItemState.Active);
        }

        var active = (await GetBoard()).Columns.Single(c => c.State == WorkItemState.Active);
        Assert.Equal(6, active.ItemCount);
        Assert.Equal(5, active.WipLimit);
        Assert.True(active.IsOverWipLimit);
    }

    [Fact]
    public async Task ExactlyAtWipLimit_IsNotAViolation()
    {
        for (var i = 0; i < 5; i++)
        {
            await AddAsync($"r{i}", WorkItemState.Resolved);
        }

        Assert.False((await GetBoard()).Columns.Single(c => c.State == WorkItemState.Resolved).IsOverWipLimit);
    }

    [Fact]
    public async Task Filters_NarrowItems_ButNotWipCounts()
    {
        for (var i = 0; i < 6; i++)
        {
            await AddAsync($"a{i}", WorkItemState.Active, i == 0 ? WorkItemType.Bug : WorkItemType.Task);
        }

        var active = (await GetBoard(new WorkItemQuery { Type = WorkItemType.Bug }))
            .Columns.Single(c => c.State == WorkItemState.Active);

        Assert.Single(active.Items);
        Assert.Equal(6, active.ItemCount);
        Assert.True(active.IsOverWipLimit);
    }

    [Fact]
    public async Task RemovedItems_AreHiddenAndCounted()
    {
        await AddAsync("gone", WorkItemState.Removed);
        await AddAsync("kept", WorkItemState.New, assignee: "Lee");

        var board = await GetBoard();
        Assert.Equal(1, board.RemovedCount);
        Assert.Equal(new[] { "kept" }, board.Columns.SelectMany(c => c.Items).Select(i => i.Title));
        Assert.Equal(new[] { "Lee" }, board.Metadata.Assignees);
    }

    [Theory]
    [InlineData(0, null, false)]
    [InlineData(100, null, false)]
    [InlineData(5, 5, false)]
    [InlineData(6, 5, true)]
    public void IsOverWip_Rules(int count, int? limit, bool expected)
    {
        Assert.Equal(expected, BoardService.IsOverWip(count, limit));
    }
}
