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
        Program = item.Program?.Name,
        ProgramVersion = item.ProgramVersion,
        Tags = item.Tags.Select(t => t.Name).OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList(),
        AiModified = item.AiModified,
        AiAgentIdentity = item.AiAgentIdentity,
        LastHumanCommentAt = item.LastHumanCommentAt,
        LastHumanCommentBy = item.LastHumanCommentBy,
        LastAgentCommentAt = item.LastAgentCommentAt,
        LastAgentCommentBy = item.LastAgentCommentBy,
        CommentCount = item.CommentCount,
        DiscussionStatus = Discussion.StatusOf(item),
        LastModifiedBy = item.LastModifiedBy,
        CreatedBy = item.CreatedBy ?? item.LastModifiedBy,
        CreatedAt = item.CreatedAt,
        UpdatedAt = item.UpdatedAt,
        History = includeHistory
            ? item.History.OrderBy(h => h.ChangeDate).ThenBy(h => h.Id).Select(ToDto).ToList()
            : null,
        FileCount = item.FileCount,
        Files = includeHistory
            ? item.Files.Where(f => f.RemovedAt == null).OrderBy(f => f.AddedAt).ThenBy(f => f.Id).Select(ToDto).ToList()
            : null,
    };

    /// <summary>Maps an attached file. The URL is the AI agent API download link.</summary>
    public static WorkItemFileDto ToDto(WorkItemFile file) => new()
    {
        Id = file.Id,
        WorkItemId = file.WorkItemId,
        FileName = file.FileName,
        ContentType = file.ContentType,
        Length = file.Length,
        AddedBy = file.AddedBy,
        IsAiAction = file.IsAiAction,
        AddedAt = file.AddedAt,
        Url = $"api/v1/workitems/{file.WorkItemId}/files/{file.Id}",
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
        EditedAt = history.EditedAt,
    };
}
