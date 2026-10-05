namespace KanbanBoard.Api.Models;

/// <summary>
/// A file (log, text output, archive) attached to a work item (1.14.0). The
/// bytes live in <see cref="Attachment"/> (stored once per content); this row
/// links them to the card under the name they were attached with.
/// </summary>
/// <remarks>
/// Removing a file only hides it (<see cref="RemovedAt"/>): the bytes and the
/// link stay for the audit trail, and the removal is recorded in history.
/// </remarks>
public class WorkItemFile
{
    public int Id { get; set; }

    /// <summary>Foreign key to <see cref="WorkItem.Id"/>.</summary>
    public int WorkItemId { get; set; }

    public WorkItem? WorkItem { get; set; }

    /// <summary>Foreign key to <see cref="Attachment.Id"/> (the stored bytes).</summary>
    public Guid AttachmentId { get; set; }

    public Attachment? Attachment { get; set; }

    /// <summary>Display name, as uploaded (path and unsafe characters removed).</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Detected from the bytes: text/plain, application/zip, application/gzip or application/x-7z-compressed.</summary>
    public string ContentType { get; set; } = string.Empty;

    public long Length { get; set; }

    public DateTime AddedAt { get; set; }

    public string AddedBy { get; set; } = string.Empty;

    /// <summary>True when an AI agent attached it.</summary>
    public bool IsAiAction { get; set; }

    /// <summary>When it was removed from the card; null while attached.</summary>
    public DateTime? RemovedAt { get; set; }

    public string? RemovedBy { get; set; }
}
