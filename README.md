# QATrack – DevOps Kanban Board for QA Tools

On-premises, lightweight Azure DevOps Boards clone that runs in-process under IIS on Windows Server.
Humans use a zero-login web board; AI agents use a secured REST API, and every change they make is tagged and audited.

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
| List (filters: `type`, `state`, `aiModified`, `assignedTo`, `top`) | `GET /api/v1/workitems` | `listWorkItems` |
| Details + full audit history | `GET /api/v1/workitems/{id}` | `getWorkItem` |
| Create | `POST /api/v1/workitems` | `createWorkItem` |
| Update / move / reassign (partial; `""` clears assignee or description) | `PATCH /api/v1/workitems/{id}` | `updateWorkItem` |
| Comment or test output | `POST /api/v1/workitems/{id}/comments` | `addWorkItemComment` |
| Board, columns, WIP | `GET /api/v1/board` | `getBoard` |

Point tool-calling agents at `/api/openapi.json`. It declares both headers as security schemes.

## Deploying to IIS

**Server prerequisites:** IIS with Management Tools, and the **.NET 8 Hosting Bundle** (it installs ASP.NET Core Module V2). Run `iisreset` after installing the bundle.

1. On the build machine: `npm run package` → `artifacts/QATrack-1.0.0.zip`.
2. Copy the zip to the server and extract it to a staging folder (not the site folder).
3. In an **elevated** PowerShell in that folder:

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
- `aria-live` announcements for moves, WIP-limit overages (assertive), filters, and theme changes.
- The Playwright suite runs axe-core scans of the board and dialogs in both themes.

## Security notes

- Strict Content-Security-Policy (no inline script or style) on the SPA; X-Frame-Options DENY, nosniff, and no-referrer.
- All UI rendering is text-only DOM building. Markdown goes through DOMPurify before it touches the DOM.
- API key comparison is constant-time. Keys are never logged. With no key configured, `/api/v1` answers 503 (fails closed).
- Input is sanitized server-side (control characters are stripped and lengths enforced), and request bodies are capped at 4 MB.
