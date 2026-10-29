namespace KanbanBoard.Api.Models;

/// <summary>Answer of GET /api/ui/search (1.26.0). Board only; not part of the AI agent API.</summary>
public sealed class SearchResultsDto
{
    /// <summary>The words searched for, as parsed (quoted phrases kept together).</summary>
    public IReadOnlyList<string> Terms { get; init; } = Array.Empty<string>();

    /// <summary>How many cases match (more than <see cref="Results"/> holds when over the limit).</summary>
    public int Total { get; init; }

    /// <summary>Best matches first: title hits, then description, then comments; newest first within.</summary>
    public IReadOnlyList<SearchResultDto> Results { get; init; } = Array.Empty<SearchResultDto>();
}

/// <summary>One matching case and where it matched.</summary>
public sealed class SearchResultDto
{
    public int Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public WorkItemType Type { get; init; }

    public WorkItemState State { get; init; }

    public int Priority { get; init; }

    public DateTime UpdatedAt { get; init; }

    /// <summary>A search word appears in the title.</summary>
    public bool TitleMatch { get; init; }

    /// <summary>Plain-text excerpt of the description around the first match, or null when it doesn't match.</summary>
    public string? Description { get; init; }

    /// <summary>Matching comments, newest first (at most a few).</summary>
    public IReadOnlyList<SearchCommentDto> Comments { get; init; } = Array.Empty<SearchCommentDto>();

    /// <summary>Further matching comments not listed.</summary>
    public int MoreComments { get; init; }
}

/// <summary>A comment that matches.</summary>
public sealed class SearchCommentDto
{
    /// <summary>The history entry holding the comment (the card dialog scrolls to it).</summary>
    public int HistoryId { get; init; }

    public string Author { get; init; } = string.Empty;

    public bool IsAiAction { get; init; }

    public string? AgentName { get; init; }

    public DateTime ChangeDate { get; init; }

    /// <summary>Plain-text excerpt around the first match.</summary>
    public string Snippet { get; init; } = string.Empty;
}
