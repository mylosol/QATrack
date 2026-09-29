using System.ComponentModel.DataAnnotations;

namespace KanbanBoard.Api.Models;

// ---------------------------------------------------------------------------
// Data transfer objects. These are the only shapes that cross the HTTP
// boundary; EF entities are never serialized directly, which prevents
// over-posting (e.g. a client setting AiModified or CreatedAt itself).
// ---------------------------------------------------------------------------

/// <summary>A single field change inside a history entry.</summary>
/// <param name="Old">Value before the change (null when unset).</param>
/// <param name="New">Value after the change (null when cleared).</param>
public sealed record FieldChange(string? Old, string? New);

/// <summary>One audit trail entry (spec 3.2).</summary>
public sealed class WorkItemHistoryDto
{
    public int Id { get; init; }

    public int WorkItemId { get; init; }

    /// <summary>UTC timestamp of the change.</summary>
    public DateTime ChangeDate { get; init; }

    /// <summary>Human display name or AI agent identity.</summary>
    public string Author { get; init; } = string.Empty;

    /// <summary>True when made through the secured AI agent API.</summary>
    public bool IsAiAction { get; init; }

    /// <summary>AI agent identity (X-Agent-Identity) when <see cref="IsAiAction"/> is true.</summary>
    public string? AgentName { get; init; }

    /// <summary>Field diffs keyed by field name. Empty for comment-only entries.</summary>
    public IReadOnlyDictionary<string, FieldChange> ChangedFields { get; init; } =
        new Dictionary<string, FieldChange>();

    /// <summary>Discussion comment or automated test output, if any.</summary>
    public string? Comment { get; init; }
}

/// <summary>Work item as returned by the API.</summary>
public sealed class WorkItemDto
{
    public int Id { get; init; }

    public string Title { get; init; } = string.Empty;

    /// <summary>Markdown description.</summary>
    public string? Description { get; init; }

    public WorkItemType Type { get; init; }

    public WorkItemState State { get; init; }

    /// <summary>1-Critical, 2-High, 3-Medium, 4-Low.</summary>
    public int Priority { get; init; }

    /// <summary>"1 - Critical" | "2 - High" | "3 - Medium" | "4 - Low".</summary>
    public string Severity { get; init; } = string.Empty;

    public string? AssignedTo { get; init; }

    public string AreaPath { get; init; } = string.Empty;

    public string IterationPath { get; init; } = string.Empty;

    /// <summary>Program the item belongs to (e.g. "ProveOut"), or null.</summary>
    public string? Program { get; init; }

    /// <summary>Tags, sorted alphabetically.</summary>
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    /// <summary>True once any AI agent has modified this item.</summary>
    public bool AiModified { get; init; }

    /// <summary>Most recent AI agent to modify the item.</summary>
    public string? AiAgentIdentity { get; init; }

    public string LastModifiedBy { get; init; } = string.Empty;

    public DateTime CreatedAt { get; init; }

    public DateTime UpdatedAt { get; init; }

    /// <summary>Full audit history (only populated on single-item requests).</summary>
    public IReadOnlyList<WorkItemHistoryDto>? History { get; init; }
}

/// <summary>Payload to create a bug, feature or other work item.</summary>
public sealed class CreateWorkItemRequest
{
    /// <summary>Short summary (required, max 255 characters).</summary>
    [Required(AllowEmptyStrings = false)]
    [MaxLength(WorkItemDefaults.TitleMaxLength)]
    public string Title { get; set; } = string.Empty;

    /// <summary>Markdown description (repro steps, acceptance criteria, logs).</summary>
    [MaxLength(WorkItemDefaults.DescriptionMaxLength)]
    public string? Description { get; set; }

    /// <summary>Work item type. Required.</summary>
    [Required]
    public WorkItemType? Type { get; set; }

    /// <summary>Initial state. Defaults to New.</summary>
    public WorkItemState? State { get; set; }

