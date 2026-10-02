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
    /// </remarks>
    /// <param name="request">The report.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("issues", Name = "reportIssue")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(ReportReceiptDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ReportReceiptDto>> Report([FromBody] ReportIssueRequest request, CancellationToken ct)
        => StatusCode(StatusCodes.Status201Created, await _reports.ReportAsync(request, ct));

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
