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