    /// <summary>1-Critical, 2-High, 3-Medium, 4-Low. Defaults to 2.</summary>
    [Range(1, 4)]
    public int? Priority { get; set; }

    /// <summary>"1 - Critical" | "2 - High" | "3 - Medium" | "4 - Low". Defaults to "3 - Medium".</summary>
    [MaxLength(32)]
    public string? Severity { get; set; }

    /// <summary>Assignee display name or email.</summary>
    [MaxLength(WorkItemDefaults.ShortTextMaxLength)]
    public string? AssignedTo { get; set; }

    /// <summary>Area path. Defaults to "Tools\QA".</summary>
    [MaxLength(WorkItemDefaults.ShortTextMaxLength)]
    public string? AreaPath { get; set; }

    /// <summary>Iteration path. Defaults to "Current".</summary>
    [MaxLength(WorkItemDefaults.ShortTextMaxLength)]
    public string? IterationPath { get; set; }

    /// <summary>Program name (case-insensitive), one of GET /api/v1/programs. Omit for none.</summary>
    [MaxLength(WorkItemDefaults.ProgramNameMaxLength)]
    public string? Program { get; set; }

    /// <summary>Tags (max 20, each max 50 characters). New tags are created on first use.</summary>
    public List<string>? Tags { get; set; }

    /// <summary>Optional note recorded with the creation history entry.</summary>
    [MaxLength(WorkItemDefaults.CommentMaxLength)]
    public string? Comment { get; set; }
}

/// <summary>
/// Partial update. Omitted (null) properties are left unchanged. To clear the
/// assignee send an empty string for <see cref="AssignedTo"/>.
/// </summary>
public sealed class UpdateWorkItemRequest
{
    [MaxLength(WorkItemDefaults.TitleMaxLength)]
    public string? Title { get; set; }

    /// <summary>New markdown description. Send an empty string to clear it.</summary>
    [MaxLength(WorkItemDefaults.DescriptionMaxLength)]
    public string? Description { get; set; }

    public WorkItemType? Type { get; set; }

    /// <summary>Move the card: New, Active, Resolved, Closed or Removed.</summary>
    public WorkItemState? State { get; set; }

    [Range(1, 4)]
    public int? Priority { get; set; }

    [MaxLength(32)]
    public string? Severity { get; set; }

    /// <summary>Reassign. Empty string unassigns.</summary>
    [MaxLength(WorkItemDefaults.ShortTextMaxLength)]
    public string? AssignedTo { get; set; }

    [MaxLength(WorkItemDefaults.ShortTextMaxLength)]
    public string? AreaPath { get; set; }

    [MaxLength(WorkItemDefaults.ShortTextMaxLength)]
    public string? IterationPath { get; set; }

    /// <summary>Program name (case-insensitive). Empty string removes the program.</summary>
    [MaxLength(WorkItemDefaults.ProgramNameMaxLength)]
    public string? Program { get; set; }

    /// <summary>Replaces ALL tags when present; send [] to remove every tag. Omit to leave tags unchanged.</summary>
    public List<string>? Tags { get; set; }

    /// <summary>Optional note explaining the change; stored on the history entry.</summary>
    [MaxLength(WorkItemDefaults.CommentMaxLength)]
    public string? Comment { get; set; }
}

/// <summary>Discussion comment or automated test output.</summary>
public sealed class AddCommentRequest
{
    /// <summary>Comment text (markdown supported). Required.</summary>
    [Required(AllowEmptyStrings = false)]
    [MaxLength(WorkItemDefaults.CommentMaxLength)]
    public string Text { get; set; } = string.Empty;
}

/// <summary>Query-string filters for listing work items and the board.</summary>
public sealed class WorkItemQuery
{
    /// <summary>Filter by work item type.</summary>
    public WorkItemType? Type { get; set; }

    /// <summary>Filter by state.</summary>
    public WorkItemState? State { get; set; }

    /// <summary>Filter by AI-modified flag.</summary>
    public bool? AiModified { get; set; }

