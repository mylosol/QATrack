using KanbanBoard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace KanbanBoard.Api.Data;

/// <summary>
/// Brings the SQLite database up to date at application start.
/// </summary>
/// <remarks>
/// Safety contract (spec "State and Database Protection"):
/// <list type="bullet">
/// <item>Only EF <c>Migrate()</c> is used - forward-only, additive migrations.</item>
/// <item>Nothing here ever drops, truncates, deletes or recreates the database.</item>
/// <item>Demo data is only inserted when explicitly enabled AND the board is empty.</item>
/// </list>
/// </remarks>
public sealed class DatabaseInitializer
{
    private readonly KanbanDbContext _db;
    private readonly ILogger<DatabaseInitializer> _logger;
    private readonly TimeProvider _clock;

    public DatabaseInitializer(KanbanDbContext db, ILogger<DatabaseInitializer> logger, TimeProvider clock)
    {
        _db = db;
        _logger = logger;
        _clock = clock;
    }

    /// <summary>
    /// Applies pending migrations, enables WAL journaling and optionally seeds demo data.
    /// </summary>
    /// <param name="seedSampleData">Insert a handful of demo cards when the board is empty.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task InitializeAsync(bool seedSampleData, CancellationToken ct = default)
    {
        var pending = (await _db.Database.GetPendingMigrationsAsync(ct)).ToList();
        if (pending.Count > 0)
        {
            _logger.LogInformation("Applying {Count} pending database migration(s): {Migrations}",
                pending.Count, string.Join(", ", pending));
        }

        await _db.Database.MigrateAsync(ct);

        await EnableWriteAheadLoggingAsync(ct);

        if (seedSampleData)
        {
            await SeedSampleDataAsync(ct);
        }
    }

    /// <summary>
    /// Switches the database to WAL mode. The setting is persisted inside the
    /// database file, so it survives restarts and app pool recycles; running it
    /// again on every start is idempotent and cheap.
    /// </summary>
    private async Task EnableWriteAheadLoggingAsync(CancellationToken ct)
    {
        var connection = _db.Database.GetDbConnection();
        var wasClosed = connection.State != System.Data.ConnectionState.Open;
        if (wasClosed)
        {
            await connection.OpenAsync(ct);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=WAL;";
            var mode = (await command.ExecuteScalarAsync(ct))?.ToString();
            if (!string.Equals(mode, "wal", StringComparison.OrdinalIgnoreCase))
            {
                // In-memory databases legitimately report "memory".
                _logger.LogWarning("SQLite journal_mode is '{Mode}', expected 'wal'.", mode);
            }
        }
        finally
        {
            if (wasClosed)
            {
                await connection.CloseAsync();
            }
        }
    }

    /// <summary>Inserts demo cards so a fresh development board is not empty.</summary>
    private async Task SeedSampleDataAsync(CancellationToken ct)
    {
        if (await _db.WorkItems.AnyAsync(ct))
        {
            return;
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        const string author = "System Seeder";
        var samples = new[]
        {
            new WorkItem
            {
                Title = "Login page throws 500 when password contains unicode",
                Type = WorkItemType.Bug, State = WorkItemState.New, Priority = 1, Severity = "1 - Critical",
                Description = "Steps:\n1. Open `/login`\n2. Enter `pässwörd`\n\n**Expected:** validation message.",
            },
            new WorkItem { Title = "Add CSV export to regression report", Type = WorkItemType.Feature, State = WorkItemState.Active, Priority = 2, AssignedTo = "QA Team" },
            new WorkItem { Title = "As a tester I can filter results by build", Type = WorkItemType.UserStory, State = WorkItemState.Active, Priority = 3 },
            new WorkItem { Title = "Flaky smoke test: checkout timeout", Type = WorkItemType.Bug, State = WorkItemState.Resolved, Priority = 2, Severity = "2 - High" },
            new WorkItem { Title = "QA tooling modernization", Type = WorkItemType.Epic, State = WorkItemState.New, Priority = 3 },
            new WorkItem { Title = "Upgrade test runner to latest", Type = WorkItemType.Task, State = WorkItemState.Closed, Priority = 4, Severity = "4 - Low" },
        };

        foreach (var item in samples)
        {
            item.CreatedAt = now;
            item.UpdatedAt = now;
            item.LastModifiedBy = author;
            item.History.Add(new WorkItemHistory
            {
                ChangeDate = now,
                Author = author,
                IsAiAction = false,
                ChangedFieldsJson = "{}",
                Comment = "Sample item created by the development seeder.",
            });
        }

        _db.WorkItems.AddRange(samples);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded {Count} sample work items.", samples.Length);
    }
}
