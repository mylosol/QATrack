using KanbanBoard.Api.Middleware;
using KanbanBoard.Api.Models;
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

    public IssueReportService(WorkItemService items, ActorContext actor, IOptionsMonitor<IssueReportingOptions> options)
    {
        _items = items;
        _actor = actor;
        _options = options;
    }

    /// <exception cref="WorkItemValidationException">Bad type, unknown program, too many tags...</exception>
    public async Task<ReportReceiptDto> ReportAsync(ReportIssueRequest request, CancellationToken ct = default)
    {
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

        var created = await _items.CreateAsync(new CreateWorkItemRequest
        {
            Title = request.Title,
            Type = type,
            State = WorkItemState.New,
            Severity = request.Severity,
            Program = request.Program,
            ProgramVersion = request.ProgramVersion,
            Description = ComposeDescription(request.Description, request.Environment),
            Tags = tags,
        }, ct);

        return new ReportReceiptDto
        {
            Id = created.Id,
            Title = created.Title,
            Type = created.Type,
            Program = created.Program,
            ProgramVersion = created.ProgramVersion,
            Tags = created.Tags,
            ReportedBy = created.LastModifiedBy,
            CreatedAt = created.CreatedAt,
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
