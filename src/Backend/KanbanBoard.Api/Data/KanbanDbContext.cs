using KanbanBoard.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace KanbanBoard.Api.Data;

/// <summary>
/// EF Core context for the SQLite store at <c>App_Data/kanban.db</c>.
/// Schema changes MUST be delivered as additive EF migrations; the application
/// never calls EnsureDeleted/EnsureCreated against a real database.
/// </summary>
public class KanbanDbContext : DbContext
{
    public KanbanDbContext(DbContextOptions<KanbanDbContext> options)
        : base(options)
    {
    }

    public DbSet<WorkItem> WorkItems => Set<WorkItem>();

    public DbSet<WorkItemHistory> WorkItemHistory => Set<WorkItemHistory>();

    public DbSet<BoardColumn> BoardColumns => Set<BoardColumn>();

    public DbSet<Swimlane> Swimlanes => Set<Swimlane>();

    /// <summary>
    /// SQLite has no native DateTime type, so values come back with
    /// <see cref="DateTimeKind.Unspecified"/>. This converter re-stamps them as
    /// UTC so the JSON serializer emits an explicit "Z" suffix.
    /// </summary>
    private static readonly ValueConverter<DateTime, DateTime> UtcConverter = new(
        toDb => toDb.Kind == DateTimeKind.Utc ? toDb : toDb.ToUniversalTime(),
        fromDb => DateTime.SpecifyKind(fromDb, DateTimeKind.Utc));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkItem>(e =>
        {
            e.ToTable("WorkItem");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Title).IsRequired().HasMaxLength(WorkItemDefaults.TitleMaxLength);
            e.Property(x => x.Description);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(32).IsRequired();
            e.Property(x => x.State).HasConversion<string>().HasMaxLength(32).IsRequired();
            e.Property(x => x.Priority).IsRequired().HasDefaultValue(WorkItemDefaults.Priority);
            e.Property(x => x.Severity).IsRequired().HasMaxLength(32).HasDefaultValue(WorkItemDefaults.Severity);
            e.Property(x => x.AssignedTo).HasMaxLength(WorkItemDefaults.ShortTextMaxLength);
            e.Property(x => x.AreaPath).IsRequired().HasMaxLength(WorkItemDefaults.ShortTextMaxLength)
                .HasDefaultValue(WorkItemDefaults.AreaPath);
            e.Property(x => x.IterationPath).IsRequired().HasMaxLength(WorkItemDefaults.ShortTextMaxLength)
                .HasDefaultValue(WorkItemDefaults.IterationPath);
            e.Property(x => x.AiModified).IsRequired().HasDefaultValue(false);
            e.Property(x => x.AiAgentIdentity).HasMaxLength(WorkItemDefaults.ShortTextMaxLength);
            e.Property(x => x.LastModifiedBy).IsRequired().HasMaxLength(WorkItemDefaults.ShortTextMaxLength);
            e.Property(x => x.CreatedAt).HasConversion(UtcConverter);
            e.Property(x => x.UpdatedAt).HasConversion(UtcConverter);

            // Indexes backing the board query and the API quick filters.
            e.HasIndex(x => x.State);
            e.HasIndex(x => x.Type);
            e.HasIndex(x => x.AssignedTo);
            e.HasIndex(x => x.AiModified);

            e.HasMany(x => x.History)
                .WithOne(h => h.WorkItem!)
                .HasForeignKey(h => h.WorkItemId)
                // Audit rows must never be silently cascaded away.
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<WorkItemHistory>(e =>
        {
            e.ToTable("WorkItemHistory");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.ChangeDate).HasConversion(UtcConverter);
            e.Property(x => x.Author).IsRequired().HasMaxLength(WorkItemDefaults.ShortTextMaxLength);
            e.Property(x => x.IsAiAction).IsRequired();
            e.Property(x => x.AgentName).HasMaxLength(WorkItemDefaults.ShortTextMaxLength);
            e.Property(x => x.ChangedFieldsJson).IsRequired();
            e.Property(x => x.Comment);
            e.HasIndex(x => new { x.WorkItemId, x.ChangeDate });
        });

        modelBuilder.Entity<BoardColumn>(e =>
        {
            e.ToTable("BoardColumn");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(64);
            e.Property(x => x.State).HasConversion<string>().HasMaxLength(32).IsRequired();
            e.HasIndex(x => x.State).IsUnique();

            // Default columns from spec 3.3, seeded through the migration so
            // existing databases receive them without a destructive rebuild.
            e.HasData(
                new BoardColumn { Id = 1, Name = "New", State = WorkItemState.New, WipLimit = null, SortOrder = 0 },
                new BoardColumn { Id = 2, Name = "Active", State = WorkItemState.Active, WipLimit = 5, SortOrder = 1 },
                new BoardColumn { Id = 3, Name = "Resolved", State = WorkItemState.Resolved, WipLimit = 5, SortOrder = 2 },
                new BoardColumn { Id = 4, Name = "Closed", State = WorkItemState.Closed, WipLimit = null, SortOrder = 3 });
        });

        modelBuilder.Entity<Swimlane>(e =>
        {
            e.ToTable("Swimlane");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(64);
            e.HasData(new Swimlane { Id = 1, Name = "Default", SortOrder = 0, IsDefault = true });
        });
    }
}
