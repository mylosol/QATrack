using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace KanbanBoard.Api.Data;

/// <summary>
/// Used only by the <c>dotnet ef</c> tooling to scaffold migrations. It points
/// at a throwaway file so migration authoring never touches a real database.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<KanbanDbContext>
{
    public KanbanDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<KanbanDbContext>()
            .UseSqlite("Data Source=design-time.db")
            .Options;
        return new KanbanDbContext(options);
    }
}
