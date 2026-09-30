using System.Net;
using System.Net.Http.Json;
using KanbanBoard.Api.Models;
using KanbanBoard.Tests.Infrastructure;

namespace KanbanBoard.Tests.Api;

/// <summary>1.8.0: an agent polling for changes and human comments over HTTP.</summary>
public sealed class ChangeDetectionApiTests : IClassFixture<KanbanApiFactory>
{
    private readonly KanbanApiFactory _factory;

    public ChangeDetectionApiTests(KanbanApiFactory factory)
    {
        _factory = factory;
    }

    private static string Since(DateTime utc) => Uri.EscapeDataString(utc.ToString("O"));

    [Fact]
    public async Task Agent_SeesAHumanCommentOnACardItAlreadyHandled()
    {
        var agent = _factory.CreateAgentClient("Poller-Bot");
        var human = _factory.CreateUiClient("Robert");

        var created = await (await agent.PostAsJsonAsync("/api/v1/workitems", new { title = "Poll me", type = "Bug" }))
            .Content.ReadFromJsonAsync<WorkItemDto>(KanbanApiFactory.Json);
        await agent.PostAsJsonAsync($"/api/v1/workitems/{created!.Id}/comments", new { text = "Investigating." });
        var afterAgent = await agent.GetFromJsonAsync<WorkItemDto>($"/api/v1/workitems/{created.Id}", KanbanApiFactory.Json);
        var lastSeen = afterAgent!.UpdatedAt;

        var quiet = await agent.GetFromJsonAsync<List<WorkItemDto>>($"/api/v1/workitems?updatedSince={Since(lastSeen)}", KanbanApiFactory.Json);
        Assert.DoesNotContain(quiet!, w => w.Id == created.Id);
        Assert.Null(afterAgent.LastHumanCommentAt);

        await Task.Delay(20); // distinct timestamps on a real clock
        var comment = await human.PostAsJsonAsync($"/api/ui/workitems/{created.Id}/comments", new { text = "Also happens on login." });
        Assert.Equal(HttpStatusCode.Created, comment.StatusCode);

        var changed = await agent.GetFromJsonAsync<List<WorkItemDto>>($"/api/v1/workitems?updatedSince={Since(lastSeen)}", KanbanApiFactory.Json);
        var card = Assert.Single(changed!, w => w.Id == created.Id);
        Assert.Equal("Robert", card.LastHumanCommentBy);
        Assert.True(card.LastHumanCommentAt > lastSeen);

        // The board reports it too.
        var board = await agent.GetFromJsonAsync<BoardDto>($"/api/v1/board?updatedSince={Since(lastSeen)}", KanbanApiFactory.Json);
        Assert.Contains(board!.Columns.SelectMany(c => c.Items), w => w.Id == created.Id && w.LastHumanCommentBy == "Robert");

        // And the comment text is in the item's history.
        var detail = await agent.GetFromJsonAsync<WorkItemDto>($"/api/v1/workitems/{created.Id}", KanbanApiFactory.Json);
        var entry = detail!.History!.Last();
        Assert.False(entry.IsAiAction);
        Assert.Equal("Also happens on login.", entry.Comment);
    }

    [Fact]
    public async Task UpdatedSince_AcceptsOffsets_AndRejectsGarbage()
    {
        var agent = _factory.CreateAgentClient();
        var offset = Uri.EscapeDataString("2026-09-30T14:00:00+02:00");
        Assert.Equal(HttpStatusCode.OK, (await agent.GetAsync($"/api/v1/workitems?updatedSince={offset}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await agent.GetAsync("/api/v1/workitems?updatedSince=yesterday")).StatusCode);
    }

    [Fact]
    public async Task Agent_FindsUnansweredHumanComments_ViaMetaAndTheDiscussionFilter()
    {
        var agent = _factory.CreateAgentClient("Reply-Bot");
        var human = _factory.CreateUiClient("Robert");
        var created = await (await agent.PostAsJsonAsync("/api/v1/workitems", new { title = "Talk to me", type = "Bug" }))
            .Content.ReadFromJsonAsync<WorkItemDto>(KanbanApiFactory.Json);

        await human.PostAsJsonAsync($"/api/ui/workitems/{created!.Id}/comments", new { text = "Please retest on 2.4.2." });

        var meta = await agent.GetFromJsonAsync<ApiMetaDto>("/api/v1/meta", KanbanApiFactory.Json);
        Assert.True(meta!.AwaitingAgentCount >= 1);
        Assert.Contains("discussion=AwaitingAgent", meta.Instructions);
        var waiting = await agent.GetFromJsonAsync<List<WorkItemDto>>("/api/v1/workitems?discussion=AwaitingAgent", KanbanApiFactory.Json);
        Assert.Contains(waiting!, w => w.Id == created.Id && w.DiscussionStatus == DiscussionStatus.AwaitingAgent);

        await agent.PostAsJsonAsync($"/api/v1/workitems/{created.Id}/comments", new { text = "Retested: fixed." });

        var after = await agent.GetFromJsonAsync<List<WorkItemDto>>("/api/v1/workitems?discussion=AwaitingAgent", KanbanApiFactory.Json);
        Assert.DoesNotContain(after!, w => w.Id == created.Id);

        // The human sees an unread reply until they open the card.
        var card = await human.GetFromJsonAsync<WorkItemDto>($"/api/ui/workitems/{created.Id}", KanbanApiFactory.Json);
        Assert.Equal(DiscussionStatus.UnreadReply, card!.DiscussionStatus);
        Assert.Equal(HttpStatusCode.NoContent, (await human.PostAsync($"/api/ui/workitems/{created.Id}/read", null)).StatusCode);
        Assert.Null((await human.GetFromJsonAsync<WorkItemDto>($"/api/ui/workitems/{created.Id}", KanbanApiFactory.Json))!.DiscussionStatus);
    }

    [Fact]
    public async Task MarkRead_NeedsTheAntiForgeryHeader_AndIsNotOnTheAgentApi()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await _factory.CreateClient().PostAsync("/api/ui/workitems/1/read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _factory.CreateAgentClient().PostAsync("/api/v1/workitems/1/read", null)).StatusCode);
    }
}
