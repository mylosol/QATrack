using KanbanBoard.Api.Data;
using KanbanBoard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace KanbanBoard.Api.Services;

/// <summary>
/// Work item use cases shared by the AI agent API and the browser UI.
/// </summary>
/// <remarks>
/// Every mutation funnels through <see cref="StampAndRecord"/>, which is the
/// single place that enforces the spec 4.1 audit rules: LastModifiedBy,
/// UpdatedAt, the sticky AiModified flag, AiAgentIdentity and one
/// WorkItemHistory row per change.
/// </remarks>
public sealed class WorkItemService
{
    /// <summary>Default page size for list queries.</summary>
    public const int DefaultTop = 500;

    /// <summary>Magic assignee filter value meaning "no assignee".</summary>
    public const string UnassignedFilter = "unassigned";

    private readonly KanbanDbContext _db;
    private readonly IActorContext _actor;
    private readonly TimeProvider _clock;

    public WorkItemService(KanbanDbContext db, IActorContext actor, TimeProvider clock)
    {
        _db = db;
        _actor = actor;
        _clock = clock;
    }

    /// <summary>Lists work items matching the (all optional) filters, ordered by id.</summary>
    public async Task<IReadOnlyList<WorkItemDto>> ListAsync(WorkItemQuery query, CancellationToken ct = default)
    {
        var items = await ApplyFilters(_db.WorkItems.AsNoTracking(), query)
            .OrderBy(w => w.Id)
            .Take(Math.Clamp(query.Top ?? DefaultTop, 1, 1000))
            .ToListAsync(ct);
        return items.Select(i => WorkItemMapper.ToDto(i)).ToList();
    }

    /// <summary>Returns one work item with its full audit history.</summary>
    /// <exception cref="WorkItemNotFoundException">When the id does not exist.</exception>
    public async Task<WorkItemDto> GetAsync(int id, CancellationToken ct = default)
    {
        var item = await _db.WorkItems.AsNoTracking()
                       .Include(w => w.History)
                       .AsSplitQuery()
                       .FirstOrDefaultAsync(w => w.Id == id, ct)
                   ?? throw new WorkItemNotFoundException(id);
        return WorkItemMapper.ToDto(item, includeHistory: true);
    }

