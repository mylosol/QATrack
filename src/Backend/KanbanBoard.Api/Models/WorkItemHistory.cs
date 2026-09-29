namespace KanbanBoard.Api.Models;

/// <summary>
/// Immutable audit trail row (spec 3.2). One row is written per create, update
/// or comment. Rows are never updated or deleted by the application.
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
}
