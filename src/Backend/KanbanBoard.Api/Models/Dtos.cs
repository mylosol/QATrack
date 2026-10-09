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

    /// <summary>
    /// UTC time the comment was last edited by its author (1.13.0), or null if it never was.
    /// An edit also moves the card's lastHumanCommentAt forward, so an edited comment is
    /// awaiting the agent again and shows up in updatedSince polls.
    /// </summary>
    public DateTime? EditedAt { get; init; }
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

    /// <summary>Assignee, if one was set. Optional and not shown on the board (since 1.6.0): the AI badge and history already identify the agent.</summary>
    public string? AssignedTo { get; init; }

    public string AreaPath { get; init; } = string.Empty;

    public string IterationPath { get; init; } = string.Empty;

    /// <summary>Program the item belongs to (e.g. "ProveOut"), or null.</summary>
    public string? Program { get; init; }

    /// <summary>Version of the program the bug was found in (e.g. "2.4.1"). Optional; the board shows and edits it for Bugs only.</summary>
    public string? ProgramVersion { get; init; }

    /// <summary>Tags, sorted alphabetically.</summary>
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    /// <summary>True once any AI agent has modified this item.</summary>
    public bool AiModified { get; init; }

    /// <summary>Most recent AI agent to modify the item.</summary>
    public string? AiAgentIdentity { get; init; }

    /// <summary>When a human (not an AI agent) last wrote a comment on this item (UTC), or null. If it is newer than the last one you read, GET the item and read its history.</summary>
    public DateTime? LastHumanCommentAt { get; init; }

    /// <summary>Who wrote that most recent human comment.</summary>
    public string? LastHumanCommentBy { get; init; }

    /// <summary>When an AI agent last commented on this item (UTC), or null.</summary>
    public DateTime? LastAgentCommentAt { get; init; }

    /// <summary>Which agent wrote that most recent agent comment.</summary>
    public string? LastAgentCommentBy { get; init; }

    /// <summary>Number of comments in the item's history.</summary>
    public int CommentCount { get; init; }

    /// <summary>
    /// AwaitingAgent: a human commented and no agent has replied yet - read the item's history and reply
    /// with a comment. UnreadReply: an agent replied and no human has read it yet. Null: nothing pending.
    /// </summary>
    public DiscussionStatus? DiscussionStatus { get; init; }

    public string LastModifiedBy { get; init; } = string.Empty;

    /// <summary>
    /// Who created the item (1.16.0): a person's board name, an AI agent identity, or, for issues
    /// reported from inside a program under test, "Name (in-app report)" ("In-app report" when
    /// the person gave no name). Never changes after creation.
    /// </summary>
    public string CreatedBy { get; init; } = string.Empty;

    public DateTime CreatedAt { get; init; }

    public DateTime UpdatedAt { get; init; }

    /// <summary>Full audit history (only populated on single-item requests).</summary>
    public IReadOnlyList<WorkItemHistoryDto>? History { get; init; }

    /// <summary>Number of files attached (logs, text output, archives) (1.14.0).</summary>
    public int FileCount { get; init; }

    /// <summary>
    /// Attached files, oldest first (only populated on single-item requests, like history) (1.14.0).
    /// Download one with GET /api/v1/workitems/{id}/files/{fileId}.
    /// </summary>
    public IReadOnlyList<WorkItemFileDto>? Files { get; init; }
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

    /// <summary>Assignee display name or email. Optional and not shown on the board (since 1.6.0): the AI badge and history already identify the agent.</summary>
    [MaxLength(WorkItemDefaults.ShortTextMaxLength)]
    public string? AssignedTo { get; set; }

    /// <summary>Area path. Defaults to "Tools\QA". Not shown on the board since 1.4.0; use program instead.</summary>
    [MaxLength(WorkItemDefaults.ShortTextMaxLength)]
    public string? AreaPath { get; set; }

    /// <summary>Iteration path. Defaults to "Current".</summary>
    [MaxLength(WorkItemDefaults.ShortTextMaxLength)]
    public string? IterationPath { get; set; }

    /// <summary>Program name (case-insensitive), one of GET /api/v1/programs. Omit for none.</summary>
    [MaxLength(WorkItemDefaults.ProgramNameMaxLength)]
    public string? Program { get; set; }

    /// <summary>Version of the program the bug was found in (e.g. "2.4.1"). Optional; the board shows and edits it for Bugs only. Max 64 characters.</summary>
    [MaxLength(WorkItemDefaults.ProgramVersionMaxLength)]
    public string? ProgramVersion { get; set; }

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

    /// <summary>Reassign; empty string unassigns. Optional and not shown on the board (since 1.6.0): the AI badge and history already identify the agent.</summary>
    [MaxLength(WorkItemDefaults.ShortTextMaxLength)]
    public string? AssignedTo { get; set; }

    [MaxLength(WorkItemDefaults.ShortTextMaxLength)]
    public string? AreaPath { get; set; }

    [MaxLength(WorkItemDefaults.ShortTextMaxLength)]
    public string? IterationPath { get; set; }

    /// <summary>Program name (case-insensitive). Empty string removes the program.</summary>
    [MaxLength(WorkItemDefaults.ProgramNameMaxLength)]
    public string? Program { get; set; }

    /// <summary>Version of the program the bug was found in (e.g. "2.4.1"). Optional; the board shows and edits it for Bugs only. Empty string clears it.</summary>
    [MaxLength(WorkItemDefaults.ProgramVersionMaxLength)]
    public string? ProgramVersion { get; set; }

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

