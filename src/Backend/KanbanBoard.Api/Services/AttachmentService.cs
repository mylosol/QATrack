using System.Security.Cryptography;
using KanbanBoard.Api.Data;
using KanbanBoard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace KanbanBoard.Api.Services;

/// <summary>
/// Stores images pasted or inserted into descriptions and comments (1.4.0).
/// </summary>
/// <remarks>
/// Only raster images are accepted, identified by their magic bytes - the
/// client's file name and Content-Type are never trusted. SVG is refused on
/// purpose: it can carry script. Images live in kanban.db so they share its
/// backups and are never touched by a redeploy.
/// </remarks>
public sealed class AttachmentService
{
    /// <summary>Relative URL prefix the board uses in markdown image links.</summary>
    public const string UiUrlPrefix = "api/ui/attachments/";

    private readonly KanbanDbContext _db;
    private readonly IActorContext _actor;
    private readonly TimeProvider _clock;

    public AttachmentService(KanbanDbContext db, IActorContext actor, TimeProvider clock)
    {
        _db = db;
        _actor = actor;
        _clock = clock;
    }

    /// <summary>
    /// Validates and stores an image. Identical bytes are stored once; a
    /// duplicate upload reuses the stored bytes but keeps its own file name
    /// (the alt text in the returned markdown).
    /// </summary>
    /// <exception cref="WorkItemValidationException">Empty, too large or not a supported image.</exception>
    public async Task<AttachmentDto> UploadAsync(Stream content, string? fileName, CancellationToken ct = default)
    {
        var bytes = await ReadLimitedAsync(content, WorkItemDefaults.AttachmentMaxBytes, "Images", ct);
        var contentType = DetectImageType(bytes)
                          ?? throw new WorkItemValidationException("file", "Only PNG, JPEG, GIF or WebP images can be uploaded.");

        var (id, storedType) = await StoreAsync(bytes, CleanFileName(fileName, contentType), contentType, ct);
        return ToDto(id, CleanFileName(fileName, storedType), storedType, bytes.Length);
    }

    /// <summary>
    /// Stores bytes once per content (SHA-256 + length) and returns the id and the
    /// content type they are stored under. Shared by images and attached files.
    /// </summary>
    internal async Task<(Guid Id, string ContentType)> StoreAsync(byte[] bytes, string fileName, string contentType, CancellationToken ct)
    {
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var existing = await _db.Attachments.AsNoTracking()
            .Where(a => a.Sha256 == sha && a.Length == bytes.Length)
            .Select(a => new { a.Id, a.ContentType })
            .FirstOrDefaultAsync(ct);
        if (existing is not null)
        {
            return (existing.Id, existing.ContentType);
        }

        var attachment = new Attachment
        {
            Id = Guid.NewGuid(),
            FileName = fileName,
            ContentType = contentType,
            Length = bytes.Length,
            Content = bytes,
            Sha256 = sha,
            UploadedBy = _actor.DisplayName,
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        };
        _db.Attachments.Add(attachment);
        await _db.SaveChangesAsync(ct);
        return (attachment.Id, contentType);
    }

    /// <summary>Returns the stored image, or null when the id is unknown.</summary>
    public Task<Attachment?> FindAsync(Guid id, CancellationToken ct = default) =>
        _db.Attachments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);

    /// <summary>Identifies PNG, JPEG, GIF and WebP by their signatures; anything else is null.</summary>
    public static string? DetectImageType(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 8 && b[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return "image/png";
        }

        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (b.Length >= 6 && (b[..6].SequenceEqual("GIF87a"u8) || b[..6].SequenceEqual("GIF89a"u8)))
        {
            return "image/gif";
        }

        if (b.Length >= 12 && b[..4].SequenceEqual("RIFF"u8) && b[8..12].SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        return null;
    }

    /// <summary>Keeps a display-safe file name: no path, no control or markdown-breaking characters.</summary>
    internal static string CleanFileName(string? fileName, string contentType)
    {
        var name = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/'));
        var cleaned = new string(name.Where(c => !char.IsControl(c) && c is not ('[' or ']' or '(' or ')' or '<' or '>')).ToArray());
        cleaned = TextSanitizer.SingleLine(cleaned, 100) ?? string.Empty;
        if (cleaned.Length > 0)
        {
            return cleaned;
        }

        return "image." + contentType["image/".Length..].Replace("jpeg", "jpg");
    }

    private static AttachmentDto ToDto(Guid id, string fileName, string contentType, long length)
    {
        var url = UiUrlPrefix + id.ToString("D");
        return new AttachmentDto
        {
            Id = id,
            FileName = fileName,
            ContentType = contentType,
            Length = length,
            Url = url,
            Markdown = $"![{fileName}]({url})",
        };
    }

    internal static async Task<byte[]> ReadLimitedAsync(Stream content, int maxBytes, string what, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                throw new WorkItemValidationException("file",
                    $"{what} must be {maxBytes / (1024 * 1024)} MB or smaller.");
            }

            buffer.Write(chunk, 0, read);
        }

        if (buffer.Length == 0)
        {
            throw new WorkItemValidationException("file", "The uploaded file is empty.");
        }

        return buffer.ToArray();
    }
}
