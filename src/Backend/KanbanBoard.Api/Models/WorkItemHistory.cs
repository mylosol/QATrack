namespace KanbanBoard.Api.Models;

/// <summary>
/// Audit trail row (spec 3.2). One row is written per create, update or
/// comment. Rows are never deleted by the application; the only change ever
/// made to one is its author editing the comment text (1.13.0), and the
/// replaced text is kept as a <see cref="CommentRevision"/>.
/// </summary>
public class WorkItemHistory
{
    public int Id { get; set; }

    /// <summary>Foreign key to <see cref="WorkItem.Id"/>.</summary>
    public int WorkItemId { get; set; }

    public WorkItem? WorkItem { get; set; }

    public DateTime ChangeDate { get; set; }

    /// <summary>Human display name, or the agent identity for AI actions.</summary>
    public string Author { get; set; } = string.Empty;

    /// <summary>True when the change came through the secured AI agent API.</summary>
    public bool IsAiAction { get; set; }

    /// <summary>Agent identity (X-Agent-Identity) for AI actions; null for humans.</summary>
    public string? AgentName { get; set; }

    /// <summary>
    /// JSON object of field diffs: <c>{"State":{"old":"New","new":"Active"}}</c>.
    /// An empty object (<c>{}</c>) denotes a comment-only entry.
    /// </summary>
    public string ChangedFieldsJson { get; set; } = "{}";

    /// <summary>Optional discussion comment or automated test output.</summary>
    public string? Comment { get; set; }

    /// <summary>When the comment was last edited by its author (1.13.0); null if never.</summary>
    public DateTime? EditedAt { get; set; }

    /// <summary>Earlier versions of <see cref="Comment"/>, oldest first.</summary>
    public List<CommentRevision> Revisions { get; set; } = new();
}

/// <summary>
/// The text a comment had before an edit (1.13.0). Kept so editing a comment
/// never destroys the audit trail.
/// </summary>
public class CommentRevision
{
    public int Id { get; set; }

    /// <summary>Foreign key to <see cref="WorkItemHistory.Id"/>.</summary>
    public int HistoryId { get; set; }

    public WorkItemHistory? History { get; set; }

    /// <summary>The comment text before the edit.</summary>
    public string Comment { get; set; } = string.Empty;

    /// <summary>When it was replaced.</summary>
    public DateTime ReplacedAt { get; set; }

    /// <summary>Who replaced it (always the comment's author).</summary>
    public string ReplacedBy { get; set; } = string.Empty;
}