/// <summary>A file attached to a work item (1.14.0).</summary>
public sealed class WorkItemFileDto
{
    /// <summary>File id, unique across the board.</summary>
    public int Id { get; init; }

    public int WorkItemId { get; init; }

    /// <summary>Name it was attached with, e.g. "app.log".</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>"text/plain" for logs and other text, or "application/zip", "application/gzip", "application/x-7z-compressed".</summary>
    public string ContentType { get; init; } = string.Empty;

    /// <summary>Size in bytes.</summary>
    public long Length { get; init; }

    /// <summary>Who attached it: a person's board name, a reporter, or an AI agent identity.</summary>
    public string AddedBy { get; init; } = string.Empty;

    /// <summary>True when an AI agent attached it.</summary>
    public bool IsAiAction { get; init; }

    /// <summary>UTC time it was attached.</summary>
    public DateTime AddedAt { get; init; }

    /// <summary>Download link for AI agents, relative to the site root, e.g. "api/v1/workitems/31/files/7" (send the API key).</summary>
    public string Url { get; init; } = string.Empty;
}

/// <summary>New text for a comment, edited by its author on the board (1.13.0).</summary>
public sealed class EditCommentRequest
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

    /// <summary>Exact (case-insensitive) assignee match; "unassigned" finds items with none. The board no longer shows assignees (1.6.0).</summary>
    [MaxLength(WorkItemDefaults.ShortTextMaxLength)]
    public string? AssignedTo { get; set; }

    /// <summary>Program name (case-insensitive).</summary>
    [MaxLength(WorkItemDefaults.ProgramNameMaxLength)]
    public string? Program { get; set; }

    /// <summary>Only items carrying this tag (case-insensitive).</summary>
    [MaxLength(WorkItemDefaults.TagMaxLength)]
    public string? Tag { get; set; }

    /// <summary>
    /// Only items whose discussion is in this state. AwaitingAgent lists every card with a human comment
    /// no agent has answered yet.
    /// </summary>
    public DiscussionStatus? Discussion { get; set; }

    /// <summary>
    /// Only items changed (fields, state, comments) strictly after this instant. Pass the newest
    /// <c>updatedAt</c> you have seen, e.g. 2026-09-30T14:05:12.3456789Z (always include the Z or an offset).
    /// </summary>
    public DateTimeOffset? UpdatedSince { get; set; }

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

    /// <summary>Distinct assignees on the board. Kept for compatibility; the board no longer shows assignees (1.6.0).</summary>
    public IReadOnlyList<string> Assignees { get; init; } = Array.Empty<string>();

    /// <summary>Available programs in display order.</summary>
    public IReadOnlyList<string> Programs { get; init; } = Array.Empty<string>();

    /// <summary>Every known tag, alphabetically (for suggestions and the tag filter).</summary>
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    /// <summary>Cards on the board awaiting an agent reply (whole board, ignoring filters).</summary>
    public int AwaitingAgentCount { get; init; }

    /// <summary>Cards with an agent reply no human has read yet (whole board, ignoring filters).</summary>
    public int UnreadReplyCount { get; init; }
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

/// <summary>One release's changes to the AI agent API.</summary>
public sealed class ApiChangeDto
{
    /// <summary>Release (Semantic Version) that introduced the change.</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>Release date (yyyy-MM-dd).</summary>
    public string Date { get; init; } = string.Empty;

    /// <summary>The X-API-Schema-Version this release produces, when known.</summary>
    public string? SchemaVersion { get; init; }

    /// <summary>True when existing calls must change. Breaking changes ship as a new /api/vN.</summary>
    public bool Breaking { get; init; }

    /// <summary>What changed, in plain language.</summary>
    public IReadOnlyList<string> Summary { get; init; } = Array.Empty<string>();
}

/// <summary>What an agent needs to know whether its understanding of this API is current.</summary>
public sealed class ApiMetaDto
{
    public string Name { get; init; } = "QATrack";

    /// <summary>Application Semantic Version.</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>API contract generation in the URL (v1). Breaking changes would move to v2.</summary>
    public string ApiVersion { get; init; } = "v1";

    /// <summary>Fingerprint of the OpenAPI document; same value as the X-API-Schema-Version header.</summary>
    public string SchemaVersion { get; init; } = string.Empty;

    /// <summary>OpenAPI 3.0 document to re-read when schemaVersion changes.</summary>
    public string OpenApiUrl { get; init; } = string.Empty;

    /// <summary>Human-readable API docs.</summary>
    public string DocsUrl { get; init; } = string.Empty;

    /// <summary>How agents should use schemaVersion and answer human comments.</summary>
    public string Instructions { get; init; } = string.Empty;

    /// <summary>
    /// Cards with a human comment no agent has answered yet. When above zero, list them with
    /// GET /api/v1/workitems?discussion=AwaitingAgent and reply to each.
    /// </summary>
    public int AwaitingAgentCount { get; init; }

    /// <summary>API changes, newest first (only those after ?since= when given).</summary>
    public IReadOnlyList<ApiChangeDto> Changes { get; init; } = Array.Empty<ApiChangeDto>();
}
