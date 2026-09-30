using KanbanBoard.Api.Models;
using KanbanBoard.Api.Services;
using KanbanBoard.Tests.Infrastructure;

namespace KanbanBoard.Tests.Services;

/// <summary>
/// 1.8.0: letting agents find what changed - the updatedSince filter and the
/// lastHumanCommentAt / lastHumanCommentBy fields.
/// </summary>
public sealed class ChangeDetectionTests : IAsyncLifetime, IDisposable
{
    private readonly TempSqliteDatabase _temp = new();
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero));

    public Task InitializeAsync() => _temp.InitializeAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _temp.Dispose();

    private WorkItemService As(string name, bool ai = false)
    {
        var actor = new ActorContext();
        if (ai)
        {
            actor.SetAiAgent(name);
        }
        else
        {
            actor.SetHuman(name);
        }

        return new WorkItemService(_temp.CreateContext(), actor, _clock);
    }

    private static CreateWorkItemRequest Bug(string title = "Crash") => new() { Title = title, Type = WorkItemType.Bug };

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    [Fact]
    public async Task NewItems_HaveNoHumanComment()
    {
        var created = await As("Dana").CreateAsync(Bug());
        Assert.Null(created.LastHumanCommentAt);
        Assert.Null(created.LastHumanCommentBy);
    }

    [Fact]
    public async Task HumanComment_IsReportedOnTheItem_InListsToo()
    {
        var created = await As("Codex-Fixer", ai: true).CreateAsync(Bug());
        _clock.Advance(TimeSpan.FromMinutes(5));

        await As("Robert").AddCommentAsync(created.Id, new AddCommentRequest { Text = "Still broken on 2.4.2" });

        var listed = Assert.Single(await As("x").ListAsync(new WorkItemQuery()));
        Assert.Equal(Now, listed.LastHumanCommentAt);
        Assert.Equal("Robert", listed.LastHumanCommentBy);
        Assert.Equal(Now, listed.UpdatedAt);
        Assert.Null(listed.History); // lists still don't carry history
    }

    [Fact]
    public async Task AgentComments_DoNotCountAsHumanComments()
    {
        var created = await As("Robert").CreateAsync(Bug());
        await As("Robert").AddCommentAsync(created.Id, new AddCommentRequest { Text = "Please look at this" });
        var humanAt = Now;
        _clock.Advance(TimeSpan.FromMinutes(1));

        await As("Codex-Fixer", ai: true).AddCommentAsync(created.Id, new AddCommentRequest { Text = "On it" });

        var item = await As("x").GetAsync(created.Id);
        Assert.Equal(humanAt, item.LastHumanCommentAt);
        Assert.Equal("Robert", item.LastHumanCommentBy);
    }

    [Fact]
    public async Task HumanUpdateNotes_Count_ButPlainEditsDoNot()
    {
        var created = await As("Robert").CreateAsync(Bug());
        _clock.Advance(TimeSpan.FromMinutes(1));

        await As("Robert").UpdateAsync(created.Id, new UpdateWorkItemRequest { Priority = 1 });
        Assert.Null((await As("x").GetAsync(created.Id)).LastHumanCommentAt);

        await As("Dana").UpdateAsync(created.Id, new UpdateWorkItemRequest { State = WorkItemState.Active, Comment = "Reopening" });
        var item = await As("x").GetAsync(created.Id);
        Assert.Equal(Now, item.LastHumanCommentAt);
        Assert.Equal("Dana", item.LastHumanCommentBy);
    }

    [Fact]
    public async Task UpdatedSince_ReturnsOnlyItemsChangedStrictlyAfterIt()
    {
        var first = await As("Robert").CreateAsync(Bug("first"));
        _clock.Advance(TimeSpan.FromSeconds(1));
        var second = await As("Robert").CreateAsync(Bug("second"));
        var seen = new DateTimeOffset(second.UpdatedAt, TimeSpan.Zero);

        // Nothing changed after the newest updatedAt we saw.
        Assert.Empty(await As("x").ListAsync(new WorkItemQuery { UpdatedSince = seen }));

        _clock.Advance(TimeSpan.FromSeconds(1));
        await As("Robert").AddCommentAsync(first.Id, new AddCommentRequest { Text = "new info" });

        var changed = Assert.Single(await As("x").ListAsync(new WorkItemQuery { UpdatedSince = seen }));
        Assert.Equal(first.Id, changed.Id);
        Assert.Equal(2, (await As("x").ListAsync(new WorkItemQuery { UpdatedSince = seen.AddHours(-1) })).Count);
    }

    [Fact]
    public async Task UpdatedSince_HonoursTheOffset()
    {
        await As("Robert").CreateAsync(Bug()); // 12:00Z

        // 13:59+02:00 is 11:59Z - before the change; 14:01+02:00 is 12:01Z - after it.
        Assert.Single(await As("x").ListAsync(new WorkItemQuery { UpdatedSince = new DateTimeOffset(2026, 9, 30, 13, 59, 0, TimeSpan.FromHours(2)) }));
        Assert.Empty(await As("x").ListAsync(new WorkItemQuery { UpdatedSince = new DateTimeOffset(2026, 9, 30, 14, 1, 0, TimeSpan.FromHours(2)) }));
    }

    [Fact]
    public async Task UpdatedSince_AlsoFiltersTheBoard()
    {
        var old = await As("Robert").CreateAsync(Bug("old"));
        _clock.Advance(TimeSpan.FromMinutes(1));
        var fresh = await As("Robert").CreateAsync(Bug("fresh"));

        var board = await new BoardService(_temp.CreateContext())
            .GetBoardAsync(new WorkItemQuery { UpdatedSince = new DateTimeOffset(old.UpdatedAt, TimeSpan.Zero) });

        var card = Assert.Single(board.Columns.SelectMany(c => c.Items));
        Assert.Equal(fresh.Id, card.Id);
        Assert.Equal(2, board.Columns[0].ItemCount); // WIP counts still cover the whole board
    }

    // ---- discussion status (1.9.0) -------------------------------------------------

    [Fact]
    public void StatusOf_FollowsWhoSpokeLast_AndWhetherAHumanReadIt()
    {
        var t0 = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
        Assert.Null(Discussion.StatusOf(null, null, null));
        Assert.Equal(DiscussionStatus.AwaitingAgent, Discussion.StatusOf(t0, null, null));
        Assert.Equal(DiscussionStatus.AwaitingAgent, Discussion.StatusOf(t0.AddMinutes(1), t0, t0.AddMinutes(5)));
        Assert.Equal(DiscussionStatus.UnreadReply, Discussion.StatusOf(null, t0, null));
        Assert.Equal(DiscussionStatus.UnreadReply, Discussion.StatusOf(t0, t0.AddMinutes(1), t0.AddSeconds(30)));
        Assert.Null(Discussion.StatusOf(t0, t0.AddMinutes(1), t0.AddMinutes(1)));
    }

    [Fact]
    public async Task Conversation_MovesBetweenUnreadReply_AwaitingAgent_AndNothing()
    {
        var item = await As("Robert").CreateAsync(Bug());
        Assert.Null(item.DiscussionStatus);
        Assert.Equal(0, item.CommentCount);

        _clock.Advance(TimeSpan.FromMinutes(1));
        await As("Codex-Fixer", ai: true).AddCommentAsync(item.Id, new AddCommentRequest { Text = "Looked at it; needs logs." });
        var afterAgent = await As("x").GetAsync(item.Id);
        Assert.Equal(DiscussionStatus.UnreadReply, afterAgent.DiscussionStatus);
        Assert.Equal("Codex-Fixer", afterAgent.LastAgentCommentBy);
        Assert.Equal(1, afterAgent.CommentCount);

        _clock.Advance(TimeSpan.FromMinutes(1));
        await As("Robert").MarkReadAsync(item.Id);
        Assert.Null((await As("x").GetAsync(item.Id)).DiscussionStatus);

        _clock.Advance(TimeSpan.FromMinutes(1));
        await As("Robert").AddCommentAsync(item.Id, new AddCommentRequest { Text = "Logs attached." });
        var afterHuman = await As("x").GetAsync(item.Id);
        Assert.Equal(DiscussionStatus.AwaitingAgent, afterHuman.DiscussionStatus);
        Assert.Equal(2, afterHuman.CommentCount);

        _clock.Advance(TimeSpan.FromMinutes(1));
        await As("Codex-Fixer", ai: true).UpdateAsync(item.Id, new UpdateWorkItemRequest { State = WorkItemState.Active });
        // A move without a comment is not a reply.
        Assert.Equal(DiscussionStatus.AwaitingAgent, (await As("x").GetAsync(item.Id)).DiscussionStatus);

        await As("Codex-Fixer", ai: true).UpdateAsync(item.Id, new UpdateWorkItemRequest { Comment = "Fixed in 2.4.2." });
        Assert.Equal(DiscussionStatus.UnreadReply, (await As("x").GetAsync(item.Id)).DiscussionStatus);
    }

    [Fact]
    public async Task MarkRead_IsNotAnEdit()
    {
        var item = await As("Codex-Fixer", ai: true).CreateAsync(new CreateWorkItemRequest { Title = "x", Type = WorkItemType.Bug, Comment = "Filed from nightly run" });
        var before = await As("x").GetAsync(item.Id);
        Assert.Equal(DiscussionStatus.UnreadReply, before.DiscussionStatus);

        _clock.Advance(TimeSpan.FromMinutes(1));
        await As("Robert").MarkReadAsync(item.Id);

        var after = await As("x").GetAsync(item.Id);
        Assert.Null(after.DiscussionStatus);
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);
        Assert.Equal(before.History!.Count, after.History!.Count);
        Assert.Empty(await As("x").ListAsync(new WorkItemQuery { UpdatedSince = new DateTimeOffset(before.UpdatedAt, TimeSpan.Zero) }));
    }

    [Fact]
    public async Task MarkRead_OfAnUnknownItem_Throws()
    {
        await Assert.ThrowsAsync<WorkItemNotFoundException>(() => As("Robert").MarkReadAsync(424242));
    }

    [Fact]
    public async Task DiscussionFilter_AndBoardCounts()
    {
        var waiting = await As("Robert").CreateAsync(Bug("waiting"));
        await As("Robert").AddCommentAsync(waiting.Id, new AddCommentRequest { Text = "Any update?" });
        _clock.Advance(TimeSpan.FromMinutes(1));
        var replied = await As("Robert").CreateAsync(Bug("replied"));
        await As("Codex-Fixer", ai: true).AddCommentAsync(replied.Id, new AddCommentRequest { Text = "Done." });
        await As("Robert").CreateAsync(Bug("quiet"));

        Assert.Equal(waiting.Id, Assert.Single(await As("x").ListAsync(new WorkItemQuery { Discussion = DiscussionStatus.AwaitingAgent })).Id);
        Assert.Equal(replied.Id, Assert.Single(await As("x").ListAsync(new WorkItemQuery { Discussion = DiscussionStatus.UnreadReply })).Id);
        Assert.Equal(1, await As("x").CountOnBoardAsync(new WorkItemQuery { Discussion = DiscussionStatus.AwaitingAgent }));

        var board = await new BoardService(_temp.CreateContext()).GetBoardAsync(new WorkItemQuery { Discussion = DiscussionStatus.AwaitingAgent });
        Assert.Equal(1, board.Metadata.AwaitingAgentCount);
        Assert.Equal(1, board.Metadata.UnreadReplyCount);
        Assert.Equal(waiting.Id, Assert.Single(board.Columns.SelectMany(c => c.Items)).Id);
    }
}
