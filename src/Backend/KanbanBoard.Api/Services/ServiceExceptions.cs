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
