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

    public DbSet<CommentRevision> CommentRevisions => Set<CommentRevision>();

    public DbSet<WorkItemFile> WorkItemFiles => Set<WorkItemFile>();

    public DbSet<BoardColumn> BoardColumns => Set<BoardColumn>();

    public DbSet<Swimlane> Swimlanes => Set<Swimlane>();

    public DbSet<WorkProgram> Programs => Set<WorkProgram>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<Attachment> Attachments => Set<Attachment>();

    /// <summary>
    /// SQLite has no native DateTime type, so values come back with
    /// <see cref="DateTimeKind.Unspecified"/>. This converter re-stamps them as
    /// UTC so the JSON serializer emits an explicit "Z" suffix.
    /// </summary>
    private static readonly ValueConverter<DateTime, DateTime> UtcConverter = new(
        toDb => toDb.Kind == DateTimeKind.Utc ? toDb : toDb.ToUniversalTime(),
        fromDb => DateTime.SpecifyKind(fromDb, DateTimeKind.Utc));

    /// <summary>Nullable counterpart of <see cref="UtcConverter"/>.</summary>
    private static readonly ValueConverter<DateTime?, DateTime?> NullableUtcConverter = new(
        toDb => toDb == null ? null : toDb.Value.Kind == DateTimeKind.Utc ? toDb : toDb.Value.ToUniversalTime(),
        fromDb => fromDb == null ? null : DateTime.SpecifyKind(fromDb.Value, DateTimeKind.Utc));

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
            e.Property(x => x.ProgramVersion).HasMaxLength(WorkItemDefaults.ProgramVersionMaxLength);
            e.Property(x => x.LastHumanCommentAt).HasConversion(NullableUtcConverter);
            e.Property(x => x.LastHumanCommentBy).HasMaxLength(WorkItemDefaults.ShortTextMaxLength);
            e.Property(x => x.LastAgentCommentAt).HasConversion(NullableUtcConverter);
            e.Property(x => x.LastAgentCommentBy).HasMaxLength(WorkItemDefaults.ShortTextMaxLength);
            e.Property(x => x.CommentCount).IsRequired().HasDefaultValue(0);
            e.Property(x => x.FileCount).IsRequired().HasDefaultValue(0);
            e.Property(x => x.HumanReadAt).HasConversion(NullableUtcConverter);
            e.Property(x => x.ReportKey).HasMaxLength(WorkItemDefaults.ReportKeyMaxLength);
            // SQLite allows many NULLs in a unique index: only reports carry a key.
            e.HasIndex(x => x.ReportKey).IsUnique();
            e.HasIndex(x => x.UpdatedAt);
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
            e.Property(x => x.EditedAt).HasConversion(NullableUtcConverter);
            e.HasIndex(x => new { x.WorkItemId, x.ChangeDate });
            e.HasMany(x => x.Revisions)
                .WithOne(r => r.History!)
                .HasForeignKey(r => r.HistoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<WorkItemFile>(e =>
        {
            e.ToTable("WorkItemFile");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.FileName).IsRequired().HasMaxLength(WorkItemDefaults.FileNameMaxLength);
            e.Property(x => x.ContentType).IsRequired().HasMaxLength(100);
            e.Property(x => x.AddedAt).HasConversion(UtcConverter);
            e.Property(x => x.AddedBy).IsRequired().HasMaxLength(WorkItemDefaults.ShortTextMaxLength);
            e.Property(x => x.RemovedAt).HasConversion(NullableUtcConverter);
            e.Property(x => x.RemovedBy).HasMaxLength(WorkItemDefaults.ShortTextMaxLength);
            e.HasIndex(x => x.WorkItemId);
            // Files are part of the audit trail: never cascaded away.
            e.HasOne(x => x.WorkItem).WithMany(w => w.Files).HasForeignKey(x => x.WorkItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Attachment).WithMany().HasForeignKey(x => x.AttachmentId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CommentRevision>(e =>
        {
            e.ToTable("CommentRevision");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Comment).IsRequired();
            e.Property(x => x.ReplacedAt).HasConversion(UtcConverter);
            e.Property(x => x.ReplacedBy).IsRequired().HasMaxLength(WorkItemDefaults.ShortTextMaxLength);
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
            // 1.11.0: no WIP limits by default (spec 3.3 had Active/Resolved = 5).
            // AI agents work many cards in parallel, so a 5-card limit was
            // permanently exceeded. The warning logic is kept and applies again
            // as soon as a column is given a limit.
            e.HasData(
                new BoardColumn { Id = 1, Name = "New", State = WorkItemState.New, WipLimit = null, SortOrder = 0 },
                new BoardColumn { Id = 2, Name = "Active", State = WorkItemState.Active, WipLimit = null, SortOrder = 1 },
                new BoardColumn { Id = 3, Name = "Resolved", State = WorkItemState.Resolved, WipLimit = null, SortOrder = 2 },
                new BoardColumn { Id = 4, Name = "Closed", State = WorkItemState.Closed, WipLimit = null, SortOrder = 3 });
        });

        // ---- 1.4.0: programs, tags, attachments (additive, backward compatible) ----
        modelBuilder.Entity<WorkProgram>(e =>
        {
            e.ToTable("Program");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(WorkItemDefaults.ProgramNameMaxLength);
            e.Property(x => x.NormalizedName).IsRequired().HasMaxLength(WorkItemDefaults.ProgramNameMaxLength);
            e.HasIndex(x => x.NormalizedName).IsUnique();
            e.Property(x => x.CreatedAt).HasConversion(UtcConverter);
            var seeded = new DateTime(2026, 9, 29, 0, 0, 0, DateTimeKind.Utc);
            e.HasData(WorkItemDefaults.InitialPrograms.Select((name, i) => new WorkProgram
            {
                Id = i + 1,
                Name = name,
                NormalizedName = name.ToUpperInvariant(),
                SortOrder = i,
                CreatedAt = seeded,
            }));
        });

        modelBuilder.Entity<WorkItem>(e =>
        {
            e.HasOne(x => x.Program)
                .WithMany()
                .HasForeignKey(x => x.ProgramId)
                // A program in use cannot be deleted out from under its items.
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.ProgramId);

            e.HasMany(x => x.Tags)
                .WithMany(t => t.WorkItems)
                .UsingEntity<Dictionary<string, object>>(
                    "WorkItemTag",
                    r => r.HasOne<Tag>().WithMany().HasForeignKey("TagId").OnDelete(DeleteBehavior.Cascade),
                    l => l.HasOne<WorkItem>().WithMany().HasForeignKey("WorkItemId").OnDelete(DeleteBehavior.Cascade),
                    j =>
                    {
                        j.ToTable("WorkItemTag");
                        j.HasKey("WorkItemId", "TagId");
                        j.HasIndex("TagId");
                    });
        });

        modelBuilder.Entity<Tag>(e =>
        {
            e.ToTable("Tag");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(WorkItemDefaults.TagMaxLength);
            e.Property(x => x.NormalizedName).IsRequired().HasMaxLength(WorkItemDefaults.TagMaxLength);
            e.HasIndex(x => x.NormalizedName).IsUnique();
        });

        modelBuilder.Entity<Attachment>(e =>
        {
            e.ToTable("Attachment");
            e.HasKey(x => x.Id);
            e.Property(x => x.FileName).IsRequired().HasMaxLength(WorkItemDefaults.ShortTextMaxLength);
            e.Property(x => x.ContentType).IsRequired().HasMaxLength(64);
            e.Property(x => x.Content).IsRequired();
            e.Property(x => x.Sha256).IsRequired().HasMaxLength(64);
            e.HasIndex(x => x.Sha256);
            e.Property(x => x.UploadedBy).IsRequired().HasMaxLength(WorkItemDefaults.ShortTextMaxLength);
            e.Property(x => x.CreatedAt).HasConversion(UtcConverter);
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
