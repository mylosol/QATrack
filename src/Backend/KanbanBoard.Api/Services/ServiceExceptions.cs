namespace KanbanBoard.Api.Services;

/// <summary>Raised when a work item id does not exist. Mapped to HTTP 404.</summary>
public sealed class WorkItemNotFoundException : Exception
{
    public WorkItemNotFoundException(int id)
        : base($"Work item {id} was not found.")
    {
        WorkItemId = id;
    }

    public int WorkItemId { get; }
}

/// <summary>
/// Raised for business-rule validation failures the model binder cannot catch
/// (blank titles after trimming, unknown severities...). Mapped to HTTP 400
/// with a ValidationProblemDetails body.
/// </summary>
public sealed class WorkItemValidationException : Exception
{
    public WorkItemValidationException(string field, string message)
        : this(new Dictionary<string, string[]> { [field] = new[] { message } })
    {
    }

    public WorkItemValidationException(IDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.")
    {
        Errors = new Dictionary<string, string[]>(errors);
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

/// <summary>
/// Raised when the caller may not perform an action on an existing resource,
/// e.g. editing someone else's comment (1.13.0). Mapped to HTTP 403.
/// </summary>
public sealed class WorkItemForbiddenException : Exception
{
    public WorkItemForbiddenException(string message)
        : base(message)
    {
    }
}

/// <summary>A file id that is not attached to the work item (or was removed). Mapped to HTTP 404.</summary>
public sealed class FileNotFoundOnItemException : Exception
{
    public FileNotFoundOnItemException(int workItemId, int fileId)
        : base($"File {fileId} is not attached to work item {workItemId}.")
    {
    }
}

/// <summary>A comment (history entry) id that does not exist on the work item. Mapped to HTTP 404.</summary>
public sealed class CommentNotFoundException : Exception
{
    public CommentNotFoundException(int workItemId, int commentId)
        : base($"Comment {commentId} was not found on work item {workItemId}.")
    {
    }
}
