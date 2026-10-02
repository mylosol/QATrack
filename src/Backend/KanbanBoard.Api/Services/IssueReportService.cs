using KanbanBoard.Api.Data;
using KanbanBoard.Api.Middleware;
using KanbanBoard.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KanbanBoard.Api.Services;

/// <summary>
/// Files in-app issue reports on the board as human reports (1.12.0): never
/// AI-flagged, authored by the reporter, tagged <c>in-app-report</c>, always New.
/// </summary>
public sealed class IssueReportService
{
    /// <summary>Extra tags a report may carry (on top of the in-app-report tag).</summary>
    public const int MaxExtraTags = 10;

    private readonly WorkItemService _items;
    private readonly ActorContext _actor;
    private readonly IOptionsMonitor<IssueReportingOptions> _options;
    private readonly KanbanDbContext _db;

    public IssueReportService(WorkItemService items, ActorContext actor, IOptionsMonitor<IssueReportingOptions> options, KanbanDbContext db)
    {
        _items = items;
        _actor = actor;
        _options = options;
        _db = db;
    }

    /// <summary>
    /// Files a report. With an <paramref name="idempotencyKey"/> a resend (after a
    /// timeout, a double click...) returns the card filed the first time, marked
    /// <see cref="ReportReceiptDto.Replayed"/>, instead of filing a duplicate.
    /// </summary>
    /// <param name="request">The report.</param>
    /// <param name="idempotencyKey">Optional unique id the program gave this report (1.13.0).</param>
    /// <param name="cardUrl">Builds the board link for a card id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="WorkItemValidationException">Bad type, unknown program, too many tags, bad key...</exception>
    public async Task<ReportReceiptDto> ReportAsync(ReportIssueRequest request, string? idempotencyKey, Func<int, string> cardUrl, CancellationToken ct = default)
    {
        var key = ValidateIdempotencyKey(idempotencyKey);
        if (key is not null && await FindByKeyAsync(key, cardUrl, ct) is { } earlier)
        {
            return earlier;
        }

        var type = request.Type ?? WorkItemType.Bug;
        if (type is not (WorkItemType.Bug or WorkItemType.Feature))
        {
            throw new WorkItemValidationException(nameof(request.Type), "In-app reports must be a Bug or a Feature.");
        }

        if (request.Tags is { Count: > MaxExtraTags })
        {
            throw new WorkItemValidationException(nameof(request.Tags), $"A report can carry at most {MaxExtraTags} extra tags.");
        }

        _actor.SetReporter(request.Reporter);

        var tags = new List<string> { _options.CurrentValue.Tag };
        tags.AddRange(request.Tags ?? Enumerable.Empty<string>());

        var create = new CreateWorkItemRequest
        {
            Title = request.Title,
            Type = type,
            State = WorkItemState.New,
            Severity = request.Severity,
            Program = request.Program,
            ProgramVersion = request.ProgramVersion,
            Description = ComposeDescription(request.Description, request.Environment),
            Tags = tags,
        };

        WorkItemDto created;
        try
        {
            created = await _items.CreateAsync(create, key, ct);
        }
        catch (DbUpdateException) when (key is not null)
        {
            // Two sends with the same key raced; the other one won. Answer with its card.
            _db.ChangeTracker.Clear();
            return await FindByKeyAsync(key, cardUrl, ct) ?? throw new InvalidOperationException("Report key conflict without a matching card.");
        }

        return Receipt(created, cardUrl, replayed: false);
    }

    /// <summary>Accepts 1-128 visible ASCII characters (a UUID is ideal); blank means "no key".</summary>
    internal static string? ValidateIdempotencyKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var trimmed = key.Trim();
        if (trimmed.Length > WorkItemDefaults.ReportKeyMaxLength || trimmed.Any(c => c is < '!' or > '~'))
        {
            throw new WorkItemValidationException(IdempotencyKeyHeader,
                $"The {IdempotencyKeyHeader} must be 1-{WorkItemDefaults.ReportKeyMaxLength} visible ASCII characters, e.g. a UUID.");
        }

        return trimmed;
    }

    /// <summary>Header carrying a report's unique id.</summary>
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    private async Task<ReportReceiptDto?> FindByKeyAsync(string key, Func<int, string> cardUrl, CancellationToken ct)
    {
        var id = await _db.WorkItems.AsNoTracking().Where(w => w.ReportKey == key).Select(w => (int?)w.Id).FirstOrDefaultAsync(ct);
        return id is null ? null : Receipt(await _items.GetAsync(id.Value, ct), cardUrl, replayed: true);
    }

    private static ReportReceiptDto Receipt(WorkItemDto card, Func<int, string> cardUrl, bool replayed) => new()
    {
        Id = card.Id,
        Url = cardUrl(card.Id),
        Replayed = replayed,
        Title = card.Title,
        Type = card.Type,
        Program = card.Program,
        ProgramVersion = card.ProgramVersion,
        Tags = card.Tags,
        // The creation entry's author: later edits on the board don't change who reported it.
        ReportedBy = card.History?.FirstOrDefault()?.Author ?? card.LastModifiedBy,
        CreatedAt = card.CreatedAt,
    };

    /// <summary>Health check for the program's "Check connection" button. Files nothing.</summary>
    public async Task<ReportPingDto> PingAsync(string? program, CancellationToken ct = default)
    {
        var name = TextSanitizer.SingleLine(program, WorkItemDefaults.ProgramNameMaxLength);
        bool? known = null;
        if (name is not null)
        {
            var normalized = name.ToUpperInvariant();
            known = await _db.Programs.AsNoTracking().AnyAsync(p => p.NormalizedName == normalized, ct);
        }

        return new ReportPingDto
        {
            Version = AppVersion.Current.Version,
            RequestsPerMinute = _options.CurrentValue.RequestsPerMinute,
            Program = name,
            ProgramKnown = known,
        };
    }

    /// <summary>
    /// Appends the environment details as a fenced code block. The fence is
    /// longer than any backtick run inside, so the details can't break out of
    /// it and inject markdown.
    /// </summary>
    internal static string? ComposeDescription(string? description, string? environment)
    {
        var details = TextSanitizer.MultiLine(environment, 20_000)?.Trim('\n');
        if (details is null)
        {
            return description;
        }

        var longestRun = 0;
        var run = 0;
        foreach (var ch in details)
        {
            run = ch == '`' ? run + 1 : 0;
            longestRun = Math.Max(longestRun, run);
        }

        var fence = new string('`', Math.Max(3, longestRun + 1));
        var block = $"**Environment**\n\n{fence}text\n{details}\n{fence}";
        return string.IsNullOrWhiteSpace(description) ? block : $"{description.TrimEnd()}\n\n{block}";
    }
}
