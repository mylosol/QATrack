using System.Net;
using System.Net.Http.Json;
using KanbanBoard.Api.Data;
using KanbanBoard.Api.Models;
using KanbanBoard.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KanbanBoard.Tests.Api;

/// <summary>1.13.0: people edit the comments they wrote on the board.</summary>
public sealed class CommentEditApiTests : IClassFixture<KanbanApiFactory>
{
    private readonly KanbanApiFactory _factory;

    public CommentEditApiTests(KanbanApiFactory factory)
    {
        _factory = factory;
    }

    private async Task<(int ItemId, WorkItemHistoryDto Comment)> CommentAs(string name, string text)
    {
        var ui = _factory.CreateUiClient(name);
        var item = (await (await ui.PostAsJsonAsync("/api/ui/workitems", new { title = "Edit me", type = "Bug" }))
            .Content.ReadFromJsonAsync<WorkItemDto>(KanbanApiFactory.Json))!;
        var comment = (await (await ui.PostAsJsonAsync($"/api/ui/workitems/{item.Id}/comments", new { text }))
            .Content.ReadFromJsonAsync<WorkItemHistoryDto>(KanbanApiFactory.Json))!;
        return (item.Id, comment);
    }

    private static string Path(int itemId, int commentId) => $"/api/ui/workitems/{itemId}/comments/{commentId}";

    [Fact]
    public async Task Author_EditsTheirComment_PreviousTextIsKept()
    {
        var (id, comment) = await CommentAs("Robert", "Fails on login.");
        var response = await _factory.CreateUiClient("robert").PutAsJsonAsync(Path(id, comment.Id), new { text = "Fails on logout, not login." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var edited = (await response.Content.ReadFromJsonAsync<WorkItemHistoryDto>(KanbanApiFactory.Json))!;
        Assert.Equal(comment.Id, edited.Id);
        Assert.Equal("Fails on logout, not login.", edited.Comment);
        Assert.Equal("Robert", edited.Author);
        Assert.Equal(comment.ChangeDate, edited.ChangeDate);
        Assert.NotNull(edited.EditedAt);

        // No new history row; the edit shows on the entry.
        var card = (await _factory.CreateAgentClient().GetFromJsonAsync<WorkItemDto>($"/api/v1/workitems/{id}", KanbanApiFactory.Json))!;
        Assert.Equal(2, card.History!.Count);
        Assert.Equal(1, card.CommentCount);
        Assert.Equal(edited.EditedAt, card.History![1].EditedAt);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KanbanDbContext>();
        var revision = Assert.Single(await db.CommentRevisions.Where(r => r.HistoryId == comment.Id).ToListAsync());
        Assert.Equal("Fails on login.", revision.Comment);
        Assert.Equal("robert", revision.ReplacedBy);
    }

    [Fact]
    public async Task AnEdit_PutsTheCardBackInFrontOfTheAgent()
    {
        var agent = _factory.CreateAgentClient("Fixer-Bot");
        var (id, comment) = await CommentAs("Robert", "Please check the export.");
        await agent.PostAsJsonAsync($"/api/v1/workitems/{id}/comments", new { text = "Checked, works for me." });
        var answered = (await agent.GetFromJsonAsync<WorkItemDto>($"/api/v1/workitems/{id}", KanbanApiFactory.Json))!;
        Assert.NotEqual(DiscussionStatus.AwaitingAgent, answered.DiscussionStatus);

        await Task.Delay(20);
        await _factory.CreateUiClient("Robert").PutAsJsonAsync(Path(id, comment.Id), new { text = "Please check the CSV export." });

        var since = Uri.EscapeDataString(answered.UpdatedAt.ToString("O"));
        var polled = await agent.GetFromJsonAsync<List<WorkItemDto>>($"/api/v1/workitems?updatedSince={since}", KanbanApiFactory.Json);
        var card = Assert.Single(polled!, w => w.Id == id);
        Assert.Equal(DiscussionStatus.AwaitingAgent, card.DiscussionStatus);
        Assert.Equal("Robert", card.LastHumanCommentBy);
    }

    [Fact]
    public async Task SomeoneElsesComment_Is403()
    {
        var (id, comment) = await CommentAs("Robert", "Mine.");
        var response = await _factory.CreateUiClient("Alex").PutAsJsonAsync(Path(id, comment.Id), new { text = "Hijacked." });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("Only Robert can edit", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AnAgentComment_CannotBeEditedFromTheBoard()
    {
        var (id, _) = await CommentAs("Robert", "Question.");
        var agentComment = (await (await _factory.CreateAgentClient("Claude-Code-Agent-v1").PostAsJsonAsync($"/api/v1/workitems/{id}/comments", new { text = "Answer." }))
            .Content.ReadFromJsonAsync<WorkItemHistoryDto>(KanbanApiFactory.Json))!;

        var response = await _factory.CreateUiClient("Claude-Code-Agent-v1").PutAsJsonAsync(Path(id, agentComment.Id), new { text = "Rewritten." });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MissingOrNonComment_Is404_AndBlankIs400()
    {
        var (id, comment) = await CommentAs("Robert", "Text.");
        var ui = _factory.CreateUiClient("Robert");
        var card = (await ui.GetFromJsonAsync<WorkItemDto>($"/api/ui/workitems/{id}", KanbanApiFactory.Json))!;
        var creationEntry = card.History!.First(h => h.Comment is null);

        Assert.Equal(HttpStatusCode.NotFound, (await ui.PutAsJsonAsync(Path(id, creationEntry.Id), new { text = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ui.PutAsJsonAsync(Path(id, 999_999), new { text = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ui.PutAsJsonAsync(Path(999_999, comment.Id), new { text = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ui.PutAsJsonAsync(Path(id, comment.Id), new { text = "   " })).StatusCode);
    }

    [Fact]
    public async Task SameText_IsNotAnEdit()
    {
        var (id, comment) = await CommentAs("Robert", "Unchanged.");
        var before = (await _factory.CreateAgentClient().GetFromJsonAsync<WorkItemDto>($"/api/v1/workitems/{id}", KanbanApiFactory.Json))!;

        var edited = await (await _factory.CreateUiClient("Robert").PutAsJsonAsync(Path(id, comment.Id), new { text = "Unchanged." }))
            .Content.ReadFromJsonAsync<WorkItemHistoryDto>(KanbanApiFactory.Json);

        Assert.Null(edited!.EditedAt);
        var after = (await _factory.CreateAgentClient().GetFromJsonAsync<WorkItemDto>($"/api/v1/workitems/{id}", KanbanApiFactory.Json))!;
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);
    }

    [Fact]
    public async Task EditingNeedsTheBoardsAntiForgeryHeader_AndIsNotOnTheAgentApi()
    {
        var (id, comment) = await CommentAs("Robert", "Text.");

        var bare = _factory.CreateClient();
        bare.DefaultRequestHeaders.Add("X-User-Display-Name", "Robert");
        Assert.NotEqual(HttpStatusCode.OK, (await bare.PutAsJsonAsync(Path(id, comment.Id), new { text = "x" })).StatusCode);

        var agent = _factory.CreateAgentClient();
        var v1 = await agent.PutAsJsonAsync($"/api/v1/workitems/{id}/comments/{comment.Id}", new { text = "x" });
        Assert.Contains(v1.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
    }
}
