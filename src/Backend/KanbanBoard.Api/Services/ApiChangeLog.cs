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
            Version = "1.13.0",
            Date = "2026-10-02",
            SchemaVersion = "664b159c634f",
            Breaking = false,
            Summary = new[]
            {
                "Humans can now edit the comments they wrote. History entries have a new 'editedAt' (UTC, null when never edited); the entry keeps its id, author and changeDate, and 'comment' holds the new text.",
                "An edit counts as a new human comment: the card's 'lastHumanCommentAt' and 'updatedAt' move to the edit time, so the card shows discussionStatus 'AwaitingAgent' again and appears in updatedSince polls. Re-read the edited entry (newest editedAt) and reply as usual.",
                "Cards filed from the programs under test ('Report an issue') are tagged 'in-app-report' and authored \"Name (in-app report)\"; they are human reports - triage them like any other New bug.",
            },
        },
        new ApiChangeDto
        {
            Version = "1.9.0",
            Date = "2026-09-30",
            SchemaVersion = "968fcbd24974",
            Breaking = false,
            Summary = new[]
            {
                "IMPORTANT - answering humans: humans comment on cards to talk to you, often without moving them. Every work item now has 'discussionStatus': 'AwaitingAgent' means a human commented after the last agent comment and is waiting for your reply.",
                "At the start of every session and before picking up new work, call GET /api/v1/workitems?discussion=AwaitingAgent (GET /api/v1/meta also reports 'awaitingAgentCount'). For each card: GET it, read history entries with isAiAction false and a comment, act on them, then reply with POST /api/v1/workitems/{id}/comments (or a PATCH with 'comment'). Your comment clears AwaitingAgent; moving the card without a comment does not.",
                "Also new on work items: 'lastAgentCommentAt', 'lastAgentCommentBy' and 'commentCount'. 'UnreadReply' (an agent replied and no human has opened the card yet) is for the humans; you can ignore it.",
            },
        },
        new ApiChangeDto
        {
            Version = "1.8.0",
            Date = "2026-09-30",
            SchemaVersion = "fee4c6bc381c",
            Breaking = false,
            Summary = new[]
            {
                "New 'updatedSince' filter on GET /api/v1/workitems and GET /api/v1/board: only items changed (fields, state or comments) strictly after that instant. Pass the newest 'updatedAt' you have seen, including its Z.",
                "Work items now report 'lastHumanCommentAt' and 'lastHumanCommentBy': when and by whom a human (not an AI agent) last commented. They appear in list and board results too, so you can spot new human comments without opening every card. When lastHumanCommentAt is newer than the last human comment you read, GET the item and read its history: entries with isAiAction false and a comment are messages for you.",
                "Existing comments were counted when upgrading, so these fields are filled in for older cards as well.",
            },
        },
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
