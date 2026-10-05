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
            .Include(w => w.Program)
            .Include(w => w.Tags)
            .AsSplitQuery()
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
                       .Include(w => w.Files)
                       .Include(w => w.Program)
                       .Include(w => w.Tags)
                       .AsSplitQuery()
                       .FirstOrDefaultAsync(w => w.Id == id, ct)
                   ?? throw new WorkItemNotFoundException(id);
        return WorkItemMapper.ToDto(item, includeHistory: true);
    }

    /// <summary>Creates a work item and its "created" history entry.</summary>
    public Task<WorkItemDto> CreateAsync(CreateWorkItemRequest request, CancellationToken ct = default) =>
        CreateAsync(request, reportKey: null, ct);

    /// <summary>Creates a work item; <paramref name="reportKey"/> is an in-app report's Idempotency-Key (1.13.0).</summary>
    /// <exception cref="DbUpdateException">Another card already carries <paramref name="reportKey"/>.</exception>
    internal async Task<WorkItemDto> CreateAsync(CreateWorkItemRequest request, string? reportKey, CancellationToken ct)
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
            ProgramVersion = TextSanitizer.SingleLine(request.ProgramVersion, WorkItemDefaults.ProgramVersionMaxLength),
            ReportKey = reportKey,
            CreatedAt = now,
        };

        item.Program = await ResolveProgramAsync(request.Program, ct);
        if (request.Tags is not null)
        {
            item.Tags.AddRange(await ResolveTagsAsync(NormalizeTags(request.Tags), ct));
        }

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
        var item = await _db.WorkItems
                       .Include(w => w.History)
                       .Include(w => w.Files)
                       .Include(w => w.Program)
                       .Include(w => w.Tags)
                       .AsSplitQuery()
                       .FirstOrDefaultAsync(w => w.Id == id, ct)
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

        if (request.ProgramVersion is not null)
        {
            // Empty string clears it.
            item.ProgramVersion = TextSanitizer.SingleLine(request.ProgramVersion, WorkItemDefaults.ProgramVersionMaxLength);
        }

        if (request.Program is not null)
        {
            // Empty string removes the program.
            var program = await ResolveProgramAsync(request.Program, ct);
            item.Program = program;
            item.ProgramId = program?.Id;
        }

        if (request.Tags is not null)
        {
            // Replace-all semantics. Tags are shared rows, so the tracked
            // instances are reused (EF identity resolution keeps them unique).
            var wanted = await ResolveTagsAsync(NormalizeTags(request.Tags), ct);
            item.Tags.RemoveAll(t => !wanted.Contains(t));
            item.Tags.AddRange(wanted.Where(t => !item.Tags.Contains(t)).ToList());
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

    /// <summary>
    /// A human edits the text of a comment they wrote (1.13.0). The replaced text
    /// is kept as a <see cref="CommentRevision"/>. The edit counts as a fresh human
    /// comment for the discussion status (the agent should re-read it) and moves
    /// UpdatedAt, so agents polling updatedSince see it. No new history row.
    /// </summary>
    /// <exception cref="WorkItemNotFoundException">When the work item does not exist.</exception>
    /// <exception cref="CommentNotFoundException">When the entry does not exist on it or has no comment.</exception>
    /// <exception cref="WorkItemForbiddenException">An AI comment, or someone else's.</exception>
    public async Task<WorkItemHistoryDto> EditCommentAsync(int id, int commentId, EditCommentRequest request, CancellationToken ct = default)
    {
        var text = TextSanitizer.MultiLine(request.Text, WorkItemDefaults.CommentMaxLength)
                   ?? throw new WorkItemValidationException(nameof(request.Text), "Comment text is required.");

        var item = await _db.WorkItems.FirstOrDefaultAsync(w => w.Id == id, ct)
                   ?? throw new WorkItemNotFoundException(id);
        var entry = await _db.WorkItemHistory.FirstOrDefaultAsync(h => h.Id == commentId && h.WorkItemId == id, ct);
        if (entry?.Comment is null)
        {
            throw new CommentNotFoundException(id, commentId);
        }

        if (_actor.IsAi || entry.IsAiAction)
        {
            throw new WorkItemForbiddenException("AI agent comments cannot be edited.");
        }

        if (!string.Equals(entry.Author, _actor.DisplayName, StringComparison.OrdinalIgnoreCase))
        {
            throw new WorkItemForbiddenException(
                $"Only {entry.Author} can edit this comment. Set your name on the board to the name the comment was posted under.");
        }

        if (entry.Comment == text)
        {
            return WorkItemMapper.ToDto(entry);
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        _db.CommentRevisions.Add(new CommentRevision
        {
            HistoryId = entry.Id,
            Comment = entry.Comment,
            ReplacedAt = now,
            ReplacedBy = _actor.DisplayName,
        });
        entry.Comment = text;
        entry.EditedAt = now;

        item.UpdatedAt = now;
        item.LastModifiedBy = _actor.DisplayName;
        item.LastHumanCommentAt = now;
        item.LastHumanCommentBy = _actor.DisplayName;

        await _db.SaveChangesAsync(ct);
        return WorkItemMapper.ToDto(entry);
    }

    /// <summary>
    /// Records that a human opened the card, clearing an unread agent reply. Not an
    /// edit: no history row, UpdatedAt unchanged (so agents polling updatedSince
    /// are not woken up by it).
    /// </summary>
    /// <exception cref="WorkItemNotFoundException">When the id does not exist.</exception>
    public async Task MarkReadAsync(int id, CancellationToken ct = default)
    {
        var item = await _db.WorkItems.FirstOrDefaultAsync(w => w.Id == id, ct)
                   ?? throw new WorkItemNotFoundException(id);
        if (Discussion.StatusOf(item) == DiscussionStatus.UnreadReply)
        {
            item.HumanReadAt = _clock.GetUtcNow().UtcDateTime;
            await _db.SaveChangesAsync(ct);
        }
    }

    /// <summary>Counts items on the board (Removed excluded) matching the filters.</summary>
    public Task<int> CountOnBoardAsync(WorkItemQuery query, CancellationToken ct = default) =>
        ApplyFilters(_db.WorkItems.AsNoTracking(), query).CountAsync(w => w.State != WorkItemState.Removed, ct);

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

        var program = TextSanitizer.SingleLine(query.Program, WorkItemDefaults.ProgramNameMaxLength);
        if (program is not null)
        {
            var normalized = program.ToUpperInvariant();
            source = source.Where(w => w.Program != null && w.Program.NormalizedName == normalized);
        }

        if (query.Discussion is not null)
        {
            source = Discussion.Where(source, query.Discussion.Value);
        }

        if (query.UpdatedSince is not null)
        {
            var since = query.UpdatedSince.Value.UtcDateTime;
            source = source.Where(w => w.UpdatedAt > since);
        }

        var tag = TextSanitizer.SingleLine(query.Tag, WorkItemDefaults.TagMaxLength);
        if (tag is not null)
        {
            var normalized = tag.ToUpperInvariant();
            source = source.Where(w => w.Tags.Any(t => t.NormalizedName == normalized));
        }

        return source;
    }

    /// <summary>
    /// Cleans a tag list: trims, strips control characters, splits on ',' and ';'
    /// (so "ui, login" is two tags), drops blanks and case-insensitive duplicates.
    /// </summary>
    /// <exception cref="WorkItemValidationException">A tag is too long or there are too many.</exception>
    internal static IReadOnlyList<string> NormalizeTags(IEnumerable<string?> input)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in input)
        {
            foreach (var part in (raw ?? string.Empty).Split(',', ';'))
            {
                var tag = TextSanitizer.SingleLine(part, int.MaxValue);
                if (tag is null)
                {
                    continue;
                }

                if (tag.Length > WorkItemDefaults.TagMaxLength)
                {
                    throw new WorkItemValidationException(nameof(WorkItem.Tags),
                        $"Tags can be at most {WorkItemDefaults.TagMaxLength} characters long.");
                }

                if (seen.Add(tag))
                {
                    result.Add(tag);
                }
            }
        }

        if (result.Count > WorkItemDefaults.MaxTagsPerItem)
        {
            throw new WorkItemValidationException(nameof(WorkItem.Tags),
                $"A work item can have at most {WorkItemDefaults.MaxTagsPerItem} tags.");
        }

        return result;
    }

    /// <summary>Finds a program by name (case-insensitive). Blank means "no program".</summary>
    /// <exception cref="WorkItemValidationException">The program does not exist.</exception>
    private async Task<WorkProgram?> ResolveProgramAsync(string? name, CancellationToken ct)
    {
        var clean = TextSanitizer.SingleLine(name, WorkItemDefaults.ProgramNameMaxLength);
        if (clean is null)
        {
            return null;
        }

        var normalized = clean.ToUpperInvariant();
        var program = await _db.Programs.FirstOrDefaultAsync(p => p.NormalizedName == normalized, ct);
        if (program is not null)
        {
            return program;
        }

        var known = await _db.Programs.OrderBy(p => p.SortOrder).Select(p => p.Name).ToListAsync(ct);
        throw new WorkItemValidationException(nameof(WorkItem.Program),
            $"Unknown program '{clean}'. Use one of: {string.Join(", ", known)} - or add it first with POST /api/v1/programs.");
    }

    /// <summary>Maps cleaned tag names to Tag entities, creating the ones that do not exist yet.</summary>
    private async Task<List<Tag>> ResolveTagsAsync(IReadOnlyList<string> names, CancellationToken ct)
    {
        var result = new List<Tag>(names.Count);
        if (names.Count == 0)
        {
            return result;
        }

        var normalized = names.Select(n => n.ToUpperInvariant()).Distinct().ToList();
        var byKey = await _db.Tags
            .Where(t => normalized.Contains(t.NormalizedName))
            .ToDictionaryAsync(t => t.NormalizedName, ct);

        foreach (var name in names)
        {
            var key = name.ToUpperInvariant();
            if (!byKey.TryGetValue(key, out var tag))
            {
                tag = new Tag { Name = name, NormalizedName = key };
                _db.Tags.Add(tag);
                byKey[key] = tag;
            }

            if (!result.Contains(tag))
            {
                result.Add(tag);
            }
        }

        return result;
    }

    /// <summary>
    /// The one place audit rules are enforced (spec 4.1). Stamps modification
    /// metadata on <paramref name="item"/> and appends a history row.
    /// </summary>
    internal WorkItemHistory StampAndRecord(
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

        if (comment is not null)
        {
            // Drives DiscussionStatus and lets agents/humans spot new comments from list/board results.
            item.CommentCount++;
            if (_actor.IsAi)
            {
                item.LastAgentCommentAt = now;
                item.LastAgentCommentBy = _actor.AgentIdentity;
            }
            else
            {
                item.LastHumanCommentAt = now;
                item.LastHumanCommentBy = _actor.DisplayName;
            }
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
