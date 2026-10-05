using KanbanBoard.Api.Models;
using KanbanBoard.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace KanbanBoard.Api.Controllers;

/// <summary>Program dropdown options for AI agents (1.4.0).</summary>
[ApiController]
[Route("api/v1/programs")]
[Tags("Programs")]
public sealed class ProgramsController : ControllerBase
{
    private readonly ProgramService _programs;

    public ProgramsController(ProgramService programs)
    {
        _programs = programs;
    }

    /// <summary>List the programs a work item can belong to.</summary>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet(Name = "listPrograms")]
    [ProducesResponseType(typeof(IReadOnlyList<ProgramDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ProgramDto>>> List(CancellationToken ct)
        => Ok(await _programs.ListAsync(ct));

    /// <summary>Add a program. Idempotent: an existing name (any case) returns 200 with that program.</summary>
    /// <param name="request">Program name.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost(Name = "createProgram")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ProgramDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProgramDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProgramDto>> Create([FromBody] CreateProgramRequest request, CancellationToken ct)
    {
        var (program, created) = await _programs.CreateAsync(request, ct);
        return created ? StatusCode(StatusCodes.Status201Created, program) : Ok(program);
    }
}

/// <summary>Image uploads for work item descriptions and comments (1.4.0).</summary>
[ApiController]
[Route("api/v1/attachments")]
[Tags("Attachments")]
public sealed class AttachmentsController : ControllerBase
{
    private readonly AttachmentService _attachments;

    public AttachmentsController(AttachmentService attachments)
    {
        _attachments = attachments;
    }

    /// <summary>Upload an image (PNG, JPEG, GIF or WebP, max 5 MB).</summary>
    /// <remarks>
    /// Send <c>multipart/form-data</c> with the image in a part named <c>file</c>. Put the returned
    /// <c>markdown</c> value into a work item description or comment to show the image on the board.
    /// </remarks>
    /// <param name="file">The image.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost(Name = "uploadAttachment")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(AttachmentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public Task<ActionResult<AttachmentDto>> Upload(IFormFile file, CancellationToken ct)
        => AttachmentEndpoints.UploadAsync(this, _attachments, file, "getAttachment", ct);

    /// <summary>Download an uploaded image.</summary>
    /// <param name="id">Attachment id (the GUID in <c>api/ui/attachments/{id}</c> links).</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("{id:guid}", Name = "getAttachment")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<IActionResult> Get(Guid id, CancellationToken ct)
        => AttachmentEndpoints.GetAsync(this, _attachments, id, ct);
}

/// <summary>Log files and other text output attached to work items, for AI agents (1.14.0).</summary>
[ApiController]
[Route("api/v1/workitems/{id:int}/files")]
[Produces("application/json")]
[Tags("Work item files")]
public sealed class WorkItemFilesController : ControllerBase
{
    private readonly FileService _files;

    public WorkItemFilesController(FileService files)
    {
        _files = files;
    }

    /// <summary>List the files attached to a work item (logs, text output, archives).</summary>
    /// <remarks>GET /api/v1/workitems/{id} includes the same list as 'files'; 'fileCount' is on every work item.</remarks>
    /// <param name="id">Work item id.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet(Name = "listWorkItemFiles")]
    [ProducesResponseType(typeof(IReadOnlyList<WorkItemFileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<WorkItemFileDto>>> List(int id, CancellationToken ct)
        => Ok(await _files.ListAsync(id, ct));

    /// <summary>Download an attached file.</summary>
    /// <remarks>
    /// Text files (logs) come back as text/plain with their charset; archives as
    /// application/zip, application/gzip or application/x-7z-compressed.
    /// </remarks>
    /// <param name="id">Work item id.</param>
    /// <param name="fileId">File id from the work item's 'files'.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("{fileId:int}", Name = "getWorkItemFile")]
    [Produces("text/plain", "application/zip", "application/gzip", "application/x-7z-compressed", "application/json")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<IActionResult> Download(int id, int fileId, CancellationToken ct)
        => FileEndpoints.DownloadAsync(this, _files, id, fileId, download: true, ct);

    /// <summary>Attach a file to a work item, e.g. a test log (text up to 20 MB, or a .zip/.gz/.7z archive).</summary>
    /// <remarks>
    /// Send multipart/form-data with the file in a part named 'file'. The type is detected from the
    /// content: text of any encoding, or a zip/gzip/7z archive; anything else is refused with 400.
    /// Recorded in the history as a change to 'Files' and marks the card AI-modified.
    /// </remarks>
    /// <param name="id">Work item id.</param>
    /// <param name="file">The file.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost(Name = "attachWorkItemFile")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(FileEndpoints.RequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = FileEndpoints.RequestLimit)]
    [ProducesResponseType(typeof(WorkItemFileDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<ActionResult<WorkItemFileDto>> Attach(int id, IFormFile file, CancellationToken ct)
        => FileEndpoints.AttachAsync(this, _files, id, file, "getWorkItemFile", ct);
}

/// <summary>Upload/download logic for work item files, shared by the AI and browser controllers.</summary>
internal static class FileEndpoints
{
    /// <summary>Largest request for a file upload: the file plus room for the multipart wrapping.</summary>
    public const int RequestLimit = WorkItemDefaults.FileMaxBytes + 1024 * 1024;

    public static async Task<ActionResult<WorkItemFileDto>> AttachAsync(
        ControllerBase controller, FileService files, int id, IFormFile file, string getRouteName, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        var dto = await files.AttachAsync(id, stream, file.FileName, ct);
        return controller.CreatedAtRoute(getRouteName, new { id, fileId = dto.Id }, dto);
    }

    public static async Task<IActionResult> DownloadAsync(
        ControllerBase controller, FileService files, int id, int fileId, bool download, CancellationToken ct)
    {
        var (file, content) = await files.OpenAsync(id, fileId, ct);
        var isText = file.ContentType == FileService.Text;
        var contentType = isText ? $"{FileService.Text}; charset={FileService.TextCharset(content)}" : file.ContentType;
        var disposition = new Microsoft.Net.Http.Headers.ContentDispositionHeaderValue(download || !isText ? "attachment" : "inline");
        disposition.SetHttpFileName(file.FileName);
        controller.Response.Headers.ContentDisposition = disposition.ToString();
        // A file can be removed, so don't let a cache keep serving it. Text is
        // served as text/plain with nosniff and the strict CSP (SecurityHeadersMiddleware),
        // so a browser shows it and never runs it.
        controller.Response.Headers.CacheControl = "private, no-cache";
        return controller.File(content, contentType);
    }
}

/// <summary>Upload/download logic shared by the AI and browser controllers.</summary>
internal static class AttachmentEndpoints
{
    public static async Task<ActionResult<AttachmentDto>> UploadAsync(
        ControllerBase controller, AttachmentService attachments, IFormFile file, string getRouteName, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        var dto = await attachments.UploadAsync(stream, file.FileName, ct);
        return controller.CreatedAtRoute(getRouteName, new { id = dto.Id }, dto);
    }

    public static async Task<IActionResult> GetAsync(ControllerBase controller, AttachmentService attachments, Guid id, CancellationToken ct)
    {
        var attachment = await attachments.FindAsync(id, ct);
        if (attachment is null)
        {
            return controller.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not Found",
                detail: $"Attachment {id} was not found.");
        }

        // Content never changes for an id; private because the board may be password-protected.
        controller.Response.Headers.CacheControl = "private, max-age=31536000, immutable";
        controller.Response.Headers.ContentDisposition = "inline";
        return controller.File(attachment.Content, attachment.ContentType);
    }
}
