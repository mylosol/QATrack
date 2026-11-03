using System.Net;
using System.Net.Http.Json;
using KanbanBoard.Api.Data;
using KanbanBoard.Api.Models;
using KanbanBoard.Tests.Infrastructure;
using KanbanBoard.Tests.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KanbanBoard.Tests.Api;

/// <summary>1.27.0: people delete the comments they wrote, completely.</summary>
public sealed class CommentDeleteApiTests : IClassFixture<KanbanApiFactory>
{
    private readonly KanbanApiFactory _factory;

    public CommentDeleteApiTests(KanbanApiFactory factory)
    {
        _factory = factory;
    }

    private async Task<int> CardBy(string name)
    {
        var item = await (await _factory.CreateUiClient(name).PostAsJsonAsync("/api/ui/workitems", new { title = "Delete me", type = "Bug" }))
            .Content.ReadFromJsonAsync<WorkItemDto>(KanbanApiFactory.Json);
        return item!.Id;
    }

    private async Task<WorkItemHistoryDto> Comment(HttpClient client, string path, string text) =>
        (await (await client.PostAsJsonAsync(path, new { text })).Content.ReadFromJsonAsync<WorkItemHistoryDto>(KanbanApiFactory.Json))!;

    private Task<WorkItemDto?> Card(int id) =>
        _factory.CreateAgentClient().GetFromJsonAsync<WorkItemDto>($"/api/v1/workitems/{id}", KanbanApiFactory.Json);

    private async Task<string> UploadPicture(string name)
    {
        // Unique bytes, so the picture isn't shared with another test's upload.
        var bytes = ProgramTagAttachmentServiceTests.Png.Concat(Guid.NewGuid().ToByteArray()).ToArray();
        var response = await _factory.CreateUiClient("Robert").PostAsync("/api/ui/attachments",
            new MultipartFormDataContent { { new ByteArrayContent(bytes), "file", name } });
        return (await response.Content.ReadFromJsonAsync<AttachmentDto>(KanbanApiFactory.Json))!.Markdown;
    }

    private static string Path(int itemId, int commentId) => $"/api/ui/workitems/{itemId}/comments/{commentId}";

    [Fact]
    public async Task Author_DeletesTheirComment_TextAndEarlierVersionsAreGone()
    {
        var id = await CardBy("Robert");
        var ui = _factory.CreateUiClient("Robert");
        var keep = await Comment(ui, $"/api/ui/workitems/{id}/comments", "Keep this one.");
        var comment = await Comment(ui, $"/api/ui/workitems/{id}/comments", "Wrong card, sorry.");
        await ui.PutAsJsonAsync(Path(id, comment.Id), new { text = "Still the wrong card." });

        var response = await _factory.CreateUiClient("robert").DeleteAsync(Path(id, comment.Id));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var card = (await Card(id))!;
        Assert.DoesNotContain(card.History!, h => h.Id == comment.Id);
        Assert.Contains(card.History!, h => h.Id == keep.Id);
        Assert.Equal(1, card.CommentCount);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<KanbanDbContext>();
        Assert.False(await db.WorkItemHistory.AnyAsync(h => h.Id == comment.Id));
        Assert.False(await db.CommentRevisions.AnyAsync(r => r.HistoryId == comment.Id));
        Assert.False(await db.WorkItemHistory.AnyAsync(h => h.Comment!.Contains("wrong card")));
    }

    [Fact]
    public async Task ACommentPostedWithAMove_KeepsTheMove_AndLosesOnlyTheComment()
    {
        var id = await CardBy("Robert");
        var ui = _factory.CreateUiClient("Robert");
        await ui.PatchAsJsonAsync($"/api/ui/workitems/{id}", new { state = "Active", comment = "Starting on it." });
        var moved = (await Card(id))!.History!.Single(h => h.Comment == "Starting on it.");

        Assert.Equal(HttpStatusCode.NoContent, (await ui.DeleteAsync(Path(id, moved.Id))).StatusCode);

        var card = (await Card(id))!;
        var entry = Assert.Single(card.History!, h => h.Id == moved.Id);
        Assert.Null(entry.Comment);
        Assert.Contains("State", entry.ChangedFields.Keys);
        Assert.Equal(WorkItemState.Active, card.State);
        Assert.Equal(0, card.CommentCount);
        Assert.Null(card.LastHumanCommentAt);
    }

