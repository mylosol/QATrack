# QATrack in-app issue reporting

How a program under test adds a **"Report an issue"** feature that files straight onto the QATrack board.

Reports are recorded as **human** reports, never as AI. They land in **New**, are tagged `in-app-report`, and are authored by the name the person typed (e.g. "Jane Doe (in-app report)").

## Quick reference

| What | How |
|---|---|
| Key header | `X-Reporter-Key: <key>` (not the AI agent key `X-API-Key`) |
| File a report | `POST /api/report/issues` (JSON) |
| Upload a screenshot | `POST /api/report/attachments` (multipart, part named `file`) |
| Upload a log file | `POST /api/report/files` (multipart, part named `file`; 1.14.0) |
| No duplicates on resend | `Idempotency-Key: <uuid>` header on `POST /api/report/issues` (1.13.0) |
| Check connection | `GET /api/report/ping` (files nothing, 1.13.0) |
| Machine-readable spec | `GET /api/openapi-report.json`, also in `/api/docs` ("QATrack in-app issue reporting") |

All paths are relative to the board, e.g. `https://qatrack.xmlbridge.work/api/report/issues`.

## Key and limits

- The server administrator gets the reporter key from the QATrack install output (`X-Reporter-Key: ...`), or from `appsettings.Production.json` → `IssueReporting:ApiKey`.
- The key ships inside your program, so treat it as semi-public. It can only **file reports**, **upload screenshots** and **ping**. It cannot read, change or comment on the board.
- **Rate limit:** 20 requests per minute per IP address. Reports, uploads and pings all count. A report with two screenshots and a log file is four requests.

## File a report

```http
POST /api/report/issues
X-Reporter-Key: <key>
Idempotency-Key: 6f1c2a0e-5b7d-4c1e-9a43-2d8e7f0b9c11
Content-Type: application/json

{
  "title": "Export button does nothing",
  "description": "Clicked **Export** on the results page; nothing happened.\n\n![screen.png](api/ui/attachments/3f2a...)",
  "type": "Bug",
  "severity": "High",
  "program": "ProveOut",
  "programVersion": "2.4.1",
  "reporter": "Jane Doe",
  "environment": "OS: Windows 11 23H2\nBuild: 2.4.1+5f2c9a1\nLast log lines: ...",
  "tags": ["export"]
}
```

| Field | Required | Notes |
|---|---|---|
| `title` | yes | Max 255 characters. |
| `description` | no | Markdown. Put screenshot `markdown` here (see below). |
| `type` | no | `Bug` (default) or `Feature` (for suggestions). Other types are rejected. |
| `severity` | no | `"1 - Critical"`, `"2 - High"`, `"3 - Medium"` (default), `"4 - Low"`; `"High"` or `"2"` also work. Leave it out to let triage decide (it shows as Medium). |
| `program` | no | Must match a program on the board (case-insensitive), e.g. `ProveOut`, `CallOut`. An unknown name is rejected with HTTP 400. `GET /api/report/ping?program=...` checks it. |
| `programVersion` | no | The program's own version, max 64 characters. |
| `reporter` | no | Name or email the person typed, max 64 characters. Shown as the author "Jane Doe (in-app report)". When omitted, the author is "In-app report". |
| `environment` | no | OS, build, settings, recent log lines. Appended to the description as a code block. Max 20,000 characters. |
| `tags` | no | Up to 10 extra tags. `in-app-report` is always added. |
| `files` | no | Up to 5 log files: the `id`s returned by `POST /api/report/files`. They appear in the card's Files list. An unknown id is rejected with HTTP 400 and nothing is filed. |

**Priority** is not accepted: the board sets it during triage (default 2 - High).

### Response

HTTP **201 Created**:

```json
{
  "id": 31,
  "url": "https://qatrack.xmlbridge.work/?item=31",
  "replayed": false,
  "title": "Export button does nothing",
  "type": "Bug",
  "program": "ProveOut",
  "programVersion": "2.4.1",
  "tags": ["export", "in-app-report"],
  "files": ["app.log"],
  "reportedBy": "Jane Doe (in-app report)",
  "createdAt": "2026-10-02T15:04:05.1234567Z"
}
```

- `id` is the card number: show "Sent to the QA board as #31".
- `url` opens that card on the board (after signing in to the board, if it has a password). The reporter key can't read the board, so this receipt is the only way to know the card.

