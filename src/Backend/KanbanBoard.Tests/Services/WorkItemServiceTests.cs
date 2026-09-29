using KanbanBoard.Api.Models;
using KanbanBoard.Api.Services;
using KanbanBoard.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace KanbanBoard.Tests.Services;

/// <summary>
/// Unit tests for the work item use cases and - critically - the audit rules
/// from spec 4.1 that every mutation must follow.
/// </summary>
public sealed class WorkItemServiceTests : IAsyncLifetime, IDisposable
{
    private readonly TempSqliteDatabase _temp = new();
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero));

    public Task InitializeAsync() => _temp.InitializeAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _temp.Dispose();

    private WorkItemService CreateService(ActorContext actor) => new(_temp.CreateContext(), actor, _clock);

    private static ActorContext Human(string? name = null)
    {
        var actor = new ActorContext();
        actor.SetHuman(name);
        return actor;
    }

    private static ActorContext Agent(string identity = "Codex-Fixer")
    {
        var actor = new ActorContext();
        actor.SetAiAgent(identity);
        return actor;
    }

    private static CreateWorkItemRequest NewBug(string title = "Crash on save") =>
        new() { Title = title, Type = WorkItemType.Bug };

    [Fact]
    public async Task Create_AppliesSpecDefaults()
    {
        var created = await CreateService(Human()).CreateAsync(NewBug());

        Assert.True(created.Id > 0);
        Assert.Equal(WorkItemState.New, created.State);
        Assert.Equal(2, created.Priority);
        Assert.Equal("3 - Medium", created.Severity);
        Assert.Equal(@"Tools\QA", created.AreaPath);
        Assert.Equal("Current", created.IterationPath);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, created.CreatedAt);
        Assert.Equal(created.CreatedAt, created.UpdatedAt);
    }

    [Fact]
    public async Task Create_ByHuman_IsNotAiModified_AndRecordsHistory()
    {
        var created = await CreateService(Human("Dana")).CreateAsync(NewBug());

        Assert.False(created.AiModified);
        Assert.Null(created.AiAgentIdentity);
        Assert.Equal("Dana", created.LastModifiedBy);
        var entry = Assert.Single(created.History!);
        Assert.False(entry.IsAiAction);
        Assert.Null(entry.AgentName);
        Assert.Equal("Dana", entry.Author);
        Assert.Equal("Crash on save", entry.ChangedFields["Title"].New);
        Assert.Null(entry.ChangedFields["Title"].Old);
    }

    [Fact]
    public async Task Create_ByAgent_IsTaggedAsAiModified()
    {
        var created = await CreateService(Agent("Claude-Code-Agent-v1")).CreateAsync(NewBug());

        Assert.True(created.AiModified);
        Assert.Equal("Claude-Code-Agent-v1", created.AiAgentIdentity);
        Assert.Equal("Claude-Code-Agent-v1", created.LastModifiedBy);
        var entry = Assert.Single(created.History!);
        Assert.True(entry.IsAiAction);
        Assert.Equal("Claude-Code-Agent-v1", entry.AgentName);
    }

    [Fact]
    public async Task Update_ByAgent_TagsItem_AndRecordsFieldDiff()
    {
        var created = await CreateService(Human()).CreateAsync(NewBug());
        _clock.Advance(TimeSpan.FromMinutes(5));

        var updated = await CreateService(Agent()).UpdateAsync(created.Id, new UpdateWorkItemRequest
        {
            State = WorkItemState.Active,
            AssignedTo = "Codex-Fixer",
            Comment = "Picked up by agent",
        });

        Assert.True(updated.AiModified);
        Assert.Equal("Codex-Fixer", updated.AiAgentIdentity);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, updated.UpdatedAt);
        Assert.Equal(2, updated.History!.Count);
        var entry = updated.History[^1];
        Assert.True(entry.IsAiAction);
        Assert.Equal("Picked up by agent", entry.Comment);
        Assert.Equal(new FieldChange("New", "Active"), entry.ChangedFields["State"]);
        Assert.Equal(new FieldChange(null, "Codex-Fixer"), entry.ChangedFields["AssignedTo"]);
        Assert.Equal(2, entry.ChangedFields.Count);
    }

    [Fact]
    public async Task AiModified_IsSticky_AfterLaterHumanEdit()
    {
        var created = await CreateService(Agent("Bot-A")).CreateAsync(NewBug());

        var updated = await CreateService(Human("Pat")).UpdateAsync(created.Id, new UpdateWorkItemRequest { Priority = 1 });

        Assert.True(updated.AiModified);
        Assert.Equal("Bot-A", updated.AiAgentIdentity);
        Assert.Equal("Pat", updated.LastModifiedBy);
        Assert.False(updated.History![^1].IsAiAction);
    }

    [Fact]
    public async Task Update_WithNoEffectiveChange_IsNoOp()
    {
        var created = await CreateService(Human()).CreateAsync(NewBug());
        _clock.Advance(TimeSpan.FromHours(1));

        var updated = await CreateService(Agent()).UpdateAsync(created.Id, new UpdateWorkItemRequest
        {
            Title = "  Crash on save  ",
            State = WorkItemState.New,
        });

        Assert.False(updated.AiModified);
        Assert.Equal(created.UpdatedAt, updated.UpdatedAt);
        Assert.Single(updated.History!);
    }

    [Fact]
    public async Task Update_EmptyAssignee_Unassigns()
    {
        var created = await CreateService(Human()).CreateAsync(new CreateWorkItemRequest
        {
            Title = "x", Type = WorkItemType.Task, AssignedTo = "Sam",
        });

        var updated = await CreateService(Human()).UpdateAsync(created.Id, new UpdateWorkItemRequest { AssignedTo = "" });

        Assert.Null(updated.AssignedTo);
        Assert.Equal(new FieldChange("Sam", null), updated.History![^1].ChangedFields["AssignedTo"]);
    }

    [Fact]
    public async Task Update_UnknownId_Throws()
    {
        await Assert.ThrowsAsync<WorkItemNotFoundException>(() =>
            CreateService(Human()).UpdateAsync(9999, new UpdateWorkItemRequest { Title = "x" }));
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("\u0007\u0008")]
    public async Task Create_BlankTitle_IsRejected(string title)
    {
        var ex = await Assert.ThrowsAsync<WorkItemValidationException>(() =>
            CreateService(Human()).CreateAsync(NewBug(title)));
        Assert.Contains("Title", ex.Errors.Keys);
    }

    [Fact]
    public async Task Create_StripsControlCharactersFromTitle()
    {
        var created = await CreateService(Human()).CreateAsync(NewBug("Bad\u0000 title\r\n"));
        Assert.Equal("Bad title", created.Title);
    }

    [Theory]
    [InlineData("1 - Critical", "1 - Critical")]
    [InlineData("2", "2 - High")]
    [InlineData("medium", "3 - Medium")]
    [InlineData("4-low", "4 - Low")]
    public void NormalizeSeverity_AcceptsFriendlyForms(string input, string expected)
    {
        Assert.Equal(expected, WorkItemService.NormalizeSeverity(input));
    }

    [Fact]
    public void NormalizeSeverity_RejectsUnknownValues()
    {
        Assert.Throws<WorkItemValidationException>(() => WorkItemService.NormalizeSeverity("Catastrophic"));
    }

    [Fact]
    public async Task Create_InvalidPriority_IsRejected()
    {
        await Assert.ThrowsAsync<WorkItemValidationException>(() =>
            CreateService(Human()).CreateAsync(new CreateWorkItemRequest { Title = "p", Type = WorkItemType.Bug, Priority = 9 }));
    }

    [Fact]
    public async Task AddComment_ByAgent_TagsItem_AndStoresMultilineOutput()
    {
        var created = await CreateService(Human()).CreateAsync(NewBug());

        var entry = await CreateService(Agent("Test-Runner")).AddCommentAsync(created.Id, new AddCommentRequest
        {
            Text = "Tests: 12 passed\r\n1 failed",
        });

        Assert.True(entry.IsAiAction);
        Assert.Equal("Test-Runner", entry.AgentName);
        Assert.Equal("Tests: 12 passed\n1 failed", entry.Comment);
        Assert.Empty(entry.ChangedFields);

        var reloaded = await CreateService(Human()).GetAsync(created.Id);
        Assert.True(reloaded.AiModified);
        Assert.Equal("Test-Runner", reloaded.AiAgentIdentity);
    }

    [Fact]
    public async Task AddComment_BlankText_IsRejected()
    {
        var created = await CreateService(Human()).CreateAsync(NewBug());
        await Assert.ThrowsAsync<WorkItemValidationException>(() =>
            CreateService(Human()).AddCommentAsync(created.Id, new AddCommentRequest { Text = " \n " }));
    }

    [Fact]
    public async Task List_FiltersCombineWithAnd()
    {
        var human = CreateService(Human());
        await human.CreateAsync(new CreateWorkItemRequest { Title = "b1", Type = WorkItemType.Bug, AssignedTo = "Ana" });
        await human.CreateAsync(new CreateWorkItemRequest { Title = "b2", Type = WorkItemType.Bug });
        await human.CreateAsync(new CreateWorkItemRequest { Title = "f1", Type = WorkItemType.Feature, AssignedTo = "ana" });
        await CreateService(Agent()).CreateAsync(new CreateWorkItemRequest { Title = "b3", Type = WorkItemType.Bug, State = WorkItemState.Active });

        var svc = CreateService(Human());
        Assert.Equal(3, (await svc.ListAsync(new WorkItemQuery { Type = WorkItemType.Bug })).Count);
        Assert.Equal(new[] { "b1", "f1" }, (await svc.ListAsync(new WorkItemQuery { AssignedTo = "ANA" })).Select(i => i.Title));
        Assert.Equal(new[] { "b2", "b3" }, (await svc.ListAsync(new WorkItemQuery { AssignedTo = "unassigned" })).Select(i => i.Title));
        Assert.Equal("b3", Assert.Single(await svc.ListAsync(new WorkItemQuery { AiModified = true })).Title);
        Assert.Equal("b3", Assert.Single(await svc.ListAsync(new WorkItemQuery { Type = WorkItemType.Bug, State = WorkItemState.Active })).Title);
        Assert.Equal(2, (await svc.ListAsync(new WorkItemQuery { Top = 2 })).Count);
    }

    [Fact]
    public async Task HistoryRows_ArePersistedWithoutDuplication()
    {
        var created = await CreateService(Human()).CreateAsync(NewBug());
        await CreateService(Human()).UpdateAsync(created.Id, new UpdateWorkItemRequest { State = WorkItemState.Active });
        await CreateService(Human()).UpdateAsync(created.Id, new UpdateWorkItemRequest { State = WorkItemState.Resolved });

        await using var db = _temp.CreateContext();
        Assert.Equal(3, await db.WorkItemHistory.CountAsync(h => h.WorkItemId == created.Id));
    }
}
