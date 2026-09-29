namespace KanbanBoard.Api.Models;

/// <summary>
/// A program (e.g. "ProveOut", "CallOut") a work item belongs to. Replaces the
/// free-text Area path in the UI (1.4.0); new programs are added from the
/// board's "+" button or the API. <see cref="WorkItem.AreaPath"/> is kept for
/// API compatibility.
/// </summary>
public class WorkProgram
{
    public int Id { get; set; }

    /// <summary>Display name, unique case-insensitively (see <see cref="NormalizedName"/>).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Upper-invariant name backing the unique index.</summary>
    public string NormalizedName { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>A free-form label; many per work item, shared across items.</summary>
public class Tag
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Upper-invariant name backing the unique index ("bug" and "BUG" are one tag).</summary>
    public string NormalizedName { get; set; } = string.Empty;

    public List<WorkItem> WorkItems { get; set; } = new();
}

/// <summary>
/// An uploaded image referenced from a description or comment. Stored inside
/// kanban.db so it is covered by the same backups and never touched by deploys.
/// </summary>
public class Attachment
{
    /// <summary>Random id (not guessable, unlike an autoincrement).</summary>
    public Guid Id { get; set; }

    public string FileName { get; set; } = string.Empty;

    /// <summary>Detected from the file's bytes, never taken from the client.</summary>
    public string ContentType { get; set; } = string.Empty;

    public long Length { get; set; }

    public byte[] Content { get; set; } = Array.Empty<byte>();

    /// <summary>Hex SHA-256 of <see cref="Content"/>; identical uploads are stored once.</summary>
    public string Sha256 { get; set; } = string.Empty;

    public string UploadedBy { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
