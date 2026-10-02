using KanbanBoard.Api.Models;
using KanbanBoard.Tests.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

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
    public async Task Initialize_SeedsDefaultColumns_WithoutWipLimits()
    {
        using var temp = new TempSqliteDatabase();
        await temp.InitializeAsync();

        await using var db = temp.CreateContext();
        var columns = await db.BoardColumns.OrderBy(c => c.SortOrder).ToListAsync();

        Assert.Collection(columns,
            c => { Assert.Equal("New", c.Name); Assert.Equal(WorkItemState.New, c.State); Assert.Null(c.WipLimit); },
            c => { Assert.Equal("Active", c.Name); Assert.Equal(WorkItemState.Active, c.State); Assert.Null(c.WipLimit); },
            c => { Assert.Equal("Resolved", c.Name); Assert.Equal(WorkItemState.Resolved, c.State); Assert.Null(c.WipLimit); },
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
    public async Task Upgrade_From130Schema_KeepsExistingItems_AndAddsPrograms()
    {
        // A database created by 1.3.0 (InitialCreate only) with a live item.
        using var temp = new TempSqliteDatabase();
        await using (var old = temp.CreateContext())
        {
            await old.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>()
                .MigrateAsync("20260929161034_InitialCreate");
            await old.Database.ExecuteSqlRawAsync(
                "INSERT INTO WorkItem (Title, Type, State, Priority, Severity, AreaPath, IterationPath, AiModified, LastModifiedBy, CreatedAt, UpdatedAt) " +
                "VALUES ('Pre-1.4 item', 'Bug', 'Active', 1, '1 - Critical', 'Tools/QA', 'Current', 0, 'tester', '2026-09-01 00:00:00', '2026-09-01 00:00:00')");
        }

        await temp.InitializeAsync();

        await using var db = temp.CreateContext();
        var item = await db.WorkItems.Include(w => w.Tags).SingleAsync();
        Assert.Equal("Pre-1.4 item", item.Title);
        Assert.Null(item.ProgramId);
        Assert.Empty(item.Tags);
        Assert.Equal(new[] { "ProveOut", "CallOut" }, await db.Programs.OrderBy(p => p.SortOrder).Select(p => p.Name).ToListAsync());
    }

    [Fact]
    public async Task Upgrade_To180_BackfillsTheLastHumanComment_FromHistory()
    {
        using var temp = new TempSqliteDatabase();
        await using (var old = temp.CreateContext())
        {
            await old.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>()
                .MigrateAsync("20260930165826_AddProgramVersion");
            await old.Database.ExecuteSqlRawAsync(
                "INSERT INTO WorkItem (Id, Title, Type, State, Priority, Severity, AreaPath, IterationPath, AiModified, LastModifiedBy, CreatedAt, UpdatedAt) VALUES " +
                "(1, 'Commented', 'Bug', 'New', 2, '3 - Medium', 'x', 'Current', 1, 'Bot', '2026-09-30 10:00:00', '2026-09-30 13:00:00'), " +
                "(2, 'Silent', 'Bug', 'New', 2, '3 - Medium', 'x', 'Current', 0, 'Dana', '2026-09-30 10:00:00', '2026-09-30 10:00:00')");
            // Braces are doubled: ExecuteSqlRaw treats them as format placeholders.
            await old.Database.ExecuteSqlRawAsync(
                "INSERT INTO WorkItemHistory (WorkItemId, ChangeDate, Author, IsAiAction, AgentName, ChangedFieldsJson, Comment) VALUES " +
                "(1, '2026-09-30 10:00:00', 'Dana', 0, NULL, '{{}}', 'first human note'), " +
                "(1, '2026-09-30 11:00:00', 'Robert', 0, NULL, '{{}}', 'latest human note'), " +
                "(1, '2026-09-30 12:00:00', 'Robert', 0, NULL, '{{\"State\":{{}}}}', NULL), " +
                "(1, '2026-09-30 13:00:00', 'Bot', 1, 'Bot', '{{}}', 'agent reply'), " +
                "(2, '2026-09-30 10:00:00', 'Dana', 0, NULL, '{{}}', NULL)");
        }

        await temp.InitializeAsync();

        await using var db = temp.CreateContext();
        var commented = await db.WorkItems.SingleAsync(w => w.Id == 1);
        Assert.Equal(new DateTime(2026, 9, 30, 11, 0, 0, DateTimeKind.Utc), commented.LastHumanCommentAt);
        Assert.Equal("Robert", commented.LastHumanCommentBy);
        var silent = await db.WorkItems.SingleAsync(w => w.Id == 2);
        Assert.Null(silent.LastHumanCommentAt);
        Assert.Null(silent.LastHumanCommentBy);

        // 1.9.0 backfill: counts, last agent comment, and "unread" for agent replies nobody has opened.
        Assert.Equal(3, commented.CommentCount);
        Assert.Equal(new DateTime(2026, 9, 30, 13, 0, 0, DateTimeKind.Utc), commented.LastAgentCommentAt);
        Assert.Equal("Bot", commented.LastAgentCommentBy);
        Assert.Null(commented.HumanReadAt);
        Assert.Equal(KanbanBoard.Api.Models.DiscussionStatus.UnreadReply, KanbanBoard.Api.Services.Discussion.StatusOf(commented));
        Assert.Equal(0, silent.CommentCount);
        Assert.Null(KanbanBoard.Api.Services.Discussion.StatusOf(silent));
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
