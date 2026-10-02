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
}

/// <summary>
/// Confirmation of a filed report. Deliberately minimal: the reporter key cannot
/// read the board, so it gets back only what it sent plus the new id.
/// </summary>
public sealed class ReportReceiptDto
{
    /// <summary>Work item id on the board (e.g. to show "Thanks - reference #123").</summary>
    public int Id { get; init; }

    public string Title { get; init; } = string.Empty;

    public WorkItemType Type { get; init; }

    public string? Program { get; init; }

    public string? ProgramVersion { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    /// <summary>Author recorded on the card, e.g. "Jane Doe (in-app report)".</summary>
    public string ReportedBy { get; init; } = string.Empty;

    public DateTime CreatedAt { get; init; }
}
