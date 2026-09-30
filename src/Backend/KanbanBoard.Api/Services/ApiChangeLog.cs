using KanbanBoard.Api.Models;

namespace KanbanBoard.Api.Services;

/// <summary>
/// Plain-language history of changes to the AI agent API (<c>/api/v1</c>),
/// served by <c>GET /api/v1/meta</c> so agents learn WHAT changed, not just
/// that something did. Only releases that changed the API are listed.
/// </summary>
/// <remarks>
/// Every API change needs a new entry here, newest first, whose
/// <see cref="ApiChangeDto.SchemaVersion"/> is the fingerprint the release
/// produces. A test compares it with the live fingerprint, so an API change
/// without an entry fails the build.
/// </remarks>
public static class ApiChangeLog
{
    public static readonly IReadOnlyList<ApiChangeDto> Entries = new[]
    {
        new ApiChangeDto
        {
            Version = "1.7.0",
            Date = "2026-09-30",
            SchemaVersion = "9dbe70ea4782",
            Breaking = false,
            Summary = new[]
            {
                "Work items have an optional 'programVersion' (text, max 64 characters): the version of the program a bug was found in, e.g. \"2.4.1\". Set it when creating or updating Bugs; send \"\" to clear it. Changes are recorded in history.",
                "The board shows and edits programVersion only for Bugs. It is still stored if the type later changes, so nothing is lost.",
            },
        },
        new ApiChangeDto
        {
            Version = "1.6.0",
            Date = "2026-09-30",
            SchemaVersion = "c008d4d6f5f9",
            Breaking = false,
            Summary = new[]
            {
                "The board no longer shows or edits 'assignedTo'. It is still accepted, stored and returned (and the assignedTo filter still works), but you do not need to set it: the AI badge and each card's history already record which agent made a change (X-Agent-Identity).",
                "Documentation only: 'areaPath' has not been shown on the board since 1.4.0; use 'program' instead.",
            },
        },
        new ApiChangeDto
        {
            Version = "1.5.0",
            Date = "2026-09-29",
            SchemaVersion = "f79557855e81",
            Breaking = false,
            Summary = new[]
            {
                "Every /api/v1 response now carries X-API-Schema-Version (a fingerprint of this API's OpenAPI document) and Link: </api/openapi.json>; rel=\"service-desc\". When the value differs from the one you remembered, re-read /api/openapi.json.",
                "New GET /api/v1/meta (getApiMeta): current version, schemaVersion and this change list; use ?since=<version> to see only newer changes.",
                "/api/openapi.json supports ETag / If-None-Match (304 when unchanged). /api/version also reports apiSchemaVersion.",
                "HTTP 503 with an HTML body means the server is being updated: wait a few seconds and retry.",
            },
        },
        new ApiChangeDto
        {
            Version = "1.4.0",
            Date = "2026-09-29",
            Breaking = false,
            Summary = new[]
            {
                "Work items have 'program' (a name from GET /api/v1/programs, or null) and 'tags' (list of strings). On PATCH, program \"\" removes it and tags replaces the whole list.",
                "New list/board filters: program and tag.",
                "New GET/POST /api/v1/programs (listPrograms, createProgram).",
                "New POST /api/v1/attachments (multipart part 'file': PNG, JPEG, GIF or WebP up to 5 MB) returning ready-to-paste image markdown, and GET /api/v1/attachments/{id}.",
            },
        },
        new ApiChangeDto
        {
            Version = "1.0.0",
            Date = "2026-09-29",
            Breaking = false,
            Summary = new[]
            {
                "Initial API: list, get, create, update and comment on work items, and read the board. Every call needs X-API-Key and X-Agent-Identity.",
            },
        },
    };

    /// <summary>Entries newer than <paramref name="since"/> (all when null), newest first.</summary>
    public static IReadOnlyList<ApiChangeDto> Since(string? since) =>
        since is null
            ? Entries
            : Entries.Where(e => AppVersion.Compare(e.Version, since) > 0).ToList();
}
