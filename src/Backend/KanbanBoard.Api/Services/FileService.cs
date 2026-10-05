using System.Text.Unicode;
using KanbanBoard.Api.Data;
using KanbanBoard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace KanbanBoard.Api.Services;

/// <summary>
/// Files attached to work items (1.14.0): logs, test output and other text,
/// and .zip / .gz / .7z archives of them.
/// </summary>
/// <remarks>
/// The type is decided from the bytes, never from the name or the client's
/// Content-Type. Text is served as text/plain (with nosniff, so a browser never
/// runs it); archives are always downloads. Attaching and removing are recorded
/// in the card's history; removing only hides the file.
/// </remarks>
public sealed class FileService
{
    public const string Text = "text/plain";
    public const string Zip = "application/zip";
    public const string Gzip = "application/gzip";
    public const string SevenZip = "application/x-7z-compressed";

    /// <summary>History field name for attach/remove entries.</summary>
    public const string HistoryField = "Files";

    /// <summary>What can be attached, for error messages and docs.</summary>
    public const string AcceptedDescription = "log or other text files, or .zip, .gz or .7z archives";

    private readonly KanbanDbContext _db;
    private readonly IActorContext _actor;
    private readonly TimeProvider _clock;
    private readonly AttachmentService _blobs;
    private readonly WorkItemService _items;

    public FileService(KanbanDbContext db, IActorContext actor, TimeProvider clock, AttachmentService blobs, WorkItemService items)
    {
        _db = db;
        _actor = actor;
        _clock = clock;
        _blobs = blobs;
        _items = items;
    }

    /// <summary>Reads, validates and stores an upload; returns the stored id, name, type and size. Not linked to a card yet.</summary>
    /// <exception cref="WorkItemValidationException">Empty, too large, or not a log/text file or archive.</exception>
    public async Task<(Guid AttachmentId, string FileName, string ContentType, long Length)> StoreAsync(Stream content, string? fileName, CancellationToken ct = default)
    {
        var bytes = await AttachmentService.ReadLimitedAsync(content, WorkItemDefaults.FileMaxBytes, "Files", ct);
        var contentType = DetectFileType(bytes)
                          ?? throw new WorkItemValidationException("file",
                              $"Only {AcceptedDescription} can be attached. Put screenshots in the description or a comment.");
        var name = CleanFileName(fileName, contentType);
        var (id, _) = await _blobs.StoreAsync(bytes, name, contentType, ct);
        return (id, name, contentType, bytes.Length);
    }

    /// <summary>Uploads a file and attaches it to the work item, recording it in history.</summary>
    /// <exception cref="WorkItemNotFoundException">When the work item does not exist.</exception>
    public async Task<WorkItemFileDto> AttachAsync(int workItemId, Stream content, string? fileName, CancellationToken ct = default)
    {
        // Check the card first, so a typo in the id doesn't store a 20 MB orphan.
        if (!await _db.WorkItems.AnyAsync(w => w.Id == workItemId, ct))
        {
            throw new WorkItemNotFoundException(workItemId);
        }

        var stored = await StoreAsync(content, fileName, ct);
        return await LinkAsync(workItemId, stored.AttachmentId, stored.FileName, stored.ContentType, stored.Length, ct);
    }

    /// <summary>Attaches already stored files (an in-app report's uploads) to a new card.</summary>
    internal async Task LinkStoredAsync(int workItemId, IReadOnlyList<Guid> attachmentIds, CancellationToken ct)
    {
        foreach (var id in attachmentIds)
        {
            var blob = await _db.Attachments.AsNoTracking()
                .Where(a => a.Id == id)
                .Select(a => new { a.FileName, a.ContentType, a.Length })
                .FirstAsync(ct);
            await LinkAsync(workItemId, id, blob.FileName, blob.ContentType, blob.Length, ct);
        }
    }

    /// <summary>The ids that are not stored files (unknown, or images).</summary>
    internal async Task<IReadOnlyList<Guid>> FindMissingAsync(IReadOnlyCollection<Guid> attachmentIds, CancellationToken ct)
    {
        var found = await _db.Attachments.AsNoTracking()
            .Where(a => attachmentIds.Contains(a.Id) && !a.ContentType.StartsWith("image/"))
            .Select(a => a.Id)
            .ToListAsync(ct);
        return attachmentIds.Except(found).ToList();
    }

    private async Task<WorkItemFileDto> LinkAsync(int workItemId, Guid attachmentId, string fileName, string contentType, long length, CancellationToken ct)
    {
        var item = await _db.WorkItems.FirstOrDefaultAsync(w => w.Id == workItemId, ct)
                   ?? throw new WorkItemNotFoundException(workItemId);
        var now = _clock.GetUtcNow().UtcDateTime;
        var file = new WorkItemFile
        {
            WorkItemId = workItemId,
            AttachmentId = attachmentId,
            FileName = fileName,
            ContentType = contentType,
            Length = length,
            AddedAt = now,
            AddedBy = _actor.DisplayName,
            IsAiAction = _actor.IsAi,
        };
        _db.WorkItemFiles.Add(file);
        item.FileCount++;
        _items.StampAndRecord(item, new Dictionary<string, FieldChange> { [HistoryField] = new(null, fileName) }, null, now);
        await _db.SaveChangesAsync(ct);
        return WorkItemMapper.ToDto(file);
    }

