#!/usr/bin/env bash
# =============================================================================
# QATrack - server administration (Linux). Run with sudo.
#
#   qatrack-admin status                 service, version, database, backups
#   qatrack-admin logs [lines]           recent application log
#   qatrack-admin keys                   show the AI agent and reporter keys
#   qatrack-admin set-password           set the board password (asks for it)
#   qatrack-admin set-key agent|reporter set a key (asks for it; e.g. the old IIS key)
#   qatrack-admin import-settings FILE   copy the keys and password hash from an
#                                        IIS appsettings.Production.json
#   qatrack-admin import-db FILE         move an existing kanban.db onto this server
#                                        (only onto an empty board unless --replace)
#   qatrack-admin backup [--daily]       consistent copy of the database now
#
# Settings changes apply live (the app watches its settings file).
# =============================================================================
set -euo pipefail

DATA_DIR=/var/lib/qatrack
DB="$DATA_DIR/kanban.db"
BACKUPS="$DATA_DIR/backups"
SETTINGS=/etc/qatrack/appsettings.Production.json
APP_DLL=/opt/qatrack/current/KanbanBoard.Api.dll
KEEP_DAILY=14
MIN_KEY_LENGTH=16

die() { printf 'ERROR: %s\n' "$*" >&2; exit 1; }
need_root() { [[ $EUID -eq 0 ]] || die "run with sudo."; }
need_settings() { [[ -f "$SETTINGS" ]] || die "$SETTINGS not found; run setup-server.sh first."; }

# Writes one JSON value into the settings file atomically, keeping owner and mode.
set_setting() {
  local path="$1" value="$2" tmp
  tmp="$(mktemp "$SETTINGS.XXXXXX")"
  jq --arg v "$value" "setpath(\$p; \$v)" --argjson p "$path" "$SETTINGS" > "$tmp"
  chown --reference="$SETTINGS" "$tmp"
  chmod --reference="$SETTINGS" "$tmp"
  mv -f "$tmp" "$SETTINGS"
}

read_secret() {
  local prompt="$1" value
  if [[ -t 0 ]]; then
    read -r -s -p "$prompt" value; echo >&2
  else
    read -r value
  fi
  printf '%s' "$value"
}

backup_db() {
  local label="$1" target
  [[ -f "$DB" ]] || { echo "No database yet ($DB)."; return 0; }
  target="$BACKUPS/$label-$(date -u +%Y%m%dT%H%M%SZ).db"
  runuser -u qatrack -- sqlite3 "$DB" ".backup '$target'"
  echo "$target"
}

count_items() {
  sqlite3 -readonly "$1" "SELECT COUNT(*) FROM WorkItem;" 2>/dev/null || echo "?"
}

