namespace KanbanBoard.Api.Models;

/// <summary>
/// A board column bound to exactly one <see cref="WorkItemState"/> (spec 3.3).
/// </summary>
public class BoardColumn
{
    public int Id { get; set; }

    /// <summary>Display name (defaults match the mapped state name).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The work item state whose items render in this column.</summary>
    public WorkItemState State { get; set; }

    /// <summary>Work-in-progress limit; <c>null</c> means unlimited.</summary>
    public int? WipLimit { get; set; }

    /// <summary>Left-to-right ordering.</summary>
    public int SortOrder { get; set; }
}

/// <summary>
/// Horizontal board lane (spec 3.3). The default board ships with a single
/// lane; the entity exists so additional lanes can be added without a
/// breaking schema change.
/// </summary>
public class Swimlane
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public bool IsDefault { get; set; }
}
