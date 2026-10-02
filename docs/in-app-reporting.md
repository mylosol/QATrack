# QATrack in-app issue reporting

How a program under test adds a **"Report an issue"** feature that files straight onto the QATrack board.

Reports are recorded as **human** reports, never as AI. They land in **New**, are tagged `in-app-report`, and are authored by the name the person typed (e.g. "Jane Doe (in-app report)").

## Key and limits

- **Header:** send the reporter key as `X-Reporter-Key: <key>`.
  - This is **not** the AI agent key (`X-API-Key`).
  - The server administrator gets the reporter key from the QATrack install output, or from `appsettings.Production.json` → `IssueReporting:ApiKey`.
- **What the key can and can't do:** it ships inside your program, so treat it as semi-public.
  - It can only **file reports** and **upload screenshots**.
  - It cannot read, change or comment on the board.
- **Rate limit:** 20 requests per minute per IP address (reports and uploads together). Over the limit, the server returns HTTP 429 with `Retry-After: 60`.
- **Machine-readable spec:** `GET /api/openapi-report.json` (public). The same spec is in the API docs at `/api/docs`; pick "QATrack in-app issue reporting" in the top-right selector.

## File a report

```http
POST /api/report/issues
X-Reporter-Key: <key>
Content-Type: application/json

{
  "title": "Export button does nothing",
  "description": "Clicked **Export** on the results page; nothing happened.",
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
| `description` | no | Markdown. |
| `type` | no | `Bug` (default) or `Feature` (for suggestions). |
| `severity` | no | `"1 - Critical"`, `"2 - High"`, `"3 - Medium"` (default), `"4 - Low"`; `"High"` or `"2"` also work. |
| `program` | no | Must match a program on the board (case-insensitive), e.g. `ProveOut`, `CallOut`. An unknown name is rejected with HTTP 400. |
| `programVersion` | no | The program's own version, max 64 characters. |
| `reporter` | no | Name or email the person typed. Shown as the author. When omitted, the author is "In-app report". |
| `environment` | no | OS, build, settings, recent log lines. Appended to the description as a code block. Max 20,000 characters. |
| `tags` | no | Up to 10 extra tags. `in-app-report` is always added. |

**Response:** HTTP 201 with `{ "id": 123, "title": ..., "type": ..., "program": ..., "programVersion": ..., "tags": [...], "reportedBy": "Jane Doe (in-app report)", "createdAt": ... }`. Show the person the id, e.g. "Thanks! Reference #123".

## Include a screenshot

1. Upload it:
   ```http
   POST /api/report/attachments
   X-Reporter-Key: <key>
   Content-Type: multipart/form-data
   ```
   - Send the image in a part named `file`.
   - Accepted: PNG, JPEG, GIF or WebP, up to 5 MB.
2. The response contains `markdown`, e.g. `![screen.png](api/ui/attachments/<id>)`. Put that text into the report's `description`.

## Errors

| Status | Meaning |
|---|---|
| 400 | Invalid report. For example: missing title, an unknown program, a type other than Bug or Feature, or too many tags. The body is a JSON problem with the details. |
| 401 | Missing or wrong `X-Reporter-Key`. |
| 429 | Too many reports from this IP. Wait for `Retry-After` seconds. |
| 503 with JSON | Reporting is not configured on the server. |
| 503 with HTML | The server is being updated; retry shortly. |

**Recommended client behaviour:** keep the report locally on 429 or 503 and retry later, so a person's report is never lost.
