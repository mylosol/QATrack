using KanbanBoard.Api.Models;

namespace KanbanBoard.Api.Services;

/// <summary>
/// Plain-language history of changes to the in-app reporting API
/// (<c>/api/report</c>), served by <c>GET /api/report/meta</c> so the programs
/// under test learn WHAT changed, not just that something did (1.18.0).
/// </summary>
/// <remarks>
/// Every change to the reporting API needs a new entry here, newest first, whose
/// <see cref="ApiChangeDto.SchemaVersion"/> is the fingerprint of
/// <c>/api/openapi-report.json</c> the release produces. A test compares it with
/// the live fingerprint, so a reporting API change without an entry fails the build.
/// Fingerprints of releases before 1.18.0 were never published, so they are null.
/// </remarks>
public static class ReportApiChangeLog
{
    public static readonly IReadOnlyList<ApiChangeDto> Entries = new[]
    {
        new ApiChangeDto
        {
            Version = "1.18.0",
            Date = "2026-10-15",
            SchemaVersion = "73d2801e810b",
            Breaking = false,
            Summary = new[]
            {
                "Every /api/report response now carries an X-API-Schema-Version header (a fingerprint of /api/openapi-report.json) and a Link header pointing at that document. Remember the value; when a response carries a different one, the reporting API has changed: read GET /api/report/meta?since=<the board version you built against> to see what's new, and re-read /api/openapi-report.json.",
                "GET /api/report/ping also returns 'schemaVersion', so a 'Check connection' button can tell you about changes too.",
            },
        },
        new ApiChangeDto
        {
            Version = "1.14.0",
            Date = "2026-10-05",
            Breaking = false,
            Summary = new[]
            {
                "Reports can include log files: upload each with POST /api/report/files (multipart, part 'file'; text in any encoding or a .zip/.gz/.7z archive, max 20 MB) and list the returned ids in the report's 'files' (max 5). The receipt lists the attached file names. Screenshots to show inline still go through POST /api/report/attachments.",
            },
        },
        new ApiChangeDto
        {
            Version = "1.13.0",
            Date = "2026-10-02",
            Breaking = false,
            Summary = new[]
            {
                "Send an Idempotency-Key header (a new UUID per report, the same one on every retry): a resend answers 200 with the card filed the first time ('replayed': true) instead of filing a duplicate.",
                "The receipt includes 'url', a link that opens the new card on the board.",
                "GET /api/report/ping checks the key and connection without filing anything (?program= also checks a program name).",
            },
        },
        new ApiChangeDto
        {
            Version = "1.12.0",
            Date = "2026-10-02",
            Breaking = false,
            Summary = new[]
            {
                "In-app issue reporting: POST /api/report/issues files a Bug or Feature with the X-Reporter-Key header; POST /api/report/attachments uploads screenshots. Reports are recorded as human reports, tagged in-app-report.",
            },
        },
    };

    /// <summary>Entries newer than <paramref name="since"/> (all when null), newest first.</summary>
    public static IReadOnlyList<ApiChangeDto> Since(string? since) =>
        since is null
            ? Entries
            : Entries.Where(e => AppVersion.Compare(e.Version, since) > 0).ToList();
}
