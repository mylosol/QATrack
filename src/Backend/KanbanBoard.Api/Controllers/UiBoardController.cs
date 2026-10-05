using KanbanBoard.Api.Models;
using KanbanBoard.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace KanbanBoard.Api.Controllers;

/// <summary>
/// Unauthenticated endpoints used by the browser SPA (spec 5.1 "Zero-authentication access").
/// </summary>
/// <remarks>
/// These mirror the AI API but are recorded as human actions. They are hidden
/// from the OpenAPI document (which targets AI tool calling) and protected
/// against cross-site request forgery by <c>UiRequestGuardMiddleware</c>,
/// which requires a custom header on every mutating request.
/// </remarks>
[ApiController]
[Route("api/ui")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class UiBoardController : ControllerBase
{
    private readonly WorkItemService _items;
    private readonly BoardService _board;
    private readonly ProgramService _programs;
    private readonly AttachmentService _attachments;
    private readonly FileService _files;

    public UiBoardController(WorkItemService items, BoardService board, ProgramService programs, AttachmentService attachments, FileService files)
    {
        _items = items;
        _board = board;
        _programs = programs;
        _attachments = attachments;
        _files = files;
    }

    /// <summary>Program dropdown options.</summary>
    [HttpGet("programs")]
    public async Task<ActionResult<IReadOnlyList<ProgramDto>>> ListPrograms(CancellationToken ct)
        => Ok(await _programs.ListAsync(ct));

    /// <summary>The dropdown's "+" button. Idempotent for an existing name.</summary>
    [HttpPost("programs")]
    public async Task<ActionResult<ProgramDto>> CreateProgram([FromBody] CreateProgramRequest request, CancellationToken ct)
    {
        var (program, created) = await _programs.CreateAsync(request, ct);
        return created ? StatusCode(StatusCodes.Status201Created, program) : Ok(program);
    }

    /// <summary>Image inserted, pasted or dropped into the rich text editor.</summary>
    [HttpPost("attachments")]
    public Task<ActionResult<AttachmentDto>> UploadAttachment(IFormFile file, CancellationToken ct)
        => AttachmentEndpoints.UploadAsync(this, _attachments, file, "uiGetAttachment", ct);

    /// <summary>Image referenced from markdown as <c>api/ui/attachments/{id}</c>.</summary>
    [HttpGet("attachments/{id:guid}", Name = "uiGetAttachment")]
    public Task<IActionResult> GetAttachment(Guid id, CancellationToken ct)
        => AttachmentEndpoints.GetAsync(this, _attachments, id, ct);

    /// <summary>Board with columns, WIP status and filtered cards.</summary>
    [HttpGet("board")]
    public async Task<ActionResult<BoardDto>> GetBoard([FromQuery] WorkItemQuery filter, CancellationToken ct)
        => Ok(await _board.GetBoardAsync(filter, ct));

    /// <summary>Single work item with history (card modal).</summary>
    [HttpGet("workitems/{id:int}")]
    public async Task<ActionResult<WorkItemDto>> Get(int id, CancellationToken ct)
        => Ok(await _items.GetAsync(id, ct));

    /// <summary>Create a work item from the "New work item" dialog.</summary>
    [HttpPost("workitems")]
    public async Task<ActionResult<WorkItemDto>> Create([FromBody] CreateWorkItemRequest request, CancellationToken ct)
    {
        var created = await _items.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    /// <summary>Edit fields or move a card (drag-and-drop / keyboard move).</summary>
    [HttpPatch("workitems/{id:int}")]
    public async Task<ActionResult<WorkItemDto>> Update(int id, [FromBody] UpdateWorkItemRequest request, CancellationToken ct)
        => Ok(await _items.UpdateAsync(id, request, ct));

    /// <summary>The card dialog was opened: clears an unread agent reply (1.9.0).</summary>
    [HttpPost("workitems/{id:int}/read")]
    public async Task<IActionResult> MarkRead(int id, CancellationToken ct)
    {
        await _items.MarkReadAsync(id, ct);
        return NoContent();
    }

    /// <summary>Add a discussion comment.</summary>
    [HttpPost("workitems/{id:int}/comments")]
    public async Task<ActionResult<WorkItemHistoryDto>> AddComment(int id, [FromBody] AddCommentRequest request, CancellationToken ct)
    {
        var entry = await _items.AddCommentAsync(id, request, ct);
        return CreatedAtAction(nameof(Get), new { id }, entry);
    }

    /// <summary>Attach a log or other file to a card (1.14.0).</summary>
    [HttpPost("workitems/{id:int}/files")]
    [RequestSizeLimit(FileEndpoints.RequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = FileEndpoints.RequestLimit)]
    public Task<ActionResult<WorkItemFileDto>> AttachFile(int id, IFormFile file, CancellationToken ct)
        => FileEndpoints.AttachAsync(this, _files, id, file, "uiGetFile", ct);

    /// <summary>View (text, inline) or download (?download=true) an attached file.</summary>
    [HttpGet("workitems/{id:int}/files/{fileId:int}", Name = "uiGetFile")]
    public Task<IActionResult> GetFile(int id, int fileId, [FromQuery] bool download, CancellationToken ct)
        => FileEndpoints.DownloadAsync(this, _files, id, fileId, download, ct);

    /// <summary>Remove a file from a card (kept for the audit trail).</summary>
    [HttpDelete("workitems/{id:int}/files/{fileId:int}")]
    public async Task<IActionResult> RemoveFile(int id, int fileId, CancellationToken ct)
    {
        await _files.RemoveAsync(id, fileId, ct);
        return NoContent();
    }

    /// <summary>Edit your own comment (1.13.0). The previous text is kept for the audit trail.</summary>
    [HttpPut("workitems/{id:int}/comments/{commentId:int}")]
    public async Task<ActionResult<WorkItemHistoryDto>> EditComment(int id, int commentId, [FromBody] EditCommentRequest request, CancellationToken ct)
        => Ok(await _items.EditCommentAsync(id, commentId, request, ct));
}
