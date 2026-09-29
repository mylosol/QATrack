namespace KanbanBoard.Api.Models;

/// <summary>
/// Work item categories mirroring Azure DevOps process templates.
/// Persisted as TEXT (the enum member name) in SQLite.
/// </summary>
public enum WorkItemType
{
    Bug,
    Feature,
    UserStory,
    Epic,
    Task,
}

/// <summary>
/// Lifecycle states. Every state except <see cref="Removed"/> maps to a board column;
/// removed items are retained for audit purposes but hidden from the board.
/// Persisted as TEXT (the enum member name) in SQLite.
/// </summary>
public enum WorkItemState
{
    New,
    Active,
    Resolved,
    Closed,
    Removed,
}

/// <summary>
/// Canonical constants shared by validation, seeding and the API surface.
/// </summary>
public static class WorkItemDefaults
{
    /// <summary>Default area path for new items (spec 3.1).</summary>
    public const string AreaPath = @"Tools\QA";

    /// <summary>Default iteration path for new items (spec 3.1).</summary>
    public const string IterationPath = "Current";

    /// <summary>Default priority: 2 - High (Azure DevOps default).</summary>
    public const int Priority = 2;

    /// <summary>Default severity label.</summary>
    public const string Severity = "3 - Medium";

    /// <summary>Maximum title length (spec 3.1: TEXT, Required, Max 255).</summary>
    public const int TitleMaxLength = 255;

    /// <summary>Upper bound on description size to keep SQLite rows sane (1 MB of text).</summary>
    public const int DescriptionMaxLength = 1_000_000;

    /// <summary>Max length for short free-text fields (assignee, paths, identities).</summary>
    public const int ShortTextMaxLength = 256;

    /// <summary>Max length of a single discussion comment / test output payload.</summary>
    public const int CommentMaxLength = 200_000;

    /// <summary>Max length of a program name.</summary>
    public const int ProgramNameMaxLength = 64;

    /// <summary>Max length of a tag.</summary>
    public const int TagMaxLength = 50;

    /// <summary>Max tags per work item.</summary>
    public const int MaxTagsPerItem = 20;

    /// <summary>Max size of one uploaded image.</summary>
    public const int AttachmentMaxBytes = 5 * 1024 * 1024;

    /// <summary>Programs available on a fresh board.</summary>
    public static readonly IReadOnlyList<string> InitialPrograms = new[] { "ProveOut", "CallOut" };

    /// <summary>The exact severity strings allowed by spec 3.1.</summary>
    public static readonly IReadOnlyList<string> Severities = new[]
    {
        "1 - Critical",
        "2 - High",
        "3 - Medium",
        "4 - Low",
    };

    /// <summary>Human readable priority labels keyed by numeric priority (1-4).</summary>
    public static readonly IReadOnlyDictionary<int, string> Priorities = new Dictionary<int, string>
    {
        [1] = "Critical",
        [2] = "High",
        [3] = "Medium",
        [4] = "Low",
    };
}
