using System.ComponentModel.DataAnnotations;

namespace KanbanBoard.Api.Models;

/// <summary>
/// A single card on the board (spec 3.1). All timestamps are stored and returned in UTC.
/// </summary>
public class WorkItem
{
    /// <summary>Autoincrement primary key.</summary>
    public int Id { get; set; }

    /// <summary>Short summary, required, max 255 characters.</summary>
    [MaxLength(WorkItemDefaults.TitleMaxLength)]
    public string Title { get; set; } = string.Empty;

    /// <summary>Markdown body. Rendered client-side through a sanitizer.</summary>
    public string? Description { get; set; }

    public WorkItemType Type { get; set; } = WorkItemType.Task;

    public WorkItemState State { get; set; } = WorkItemState.New;

    /// <summary>1-Critical, 2-High, 3-Medium, 4-Low.</summary>
    public int Priority { get; set; } = WorkItemDefaults.Priority;

    /// <summary>One of <see cref="WorkItemDefaults.Severities"/>.</summary>
    public string Severity { get; set; } = WorkItemDefaults.Severity;

    public string? AssignedTo { get; set; }

    public string AreaPath { get; set; } = WorkItemDefaults.AreaPath;

    public string IterationPath { get; set; } = WorkItemDefaults.IterationPath;

    /// <summary>
    /// Sticky flag: true once any AI agent has modified this item through the
    /// secured <c>/api/v1</c> API. Later human edits do not clear it, so the
    /// "AI-modified" quick filter reliably finds every agent-touched card.
    /// </summary>
    public bool AiModified { get; set; }

    /// <summary>Identity (X-Agent-Identity) of the most recent AI agent to modify this item.</summary>
    public string? AiAgentIdentity { get; set; }

    /// <summary>Display name of the last human or agent to change the item.</summary>
    public string LastModifiedBy { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>Program this item belongs to (optional).</summary>
    public int? ProgramId { get; set; }

    public WorkProgram? Program { get; set; }

    /// <summary>Version of the program the bug was found in (e.g. "2.4.1"). Optional; the board shows and edits it for Bugs only.</summary>
    public string? ProgramVersion { get; set; }

    /// <summary>When a human (not an AI agent) last wrote a comment on this item (UTC), or null. If it is newer than the last one you read, GET the item and read its history. Maintained by every mutation (1.8.0).</summary>
    public DateTime? LastHumanCommentAt { get; set; }

    /// <summary>Who wrote that most recent human comment.</summary>
    public string? LastHumanCommentBy { get; set; }

    /// <summary>When an AI agent last wrote a comment (1.9.0), or null.</summary>
    public DateTime? LastAgentCommentAt { get; set; }

    /// <summary>Which agent wrote that comment.</summary>
    public string? LastAgentCommentBy { get; set; }

    /// <summary>Number of history entries carrying a comment (1.9.0).</summary>
    public int CommentCount { get; set; }

    /// <summary>Number of files currently attached (removed ones excluded) (1.14.0).</summary>
    public int FileCount { get; set; }

    /// <summary>When a human last opened the card while it had an unread agent reply (1.9.0).</summary>
    public DateTime? HumanReadAt { get; set; }

    /// <summary>
    /// The Idempotency-Key an in-app report was filed with (1.13.0): a resend
    /// with the same key returns this card instead of filing a duplicate.
    /// </summary>
    public string? ReportKey { get; set; }

    /// <summary>Tags (many-to-many through the WorkItemTag table).</summary>
    public List<Tag> Tags { get; set; } = new();

    /// <summary>Audit trail, newest entries appended last.</summary>
    public List<WorkItemHistory> History { get; set; } = new();

    /// <summary>Attached files, including removed ones (1.14.0).</summary>
    public List<WorkItemFile> Files { get; set; } = new();
}
