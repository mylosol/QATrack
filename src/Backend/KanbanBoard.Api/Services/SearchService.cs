using System.Text.RegularExpressions;
using KanbanBoard.Api.Data;
using KanbanBoard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace KanbanBoard.Api.Services;

/// <summary>
/// Board search (1.26.0): finds cases by their title, description and comments,
/// for the search box on the board. Every word typed must appear somewhere in the
/// case (in any of the three); "quoted words" must appear together. Words match
/// from the start of a word ("time" finds "timeout", "out" doesn't find "checkout").
/// </summary>
/// <remarks>
/// Matching is done in memory on plain text: Markdown is reduced to its words, so
/// image and link addresses (e.g. "api/ui/attachments/...") never match. The board
/// holds hundreds of cases, so loading their text per search is cheap.
/// </remarks>
public sealed partial class SearchService
{
    public const int DefaultTop = 50;
    public const int MaxTop = 100;
    public const int MaxTerms = 8;
    public const int MaxQueryLength = 200;
    /// <summary>Matching comments listed per case; the rest are counted.</summary>
    public const int CommentsPerResult = 3;

    private readonly KanbanDbContext _db;

    public SearchService(KanbanDbContext db)
    {
        _db = db;
    }

    public async Task<SearchResultsDto> SearchAsync(string? query, int? top, CancellationToken ct = default)
    {
        var terms = ParseTerms(query);
        if (terms.Count == 0)
        {
            return new SearchResultsDto { Terms = terms };
        }

        var matchers = terms.Select(Matcher).ToList();
        bool Contains(string text, Regex matcher) => matcher.IsMatch(text);

        var items = await _db.WorkItems.AsNoTracking()
            .Where(w => w.State != WorkItemState.Removed)
            .Select(w => new { w.Id, w.Title, w.Description, w.Type, w.State, w.Priority, w.UpdatedAt })
            .ToListAsync(ct);
        var comments = (await _db.WorkItemHistory.AsNoTracking()
                .Where(h => h.Comment != null && h.WorkItem!.State != WorkItemState.Removed)
                .Select(h => new { h.Id, h.WorkItemId, h.Author, h.IsAiAction, h.AgentName, h.ChangeDate, h.Comment })
                .ToListAsync(ct))
            .Select(h => new { h.Id, h.WorkItemId, h.Author, h.IsAiAction, h.AgentName, h.ChangeDate, Text = PlainText(h.Comment) })
            .ToLookup(h => h.WorkItemId);

        var results = new List<(SearchResultDto Result, int Score, DateTime UpdatedAt)>();
        foreach (var item in items)
        {
            var description = PlainText(item.Description);
            var itemComments = comments[item.Id].ToList();
            // Every term somewhere in the case.
            if (!matchers.All(t => Contains(item.Title, t) || Contains(description, t) || itemComments.Any(c => Contains(c.Text, t))))
            {
                continue;
            }

            var titleMatch = matchers.Any(t => Contains(item.Title, t));
            var descriptionMatch = matchers.Any(t => Contains(description, t));
            var matchingComments = itemComments
                .Where(c => matchers.Any(t => Contains(c.Text, t)))
                .OrderByDescending(c => c.ChangeDate).ThenByDescending(c => c.Id)
                .ToList();
            var result = new SearchResultDto
            {
                Id = item.Id,
                Title = item.Title,
                Type = item.Type,
                State = item.State,
                Priority = item.Priority,
                UpdatedAt = item.UpdatedAt,
                TitleMatch = titleMatch,
                Description = descriptionMatch ? Snippet(description, matchers) : null,
                Comments = matchingComments.Take(CommentsPerResult).Select(c => new SearchCommentDto
                {
                    HistoryId = c.Id,
                    Author = c.Author,
                    IsAiAction = c.IsAiAction,
                    AgentName = c.AgentName,
                    ChangeDate = c.ChangeDate,
                    Snippet = Snippet(c.Text, matchers),
                }).ToList(),
                MoreComments = Math.Max(0, matchingComments.Count - CommentsPerResult),
            };
            // Title hits first (all terms in the title best), then description, then comments.
            var score = matchers.Count(t => Contains(item.Title, t)) * 100
                        + (descriptionMatch ? 10 : 0)
                        + Math.Min(matchingComments.Count, 9);
            results.Add((result, score, item.UpdatedAt));
        }

        return new SearchResultsDto
        {
            Terms = terms,
            Total = results.Count,
            Results = results
                .OrderByDescending(r => r.Score).ThenByDescending(r => r.UpdatedAt).ThenByDescending(r => r.Result.Id)
                .Take(Math.Clamp(top ?? DefaultTop, 1, MaxTop))
                .Select(r => r.Result)
                .ToList(),
        };
    }