    /// <summary>Files attached to a work item (removed ones excluded), oldest first.</summary>
    /// <exception cref="WorkItemNotFoundException">When the work item does not exist.</exception>
    public async Task<IReadOnlyList<WorkItemFileDto>> ListAsync(int workItemId, CancellationToken ct = default)
    {
        if (!await _db.WorkItems.AnyAsync(w => w.Id == workItemId, ct))
        {
            throw new WorkItemNotFoundException(workItemId);
        }

        var files = await _db.WorkItemFiles.AsNoTracking()
            .Where(f => f.WorkItemId == workItemId && f.RemovedAt == null)
            .OrderBy(f => f.AddedAt).ThenBy(f => f.Id)
            .ToListAsync(ct);
        return files.Select(WorkItemMapper.ToDto).ToList();
    }

    /// <summary>Removes a file from the card (hidden, kept for the audit trail) and records it in history.</summary>
    /// <exception cref="FileNotFoundOnItemException">Unknown, on another card, or already removed.</exception>
    public async Task RemoveAsync(int workItemId, int fileId, CancellationToken ct = default)
    {
        var file = await _db.WorkItemFiles.Include(f => f.WorkItem)
                       .FirstOrDefaultAsync(f => f.Id == fileId && f.WorkItemId == workItemId && f.RemovedAt == null, ct)
                   ?? throw new FileNotFoundOnItemException(workItemId, fileId);
        var now = _clock.GetUtcNow().UtcDateTime;
        file.RemovedAt = now;
        file.RemovedBy = _actor.DisplayName;
        var item = file.WorkItem!;
        item.FileCount = Math.Max(0, item.FileCount - 1);
        _items.StampAndRecord(item, new Dictionary<string, FieldChange> { [HistoryField] = new(file.FileName, null) }, null, now);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>A file's name, type and bytes for download.</summary>
    /// <exception cref="FileNotFoundOnItemException">Unknown, on another card, or removed.</exception>
    public async Task<(WorkItemFile File, byte[] Content)> OpenAsync(int workItemId, int fileId, CancellationToken ct = default)
    {
        var file = await _db.WorkItemFiles.AsNoTracking()
                       .FirstOrDefaultAsync(f => f.Id == fileId && f.WorkItemId == workItemId && f.RemovedAt == null, ct)
                   ?? throw new FileNotFoundOnItemException(workItemId, fileId);
        var content = await _db.Attachments.AsNoTracking().Where(a => a.Id == file.AttachmentId).Select(a => a.Content).FirstAsync(ct);
        return (file, content);
    }

    /// <summary>Archives by signature; otherwise text if it looks like text; anything else is null.</summary>
    public static string? DetectFileType(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 4 && b[0] == 0x50 && b[1] == 0x4B && b[2] is 0x03 or 0x05 or 0x07 && b[3] is 0x04 or 0x06 or 0x08)
        {
            return Zip;
        }

        if (b.Length >= 2 && b[0] == 0x1F && b[1] == 0x8B)
        {
            return Gzip;
        }

        if (b.Length >= 6 && b[..6].SequenceEqual(new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C }))
        {
            return SevenZip;
        }

        return LooksLikeText(b) ? Text : null;
    }

    /// <summary>
    /// UTF-16 with a byte order mark, or any 8-bit text (UTF-8, Windows-1252...):
    /// no NUL bytes and almost no control characters besides tab, newlines,
    /// form feed and the escape used by colored console logs.
    /// </summary>
    internal static bool LooksLikeText(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 2 && ((b[0] == 0xFF && b[1] == 0xFE) || (b[0] == 0xFE && b[1] == 0xFF)))
        {
            return true;
        }

        var control = 0;
        foreach (var c in b)
        {
            if (c == 0)
            {
                return false;
            }

            if (c < 0x20 && c is not (0x09 or 0x0A or 0x0D or 0x0C or 0x1B))
            {
                control++;
            }
        }

        return control <= b.Length / 100;
    }

    /// <summary>The charset to serve a text file with.</summary>
    public static string TextCharset(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE)
        {
            return "utf-16le";
        }

        if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF)
        {
            return "utf-16be";
        }

        return Utf8.IsValid(b) ? "utf-8" : "windows-1252";
    }

    /// <summary>A display-safe name: no path or control characters; falls back to a generic name.</summary>
    internal static string CleanFileName(string? fileName, string contentType)
    {
        var name = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/'));
        var cleaned = new string(name.Where(c => !char.IsControl(c) && c is not ('<' or '>' or '"')).ToArray());
        cleaned = TextSanitizer.SingleLine(cleaned, WorkItemDefaults.FileNameMaxLength) ?? string.Empty;
        if (cleaned.Length > 0)
        {
            return cleaned;
        }

        return contentType switch
        {
            Zip => "file.zip",
            Gzip => "file.gz",
            SevenZip => "file.7z",
            _ => "file.log",
        };
    }
}
