using System.Net;
using System.Net.Http.Json;
using KanbanBoard.Api.Models;
using KanbanBoard.Tests.Infrastructure;

namespace KanbanBoard.Tests.Api;

/// <summary>1.28.0: "everything that doesn't have this tag", on the board and for agents.</summary>
public sealed class WithoutTagFilterApiTests : IClassFixture<KanbanApiFactory>
{
    private readonly KanbanApiFactory _factory;

    public WithoutTagFilterApiTests(KanbanApiFactory factory)
    {
        _factory = factory;
    }

    private async Task<int> Create(params string[] tags)
    {
        var response = await _factory.CreateUiClient().PostAsJsonAsync("/api/ui/workitems", new { title = "Tagged", type = "Bug", tags });
        return (await response.Content.ReadFromJsonAsync<WorkItemDto>(KanbanApiFactory.Json))!.Id;
    }

    private static string Unique(string name) => $"{name}-{Guid.NewGuid():N}"[..20];

    [Fact]
    public async Task WithoutTag_LeavesOutTheTaggedItems_AndKeepsUntaggedOnes()
    {
        var later = Unique("later");
        var hidden = await Create(later, "ui");
        var hiddenOtherCase = await Create(later.ToUpperInvariant());
        var shown = await Create("ui");
        var untagged = await Create();

        var ids = (await _factory.CreateAgentClient().GetFromJsonAsync<List<WorkItemDto>>(
            $"/api/v1/workitems?withoutTag={later}&top=1000", KanbanApiFactory.Json))!.Select(w => w.Id).ToList();

        Assert.DoesNotContain(hidden, ids);
        Assert.DoesNotContain(hiddenOtherCase, ids);
        Assert.Contains(shown, ids);
        Assert.Contains(untagged, ids);
    }

    [Fact]
    public async Task WithoutTag_CombinesWithTag_OnTheBoard()
    {
        var area = Unique("area");
        var later = Unique("later");
        var hidden = await Create(area, later);
        var shown = await Create(area);
        var otherArea = await Create(later);

        var board = (await _factory.CreateUiClient().GetFromJsonAsync<BoardDto>(
            $"/api/ui/board?tag={area}&withoutTag={later}", KanbanApiFactory.Json))!;
        var ids = board.Columns.SelectMany(c => c.Items).Select(w => w.Id).ToList();

        Assert.Equal(new[] { shown }, ids);
        Assert.DoesNotContain(hidden, ids);
        Assert.DoesNotContain(otherArea, ids);
    }

    [Fact]
    public async Task ABlankWithoutTag_IsNoFilter_AndATooLongOneIs400()
    {
        var id = await Create(Unique("x"));
        var agent = _factory.CreateAgentClient();

        var all = await agent.GetFromJsonAsync<List<WorkItemDto>>("/api/v1/workitems?withoutTag=%20&top=1000", KanbanApiFactory.Json);
        Assert.Contains(all!, w => w.Id == id);
        var tooLong = await agent.GetAsync($"/api/v1/workitems?withoutTag={new string('a', 500)}");
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
    }
}
