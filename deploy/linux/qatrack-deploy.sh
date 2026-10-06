#!/usr/bin/env bash
# =============================================================================
# QATrack - install a release (run by CI as: sudo qatrack-deploy <tarball>).
#
#   1. checks the package (must contain the app, must NOT contain a database)
#   2. backs up the live database (online, consistent: sqlite3 .backup)
#   3. unpacks into /opt/qatrack/releases/<version>-<commit> and switches the
#      /opt/qatrack/current link to it in one atomic step
#   4. restarts the service and waits for /api/version to report the new build
#   5. if it doesn't come up, switches back to the previous release
#
# The database, keys and settings are never replaced: database changes are
# forward-only additive migrations, so the previous release also runs on a
# database the new one has upgraded.
# =============================================================================
set -euo pipefail

APP_ROOT=/opt/qatrack
RELEASES="$APP_ROOT/releases"
DATA_DIR=/var/lib/qatrack
DB="$DATA_DIR/kanban.db"
INCOMING=/home/qatrack-deploy/incoming
HEALTH_URL=http://127.0.0.1:5080/api/version
KEEP_RELEASES=5
KEEP_DEPLOY_BACKUPS=20

die() { printf 'DEPLOY FAILED: %s\n' "$*" >&2; exit 1; }
log() { printf '[deploy] %s\n' "$*"; }

[[ $EUID -eq 0 ]] || die "run with sudo."
[[ $# -eq 1 ]] || die "usage: qatrack-deploy $INCOMING/qatrack-<version>-<commit>.tar.gz"

# Only packages uploaded by CI, by exact name: this runs as root via sudo.
tarball="$(realpath -e -- "$1" 2>/dev/null)" || die "no such file: $1"
[[ "$(dirname "$tarball")" == "$INCOMING" ]] || die "package must be in $INCOMING"
name="$(basename "$tarball")"
[[ "$name" =~ ^qatrack-([0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?)-([0-9a-f]{7,40})\.tar\.gz$ ]] \
  || die "unexpected package name: $name"
version="${BASH_REMATCH[1]}"
commit="${BASH_REMATCH[3]:0:7}"
release_id="$version-$commit"

# 1. Unpack into a staging folder and check it.
staging="$(mktemp -d "$RELEASES/.staging-XXXXXX")"
trap 'rm -rf -- "$staging"' EXIT
tar -xzf "$tarball" -C "$staging" --no-same-owner --no-same-permissions
[[ -f "$staging/KanbanBoard.Api.dll" ]] || die "package has no KanbanBoard.Api.dll"
[[ -f "$staging/wwwroot/index.html" ]] || die "package has no wwwroot/index.html (frontend not built)"
if find "$staging" \( -name '*.db' -o -name '*.db-wal' -o -name '*.db-shm' \) -print -quit | grep -q .; then
  die "package contains a database file; refusing to deploy it"
fi
chown -R root:root "$staging"
find "$staging" -type d -exec chmod 0755 {} +
find "$staging" -type f -exec chmod 0644 {} +

target="$RELEASES/$release_id"
[[ -e "$target" ]] && target="$target-$(date -u +%Y%m%d%H%M%S)"
mv -- "$staging" "$target"
trap - EXIT

previous=""
[[ -L "$APP_ROOT/current" ]] && previous="$(readlink -f "$APP_ROOT/current")"
log "deploying $release_id (previous: ${previous:+$(basename "$previous")}${previous:-none})"

# 2. Back up the live database before the new version can migrate it.
if [[ -f "$DB" ]]; then
  backup="$DATA_DIR/backups/pre-deploy-$(date -u +%Y%m%dT%H%M%SZ)-$release_id.db"
  runuser -u qatrack -- sqlite3 "$DB" ".backup '$backup'" || die "database backup failed; nothing changed"
  log "database backed up to $backup"
fi

switch_to() {
  ln -sfn "$1" "$APP_ROOT/current.new"
  mv -Tf "$APP_ROOT/current.new" "$APP_ROOT/current"
}

wait_healthy() {
  local expect="$1" body
  for _ in $(seq 1 60); do
    if body="$(curl -fsS --max-time 3 "$HEALTH_URL" 2>/dev/null)"; then
      if [[ -z "$expect" ]] || jq -e --arg c "$expect" '.commit == $c' >/dev/null <<<"$body"; then
        printf '%s\n' "$body"
        return 0
      fi
    fi
    sleep 1
  done
  return 1
}

# 3-4. Switch and restart.
switch_to "$target"
systemctl restart qatrack.service
if version_json="$(wait_healthy "$commit")"; then
  log "healthy: $version_json"
else
  journalctl -u qatrack.service -n 40 --no-pager >&2 || true
  if [[ -n "$previous" && -d "$previous" ]]; then
    log "new release did not come up; rolling back to $(basename "$previous")"
    switch_to "$previous"
    systemctl restart qatrack.service
    wait_healthy "" >/dev/null || log "WARNING: the previous release did not come up either"
  fi
  die "release $release_id did not start (see the log above)"
fi

# 5. Tidy up: keep the newest releases (never the current or previous one) and backups.
current="$(readlink -f "$APP_ROOT/current")"
mapfile -t old < <(find "$RELEASES" -mindepth 1 -maxdepth 1 -type d ! -name '.staging-*' -printf '%T@ %p\n' | sort -rn | awk '{print $2}' | tail -n +$((KEEP_RELEASES + 1)))
for dir in "${old[@]}"; do
  [[ "$dir" == "$current" || "$dir" == "$previous" ]] && continue
  rm -rf -- "$dir"
done
find "$DATA_DIR/backups" -maxdepth 1 -name 'pre-deploy-*.db' -printf '%T@ %p\n' | sort -rn | awk '{print $2}' \
  | tail -n +$((KEEP_DEPLOY_BACKUPS + 1)) | xargs -r rm -f --
rm -f -- "$tarball"
log "done: $release_id is live"
