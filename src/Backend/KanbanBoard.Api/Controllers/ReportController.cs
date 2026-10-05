using KanbanBoard.Api.Middleware;
using KanbanBoard.Api.Models;
using KanbanBoard.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace KanbanBoard.Api.Controllers;

/// <summary>
/// "Report an issue" from inside the programs under test (1.12.0). Authenticated
/// with <c>X-Reporter-Key</c>; reports are recorded as human, never as AI.
/// </summary>
/// <remarks>
/// The reporter key can only file reports and upload screenshots: nothing
/// here reads or changes existing board data.
/// </remarks>
[ApiController]
[Route("api/report")]
[Tags("In-app issue reports")]
[EnableRateLimiting(IssueReportingOptions.RateLimitPolicy)]
public sealed class ReportController : ControllerBase
{
    private readonly IssueReportService _reports;
    private readonly AttachmentService _attachments;

    public ReportController(IssueReportService reports, AttachmentService attachments)
    {
        _reports = reports;
        _attachments = attachments;
    }

    /// <summary>File an issue (or a suggestion) on the board.</summary>
    /// <remarks>
    /// The card lands in New, tagged <c>in-app-report</c>, authored by <c>reporter</c> (or
    /// "In-app report"). To include a screenshot, upload it first with
    /// <c>POST /api/report/attachments</c> and put the returned <c>markdown</c> in the description.
    ///
    /// Send an <c>Idempotency-Key</c> header (a new UUID per report, reused when retrying the
    /// same report) so a resend after a timeout never files a second card: the first send
    /// answers 201 Created, any resend with the same key answers 200 OK with the same card and
    /// <c>replayed: true</c> (whatever the resend's body says).
    /// </remarks>
    /// <param name="request">The report.</param>
    /// <param name="idempotencyKey">Unique id of this report (e.g. a UUID), 1-128 visible ASCII characters. Optional but recommended.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("issues", Name = "reportIssue")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ReportReceiptDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ReportReceiptDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ReportReceiptDto>> Report(
        [FromBody] ReportIssueRequest request,
        [FromHeader(Name = IssueReportService.IdempotencyKeyHeader)] string? idempotencyKey,
        CancellationToken ct)
    {
        var receipt = await _reports.ReportAsync(request, idempotencyKey, CardUrl, ct);
        if (receipt.Replayed)
        {
            Response.Headers["Idempotent-Replayed"] = "true";
            return Ok(receipt);
        }

        return StatusCode(StatusCodes.Status201Created, receipt);
    }

    /// <summary>Upload a log file for a report (text up to 20 MB, or a .zip/.gz/.7z archive).</summary>
    /// <remarks>
    /// Send <c>multipart/form-data</c> with the file in a part named <c>file</c>, then list the
    /// returned <c>id</c> in the report's <c>files</c> (max 5 per report). The type is detected from
    /// the content: text in any encoding, or a zip/gzip/7z archive. Screenshots go through
    /// <c>POST /api/report/attachments</c> instead.
    /// </remarks>
    /// <param name="file">The log file or archive.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("files", Name = "reportFile")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(FileEndpoints.RequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = FileEndpoints.RequestLimit)]
    [ProducesResponseType(typeof(ReportFileDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ReportFileDto>> UploadFile(IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        return StatusCode(StatusCodes.Status201Created, await _reports.UploadFileAsync(stream, file.FileName, ct));
    }

    /// <summary>Check the board is reachable and the reporter key works. Files nothing.</summary>
    /// <remarks>
    /// For a "Check connection" button. 200 means reports will be accepted; 401 means a wrong
    /// key; 503 means reporting is switched off on the server. Pass <c>program</c> to also check
    /// that the board knows the program name the app reports under. Counts toward the per-minute
    /// limit like any other request.
    /// </remarks>
    /// <param name="program">Optional program name to check, e.g. "ProveOut".</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("ping", Name = "reportPing")]
    [ProducesResponseType(typeof(ReportPingDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ReportPingDto>> Ping([FromQuery] string? program, CancellationToken ct)
        => Ok(await _reports.PingAsync(program, ct));

    /// <summary>Board link that opens the card, as the people who reported it would use it.</summary>
    private string CardUrl(int id) => $"{Request.Scheme}://{Request.Host}{Request.PathBase}/?item={id}";

    /// <summary>Upload a screenshot (PNG, JPEG, GIF or WebP, max 5 MB) for a report.</summary>
    /// <remarks>
    /// Send <c>multipart/form-data</c> with the image in a part named <c>file</c>, then put the
    /// returned <c>markdown</c> into the report's description.
    /// </remarks>
    /// <param name="file">The image.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("attachments", Name = "reportAttachment")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(AttachmentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public Task<ActionResult<AttachmentDto>> Upload(IFormFile file, CancellationToken ct)
        => AttachmentEndpoints.UploadAsync(this, _attachments, file, "uiGetAttachment", ct);
}
