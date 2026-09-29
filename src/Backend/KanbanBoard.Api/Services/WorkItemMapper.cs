using KanbanBoard.Api.Models;

namespace KanbanBoard.Api.Services;

/// <summary>Entity to DTO projection helpers.</summary>
public static class WorkItemMapper
{
    /// <summary>Maps a work item, optionally including its full audit history.</summary>
    public static WorkItemDto ToDto(WorkItem item, bool includeHistory = false) => new()
    {
        Id = item.Id,
        Title = item.Title,
        Description = item.Description,
        Type = item.Type,
        State = item.State,
        Priority = item.Priority,
        Severity = item.Severity,
        AssignedTo = item.AssignedTo,
        AreaPath = item.AreaPath,
        IterationPath = item.IterationPath,
        AiModified = item.AiModified,
        AiAgentIdentity = item.AiAgentIdentity,
        LastModifiedBy = item.LastModifiedBy,
        CreatedAt = item.CreatedAt,
        UpdatedAt = item.UpdatedAt,
        History = includeHistory
            ? item.History.OrderBy(h => h.ChangeDate).ThenBy(h => h.Id).Select(ToDto).ToList()
            : null,
    };

    /// <summary>Maps a single audit entry, parsing its JSON diff payload.</summary>
    public static WorkItemHistoryDto ToDto(WorkItemHistory history) => new()
    {
        Id = history.Id,
        WorkItemId = history.WorkItemId,
        ChangeDate = history.ChangeDate,
        Author = history.Author,
        IsAiAction = history.IsAiAction,
        AgentName = history.AgentName,
        ChangedFields = WorkItemChangeTracker.Deserialize(history.ChangedFieldsJson),
        Comment = history.Comment,
    };
}
