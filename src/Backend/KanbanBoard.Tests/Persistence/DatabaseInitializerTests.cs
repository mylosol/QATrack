using KanbanBoard.Api.Models;
using KanbanBoard.Tests.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace KanbanBoard.Tests.Persistence;

/// <summary>
/// Guards the SQLite persistence contract: migrations create the schema,
/// WAL is enabled, default columns exist and - most importantly - restarting
/// the application never destroys existing data.
/// </summary>
public class DatabaseInitializerTests
{
    [Fact]
    public async Task Initialize_CreatesDatabaseFile_AndAppliesAllMigrations()
    {
        using var temp = new TempSqliteDatabase();

        await temp.InitializeAsync();

        Assert.True(File.Exists(temp.FilePath));
        await using var db = temp.CreateContext();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task Initialize_EnablesWriteAheadLogging()
    {
        using var temp = new TempSqliteDatabase();
        await temp.InitializeAsync();

        await using var connection = new SqliteConnection(temp.ConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode;";
        var mode = (string?)await cmd.ExecuteScalarAsync();

        Assert.Equal("wal", mode, ignoreCase: true);
    }

    [Fact]
    public async Task Initialize_SeedsDefaultColumnsWithSpecWipLimits()
    {
        using var temp = new TempSqliteDatabase();
        await temp.InitializeAsync();

        await using var db = temp.CreateContext();
        var columns = await db.BoardColumns.OrderBy(c => c.SortOrder).ToListAsync();

        Assert.Collection(columns,
            c => { Assert.Equal("New", c.Name); Assert.Equal(WorkItemState.New, c.State); Assert.Null(c.WipLimit); },
            c => { Assert.Equal("Active", c.Name); Assert.Equal(WorkItemState.Active, c.State); Assert.Equal(5, c.WipLimit); },
            c => { Assert.Equal("Resolved", c.Name); Assert.Equal(WorkItemState.Resolved, c.State); Assert.Equal(5, c.WipLimit); },
            c => { Assert.Equal("Closed", c.Name); Assert.Equal(WorkItemState.Closed, c.State); Assert.Null(c.WipLimit); });

        var lane = Assert.Single(await db.Swimlanes.ToListAsync());
        Assert.True(lane.IsDefault);
    }

    [Fact]
    public async Task Initialize_RunTwice_PreservesExistingWorkItems()
    {
        // Simulates an app-pool recycle / redeploy on top of a live database.
        using var temp = new TempSqliteDatabase();
        await temp.InitializeAsync();

        await using (var db = temp.CreateContext())
        {
            db.WorkItems.Add(new WorkItem
            {
                Title = "Production data that must survive",
                LastModifiedBy = "tester",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await temp.InitializeAsync(seed: true);

        await using var verify = temp.CreateContext();
        var items = await verify.WorkItems.ToListAsync();
        var item = Assert.Single(items);
        Assert.Equal("Production data that must survive", item.Title);
    }

    [Fact]
    public async Task Initialize_WithSeedOnEmptyBoard_InsertsSampleItemsOnce()
    {
        using var temp = new TempSqliteDatabase();
        await temp.InitializeAsync(seed: true);
        await temp.InitializeAsync(seed: true);

        await using var db = temp.CreateContext();
        var count = await db.WorkItems.CountAsync();
        Assert.True(count > 0);
        Assert.Equal(count, await db.WorkItemHistory.CountAsync());
    }

    [Fact]
    public async Task Initialize_WithoutSeed_LeavesBoardEmpty()
    {
        using var temp = new TempSqliteDatabase();
        await temp.InitializeAsync(seed: false);

        await using var db = temp.CreateContext();
        Assert.Equal(0, await db.WorkItems.CountAsync());
    }

    [Fact]
    public async Task WorkItem_DefaultsAndUtcTimestamps_RoundTrip()
    {
        using var temp = new TempSqliteDatabase();
        await temp.InitializeAsync();
        var stamp = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        int id;
        await using (var db = temp.CreateContext())
        {
            var item = new WorkItem { Title = "Defaults", LastModifiedBy = "t", CreatedAt = stamp, UpdatedAt = stamp };
            db.WorkItems.Add(item);
            await db.SaveChangesAsync();
            id = item.Id;
        }

        await using var read = temp.CreateContext();
        var loaded = await read.WorkItems.SingleAsync(w => w.Id == id);
        Assert.Equal(@"Tools\QA", loaded.AreaPath);
        Assert.Equal("Current", loaded.IterationPath);
        Assert.False(loaded.AiModified);
        Assert.Equal(DateTimeKind.Utc, loaded.CreatedAt.Kind);
        Assert.Equal(stamp, loaded.CreatedAt);
    }

    [Fact]
    public async Task EnumsAreStoredAsText()
    {
        using var temp = new TempSqliteDatabase();
        await temp.InitializeAsync();
        await using (var db = temp.CreateContext())
        {
            db.WorkItems.Add(new WorkItem
            {
                Title = "Text enums", Type = WorkItemType.UserStory, State = WorkItemState.Resolved,
                LastModifiedBy = "t", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await using var connection = new SqliteConnection(temp.ConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Type || '|' || State FROM WorkItem LIMIT 1;";
        Assert.Equal("UserStory|Resolved", (string?)await cmd.ExecuteScalarAsync());
    }

    [Fact]
    public async Task DeletingWorkItemWithHistory_IsRejected_ToProtectAuditTrail()
    {
        using var temp = new TempSqliteDatabase();
        await temp.InitializeAsync(seed: true);

        await using var db = temp.CreateContext();
        var item = await db.WorkItems.FirstAsync();
        db.WorkItems.Remove(item);

        await Assert.ThrowsAnyAsync<Exception>(() => db.SaveChangesAsync());
    }
}
