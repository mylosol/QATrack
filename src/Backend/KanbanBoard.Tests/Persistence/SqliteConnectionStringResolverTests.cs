using KanbanBoard.Api.Data;
using Microsoft.Data.Sqlite;

namespace KanbanBoard.Tests.Persistence;

public class SqliteConnectionStringResolverTests
{
    [Fact]
    public void RelativeDataSource_IsAnchoredToContentRoot_AndDirectoryCreated()
    {
        var root = Path.Combine(Path.GetTempPath(), "qatrack-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var resolved = SqliteConnectionStringResolver.Resolve(
                "Data Source=App_Data/kanban.db;Cache=Shared;Mode=ReadWriteCreate;", root);

            var builder = new SqliteConnectionStringBuilder(resolved);
            Assert.Equal(Path.GetFullPath(Path.Combine(root, "App_Data", "kanban.db")), builder.DataSource);
            Assert.Equal(SqliteCacheMode.Shared, builder.Cache);
            Assert.Equal(SqliteOpenMode.ReadWriteCreate, builder.Mode);
            Assert.True(Directory.Exists(Path.Combine(root, "App_Data")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void AbsoluteDataSource_IsLeftUnchanged()
    {
        var absolute = Path.Combine(Path.GetTempPath(), "qatrack-abs", "x.db");
        var resolved = SqliteConnectionStringResolver.Resolve($"Data Source={absolute}", "C:\\ignored");
        Assert.Equal(absolute, new SqliteConnectionStringBuilder(resolved).DataSource);
    }

    [Fact]
    public void InMemoryDataSource_IsLeftUnchanged()
    {
        var resolved = SqliteConnectionStringResolver.Resolve("Data Source=:memory:", "C:\\ignored");
        Assert.Equal(":memory:", new SqliteConnectionStringBuilder(resolved).DataSource);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingConnectionString_Throws(string? value)
    {
        Assert.Throws<InvalidOperationException>(() => SqliteConnectionStringResolver.Resolve(value, "C:\\root"));
    }
}
