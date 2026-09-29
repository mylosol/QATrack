# Changelog

All notable changes to QATrack are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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

[1.2.2]: https://semver.org/spec/v2.0.0.html
[1.2.1]: https://semver.org/spec/v2.0.0.html
[1.2.0]: https://semver.org/spec/v2.0.0.html
[1.1.0]: https://semver.org/spec/v2.0.0.html
[1.0.0]: https://semver.org/spec/v2.0.0.html