    /// <summary>Creates a work item and its "created" history entry.</summary>
    public async Task<WorkItemDto> CreateAsync(CreateWorkItemRequest request, CancellationToken ct = default)
    {
        var title = TextSanitizer.SingleLine(request.Title, WorkItemDefaults.TitleMaxLength)
                    ?? throw new WorkItemValidationException(nameof(request.Title), "Title is required.");
        if (request.Type is null)
        {
            throw new WorkItemValidationException(nameof(request.Type), "Type is required.");
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var item = new WorkItem
        {
            Title = title,
            Description = TextSanitizer.MultiLine(request.Description, WorkItemDefaults.DescriptionMaxLength),
            Type = request.Type.Value,
            State = request.State ?? WorkItemState.New,
            Priority = ValidatePriority(request.Priority ?? WorkItemDefaults.Priority),
            Severity = NormalizeSeverity(request.Severity) ?? WorkItemDefaults.Severity,
            AssignedTo = TextSanitizer.SingleLine(request.AssignedTo, WorkItemDefaults.ShortTextMaxLength),
            AreaPath = TextSanitizer.SingleLine(request.AreaPath, WorkItemDefaults.ShortTextMaxLength)
                       ?? WorkItemDefaults.AreaPath,
            IterationPath = TextSanitizer.SingleLine(request.IterationPath, WorkItemDefaults.ShortTextMaxLength)
                            ?? WorkItemDefaults.IterationPath,
            CreatedAt = now,
        };

        var changes = WorkItemChangeTracker.Diff(WorkItemSnapshot.Empty, WorkItemSnapshot.From(item), isCreation: true);
        StampAndRecord(item, changes, TextSanitizer.MultiLine(request.Comment, WorkItemDefaults.CommentMaxLength), now);

        _db.WorkItems.Add(item);
        await _db.SaveChangesAsync(ct);
        return WorkItemMapper.ToDto(item, includeHistory: true);
    }

    /// <summary>
    /// Applies a partial update. A request that changes nothing and carries no
    /// comment is a no-op: no history row, no AI stamp, UpdatedAt untouched.
    /// </summary>
    /// <exception cref="WorkItemNotFoundException">When the id does not exist.</exception>
    public async Task<WorkItemDto> UpdateAsync(int id, UpdateWorkItemRequest request, CancellationToken ct = default)
    {
        var item = await _db.WorkItems.Include(w => w.History).AsSplitQuery().FirstOrDefaultAsync(w => w.Id == id, ct)
                   ?? throw new WorkItemNotFoundException(id);

        var before = WorkItemSnapshot.From(item);

        if (request.Title is not null)
        {
            item.Title = TextSanitizer.SingleLine(request.Title, WorkItemDefaults.TitleMaxLength)
                         ?? throw new WorkItemValidationException(nameof(request.Title), "Title cannot be blank.");
        }

        if (request.Description is not null)
        {
            // Empty string clears the description.
            item.Description = TextSanitizer.MultiLine(request.Description, WorkItemDefaults.DescriptionMaxLength);
        }

        if (request.Type is not null)
        {
            item.Type = request.Type.Value;
        }

        if (request.State is not null)
        {
            item.State = request.State.Value;
        }

        if (request.Priority is not null)
        {
            item.Priority = ValidatePriority(request.Priority.Value);
        }

        if (request.Severity is not null)
        {
            item.Severity = NormalizeSeverity(request.Severity)
                            ?? throw new WorkItemValidationException(nameof(request.Severity), "Severity cannot be blank.");
        }

        if (request.AssignedTo is not null)
        {
            // Empty string unassigns.
            item.AssignedTo = TextSanitizer.SingleLine(request.AssignedTo, WorkItemDefaults.ShortTextMaxLength);
        }

        if (request.AreaPath is not null)
        {
            item.AreaPath = TextSanitizer.SingleLine(request.AreaPath, WorkItemDefaults.ShortTextMaxLength)
                            ?? WorkItemDefaults.AreaPath;
        }

        if (request.IterationPath is not null)
        {
            item.IterationPath = TextSanitizer.SingleLine(request.IterationPath, WorkItemDefaults.ShortTextMaxLength)
                                 ?? WorkItemDefaults.IterationPath;
        }

        var changes = WorkItemChangeTracker.Diff(before, WorkItemSnapshot.From(item));
        var comment = TextSanitizer.MultiLine(request.Comment, WorkItemDefaults.CommentMaxLength);
        if (changes.Count == 0 && comment is null)
        {
            return WorkItemMapper.ToDto(item, includeHistory: true);
        }

        StampAndRecord(item, changes, comment, _clock.GetUtcNow().UtcDateTime);
        await _db.SaveChangesAsync(ct);
        return WorkItemMapper.ToDto(item, includeHistory: true);
    }

    /// <summary>Adds a discussion comment (or automated test output) to the audit stream.</summary>
    /// <exception cref="WorkItemNotFoundException">When the id does not exist.</exception>
    public async Task<WorkItemHistoryDto> AddCommentAsync(int id, AddCommentRequest request, CancellationToken ct = default)
    {
        var text = TextSanitizer.MultiLine(request.Text, WorkItemDefaults.CommentMaxLength)
                   ?? throw new WorkItemValidationException(nameof(request.Text), "Comment text is required.");

        var item = await _db.WorkItems.FirstOrDefaultAsync(w => w.Id == id, ct)
                   ?? throw new WorkItemNotFoundException(id);

        var entry = StampAndRecord(item, new Dictionary<string, FieldChange>(), text, _clock.GetUtcNow().UtcDateTime);
        await _db.SaveChangesAsync(ct);
        return WorkItemMapper.ToDto(entry);
    }

    /// <summary>Applies the shared list/board filters to a query.</summary>
    internal static IQueryable<WorkItem> ApplyFilters(IQueryable<WorkItem> source, WorkItemQuery query)
    {
        if (query.Type is not null)
        {
            source = source.Where(w => w.Type == query.Type);
        }

        if (query.State is not null)
        {
            source = source.Where(w => w.State == query.State);
        }

        if (query.AiModified is not null)
        {
            source = source.Where(w => w.AiModified == query.AiModified);
        }

        var assignee = TextSanitizer.SingleLine(query.AssignedTo, WorkItemDefaults.ShortTextMaxLength);
        if (assignee is not null)
        {
            if (assignee.Equals(UnassignedFilter, StringComparison.OrdinalIgnoreCase))
            {
                source = source.Where(w => w.AssignedTo == null);
            }
            else
            {
                var lowered = assignee.ToLowerInvariant();
                source = source.Where(w => w.AssignedTo != null && w.AssignedTo.ToLower() == lowered);
            }
        }

        return source;
    }

    /// <summary>
    /// The one place audit rules are enforced (spec 4.1). Stamps modification
    /// metadata on <paramref name="item"/> and appends a history row.
    /// </summary>
    private WorkItemHistory StampAndRecord(
        WorkItem item,
        IReadOnlyDictionary<string, FieldChange> changes,
        string? comment,
        DateTime now)
    {
        item.UpdatedAt = now;
        item.LastModifiedBy = _actor.DisplayName;

        if (_actor.IsAi)
        {
            // Sticky: once an agent touches a card it stays flagged.
            item.AiModified = true;
            item.AiAgentIdentity = _actor.AgentIdentity;
        }

        var entry = new WorkItemHistory
        {
            ChangeDate = now,
            Author = _actor.DisplayName,
            IsAiAction = _actor.IsAi,
            AgentName = _actor.IsAi ? _actor.AgentIdentity : null,
            ChangedFieldsJson = WorkItemChangeTracker.Serialize(changes),
            Comment = comment,
        };
        item.History.Add(entry);
        return entry;
    }

    private static int ValidatePriority(int priority) =>
        priority is >= 1 and <= 4
            ? priority
            : throw new WorkItemValidationException(nameof(WorkItem.Priority), "Priority must be between 1 (Critical) and 4 (Low).");

    /// <summary>
    /// Maps user input to the canonical severity label. Accepts the exact label
    /// (case-insensitive), the bare number ("2") or the bare word ("High").
    /// </summary>
    internal static string? NormalizeSeverity(string? input)
    {
        var value = TextSanitizer.SingleLine(input, 32);
        if (value is null)
        {
            return null;
        }

        foreach (var severity in WorkItemDefaults.Severities)
        {
            var number = severity[..1];
            var word = severity[4..];
            if (value.Equals(severity, StringComparison.OrdinalIgnoreCase) ||
                value.Replace(" ", string.Empty).Equals(severity.Replace(" ", string.Empty), StringComparison.OrdinalIgnoreCase) ||
                value.Equals(number, StringComparison.Ordinal) ||
                value.Equals(word, StringComparison.OrdinalIgnoreCase))
            {
                return severity;
            }
        }

        throw new WorkItemValidationException(
            nameof(WorkItem.Severity),
            $"Severity must be one of: {string.Join(", ", WorkItemDefaults.Severities)}.");
    }
}