    [Fact]
    public async Task DeletingTheQuestion_TakesTheCardOffTheAgentsList_AndShowsInPolls()
    {
        var id = await CardBy("Robert");
        var ui = _factory.CreateUiClient("Robert");
        var agent = _factory.CreateAgentClient("Fixer-Bot");
        var first = await Comment(ui, $"/api/ui/workitems/{id}/comments", "Please check the export.");
        await Comment(agent, $"/api/v1/workitems/{id}/comments", "Checked, works for me.");
        await Task.Delay(20);
        var question = await Comment(ui, $"/api/ui/workitems/{id}/comments", "And the import?");
        var asked = (await Card(id))!;
        Assert.Equal(DiscussionStatus.AwaitingAgent, asked.DiscussionStatus);

        await Task.Delay(20);
        await ui.DeleteAsync(Path(id, question.Id));

        var since = Uri.EscapeDataString(asked.UpdatedAt.ToString("O"));
        var polled = await agent.GetFromJsonAsync<List<WorkItemDto>>($"/api/v1/workitems?updatedSince={since}", KanbanApiFactory.Json);
        var card = Assert.Single(polled!, w => w.Id == id);
        Assert.NotEqual(DiscussionStatus.AwaitingAgent, card.DiscussionStatus);
        Assert.Equal(first.ChangeDate, card.LastHumanCommentAt);
        Assert.Equal("Robert", card.LastHumanCommentBy);
        Assert.Equal(2, card.CommentCount);
        Assert.Equal("Robert", card.LastModifiedBy);
    }

    [Fact]
    public async Task PicturesInTheComment_AreDeleted_UnlessSomethingElseUsesThem()
    {
        var id = await CardBy("Robert");
        var ui = _factory.CreateUiClient("Robert");
        var onlyHere = await UploadPicture("only-here.png");
        var alsoInDescription = await UploadPicture("shared.png");
        var inEarlierVersion = await UploadPicture("earlier.png");
        var other = await CardBy("Robert");
        await ui.PatchAsJsonAsync($"/api/ui/workitems/{other}", new { description = $"See {alsoInDescription}" });

        var comment = await Comment(ui, $"/api/ui/workitems/{id}/comments", $"Screens: {onlyHere} {alsoInDescription} {inEarlierVersion}");
        await ui.PutAsJsonAsync(Path(id, comment.Id), new { text = $"Screens: {onlyHere} {alsoInDescription}" });
        await ui.DeleteAsync(Path(id, comment.Id));

        static string Url(string markdown) => "/" + markdown[(markdown.IndexOf('(') + 1)..^1];
        Assert.Equal(HttpStatusCode.NotFound, (await ui.GetAsync(Url(onlyHere))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ui.GetAsync(Url(inEarlierVersion))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ui.GetAsync(Url(alsoInDescription))).StatusCode);
    }

    [Fact]
    public async Task SomeoneElsesComment_Is403_AndAnAgentsToo()
    {
        var id = await CardBy("Robert");
        var mine = await Comment(_factory.CreateUiClient("Robert"), $"/api/ui/workitems/{id}/comments", "Mine.");
        var agents = await Comment(_factory.CreateAgentClient("Claude-Code-Agent-v1"), $"/api/v1/workitems/{id}/comments", "Answer.");

        var response = await _factory.CreateUiClient("Alex").DeleteAsync(Path(id, mine.Id));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("Only Robert can delete", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, (await _factory.CreateUiClient("Claude-Code-Agent-v1").DeleteAsync(Path(id, agents.Id))).StatusCode);
        Assert.Equal(2, (await Card(id))!.CommentCount);
    }

    [Fact]
    public async Task MissingOrNonComment_Is404()
    {
        var id = await CardBy("Robert");
        var comment = await Comment(_factory.CreateUiClient("Robert"), $"/api/ui/workitems/{id}/comments", "Text.");
        var ui = _factory.CreateUiClient("Robert");
        var creationEntry = (await Card(id))!.History!.First(h => h.Comment is null);

        Assert.Equal(HttpStatusCode.NotFound, (await ui.DeleteAsync(Path(id, creationEntry.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ui.DeleteAsync(Path(id, 999_999))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ui.DeleteAsync(Path(999_999, comment.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ui.DeleteAsync(Path(id, comment.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ui.DeleteAsync(Path(id, comment.Id))).StatusCode);
    }

    [Fact]
    public async Task DeletingNeedsTheBoardsAntiForgeryHeader_AndIsNotOnTheAgentApi()
    {
        var id = await CardBy("Robert");
        var comment = await Comment(_factory.CreateUiClient("Robert"), $"/api/ui/workitems/{id}/comments", "Text.");

        var bare = _factory.CreateClient();
        bare.DefaultRequestHeaders.Add("X-User-Display-Name", "Robert");
        Assert.NotEqual(HttpStatusCode.NoContent, (await bare.DeleteAsync(Path(id, comment.Id))).StatusCode);

        var v1 = await _factory.CreateAgentClient().DeleteAsync($"/api/v1/workitems/{id}/comments/{comment.Id}");
        Assert.Contains(v1.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
        Assert.Equal(1, (await Card(id))!.CommentCount);
    }
}
