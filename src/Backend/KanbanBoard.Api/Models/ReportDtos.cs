using System.ComponentModel.DataAnnotations;

namespace KanbanBoard.Api.Models;

/// <summary>An issue reported from inside a program under test ("Report an issue").</summary>
public sealed class ReportIssueRequest
{
    /// <summary>Short summary (required, max 255 characters).</summary>
    [Required(AllowEmptyStrings = false)]
    [MaxLength(WorkItemDefaults.TitleMaxLength)]
    public string Title { get; set; } = string.Empty;

    /// <summary>What happened, steps to reproduce, expected vs actual. Markdown.</summary>
    [MaxLength(WorkItemDefaults.DescriptionMaxLength)]
    public string? Description { get; set; }

    /// <summary>Bug (default) for problems, Feature for suggestions. Other types are not accepted.</summary>
    public WorkItemType? Type { get; set; }

    /// <summary>"1 - Critical" | "2 - High" | "3 - Medium" (default) | "4 - Low"; "High" or "2" also work.</summary>
    [MaxLength(32)]
    public string? Severity { get; set; }

    /// <summary>The reporting program's name, matching a board program (case-insensitive), e.g. "ProveOut".</summary>
    [MaxLength(WorkItemDefaults.ProgramNameMaxLength)]
    public string? Program { get; set; }

    /// <summary>The reporting program's version, e.g. "2.4.1".</summary>
    [MaxLength(WorkItemDefaults.ProgramVersionMaxLength)]
    public string? ProgramVersion { get; set; }

    /// <summary>Name or email of the person reporting, if they gave one. Shown as the author.</summary>
    [MaxLength(64)]
    public string? Reporter { get; set; }

    /// <summary>
    /// Optional machine details (OS, build, settings, recent log lines). Added to the
    /// description as a code block. Max 20,000 characters.
    /// </summary>
    [MaxLength(20_000)]
    public string? Environment { get; set; }

    /// <summary>Optional extra tags (max 10). "in-app-report" is always added.</summary>
    [MaxLength(10)]
    public List<string>? Tags { get; set; }

    /// <summary>
    /// Optional log files to attach (max 5): the ids returned by POST /api/report/files.
    /// They appear in the card's Files list under the names they were uploaded with.
    /// </summary>
    [MaxLength(WorkItemDefaults.MaxFilesPerReport)]
    public List<Guid>? Files { get; set; }
}

/// <summary>A log file uploaded for an in-app report (1.14.0). Put its id in the report's 'files'.</summary>
public sealed class ReportFileDto
{
    /// <summary>Id to list in the report's 'files'.</summary>
    public Guid Id { get; init; }

    /// <summary>The name it will be shown under on the card.</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>"text/plain", "application/zip", "application/gzip" or "application/x-7z-compressed".</summary>
    public string ContentType { get; init; } = string.Empty;

    /// <summary>Size in bytes.</summary>
    public long Length { get; init; }
}

/// <summary>
/// Confirmation of a filed report. Deliberately minimal: the reporter key cannot
/// read the board, so it gets back only what it sent plus the new id.
/// </summary>
public sealed class ReportReceiptDto
{
    /// <summary>Work item id on the board (e.g. to show "Sent to the QA board as #123").</summary>
    public int Id { get; init; }

    /// <summary>Link that opens this card on the board (1.13.0), e.g. "http://host/?item=123".</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>
    /// True when this answers a resend: the Idempotency-Key was already used, so no new
    /// card was filed and this is the card from the first send (HTTP 200 instead of 201).
    /// </summary>
    public bool Replayed { get; init; }

    public string Title { get; init; } = string.Empty;

    public WorkItemType Type { get; init; }

    public string? Program { get; init; }

    public string? ProgramVersion { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    /// <summary>Names of the files attached to the card (1.14.0).</summary>
    public IReadOnlyList<string> Files { get; init; } = Array.Empty<string>();

    /// <summary>Author recorded on the card, e.g. "Jane Doe (in-app report)".</summary>
    public string ReportedBy { get; init; } = string.Empty;

    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// Answer to <c>GET /api/report/ping</c> (1.13.0): the board is up and accepts
/// reports with this key. Files nothing.
/// </summary>
public sealed class ReportPingDto
{
    /// <summary>Always "ok" (any other situation is an error status).</summary>
    public string Status { get; init; } = "ok";

    /// <summary>Board version, e.g. "1.13.0".</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>Requests (reports, uploads and pings) allowed per minute per IP address.</summary>
    public int RequestsPerMinute { get; init; }

    /// <summary>Echo of the <c>program</c> query parameter, or null when none was sent.</summary>
    public string? Program { get; init; }

    /// <summary>
    /// When <c>program</c> was sent: true if the board knows it (reports naming it will be
    /// accepted), false if a report naming it would be rejected. Null when no program was sent.
    /// </summary>
    public bool? ProgramKnown { get; init; }
}
