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

    /// <summary>Tags (many-to-many through the WorkItemTag table).</summary>
    public List<Tag> Tags { get; set; } = new();

    /// <summary>Audit trail, newest entries appended last.</summary>
    public List<WorkItemHistory> History { get; set; } = new();
}