cmd="${1:-}"
shift || true
case "$cmd" in
  status)
    systemctl --no-pager --lines=0 status qatrack.service || true
    echo
    curl -fsS --max-time 3 http://127.0.0.1:5080/api/version && echo || echo "(the app is not answering on 127.0.0.1:5080)"
    echo
    echo "Release:  $(readlink -f /opt/qatrack/current 2>/dev/null || echo none)"
    if [[ -f "$DB" ]]; then
      echo "Database: $DB ($(du -h "$DB" | cut -f1), $(count_items "$DB") work items)"
    else
      echo "Database: none yet"
    fi
    echo "Backups:  $(find "$BACKUPS" -maxdepth 1 -name '*.db' | wc -l) in $BACKUPS, newest: $(ls -1t "$BACKUPS"/*.db 2>/dev/null | head -1 || echo none)"
    ;;

  logs)
    journalctl -u qatrack.service -n "${1:-100}" --no-pager
    ;;

  keys)
    need_root; need_settings
    echo "X-API-Key (AI agents):           $(jq -r '.AiAgentApi.ApiKey // ""' "$SETTINGS")"
    echo "X-Reporter-Key (in-app reports): $(jq -r '.IssueReporting.ApiKey // ""' "$SETTINGS")"
    if [[ -n "$(jq -r '.AccessControl.SharedPasswordHash // ""' "$SETTINGS")" ]]; then
      echo "Board password: set"
    else
      echo "Board password: NOT set (anyone who can reach the site can use the board) - run: sudo qatrack-admin set-password"
    fi
    ;;

  set-password)
    need_root; need_settings
    [[ -f "$APP_DLL" ]] || die "no release deployed yet; deploy first, then set the password."
    pw="$(read_secret 'New board password: ')"
    if [[ -t 0 ]]; then
      again="$(read_secret 'Repeat it: ')"
      [[ "$pw" == "$again" ]] || die "the passwords don't match."
    fi
    hash="$(printf '%s\n' "$pw" | dotnet "$APP_DLL" --hash-password)" || die "password rejected."
    set_setting '["AccessControl","SharedPasswordHash"]' "$hash"
    echo "Board password set. Everyone signed in is signed out and must use the new password."
    ;;

  set-key)
    need_root; need_settings
    which="${1:-}"
    case "$which" in
      agent) path='["AiAgentApi","ApiKey"]'; other='.IssueReporting.ApiKey' ;;
      reporter) path='["IssueReporting","ApiKey"]'; other='.AiAgentApi.ApiKey' ;;
      *) die "usage: qatrack-admin set-key agent|reporter" ;;
    esac
    key="$(read_secret "New $which key: ")"
    [[ ${#key} -ge $MIN_KEY_LENGTH ]] || die "keys must be at least $MIN_KEY_LENGTH characters."
    [[ "$key" != "$(jq -r "$other // \"\"" "$SETTINGS")" ]] || die "the agent and reporter keys must differ."
    set_setting "$path" "$key"
    echo "The $which key was updated."
    ;;

  import-settings)
    need_root; need_settings
    src="${1:-}"
    [[ -f "$src" ]] || die "usage: qatrack-admin import-settings /path/to/old/appsettings.Production.json"
    # Windows files often start with a byte order mark; jq doesn't accept one.
    clean="$(mktemp)"
    sed '1s/^\xEF\xBB\xBF//' "$src" > "$clean"
    jq empty "$clean" 2>/dev/null || { rm -f "$clean"; die "$src is not valid JSON."; }
    imported=0
    for pair in 'AiAgentApi.ApiKey:["AiAgentApi","ApiKey"]' \
                'IssueReporting.ApiKey:["IssueReporting","ApiKey"]' \
                'AccessControl.SharedPasswordHash:["AccessControl","SharedPasswordHash"]'; do
      field="${pair%%:*}"; path="${pair#*:}"
      value="$(jq -r ".$field // \"\"" "$clean")"
      if [[ -n "$value" ]]; then
        set_setting "$path" "$value"
        echo "Imported $field."
        imported=$((imported + 1))
      else
        echo "Skipped $field (empty in $src)."
      fi
    done
    rm -f "$clean"
    [[ $imported -gt 0 ]] || die "nothing to import."
    echo "Agents and in-app reporters keep working with their existing keys; the board password is unchanged."
    ;;

  import-db)
    need_root
    src="${1:-}"; mode="${2:-}"
    [[ -f "$src" ]] || die "usage: qatrack-admin import-db /path/to/kanban.db [--replace]"
    check="$(sqlite3 -readonly "$src" 'PRAGMA integrity_check;' 2>&1 || true)"
    [[ "$check" == "ok" ]] || die "$src failed its integrity check: $check"
    incoming_items="$(count_items "$src")"
    if [[ -f "$DB" ]]; then
      existing_items="$(count_items "$DB")"
      if [[ "$existing_items" != "0" && "$mode" != "--replace" ]]; then
        die "this server's board already has $existing_items work items. Nothing was changed. Use --replace to swap it (a backup is taken first)."
      fi
    fi
    systemctl stop qatrack.service
    if [[ -f "$DB" ]]; then
      echo "Backed up the current database to $(backup_db before-import)"
    fi
    staged="$DATA_DIR/kanban.db.import"
    # .backup reads the source consistently, including a -wal file next to it.
    sqlite3 -readonly "$src" ".backup '$staged'"
    chown qatrack:qatrack "$staged"
    chmod 0640 "$staged"
    rm -f -- "$DB-wal" "$DB-shm"
    mv -f -- "$staged" "$DB"
    systemctl start qatrack.service
    echo "Imported $incoming_items work items from $src. The app upgrades the database on start if needed."
    echo "People sign in again once (sign-in sessions don't move between servers)."
    ;;

  backup)
    need_root
    if [[ "${1:-}" == "--daily" ]]; then
      backup_db daily >/dev/null
      find "$BACKUPS" -maxdepth 1 -name 'daily-*.db' -printf '%T@ %p\n' | sort -rn | awk '{print $2}' \
        | tail -n +$((KEEP_DAILY + 1)) | xargs -r rm -f --
    else
      backup_db manual
    fi
    ;;

  *)
    sed -n '2,19p' "$0"
    exit 2
    ;;
esac
