using KanbanBoard.Api.Data;
using KanbanBoard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace KanbanBoard.Api.Services;

/// <summary>
/// Builds the board view: columns, WIP status, swimlanes and grouped cards.
/// </summary>
public sealed class BoardService
{
    private readonly KanbanDbContext _db;

    public BoardService(KanbanDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Returns the board. WIP counts always reflect the unfiltered board so a
    /// quick filter can never hide a WIP violation; <paramref name="filter"/>
    /// only narrows which cards are listed in each column.
    /// </summary>
    public async Task<BoardDto> GetBoardAsync(WorkItemQuery filter, CancellationToken ct = default)
    {
        var columns = await _db.BoardColumns.AsNoTracking().OrderBy(c => c.SortOrder).ToListAsync(ct);
        var lanes = await _db.Swimlanes.AsNoTracking().OrderBy(s => s.SortOrder).ToListAsync(ct);

        var counts = await _db.WorkItems.AsNoTracking()
            .GroupBy(w => w.State)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.State, x => x.Count, ct);

        var visible = await WorkItemService.ApplyFilters(_db.WorkItems.AsNoTracking(), filter)
            .Where(w => w.State != WorkItemState.Removed)
            .Include(w => w.Program)
            .Include(w => w.Tags)
            .AsSplitQuery()
            .OrderBy(w => w.Priority)
            .ThenBy(w => w.Id)
            .ToListAsync(ct);

        var assignees = await _db.WorkItems.AsNoTracking()
            .Where(w => w.AssignedTo != null && w.State != WorkItemState.Removed)
            .Select(w => w.AssignedTo!)
            .Distinct()
            .OrderBy(a => a)
            .ToListAsync(ct);

        var programs = await _db.Programs.AsNoTracking()
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Name)
            .Select(p => p.Name)
            .ToListAsync(ct);

        var tags = (await _db.Tags.AsNoTracking().Select(t => t.Name).ToListAsync(ct))
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var onBoard = _db.WorkItems.AsNoTracking().Where(w => w.State != WorkItemState.Removed);
        var awaitingAgent = await Discussion.Where(onBoard, DiscussionStatus.AwaitingAgent).CountAsync(ct);
        var unreadReplies = await Discussion.Where(onBoard, DiscussionStatus.UnreadReply).CountAsync(ct);

        var byState = visible.ToLookup(w => w.State);

        return new BoardDto
        {
            Columns = columns.Select(c =>
            {
                var count = counts.GetValueOrDefault(c.State);
                return new BoardColumnDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    State = c.State,
                    WipLimit = c.WipLimit,
                    ItemCount = count,
                    IsOverWipLimit = IsOverWip(count, c.WipLimit),
                    Items = byState[c.State].Select(w => WorkItemMapper.ToDto(w)).ToList(),
                };
            }).ToList(),
            Swimlanes = lanes.Select(l => new SwimlaneDto(l.Id, l.Name, l.SortOrder, l.IsDefault)).ToList(),
            RemovedCount = counts.GetValueOrDefault(WorkItemState.Removed),
            Metadata = new BoardMetadataDto
            {
                Types = Enum.GetNames<WorkItemType>(),
                States = Enum.GetNames<WorkItemState>(),
                Severities = WorkItemDefaults.Severities,
                Priorities = WorkItemDefaults.Priorities,
                Assignees = assignees,
                Programs = programs,
                Tags = tags,
                AwaitingAgentCount = awaitingAgent,
                UnreadReplyCount = unreadReplies,
            },
        };
    }

    /// <summary>A column is over its limit when it holds strictly more items than allowed.</summary>
    public static bool IsOverWip(int count, int? limit) => limit is not null && count > limit.Value;
}
