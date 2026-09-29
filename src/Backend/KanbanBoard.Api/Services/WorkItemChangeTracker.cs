using System.Globalization;
using System.Text.Json;
using KanbanBoard.Api.Models;

namespace KanbanBoard.Api.Services;

/// <summary>
/// Immutable copy of the user-editable fields of a <see cref="WorkItem"/>,
/// taken before and after a change so the two can be diffed for the audit trail.
/// </summary>
public sealed record WorkItemSnapshot(
    string Title,
    string? Description,
    WorkItemType Type,
    WorkItemState State,
    int Priority,
    string Severity,
    string? AssignedTo,
    string AreaPath,
    string IterationPath)
{
    /// <summary>Captures the tracked fields of <paramref name="item"/>.</summary>
    public static WorkItemSnapshot From(WorkItem item) => new(
        item.Title,
        item.Description,
        item.Type,
        item.State,
        item.Priority,
        item.Severity,
        item.AssignedTo,
        item.AreaPath,
        item.IterationPath);

    /// <summary>Snapshot representing "nothing" - used to diff a newly created item.</summary>
    public static readonly WorkItemSnapshot Empty = new(
        string.Empty, null, default, default, 0, string.Empty, null, string.Empty, string.Empty);
}

/// <summary>
/// Computes field-level diffs between snapshots and (de)serializes them to the
/// <c>ChangedFieldsJson</c> column.
/// </summary>
public static class WorkItemChangeTracker
{
    /// <summary>
    /// Long values (typically descriptions) are truncated in the audit record
    /// to keep history rows bounded; the live item always holds the full text.
    /// </summary>
    public const int MaxAuditValueLength = 2000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Returns every tracked field whose value differs between the snapshots.
    /// When <paramref name="isCreation"/> is true, all non-empty "after" values
    /// are reported with a null old value.
    /// </summary>
    public static Dictionary<string, FieldChange> Diff(WorkItemSnapshot before, WorkItemSnapshot after, bool isCreation = false)
    {
        var changes = new Dictionary<string, FieldChange>(StringComparer.Ordinal);

        void Track(string field, string? oldValue, string? newValue)
        {
            if (isCreation)
            {
                oldValue = null;
                if (string.IsNullOrEmpty(newValue))
                {
                    return;
                }
            }
            else if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
            {
                return;
            }

            changes[field] = new FieldChange(Truncate(oldValue), Truncate(newValue));
        }

        Track(nameof(WorkItem.Title), before.Title, after.Title);
        Track(nameof(WorkItem.Description), before.Description, after.Description);
        Track(nameof(WorkItem.Type), before.Type.ToString(), after.Type.ToString());
        Track(nameof(WorkItem.State), before.State.ToString(), after.State.ToString());
        Track(nameof(WorkItem.Priority),
            before.Priority.ToString(CultureInfo.InvariantCulture),
            after.Priority.ToString(CultureInfo.InvariantCulture));
        Track(nameof(WorkItem.Severity), before.Severity, after.Severity);
        Track(nameof(WorkItem.AssignedTo), before.AssignedTo, after.AssignedTo);
        Track(nameof(WorkItem.AreaPath), before.AreaPath, after.AreaPath);
        Track(nameof(WorkItem.IterationPath), before.IterationPath, after.IterationPath);

        return changes;
    }

    /// <summary>Serializes a diff for the ChangedFieldsJson column.</summary>
    public static string Serialize(IReadOnlyDictionary<string, FieldChange> changes) =>
        JsonSerializer.Serialize(changes, JsonOptions);

    /// <summary>
    /// Parses a ChangedFieldsJson value. Corrupt or legacy rows yield an empty
    /// dictionary instead of failing the whole history request.
    /// </summary>
    public static IReadOnlyDictionary<string, FieldChange> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, FieldChange>();
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, FieldChange>>(json, JsonOptions)
                   ?? new Dictionary<string, FieldChange>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, FieldChange>();
        }
    }

    private static string? Truncate(string? value) =>
        value is { Length: > MaxAuditValueLength }
            ? string.Concat(value.AsSpan(0, MaxAuditValueLength), "… (truncated)")
            : value;
}
