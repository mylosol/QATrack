/**
 * Vite build for the QATrack board SPA.
 *
 * - `base: './'` emits relative asset URLs so the app also works when IIS
 *   hosts it under a virtual directory (e.g. https://server/qatrack/).
 * - The bundle is written straight into the ASP.NET Core wwwroot, which the
 *   backend serves as static files (spec 1: "served directly as static assets").
 * - The dev server proxies /api to the backend (`dotnet run`, port 5080).
 * - Semantic Versioning: the app version is read from the ROOT package.json
 *   (the single source of truth, shared with Directory.Build.props) and
 *   injected as __APP_VERSION__ / __APP_COMMIT__. An invalid version fails
 *   the build.
 */
import { execSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { defineConfig } from 'vitest/config';

/** Official SemVer 2.0 pattern without build metadata (added from git below). */
const SEMVER = /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?$/;

const rootPackage = JSON.parse(readFileSync(fileURLToPath(new URL('../../package.json', import.meta.url)), 'utf8')) as {
  version?: string;
};
const appVersion = rootPackage.version ?? '';
if (!SEMVER.test(appVersion)) {
  throw new Error(`Root package.json version "${appVersion}" is not a valid Semantic Version (MAJOR.MINOR.PATCH[-prerelease]).`);
}

/** Short git commit for SemVer build metadata; empty when not in a git checkout. */
function gitCommit(): string {
  try {
    // Full SHA sliced to 7 - exactly what the backend's AppVersion.Commit does.
    // (`--short` may return more than 7 chars when a prefix is ambiguous, which
    // would make the update notifier see two different builds forever.)
    return execSync('git rev-parse HEAD', { stdio: ['ignore', 'pipe', 'ignore'] }).toString().trim().slice(0, 7);
  } catch {
    return '';
  }
}

export default defineConfig({
  base: './',
  define: {
    __APP_VERSION__: JSON.stringify(appVersion),
    __APP_COMMIT__: JSON.stringify(gitCommit()),
  },
  build: {
    outDir: '../Backend/KanbanBoard.Api/wwwroot',
    emptyOutDir: true,
    sourcemap: false,
    target: 'es2022',
  },
  server: {
    port: 5173,
    proxy: {
      '/api': 'http://localhost:5080',
    },
  },
  test: {
    environment: 'jsdom',
    include: ['src/**/*.test.ts'],
    restoreMocks: true,
  },
});