    /// <summary>
    /// Search words: whitespace separates them, "double quotes" keep a phrase together.
    /// Case is ignored later; duplicates and words beyond <see cref="MaxTerms"/> are dropped.
    /// The board parses queries the same way (services/search.ts).
    /// </summary>
    public static IReadOnlyList<string> ParseTerms(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<string>();
        }

        var text = query.Length > MaxQueryLength ? query[..MaxQueryLength] : query;
        var terms = new List<string>();
        foreach (Match m in TermPattern().Matches(text))
        {
            // An unclosed quote is just part of the typing: drop it.
            var term = Regex.Replace(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value, @"\s+", " ").Trim().Trim('"');
            if (term.Length > 0 && !terms.Contains(term, StringComparer.OrdinalIgnoreCase))
            {
                terms.Add(term);
            }
        }

        return terms.Take(MaxTerms).ToList();
    }

    /// <summary>
    /// Finds a term from the start of a word, ignoring case. The board highlights with
    /// the same rule (services/search.ts findMatches).
    /// </summary>
    public static Regex Matcher(string term) =>
        new(@"(?<![\p{L}\p{N}_])" + Regex.Escape(term), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The words of a Markdown text: images dropped, links reduced to their text, markup removed.</summary>
    public static string PlainText(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            return string.Empty;
        }

        var text = ImagePattern().Replace(markdown, " ");
        text = LinkPattern().Replace(text, "$1");
        text = HtmlTagPattern().Replace(text, " ");
        text = LineMarkupPattern().Replace(text, string.Empty);
        text = InlineMarkupPattern().Replace(text, string.Empty);
        return WhitespacePattern().Replace(text, " ").Trim();
    }

    /// <summary>
    /// About <paramref name="width"/> characters of <paramref name="text"/> around the first
    /// match of any term, cut at word boundaries, with "…" where text was left out.
    /// </summary>
    public static string Snippet(string text, IReadOnlyList<Regex> matchers, int width = 160)
    {
        if (text.Length <= width)
        {
            return text;
        }

        var first = matchers
            .Select(m => m.Match(text))
            .Where(m => m.Success)
            .Select(m => m.Index)
            .DefaultIfEmpty(0)
            .Min();
        var start = Math.Max(0, first - width / 3);
        if (start > 0)
        {
            var space = text.IndexOf(' ', start);
            start = space >= 0 && space < first ? space + 1 : start;
        }

        var end = Math.Min(text.Length, start + width);
        if (end < text.Length)
        {
            var space = text.LastIndexOf(' ', end);
            end = space > first ? space : end;
        }

        return (start > 0 ? "…" : string.Empty) + text[start..end].Trim() + (end < text.Length ? "…" : string.Empty);
    }

    [GeneratedRegex("\"([^\"]+)\"|(\\S+)")]
    private static partial Regex TermPattern();

    [GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)")]
    private static partial Regex ImagePattern();

    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex LinkPattern();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagPattern();

    [GeneratedRegex(@"(?m)^\s{0,3}(?:#{1,6}\s+|>\s?|[-*+]\s+|\d+\.\s+)")]
    private static partial Regex LineMarkupPattern();

    [GeneratedRegex(@"\*\*|__|~~|`+")]
    private static partial Regex InlineMarkupPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}
