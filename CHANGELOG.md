# Changelog

All notable changes to QATrack are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.8.0] - 2026-09-30

### Added
- **AI agents can find what changed, including new human comments, without opening every card.**
  - `updatedSince` filter on `GET /api/v1/workitems` and `GET /api/v1/board`: returns only items changed (fields, state or comments) strictly after the given instant. Agents pass the newest `updatedAt` they've seen.
  - Every work item now reports `lastHumanCommentAt` and `lastHumanCommentBy`: when, and by whom, a person (not an AI agent) last commented. These are included in list and board results, which don't carry the full history.
  - The comment text itself is still read from `GET /api/v1/workitems/{id}` (the item's history).
  - `GET /api/v1/meta` lists this as the 1.8.0 change, with a new `X-API-Schema-Version`, so agents following the "staying current" rule pick it up automatically.

### Changed
- Additive database migration `AddLastHumanComment`: two new optional columns on `WorkItem`, filled in from existing history during the upgrade (so comments made before 1.8.0 count), plus an index on `UpdatedAt`.
- The board itself looks and works the same.

## [1.7.0] - 2026-09-30

### Added
- **Program version** on Bugs: an optional text field recording which version of the program a bug was found in (e.g. `2.4.1`, up to 64 characters).
  - The card dialog shows it only when Type is **Bug**; it appears and disappears as the Type changes.
  - Bug cards show it as "Version 2.4.1". Changes are recorded in the card's history.
  - If a Bug's type is later changed, the value is kept (just not shown), so nothing is lost.
- AI agent API: `programVersion` on create, update and read (`""` clears it). `GET /api/v1/meta` lists it as the 1.7.0 change, with a new `X-API-Schema-Version`.

### Changed
- Additive database migration `AddProgramVersion`: one new optional column on `WorkItem`. Existing cards are unchanged.

## [1.6.1] - 2026-09-30

### Fixed
- **Picking a tag suggestion now adds it as a chip immediately.** Previously you had to type a comma or press Enter after choosing from the list.
- Choosing a suggestion with the arrow keys and pressing Enter could add the half-typed text (e.g. "lo") instead of the suggestion ("login"). Enter now adds the highlighted suggestion, or exactly what you typed if nothing is highlighted.

### Changed
- The tag suggestions are now QATrack's own list instead of the browser's built-in one, so they look and work the same in Edge, Chrome, Firefox and Safari.
  - Suggestions appear as you type (matches at the start first), or on ArrowDown. Click one, or highlight it with the arrow keys and press Enter.
  - Tags already on the card are no longer suggested.
  - Escape closes the list without closing the dialog.
  - It follows the accessible combobox pattern, so screen readers announce the highlighted suggestion.

## [1.6.0] - 2026-09-30

### Removed
- **"Assigned to" is gone from the board**, because AI agents work the cards and identify themselves.
  - The card dialog no longer has the field, and cards no longer show "Assigned to …" / "Unassigned".
  - The toolbar no longer has the "Assigned to" filter.
  - The AI badge and each card's history still show exactly which agent made each change.

### Changed
- `assignedTo` is kept in the API, database and history for compatibility: agents may still send it and existing values are preserved, but the board neither shows nor changes it. The API documentation says so, and `GET /api/v1/meta` lists it as the 1.6.0 change (new `X-API-Schema-Version`).
- The API documentation now also notes that `areaPath` hasn't been shown on the board since 1.4.0.
- In the card dialog, Program now takes the space Assigned to used.

## [1.5.0] - 2026-09-29

### Added
- **AI agents are told when the API changes**, so they know to re-read the documentation.
  - Every `/api/v1` response (including errors) carries `X-API-Schema-Version`, a 12-character fingerprint of the OpenAPI document. It changes only when the API itself changes (an endpoint, field, parameter or description), not on UI-only releases, and is the same on every server running the same API.
  - Every `/api/v1` response also carries `Link: </api/openapi.json>; rel="service-desc"`, the standard pointer to the API description.
  - New `GET /api/v1/meta` (`getApiMeta`) returns the app version, the fingerprint, the docs URLs, the rule below, and a plain-language list of API changes per release. `?since=1.4.0` lists only newer changes.
  - `/api/openapi.json` sends an ETag, so agents can re-check it cheaply (HTTP 304 when unchanged). `/api/version` now also reports `apiSchemaVersion`.
  - The API description (what agents read first) now states the rule: remember the fingerprint; when it changes, re-read `/api/openapi.json` and check `/api/v1/meta` for what changed; an HTTP 503 during an update means wait and retry.
- A build-time guard: the test suite fails if the API changes without a matching entry in the API change list, so agents are never left without an explanation.

## [1.4.0] - 2026-09-29

### Added
- **Program dropdown** (replaces the free-text "Area path" in the card dialog).
  - Starts with **ProveOut** and **CallOut**. The **+** button next to it adds more programs inline; a name that already exists (in any letter case) is simply selected.
  - Cards show their program, and the toolbar has a **Program** filter.
  - Changes are recorded in the card's history.
- **Tags** on work items.
  - Type a tag and press Enter or comma; each chip has its own remove button. Suggestions come from tags already in use.
  - At most 20 tags per item, 50 characters each; letter case is ignored when matching ("UI" and "ui" are one tag).
  - Cards show their tags, and the toolbar has a **Tag** filter. Tag changes are recorded in history.
- **Rich text editor** for descriptions and comments.
  - Toolbar: bold, italic, strikethrough, inline code, headings, bulleted and numbered lists, quote, code block, image, undo/redo, and a **Markdown** toggle for the raw text.
  - **Images:** insert from the toolbar, paste, or drag and drop. PNG, JPEG, GIF or WebP up to 5 MB. Images are uploaded and stored inside `kanban.db` (so database backups include them); descriptions reference them by link, never inline base64.
  - Content is still stored as **Markdown**, so the AI agent API is unchanged.
  - Text that the rich view cannot show without losing something (for example tables or HTML written by an agent) opens in Markdown mode, untouched. Saving a card only sends the description if you actually edited it.
  - Accessible: labelled toolbar with arrow-key navigation and pressed states. Tab always leaves the editor (indent list items with Ctrl+] / Ctrl+[).
- **AI agent API:**
  - `program` and `tags` on create, update, list and board; `program` and `tag` list/board filters.
  - `GET/POST /api/v1/programs` (`listPrograms`, `createProgram`).
  - `POST /api/v1/attachments` (multipart, `uploadAttachment`) returns ready-to-paste image markdown; `GET /api/v1/attachments/{id}` (`getAttachment`).

### Changed
- Additive database migration `AddProgramsTagsAttachments`: new `Program`, `Tag`, `WorkItemTag` and `Attachment` tables, plus a nullable `WorkItem.ProgramId`. Existing cards are kept unchanged (no program, no tags).
- `areaPath` stays in the API for compatibility, but the board no longer shows or edits it.
- Uploaded files are checked by their actual bytes, not their name or declared type. SVG is refused because it can contain script. Identical uploads are stored once.
- The maximum request size is now 8 MB (was 4 MB) to fit a 5 MB image upload.
- Adding a comment keeps unsaved edits in the rest of the dialog (previously the dialog was redrawn).

### Fixed
- Elements marked `hidden` could still show when they also had a layout class such as `flex`.

## [1.3.0] - 2026-09-29

### Added
- **Shared access password for the browser board.**
  - **How signing in works:** one password protects all board data (`/api/ui`). Each browser signs in once through an accessible sign-in dialog that can't be dismissed, and stays signed in for 30 days (sliding) via an HttpOnly, SameSite=Strict cookie. There's a "Sign out" link in the header.
  - **Security:** the password is stored only as a PBKDF2-SHA256 hash (600,000 iterations, random salt). Sign-in is limited to 5 attempts per minute per IP (HTTP 429 after that). Changing the password signs every browser out.
  - **Sessions survive recycles:** session keys persist in `App_Data\keys`, DPAPI-protected, so app-pool recycles don't sign people out.
  - **Expiry mid-edit:** if a session ends while a card is being edited, the sign-in dialog opens on top of it and nothing typed is lost.
  - **Unaffected:** the AI agent API (`/api/v1`, API key), `/api/version` and the API docs.
- `deploy-iis.ps1`:
  - `-SharedPassword` on Install.
  - `-Action SetPassword` to set or change the password on an installed site. It prompts twice without echo, applies live with no redeploy or restart, and keeps all other settings.
  - `-RemoveSharedPassword` to turn it off.
  - Install warns when no password is set, and Diagnose shows whether one is.

### Changed
- The installer's warm-up request uses the public `/api/version`, since board data may now require sign-in.
- Packages never include `App_Data\keys` or `App_Data\backups`, and installs never overwrite the server's keys.
- With no password configured, the board stays open exactly as in 1.2.x (backwards compatible).

## [1.2.3] - 2026-09-29

### Fixed
- The site returned **HTTP 500.19 "Unrecognized attribute 'inheritChildApplications'"** (0x8007000d) on Windows Server.
  - `web.config` no longer wraps its settings in `<location path="." inheritChildApplications="false">`. That wrapper only affects child applications nested under the site (QATrack has none), so behaviour is unchanged.
  - This deliberately deviates from the verbatim sample in spec section 6.1, and is documented in the file.
  - The handler, in-process ASP.NET Core Module, Production environment and `App_Data` hidden segment are unchanged and now covered by tests.

### Changed
- When the install's warm-up request fails, the script now prints the actual IIS / ASP.NET Core Module error (e.g. `HTTP Error 500.19 ... | Config Error: ...`) with a hint for 500.19, 500.3x and 503, instead of just "(500) Internal Server Error". This works on Windows PowerShell 5.1 and PowerShell 7.

## [1.2.2] - 2026-09-29

### Fixed
- Install failed at `Start-Website` with "The process cannot access the file because it is being used by another process (HRESULT: 0x80070020)" when the chosen port was already taken. The error gave no hint about the cause, and it also skipped the `App_Data` permission step. Now:
  - A port pre-flight runs before anything is changed. It names the other started IIS site or the process (name and PID) holding the port, and ignores QATrack's own site.
  - `App_Data` permissions are granted before the site is started. The site starts last, and if it still can't start, the failure is explained in plain language.
  - Passing `-Port`/`-HostHeader` on a re-install updates an existing site's HTTP binding. HTTPS bindings are kept.
  - The warm-up request and the version check use the site's actual port.

### Added
- `-Action Diagnose` also reports whether `-Port` is free.

## [1.2.1] - 2026-09-29

### Fixed
- `deploy-iis.ps1 -Action Install` reported "ASP.NET Core Module V2 is not installed" on servers where the .NET 8 Hosting Bundle *was* installed. The check looked in `System32\inetsrv`, but the bundle installs the module to `%ProgramFiles%\IIS\Asp.Net Core Module\V2\`. Detection now does three things:
  - asks IIS whether the `AspNetCoreModuleV2` global module is registered, then falls back to the registry and the real install paths;
  - uses the 64-bit Program Files path even from 32-bit PowerShell;
  - separately reports "installed but not registered with IIS" (Hosting Bundle installed before IIS; fix: Repair the bundle).
- The .NET 8 runtime check no longer depends on `dotnet` being on `PATH`.

### Added
- `deploy-iis.ps1 -Action Diagnose`: a read-only report of every server prerequisite, with a fix for each failure. Install now prints the same report and lists all missing prerequisites at once.

## [1.2.0] - 2026-09-29

### Added
- "New version available" notifier. A tab left open across a deploy detects the newer build and shows a small toast in the bottom-right corner, stacked above the theme switcher. The toast has **Reload** and **Dismiss** buttons and never reloads on its own, so in-progress edits are safe.
  - Detection is an uncached `GET /api/version`. The tab compares the server's `build` (`MAJOR.MINOR.PATCH+<7-char commit>`) with the build baked into the bundle. It checks every 60 s, and immediately when the tab regains focus or becomes visible.
  - Reload is forced. Any service-worker registrations and Cache Storage entries are cleared (best-effort) before reloading, so the new build always loads.
  - Dismissing hides the toast until an even newer build ships. Failed checks, including the `app_offline` 503 during a deploy, never show the toast.
- `/api/version` now returns `Cache-Control: no-store` and a canonical `build` field.

### Changed
- The SPA reads the git commit as the full SHA sliced to 7 characters, matching the backend exactly. `git rev-parse --short` can return longer IDs, which would have made the builds never match.

## [1.1.0] - 2026-09-29

### Added
- Semantic Versioning, with the root `package.json` as the single source of truth. The API assembly, the SPA and the release zip all derive their version from it, and the build fails if the version is not valid SemVer 2.0.
- The version is displayed in the board header (`v1.1.0`). Its tooltip shows the full build, including the git commit as SemVer build metadata (e.g. `1.1.0+abc1234`).
- Public `GET /api/version` endpoint returning `name`, `version`, `commit` and `informationalVersion`.
- The OpenAPI document's `info.version` now reports the application version.
- `deploy-iis.ps1` reports "Installing" or "Upgrading <old> -> <new>" and reads back the running version after deployment.
- `npm run release:patch|minor|major` and `npm run version:show`.

### Fixed
- `deploy-iis.ps1 -Action Install` failed on Windows PowerShell 5.1 with "Cannot bind argument to parameter 'Path' because it is an empty string" when `-SourcePath` was omitted. It now defaults correctly to the script's folder.
- `deploy-iis.ps1` could fail under strict mode when the site had no `appsettings.Production.json`.

## [1.0.0] - 2026-09-29

### Added
- Initial release: SQLite/EF Core persistence with WAL, the REST API for AI agents (`/api/v1`) with API-key auth and automatic AI tagging and audit history, and the zero-auth TypeScript Kanban board with drag-and-drop, keyboard moves, card dialog, quick filters and the Light/Dark/Auto theme switcher.
- WCAG 2.1 AA contrast is enforced by tests.
- OpenAPI at `/api/openapi.json` and Swagger UI at `/api/docs`.
- `deploy-iis.ps1` packaging and data-safe IIS install.

[1.3.0]: https://semver.org/spec/v2.0.0.html
[1.2.3]: https://semver.org/spec/v2.0.0.html
[1.2.2]: https://semver.org/spec/v2.0.0.html
[1.2.1]: https://semver.org/spec/v2.0.0.html
[1.2.0]: https://semver.org/spec/v2.0.0.html
[1.1.0]: https://semver.org/spec/v2.0.0.html
[1.0.0]: https://semver.org/spec/v2.0.0.html
