# QATrack – DevOps Kanban Board for QA Tools

Lightweight Azure DevOps Boards clone for QA work. It runs on Linux (Ubuntu + Caddy, deployed by GitHub Actions; see [docs/deploy-digitalocean.md](docs/deploy-digitalocean.md)) or in-process under IIS on Windows Server.
Humans use a web board (optionally behind a shared password); AI agents use a secured REST API, and every change they make is tagged and audited.

[![CI/CD](https://github.com/mylosol/QATrack/actions/workflows/ci-cd.yml/badge.svg)](https://github.com/mylosol/QATrack/actions/workflows/ci-cd.yml)

Live board: https://qatrack.xmlbridge.work · License: [MIT](LICENSE)

Full specification: [docs/QATrack-Spec.md](docs/QATrack-Spec.md)

## Architecture

| Layer | Technology | Location |
|---|---|---|
| API | ASP.NET Core 8 (in-process, `AspNetCoreModuleV2`) | `src/Backend/KanbanBoard.Api` |
| Data | SQLite + EF Core 8 migrations, WAL mode | `App_Data/kanban.db` (created on first start) |
| UI | TypeScript SPA, Vite, Tailwind 3 (built into `wwwroot`) | `src/Frontend` |
| API docs | OpenAPI 3.0 | `/api/openapi.json`, Swagger UI at `/api/docs` |
| Tests | xUnit (unit + integration + deploy script), Vitest, Playwright + axe-core | `src/Backend/KanbanBoard.Tests`, `src/Frontend` |

### Two API surfaces

| Path | Who | Auth | Audit |
|---|---|---|---|
| `/api/v1/*` | AI agents | `X-API-Key` + `X-Agent-Identity` headers (required) | Every POST/PATCH sets `AiModified=true`, `AiAgentIdentity`, and writes an `IsAiAction=true` history row |
| `/api/ui/*` | Browser SPA | None (spec 5.1). Mutations need `X-Requested-With: QATrack` (CSRF defence) | Human history rows; optional "Your name" field is recorded as the author |

Requests to `/api/ui` that carry agent headers are rejected, so an agent can't skip AI tagging by using the browser API.
`AiModified` is sticky: once an agent touches a card it stays flagged, even after later human edits.

## Prerequisites (development)

- .NET SDK 8 or later (the projects target `net8.0`; the .NET 8 runtime is needed to run the tests)
- Node.js 20+ and npm
- Microsoft Edge (for the Playwright E2E suite; or set `PW_CHANNEL=chromium` after `npx playwright install chromium`)

```bash
npm run setup
```

`setup` installs frontend packages, restores .NET tools/packages, and installs the git hooks (`core.hooksPath=.githooks`, `merge.ff=false`).

## Everyday commands

| Command | What it does |
|---|---|
| `npm run dev:api` | API on http://localhost:5080 (Development: sample data, dev API key) |
| `npm run dev:web` | Vite dev server on http://localhost:5173 with `/api` proxied to the API |
| `npm run build` | Frontend into `wwwroot`, then the .NET solution |
| `npm run test:unit` | Vitest + xUnit |
| `npm run test:e2e` | Playwright against the real backend on a throwaway database (build the frontend first) |
| `npm run verify` | **The merge gate:** typecheck → frontend unit → backend tests → production build → E2E |
| `npm run package` | Runs `verify`, then builds `artifacts/QATrack-<version>.zip` |

### Local CI and branching

The repo is local-only (spec section 7). Work goes on `feature/*` branches and is merged into `main` with `git merge --no-ff`.
The versioned `.githooks/pre-merge-commit` hook runs `npm run verify` and aborts any merge into `main` that fails.

## Versioning

QATrack follows [Semantic Versioning 2.0](https://semver.org): `MAJOR.MINOR.PATCH[-prerelease]`.

- **Single source of truth:** the `version` field in the root `package.json`. `Directory.Build.props` (.NET) and `src/Frontend/vite.config.ts` (SPA) both read it, and either build fails if it is not valid SemVer.
- The git commit is added automatically as build metadata, e.g. `1.1.0+abc1234`.
- **Where it shows:**
  - the board header (`v1.1.0`; hover for the full build)
  - `GET /api/version`
  - the OpenAPI `info.version`
  - the zip name (`QATrack-1.1.0.zip`)
  - the installer output ("Upgrading 1.0.0 -> 1.1.0")
- **Release steps:**
  1. Bump the version: `npm run release:patch` (bug fixes), `release:minor` (new backwards-compatible features) or `release:major` (breaking changes, e.g. `/api/v1` contract changes).
  2. Update `CHANGELOG.md`.
  3. Merge into `main`.
  4. Tag `vX.Y.Z` and run `npm run package`.

### Update notifier

After a redeploy, browser tabs that were already open show "A new version of QATrack is available" within 60 seconds, or immediately when the tab regains focus.

- **Reload** loads the new build.
- **✕** dismisses the toast until a newer build ships.
- It never reloads by itself, so nobody loses an in-progress edit.

It compares `GET /api/version` (`build`, served `no-store`) with the build baked into the page. It only runs in production bundles, not in the Vite dev server.

The `/api/v1` URL segment is the API contract version. It changes only with a breaking API change, independently of the app version.

## Board password

The board can be protected with one shared password. It's recommended whenever the site is reachable from outside your network.

```powershell
.\deploy-iis.ps1 -Action SetPassword -PhysicalPath C:\inetpub\QATrack          # prompts, applies live
.\deploy-iis.ps1 -Action SetPassword -PhysicalPath C:\inetpub\QATrack -RemoveSharedPassword
```

- **Signing in:** each browser signs in once and stays signed in for 30 days. Changing the password signs everyone out. You can also pass `-SharedPassword` to Install.
- **Storage and brute-force protection:** the password is stored as a PBKDF2-SHA256 hash in `appsettings.Production.json`. Sign-in is limited to 5 attempts per minute per IP.
- **AI agents are unaffected:** they keep using `X-API-Key`.
- **Without HTTPS,** the password and session cookie cross the network unencrypted. Add an HTTPS binding to the IIS site when a certificate is available.

## Using the AI agent API

```bash
curl -X POST http://server:8080/api/v1/workitems \
  -H "X-API-Key: <key from appsettings.Production.json>" \
  -H "X-Agent-Identity: Claude-Code-Agent-v1" \
  -H "Content-Type: application/json" \
  -d '{"title":"Checkout test fails on Edge","type":"Bug","severity":"2 - High"}'
```

| Operation | Endpoint | operationId |
|---|---|---|
| List (filters: `type`, `state`, `aiModified`, `assignedTo`, `program`, `tag`, `updatedSince`, `top`) | `GET /api/v1/workitems` | `listWorkItems` |
| Details + full audit history | `GET /api/v1/workitems/{id}` | `getWorkItem` |
| Create | `POST /api/v1/workitems` | `createWorkItem` |
| Update / move (partial; `""` clears the description) | `PATCH /api/v1/workitems/{id}` | `updateWorkItem` |
| Comment or test output | `POST /api/v1/workitems/{id}/comments` | `addWorkItemComment` |
| Board, columns, WIP | `GET /api/v1/board` | `getBoard` |
| Program dropdown options / add one | `GET` / `POST /api/v1/programs` | `listPrograms` / `createProgram` |
| Upload an image (multipart, part `file`; returns `markdown`) | `POST /api/v1/attachments` | `uploadAttachment` |
| Download an image | `GET /api/v1/attachments/{id}` | `getAttachment` |
| List a card's files (logs, text, archives) | `GET /api/v1/workitems/{id}/files` | `listWorkItemFiles` |
| Download a file | `GET /api/v1/workitems/{id}/files/{fileId}` | `getWorkItemFile` |
| Attach a file (multipart, part `file`; text up to 20 MB or .zip/.gz/.7z) | `POST /api/v1/workitems/{id}/files` | `attachWorkItemFile` |

Work items carry `program` (one of the programs, or null), `tags` (a list) and, for Bugs, an optional `programVersion` (the version the bug was found in; `""` clears it). On PATCH, `program: ""` removes the program and `tags` replaces the whole list (`[]` removes all); omit either to leave it unchanged. Descriptions and comments are Markdown; to show an image, upload it and put the returned `markdown` (e.g. `![shot.png](api/ui/attachments/<id>)`) into the text.

Point tool-calling agents at `/api/openapi.json`. It declares both headers as security schemes.

### Answering human comments (discussion status)

Each card's `discussionStatus` says who its discussion is waiting for:

| Status | Meaning | Clears when |
|---|---|---|
| `AwaitingAgent` ("Waiting for AI" on the board) | A person commented after the last agent comment | An agent adds a comment (moving the card doesn't count) |
| `UnreadReply` | An agent commented after the last human comment | Any person opens the card (API only; the board shows a per-person **Unread** label instead, see below) |

On the board, **Unread** is tracked per browser (so per person): each browser remembers the newest comment it has read on each card, and every comment has a **Mark unread** button. There are no user accounts, so another browser or cleared site data starts with everything unread (use **Mark all read**).

Agents should call `GET /api/v1/workitems?discussion=AwaitingAgent` at the start of every session and before picking up new work (`GET /api/v1/meta` also reports `awaitingAgentCount`), then read and reply to each card. The API description tells them so.

### Noticing new human comments

Comments live in each item's history, which only `GET /api/v1/workitems/{id}` returns. To spot new ones cheaply, agents:

1. Call `GET /api/v1/workitems?updatedSince=<newest updatedAt seen>` (or the board with the same filter) to get only changed items.
2. Compare each item's `lastHumanCommentAt` with the last human comment they read. If it's newer, fetch the item and read the history entries with `isAiAction: false` and a `comment`.

Fill in **Your name (for history)** on the board so your comments show a name instead of "Web UI User".

### Working in the card dialog (1.13.0)

- **Unsaved work is safe:** a click outside the dialog, Escape, Cancel or ✕ never throws away typed fields or an unsent comment. The dialog asks first ("Keep editing" / "Discard changes"), and reloading the page asks too.
- **Comment and move in one step:** under the comment box, **Then** picks a state, e.g. "Move to Closed". The button becomes "Comment & move to Closed": like **Save changes**, it saves any fields you edited, posts the comment, moves the card and closes the dialog (one history entry).
- **Editing your comments:** comments posted under your current board name have an **Edit** button (AI comments never do). The entry shows "(edited ...)", the earlier text is kept in the database for the audit trail, and the card goes back to "Waiting for AI" so the agent re-reads it. Agents see `editedAt` on the history entry.
- **Links to cards:** the address bar shows `?item=31` while a card is open; opening such a link opens the card.

### Files: logs and other attachments (1.14.0)

Each card has a **Files** section: **Attach files…** or drop files onto it.

- **What can be attached:** log files and any other text (any encoding), and `.zip`, `.gz` or `.7z` archives, up to **20 MB** each. The type is checked from the content, not the name, so programs and images are refused (screenshots belong in the description or a comment).
- On a **new** item, the picked files are attached when you click **Create**.
- **View** opens a text file in a new tab; **Download** saves it. Archives can only be downloaded.
- **Remove** asks first. A removed file disappears from the card, but is kept in the database and the removal is recorded in history.
- Cards with files show 📎 and the count on the board.
- AI agents see `fileCount` and `files` on work items, can download files, and can attach their own output. Attaching or removing is recorded in history and moves `updatedAt`.

### Keeping agents current when the API changes

| Signal | Where | Use |
|---|---|---|
| `X-API-Schema-Version: 968fcbd24974` | every `/api/v1` response, even errors | Fingerprint of the OpenAPI document. Changes only when the API changes. |
| `Link: </api/openapi.json>; rel="service-desc"` | every `/api/v1` response | Where to re-read the API description. |
| `GET /api/v1/meta?since=1.4.0` (`getApiMeta`) | API key required | Version, fingerprint and a plain-language list of API changes since a version. |
| `ETag` on `/api/openapi.json` | public | Re-check with `If-None-Match`; HTTP 304 when unchanged. |

The rule for agents is part of the API description itself: remember the fingerprint, and when a response carries a different one, re-read `/api/openapi.json` and call `/api/v1/meta?since=<last known version>`. Changes inside `/api/v1` are additive only; a breaking change would ship as `/api/v2`, with the old endpoints announcing their end date via `Deprecation`/`Sunset` headers.

If your agent tooling only shows the model response bodies (not headers), add one line to the agent's own instructions: *"At the start of each session call GET /api/v1/meta and re-read /api/openapi.json if schemaVersion changed."*

**For developers:** any change to the `/api/v1` surface, including XML doc comments, changes the fingerprint. The test `ApiChangeLog_NewestEntry_MatchesTheLiveContract` then fails with the new value. Add an entry to `Services/ApiChangeLog.cs` describing the change for agents, with that value.

## In-app issue reporting (programs under test)

Programs under test can offer a **"Report an issue"** feature that files straight onto the board with a separate, limited **reporter key** (`X-Reporter-Key`):

- **How reports appear:** they are recorded as **human** reports (never AI). They land in New, tagged `in-app-report`, authored by the name the person typed. The card shows "📣 Reported by <name>", and agents see it in `createdBy`.
- **What the key can do:** it can only file reports, upload screenshots and ping (`GET /api/report/ping`), and it is rate-limited per IP. It is built into the programs, so assume it can be extracted.
- **Log files:** `POST /api/report/files` uploads a log (up to 20 MB); list the returned ids in the report's `files` (up to 5).
- **No duplicates:** an `Idempotency-Key` header makes a resend return the card filed the first time. The receipt has the card's `id` and a `url` that opens it on the board.
- **Getting the key:** Install generates it and prints it once. It is stored in `appsettings.Production.json` under `IssueReporting:ApiKey`.
- **Integration guide:** [docs/in-app-reporting.md](docs/in-app-reporting.md). Machine-readable spec: `/api/openapi-report.json`.

## Deploying to Linux (DigitalOcean) with CI/CD

The production board runs on an Ubuntu 24.04 droplet behind Cloudflare and Caddy. Full guide: [docs/deploy-digitalocean.md](docs/deploy-digitalocean.md).

- **Pipeline** (`.github/workflows/ci-cd.yml`): every push and pull request runs the whole verify gate on Linux (typecheck, unit, backend, build, Playwright E2E in Chromium). A passing push to `main` is packaged and deployed to the droplet automatically.
- **Safe deploys:** the database is backed up before every deploy, never shipped or replaced, and a release that doesn't come up is switched back to the previous one.
- **Server tools:** `deploy/linux/setup-server.sh` (one-time setup), `sudo qatrack-admin` (status, keys, password, import from IIS, backups).
- **Secrets** live only on the server, in `/etc/qatrack/appsettings.Production.json`. Nothing secret is in this repository.
- **`main` is protected:** changes arrive only through pull requests whose tests passed; merging deploys.

## Deploying to IIS

**Server prerequisites:** IIS with Management Tools, and the **.NET 8 Hosting Bundle** (it installs ASP.NET Core Module V2). Run `iisreset` after installing the bundle.

1. On the build machine: `npm run package` → `artifacts/QATrack-1.0.0.zip`.
2. Copy the zip to the server and extract it to a staging folder (not the site folder).
3. Optional: check the server first (read-only, changes nothing):

   ```powershell
   .\deploy-iis.ps1 -Action Diagnose
   ```

4. In an **elevated** PowerShell in that folder:

   ```powershell
   .\deploy-iis.ps1 -Action Install -SiteName QATrack -AppPoolName QATrack -PhysicalPath C:\inetpub\QATrack -Port 8080
   ```

On first install the script prints the generated AI agent API key. Give it only to trusted agents.
You can also set `AiAgentApi:ApiKey` in `appsettings.Production.json` on the server.

### What Install does

- Creates the app pool (No Managed Code, 64-bit, AlwaysRunning) and the site, if they're missing.
- On an update: drops `app_offline.htm`, which shuts the app down cleanly and releases the SQLite file. It then backs up `kanban.db` (+ WAL/SHM) to `App_Data\backups\<UTC timestamp>` and keeps the 10 newest backups.
- Copies the new build **without mirroring or purging**. `App_Data\*.db*` files and the server's own `appsettings.Production.json` are never overwritten.
- Grants `IIS AppPool\<pool>` and `IUSR` Modify rights (Read & Execute + Write) on `App_Data` and `logs`.
- Removes `app_offline.htm` and makes a warm-up request. EF migrations then run forward-only on start.

### Production data safety

- Schema changes ship only as additive EF Core migrations. Nothing in the app or the scripts calls `EnsureDeleted`/`EnsureCreated`, drops tables, or replaces the database.
- The package never contains a database. `deploy-iis.ps1 -Action Package` strips any `*.db*` files and fails if dev settings are included.
- `web.config` hides `App_Data` from HTTP (`hiddenSegments`).
- These guarantees are enforced by tests: `DatabaseInitializerTests` (restart on live data) and `DeployScriptTests` (redeploy preserves DB/config, backup taken, bad input leaves the site untouched).

IIS cannot swap a single site with zero downtime. A redeploy shows the "being updated" page for a few seconds while binaries are replaced. Nothing is lost, and browser users need no session state because the board is stateless.

## Accessibility

- WCAG 2.1 AA colour tokens for light and dark themes. `src/styles/contrast.test.ts` fails the build if any text pairing drops below 4.5:1 or any UI boundary or focus ring below 3:1.
- Keyboard card moves: focus a card, press **Space/Enter** to pick it up, **←/→** to change columns, **Space/Enter** to drop, and **Esc** to cancel.
- `aria-live` announcements for moves, filters, and theme changes. (WIP-limit warnings are announced assertively too, but no column has a limit since 1.11.0.)
- The Playwright suite runs axe-core scans of the board and dialogs in both themes.

## Security notes

- Strict Content-Security-Policy (no inline script or style) on the SPA; X-Frame-Options DENY, nosniff, and no-referrer.
- All UI rendering is text-only DOM building. Markdown goes through DOMPurify before it touches the DOM.
- API key comparison is constant-time. Keys are never logged. With no key configured, `/api/v1` answers 503 (fails closed).
- Input is sanitized server-side (control characters are stripped and lengths enforced), and request bodies are capped at 4 MB.
