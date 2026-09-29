# Changelog

All notable changes to QATrack are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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

[1.1.0]: https://semver.org/spec/v2.0.0.html
[1.0.0]: https://semver.org/spec/v2.0.0.html
