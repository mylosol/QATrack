using KanbanBoard.Api.Data;
using KanbanBoard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace KanbanBoard.Api.Services;

/// <summary>The Program dropdown's options (1.4.0).</summary>
public sealed class ProgramService
{
    private readonly KanbanDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<ProgramService> _logger;
    private readonly IActorContext _actor;

    public ProgramService(KanbanDbContext db, TimeProvider clock, ILogger<ProgramService> logger, IActorContext actor)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
        _actor = actor;
    }

    /// <summary>All programs in display order.</summary>
    public async Task<IReadOnlyList<ProgramDto>> ListAsync(CancellationToken ct = default) =>
        await _db.Programs.AsNoTracking()
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Name)
            .Select(p => new ProgramDto(p.Id, p.Name, p.SortOrder))
            .ToListAsync(ct);

    /// <summary>
    /// Adds a program at the end of the list. Idempotent: when a program with
    /// the same name (ignoring case) exists, that one is returned instead.
    /// </summary>
    /// <returns>The program and whether it was newly created.</returns>
    public async Task<(ProgramDto Program, bool Created)> CreateAsync(CreateProgramRequest request, CancellationToken ct = default)
    {
        var name = TextSanitizer.SingleLine(request.Name, WorkItemDefaults.ProgramNameMaxLength)
                   ?? throw new WorkItemValidationException(nameof(request.Name), "Program name is required.");
        var normalized = name.ToUpperInvariant();

        var existing = await _db.Programs.AsNoTracking().FirstOrDefaultAsync(p => p.NormalizedName == normalized, ct);
        if (existing is not null)
        {
            return (new ProgramDto(existing.Id, existing.Name, existing.SortOrder), false);
        }

        var nextOrder = (await _db.Programs.MaxAsync(p => (int?)p.SortOrder, ct) ?? -1) + 1;
        var program = new WorkProgram
        {
            Name = name,
            NormalizedName = normalized,
            SortOrder = nextOrder,
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        };
        _db.Programs.Add(program);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Program '{Program}' added by {Actor}.", name, _actor.DisplayName);
        return (new ProgramDto(program.Id, program.Name, program.SortOrder), true);
    }
}
