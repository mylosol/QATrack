using Microsoft.Data.Sqlite;

namespace KanbanBoard.Api.Data;

/// <summary>
/// Normalizes the configured SQLite connection string.
/// </summary>
/// <remarks>
/// Under IIS the process working directory is not guaranteed to be the site
/// folder, so a relative <c>Data Source=App_Data/kanban.db</c> could silently
/// create a second, empty database somewhere else (e.g. system32). Relative
/// paths are therefore anchored to the application's content root and the
/// directory is created before EF opens the file.
/// </remarks>
public static class SqliteConnectionStringResolver
{
    /// <summary>
    /// Returns <paramref name="connectionString"/> with a rooted Data Source.
    /// </summary>
    /// <param name="connectionString">Raw connection string from configuration.</param>
    /// <param name="contentRootPath">Application content root (site folder).</param>
    public static string Resolve(string? connectionString, string contentRootPath)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:Kanban is not configured.");
        }

        var builder = new SqliteConnectionStringBuilder(connectionString);
        var dataSource = builder.DataSource;

        // In-memory databases (used by some tests) have nothing to anchor.
        if (string.IsNullOrWhiteSpace(dataSource) ||
            dataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase) ||
            builder.Mode == SqliteOpenMode.Memory)
        {
            return builder.ToString();
        }

        var fullPath = Path.IsPathRooted(dataSource)
            ? dataSource
            : Path.GetFullPath(Path.Combine(contentRootPath, dataSource));

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        builder.DataSource = fullPath;
        return builder.ToString();
    }
}