### No duplicates: `Idempotency-Key`

Send an `Idempotency-Key` header with every report:

- Generate a **new UUID per report** when the person first presses Send, and keep it with the report.
- Send the **same key on every retry** of that report (timeout, network error, a second click on Send, a resend after restarting the app).
- The first send answers **201 Created**. Any later send with the same key answers **200 OK** with the **same card** (`replayed: true`, header `Idempotent-Replayed: true`) and files nothing new. The body of the resend is ignored: the card is the one filed the first time.
- Simultaneous sends with the same key also file only one card.
- The key is 1-128 visible ASCII characters (a UUID is ideal). Anything else is HTTP 400.
- Without the header every send files a new card.
- Re-uploading the same screenshot on a retry is harmless: identical images are stored once.

## Include a screenshot

1. Upload it:
   ```http
   POST /api/report/attachments
   X-Reporter-Key: <key>
   Content-Type: multipart/form-data
   ```
   - Send the image in a part named `file`.
   - Accepted: PNG, JPEG, GIF or WebP, up to 5 MB.
2. The response is HTTP 201, the same shape the board's own editor gets:
   ```json
   {
     "id": "3f2a9c1e-...",
     "fileName": "screen.png",
     "contentType": "image/png",
     "length": 48213,
     "url": "api/ui/attachments/3f2a9c1e-...",
     "markdown": "![screen.png](api/ui/attachments/3f2a9c1e-...)"
   }
   ```
3. Put the `markdown` text into the report's `description`.

## Include log files

1. Upload each file:
   ```http
   POST /api/report/files
   X-Reporter-Key: <key>
   Content-Type: multipart/form-data
   ```
   - Send the file in a part named `file`.
   - Accepted: log files and any other text (UTF-8, UTF-16, Windows code pages...), or `.zip`, `.gz` or `.7z` archives, up to **20 MB**. The type is checked from the content, so the name doesn't matter, but programs and images are refused (send screenshots through `/api/report/attachments`).
   - Zip large or many logs into one archive.
2. The response is HTTP 201:
   ```json
   { "id": "9b1f0c4e-...", "fileName": "app.log", "contentType": "text/plain", "length": 48213 }
   ```
3. Put the ids in the report: `"files": ["9b1f0c4e-..."]` (up to 5).

On a retry with the same `Idempotency-Key`, re-uploading the same file is harmless: identical content is stored once, and the resend returns the card filed the first time.

## Check connection: ping

For a "Check connection" button. Files nothing.

```http
GET /api/report/ping?program=ProveOut
X-Reporter-Key: <key>
```

HTTP 200:

```json
{ "status": "ok", "version": "1.13.0", "requestsPerMinute": 20, "program": "ProveOut", "programKnown": true }
```

- `program` and `programKnown` are `null` when no `program` was sent. `programKnown: false` means reports naming that program would be rejected: ask the board admin to add it.
- 401 means a wrong key. 503 means reporting is off on the server (see below).

## When it says no

Every error body is JSON (`application/problem+json`) with `title` and `detail`, except a 503 during an update.

| Status | Meaning | Suggested message to the person |
|---|---|---|
| 400 | Invalid report: missing title, unknown program, a type other than Bug or Feature, too many tags or files, an unknown file id, bad `Idempotency-Key`. For uploads: an empty file, over 20 MB, or not a log/text file or archive. The body lists the problems per field. | Fix and resend (a programming error, usually). |
| 401 | Missing or wrong `X-Reporter-Key`. | "The QA board rejected this app's key. Please tell the development team." |
| 429 | Too many requests from this IP. Has a `Retry-After` header (seconds, currently 60). | "The QA board is busy. Your report is saved and will be sent in a minute." |
| 503 with JSON | Reporting is switched off on the server (no reporter key configured). | "Reporting is not available right now." |
| 503 with HTML | The server is being updated. | "The QA board is updating. Your report will be sent shortly." |

The 429 body looks like this:

```http
HTTP/1.1 429 Too Many Requests
Retry-After: 60
Content-Type: application/problem+json

{ "title": "Too many reports", "status": 429, "detail": "Wait a minute and try again." }
```

**Recommended client behaviour:** keep the report locally (with its `Idempotency-Key`) on a timeout, 429 or 503, and resend it later with the same key. The person's report is never lost, and never filed twice.