    /// <summary>Exact (case-insensitive) assignee match. Use "unassigned" for items with no assignee.</summary>
    [MaxLength(WorkItemDefaults.ShortTextMaxLength)]
    public string? AssignedTo { get; set; }

    /// <summary>Program name (case-insensitive).</summary>
    [MaxLength(WorkItemDefaults.ProgramNameMaxLength)]
    public string? Program { get; set; }

    /// <summary>Only items carrying this tag (case-insensitive).</summary>
    [MaxLength(WorkItemDefaults.TagMaxLength)]
    public string? Tag { get; set; }

    /// <summary>Maximum number of items to return (1-1000, default 500).</summary>
    [Range(1, 1000)]
    public int? Top { get; set; }
}

/// <summary>Board column with WIP metadata and its (filtered) cards.</summary>
public sealed class BoardColumnDto
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public WorkItemState State { get; init; }

    /// <summary>WIP limit, or null when unlimited.</summary>
    public int? WipLimit { get; init; }

    /// <summary>Total items in this column, ignoring filters (used for WIP checks).</summary>
    public int ItemCount { get; init; }

    /// <summary>True when <see cref="ItemCount"/> exceeds <see cref="WipLimit"/>.</summary>
    public bool IsOverWipLimit { get; init; }

    /// <summary>Cards in the column after applying the requested filters.</summary>
    public IReadOnlyList<WorkItemDto> Items { get; init; } = Array.Empty<WorkItemDto>();
}

/// <summary>Swimlane metadata.</summary>
public sealed record SwimlaneDto(int Id, string Name, int SortOrder, bool IsDefault);

/// <summary>Allowed values, handy for building forms and AI tool schemas.</summary>
public sealed class BoardMetadataDto
{
    public IReadOnlyList<string> Types { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> States { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Severities { get; init; } = Array.Empty<string>();

    public IReadOnlyDictionary<int, string> Priorities { get; init; } = new Dictionary<int, string>();

    /// <summary>Distinct assignees currently on the board (for filter dropdowns).</summary>
    public IReadOnlyList<string> Assignees { get; init; } = Array.Empty<string>();

    /// <summary>Available programs in display order.</summary>
    public IReadOnlyList<string> Programs { get; init; } = Array.Empty<string>();

    /// <summary>Every known tag, alphabetically (for suggestions and the tag filter).</summary>
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
}

/// <summary>Full board payload (spec 4.2 GET /api/v1/board).</summary>
public sealed class BoardDto
{
    public string Name { get; init; } = "QA Tools";

    public IReadOnlyList<BoardColumnDto> Columns { get; init; } = Array.Empty<BoardColumnDto>();

    public IReadOnlyList<SwimlaneDto> Swimlanes { get; init; } = Array.Empty<SwimlaneDto>();

    /// <summary>Number of items in the Removed state (hidden from the board).</summary>
    public int RemovedCount { get; init; }

    public BoardMetadataDto Metadata { get; init; } = new();
}

/// <summary>A program work items can belong to.</summary>
public sealed record ProgramDto(int Id, string Name, int SortOrder);

/// <summary>Adds a program to the dropdown.</summary>
public sealed class CreateProgramRequest
{
    /// <summary>Program name (max 64 characters, unique case-insensitively).</summary>
    [Required(AllowEmptyStrings = false)]
    [MaxLength(WorkItemDefaults.ProgramNameMaxLength)]
    public string Name { get; set; } = string.Empty;
}

/// <summary>An uploaded image.</summary>
public sealed class AttachmentDto
{
    public Guid Id { get; init; }

    public string FileName { get; init; } = string.Empty;

    /// <summary>image/png, image/jpeg, image/gif or image/webp (detected from the bytes).</summary>
    public string ContentType { get; init; } = string.Empty;

    public long Length { get; init; }

    /// <summary>Relative URL the board uses to display the image.</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>Ready-to-paste markdown, e.g. <c>![screenshot.png](api/ui/attachments/{id})</c>.</summary>
    public string Markdown { get; init; } = string.Empty;
}
