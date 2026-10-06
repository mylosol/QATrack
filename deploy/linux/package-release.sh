#!/usr/bin/env bash
# =============================================================================
# QATrack - build the Linux release package (used by CI; works locally too).
#
#   deploy/linux/package-release.sh            -> artifacts/qatrack-<version>-<commit>.tar.gz
#
# Needs the .NET 8 SDK, Node and git. Builds the SPA, publishes the API
# (framework-dependent for linux-x64: runs on the server's ASP.NET Core 8 runtime) and packs
# it. The package never contains a database or production settings.
# =============================================================================
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

version="$(node -p "require('./package.json').version")"
commit="$(git rev-parse --short=7 HEAD)"
name="qatrack-$version-$commit"
out="$ROOT/artifacts/linux/app"

rm -rf "$ROOT/artifacts/linux"
mkdir -p "$out"

echo "==> Building the board UI"
npm --prefix src/Frontend run build

echo "==> Publishing the API ($version+$commit)"
dotnet publish src/Backend/KanbanBoard.Api -c Release -o "$out" --nologo \
  --runtime linux-x64 --self-contained false -p:UseAppHost=false

# Never ship data or secrets: settings live in /etc/qatrack on the server.
rm -rf "$out/App_Data"
find "$out" \( -name '*.db' -o -name '*.db-wal' -o -name '*.db-shm' \) -delete
rm -f "$out/appsettings.Development.json"

[[ -f "$out/KanbanBoard.Api.dll" ]] || { echo "publish produced no KanbanBoard.Api.dll" >&2; exit 1; }
[[ -f "$out/wwwroot/index.html" ]] || { echo "publish produced no wwwroot/index.html" >&2; exit 1; }

tar -czf "$ROOT/artifacts/$name.tar.gz" -C "$out" .
echo "$ROOT/artifacts/$name.tar.gz"
