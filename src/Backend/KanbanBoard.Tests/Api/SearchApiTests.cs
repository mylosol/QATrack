using System.Net;
using System.Net.Http.Json;
using KanbanBoard.Api.Models;
using KanbanBoard.Api.Services;
using KanbanBoard.Tests.Infrastructure;

namespace KanbanBoard.Tests.Api;

/// <summary>1.26.0: the board's search box finds cases by title, description and comments.</summary>
public sealed class SearchApiTests : IClassFixture<KanbanApiFactory>
{
    private readonly KanbanApiFactory _factory;

    public SearchApiTests(KanbanApiFactory factory)
    {
        _factory = factory;
    }

    /// <summary>A word no other test uses, so results are predictable on the shared board.</summary>
    private static string Word(string prefix) => prefix + Guid.NewGuid().ToString("N")[..8];

    private async Task<WorkItemDto> Create(string title, string? description = null, string state = "New")
    {
        var response = await _factory.CreateUiClient("Robert").PostAsJsonAsync("/api/ui/workitems",
            new { title, type = "Bug", description, state });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkItemDto>(KanbanApiFactory.Json))!;
    }

    private Task<HttpResponseMessage> Comment(int id, string text, bool asAgent = false) => asAgent
        ? _factory.CreateAgentClient("Fixer-Bot").PostAsJsonAsync($"/api/v1/workitems/{id}/comments", new { text })
        : _factory.CreateUiClient("Robert").PostAsJsonAsync($"/api/ui/workitems/{id}/comments", new { text });

    private async Task<SearchResultsDto> Search(string q) =>
        (await _factory.CreateUiClient().GetFromJsonAsync<SearchResultsDto>($"/api/ui/search?q={Uri.EscapeDataString(q)}", KanbanApiFactory.Json))!;

    [Fact]
    public async Task FindsTitleDescriptionAndCommentMatches_TitleFirst_AndSaysWhere()
    {
        var word = Word("timeout");
        var inComment = await Create("Report export is slow");
        await Comment(inComment.Id, $"Seen again: {word} after 30 s.", asAgent: true);
        var inDescription = await Create("Checkout fails", $"Steps:\n\n1. Pay\n2. **{word}** shown");
        var inTitle = await Create($"Login {word} on slow networks");

        var found = await Search(word.ToUpperInvariant()); // case is ignored

        Assert.Equal(3, found.Total);
        Assert.Equal(new[] { inTitle.Id, inDescription.Id, inComment.Id }, found.Results.Select(r => r.Id));
        Assert.True(found.Results[0].TitleMatch);
        Assert.Null(found.Results[0].Description);
        Assert.Equal($"Steps: Pay {word} shown", found.Results[1].Description); // Markdown reduced to words
        var comment = Assert.Single(found.Results[2].Comments);
        Assert.Equal($"Seen again: {word} after 30 s.", comment.Snippet);
        Assert.True(comment.IsAiAction);
        Assert.Equal("Fixer-Bot", comment.AgentName);
        Assert.False(found.Results[2].TitleMatch);
    }

    [Fact]
    public async Task EveryWordMustAppear_SomewhereInTheCase_AndQuotesKeepAPhrase()
    {
        var a = Word("alpha");
        var b = Word("bravo");
        var both = await Create($"{a} in the title", $"and {b} in the description");
        await Create($"only {a} here");

        Assert.Equal(new[] { both.Id }, (await Search($"{a} {b}")).Results.Select(r => r.Id));
        Assert.Empty((await Search($"\"{a} {b}\"")).Results);
        Assert.Equal(new[] { both.Id }, (await Search($"\"{a} in the title\"")).Results.Select(r => r.Id));
    }

    [Fact]
    public async Task ImageAndLinkAddresses_DoNotMatch_OnlyTheirWords()
    {
        var word = Word("pic");
        await Create(Word("t"), $"![shot.png](api/ui/attachments/{word}) and [the docs](https://example.com/{word})");
        var linked = await Create(Word("t"), $"See [{word} notes](https://example.com/x)");

        Assert.Equal(new[] { linked.Id }, (await Search(word)).Results.Select(r => r.Id));
    }

    [Fact]
    public async Task WordsMatchFromTheStartOfAWord()
    {
        var stem = Word("sync");
        var card = await Create($"The {stem}timeout fires");
        await Create($"pre{stem} is not a match");

        Assert.Equal(new[] { card.Id }, (await Search(stem)).Results.Select(r => r.Id));
        // "timeout" only appears inside "{stem}timeout", not at the start of a word.
        Assert.Empty((await Search($"{stem} timeout")).Results);
    }

    [Fact]
    public async Task ManyMatchingComments_ListTheNewestFew_AndCountTheRest()
    {
        var word = Word("again");
        var card = await Create(Word("t"));
        for (var i = 1; i <= 5; i++)
        {
            await Comment(card.Id, $"{word} number {i}");
        }

        var result = Assert.Single((await Search(word)).Results);
        Assert.Equal(new[] { $"{word} number 5", $"{word} number 4", $"{word} number 3" }, result.Comments.Select(c => c.Snippet));
        Assert.Equal(2, result.MoreComments);
        Assert.False(result.Comments[0].IsAiAction);
        Assert.Equal("Robert", result.Comments[0].Author);
    }

    [Fact]
    public async Task ClosedCasesAreFound_RemovedOnesAreNot()
    {
        var word = Word("dup");
        var closed = await Create($"{word} closed", state: "Closed");
        var removed = await Create($"{word} removed");
        await _factory.CreateUiClient().PatchAsJsonAsync($"/api/ui/workitems/{removed.Id}", new { state = "Removed" });

        var result = Assert.Single((await Search(word)).Results);
        Assert.Equal(closed.Id, result.Id);
        Assert.Equal(WorkItemState.Closed, result.State);
    }

    [Fact]
    public async Task AnEmptyQuery_FindsNothing_AndTheEndpointIsBoardOnly()
    {
        var empty = await Search("   ");
        Assert.Empty(empty.Terms);
        Assert.Equal(0, empty.Total);

        var agent = await _factory.CreateAgentClient().GetAsync("/api/ui/search?q=x");
        Assert.False(agent.IsSuccessStatusCode);
    }

    [Theory]
    [InlineData("login timeout", new[] { "login", "timeout" })]
    [InlineData("  \"login timeout\"  edge ", new[] { "login timeout", "edge" })]
    [InlineData("\"unclosed quote", new[] { "unclosed", "quote" })]
    [InlineData("Same same SAME", new[] { "Same" })]
    [InlineData("a b c d e f g h i j", new[] { "a", "b", "c", "d", "e", "f", "g", "h" })]
    public void ParseTerms_SplitsWords_KeepsQuotedPhrases(string query, string[] expected)
    {
        Assert.Equal(expected, SearchService.ParseTerms(query));
    }

    [Fact]
    public void Snippet_CutsAroundTheMatch_AtWordBoundaries()
    {
        var text = string.Join(' ', Enumerable.Range(1, 60).Select(i => $"word{i}")) + " needle " + string.Join(' ', Enumerable.Range(61, 60).Select(i => $"word{i}"));
        var snippet = SearchService.Snippet(text, new[] { SearchService.Matcher("needle") });
        Assert.StartsWith("…", snippet);
        Assert.EndsWith("…", snippet);
        Assert.Contains("needle", snippet);
        Assert.Matches(@"^…word\d+ ", snippet); // starts on a whole word
        Assert.True(snippet.Length <= 165);
    }
}
