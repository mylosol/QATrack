using KanbanBoard.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace KanbanBoard.Tests.Infrastructure;

/// <summary>
/// A throwaway on-disk SQLite database (WAL needs a real file) in the temp
/// folder. Disposing clears the connection pool and deletes the files.
/// </summary>
public sealed class TempSqliteDatabase : IDisposable
{
    public TempSqliteDatabase()
    {
        Directory = Path.Combine(Path.GetTempPath(), "qatrack-tests", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Directory);
        FilePath = Path.Combine(Directory, "kanban.db");
        ConnectionString = $"Data Source={FilePath};Cache=Shared;Mode=ReadWriteCreate;";
    }

    /// <summary>Folder that holds the database and its WAL/SHM side files.</summary>
    public string Directory { get; }

    public string FilePath { get; }

    public string ConnectionString { get; }

    /// <summary>Creates a new context against this database.</summary>
    public KanbanDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<KanbanDbContext>().UseSqlite(ConnectionString).Options);

    /// <summary>Runs the production initializer (migrations + WAL) against this database.</summary>
    public async Task InitializeAsync(bool seed = false, TimeProvider? clock = null)
    {
        await using var db = CreateContext();
        var initializer = new DatabaseInitializer(db, NullLogger<DatabaseInitializer>.Instance, clock ?? TimeProvider.System);
        await initializer.InitializeAsync(seed);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a lingering handle on Windows must not fail the test run.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
