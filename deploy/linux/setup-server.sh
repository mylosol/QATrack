#!/usr/bin/env bash
# =============================================================================
# QATrack - one-time (and safely re-runnable) setup of an Ubuntu 24.04 server.
#
#   sudo ./setup-server.sh --domain qatrack.example.com [--email you@example.com]
#                          [--deploy-key "ssh-ed25519 AAAA... github-actions"]
#
# What it does (each step is skipped when already done):
#   - installs the ASP.NET Core 8 runtime, Caddy (HTTPS + reverse proxy),
#     sqlite3 and unattended security upgrades; adds a swap file if none
#   - creates the 'qatrack' service user and the 'qatrack-deploy' CI user
#   - creates /etc/qatrack/appsettings.Production.json with NEW random API and
#     reporter keys, printed once (an existing file is never replaced)
#   - installs the systemd service, the Caddy site, a daily database backup
#     timer, the firewall rules and the qatrack-deploy / qatrack-admin tools
#
# It never touches /var/lib/qatrack/kanban.db or the backups.
# =============================================================================
set -euo pipefail

DOMAIN=""
EMAIL=""
DEPLOY_KEY=""
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

APP_USER=qatrack
DEPLOY_USER=qatrack-deploy
APP_ROOT=/opt/qatrack
DATA_DIR=/var/lib/qatrack
CONFIG_DIR=/etc/qatrack
SETTINGS="$CONFIG_DIR/appsettings.Production.json"
APP_PORT=5080

usage() {
  sed -n '2,20p' "$0"
  exit 2
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --domain) DOMAIN="${2:-}"; shift 2 ;;
    --email) EMAIL="${2:-}"; shift 2 ;;
    --deploy-key) DEPLOY_KEY="${2:-}"; shift 2 ;;
    -h|--help) usage ;;
    *) echo "Unknown option: $1" >&2; usage ;;
  esac
done

step() { printf '\n==> %s\n' "$*"; }
die() { printf 'ERROR: %s\n' "$*" >&2; exit 1; }

[[ $EUID -eq 0 ]] || die "Run as root (sudo)."
[[ -n "$DOMAIN" ]] || die "--domain is required, e.g. --domain qatrack.example.com"
[[ "$DOMAIN" =~ ^[A-Za-z0-9.-]+$ ]] || die "Invalid domain: $DOMAIN"
# shellcheck source=/dev/null
. /etc/os-release
[[ "${ID:-}" == "ubuntu" ]] || die "This script supports Ubuntu (found ${PRETTY_NAME:-unknown})."
for f in qatrack.service Caddyfile.template qatrack-deploy.sh qatrack-admin.sh qatrack-backup.service qatrack-backup.timer; do
  [[ -f "$SCRIPT_DIR/$f" ]] || die "Missing $SCRIPT_DIR/$f (run from the deploy/linux folder)."
done

export DEBIAN_FRONTEND=noninteractive

step "Installing packages (.NET 8 runtime, sqlite3, firewall, security updates)"
apt-get update -q
apt-get install -y -q ca-certificates curl gnupg jq sqlite3 ufw unattended-upgrades \
  debian-keyring debian-archive-keyring apt-transport-https aspnetcore-runtime-8.0

if ! command -v caddy >/dev/null; then
  step "Installing Caddy from its official repository"
  curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/gpg.key' \
    | gpg --batch --yes --dearmor -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
  curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt' \
    > /etc/apt/sources.list.d/caddy-stable.list
  apt-get update -q
  apt-get install -y -q caddy
fi

if [[ -z "$(swapon --noheadings --show)" ]]; then
  step "Adding a 1 GB swap file (headroom for large uploads on a small droplet)"
  fallocate -l 1G /swapfile
  chmod 600 /swapfile
  mkswap /swapfile >/dev/null
  swapon /swapfile
  grep -q '^/swapfile ' /etc/fstab || echo '/swapfile none swap sw 0 0' >> /etc/fstab
fi

step "Users and folders"
id "$APP_USER" >/dev/null 2>&1 || useradd --system --home-dir "$DATA_DIR" --shell /usr/sbin/nologin "$APP_USER"
id "$DEPLOY_USER" >/dev/null 2>&1 || useradd --create-home --shell /bin/bash "$DEPLOY_USER"
install -d -o root -g root -m 0755 "$APP_ROOT" "$APP_ROOT/releases"
install -d -o "$APP_USER" -g "$APP_USER" -m 0750 "$DATA_DIR" "$DATA_DIR/backups"
install -d -o "$APP_USER" -g "$APP_USER" -m 0700 "$DATA_DIR/keys"
install -d -o root -g "$APP_USER" -m 0750 "$CONFIG_DIR"
install -d -o "$DEPLOY_USER" -g "$DEPLOY_USER" -m 0700 "/home/$DEPLOY_USER/.ssh" "/home/$DEPLOY_USER/incoming"

if [[ -n "$DEPLOY_KEY" ]]; then
  [[ "$DEPLOY_KEY" =~ ^(ssh-ed25519|ssh-rsa|ecdsa-sha2-nistp256)\  ]] || die "--deploy-key must be an SSH public key line."
  keys="/home/$DEPLOY_USER/.ssh/authorized_keys"
  touch "$keys"
  if ! grep -qxF "$DEPLOY_KEY" "$keys"; then
    echo "$DEPLOY_KEY" >> "$keys"
    echo "    Added the CI deploy key for $DEPLOY_USER."
  fi
  chown "$DEPLOY_USER:$DEPLOY_USER" "$keys"
  chmod 600 "$keys"
fi

step "Settings and keys ($SETTINGS)"
if [[ -f "$SETTINGS" ]]; then
  echo "    Keeping the existing settings file (keys and password unchanged)."
else
  agent_key="$(openssl rand -hex 32)"
  reporter_key="$(openssl rand -hex 32)"
  umask 027
  jq -n --arg agent "$agent_key" --arg reporter "$reporter_key" --arg data "$DATA_DIR" '{
    ConnectionStrings: { Kanban: ("Data Source=" + $data + "/kanban.db;Cache=Shared;Mode=ReadWriteCreate;") },
    AiAgentApi: { ApiKey: $agent },
    IssueReporting: { ApiKey: $reporter },
    AccessControl: { SharedPasswordHash: "", KeyDirectory: ($data + "/keys") },
    ReverseProxy: { Enabled: true },
    Database: { SeedSampleData: false }
  }' > "$SETTINGS"
  umask 022
  chown root:"$APP_USER" "$SETTINGS"
  chmod 0640 "$SETTINGS"
  cat <<EOF

    Generated new keys. Store them somewhere safe (also shown later by 'sudo qatrack-admin keys'):
      X-API-Key (AI agents):           $agent_key
      X-Reporter-Key (in-app reports): $reporter_key
    Moving from IIS? Import the old keys and password instead: 'sudo qatrack-admin import-settings <file>'.
EOF
fi

step "Admin and deploy tools"
install -o root -g root -m 0755 "$SCRIPT_DIR/qatrack-deploy.sh" /usr/local/sbin/qatrack-deploy
install -o root -g root -m 0755 "$SCRIPT_DIR/qatrack-admin.sh" /usr/local/sbin/qatrack-admin
sudoers=/etc/sudoers.d/qatrack-deploy
tmp="$(mktemp)"
echo "$DEPLOY_USER ALL=(root) NOPASSWD: /usr/local/sbin/qatrack-deploy" > "$tmp"
visudo -cf "$tmp" >/dev/null || die "Generated sudoers rule is invalid."
install -o root -g root -m 0440 "$tmp" "$sudoers"
rm -f "$tmp"

step "systemd service and daily backup timer"
install -o root -g root -m 0644 "$SCRIPT_DIR/qatrack.service" /etc/systemd/system/qatrack.service
install -o root -g root -m 0644 "$SCRIPT_DIR/qatrack-backup.service" /etc/systemd/system/qatrack-backup.service
install -o root -g root -m 0644 "$SCRIPT_DIR/qatrack-backup.timer" /etc/systemd/system/qatrack-backup.timer
systemctl daemon-reload
systemctl enable qatrack.service >/dev/null
systemctl enable --now qatrack-backup.timer >/dev/null
if [[ -e "$APP_ROOT/current" ]]; then
  systemctl restart qatrack.service
else
  echo "    No release deployed yet: the service starts with the first deploy."
fi

step "Caddy site for $DOMAIN (HTTPS via Let's Encrypt)"
# Cloudflare's published ranges, so Caddy trusts CF-Connecting-IP only from Cloudflare.
cf_ranges="$( { curl -fsS --max-time 10 https://www.cloudflare.com/ips-v4; echo; curl -fsS --max-time 10 https://www.cloudflare.com/ips-v6; } 2>/dev/null | tr -s '\n' ' ' || true)"
if [[ -z "${cf_ranges// /}" ]]; then
  cf_ranges="173.245.48.0/20 103.21.244.0/22 103.22.200.0/22 103.31.4.0/22 141.101.64.0/18 108.162.192.0/18 190.93.240.0/20 188.114.96.0/20 197.234.240.0/22 198.41.128.0/17 162.158.0.0/15 104.16.0.0/13 104.24.0.0/14 172.64.0.0/13 131.0.72.0/22 2400:cb00::/32 2606:4700::/32 2803:f800::/32 2405:b500::/32 2405:8100::/32 2a06:98c0::/29 2c0f:f248::/32"
fi
email_sed='/{{EMAIL_LINE}}/d'
[[ -n "$EMAIL" ]] && email_sed="s|{{EMAIL_LINE}}|email $EMAIL|"
sed -e "s|{{DOMAIN}}|$DOMAIN|g" -e "$email_sed" \
    -e "s|{{CLOUDFLARE_RANGES}}|$cf_ranges|" -e "s|{{APP_PORT}}|$APP_PORT|" \
    "$SCRIPT_DIR/Caddyfile.template" > /etc/caddy/Caddyfile.new
caddy validate --config /etc/caddy/Caddyfile.new --adapter caddyfile >/dev/null 2>&1 \
  || { caddy validate --config /etc/caddy/Caddyfile.new --adapter caddyfile; die "Caddyfile is invalid."; }
mv /etc/caddy/Caddyfile.new /etc/caddy/Caddyfile
# 'caddy validate' runs as root and opens (creates) the access log; Caddy itself runs as 'caddy'.
install -d -o caddy -g caddy -m 0755 /var/log/caddy
chown -R caddy:caddy /var/log/caddy
systemctl enable caddy >/dev/null
systemctl reload-or-restart caddy

step "Firewall (SSH, HTTP, HTTPS)"
ufw allow OpenSSH >/dev/null
ufw allow 80/tcp >/dev/null
ufw allow 443/tcp >/dev/null
ufw --force enable >/dev/null
ufw status | sed 's/^/    /'

step "Automatic security updates"
dpkg-reconfigure -f noninteractive unattended-upgrades >/dev/null

cat <<EOF

Setup complete.
  App folder:    $APP_ROOT/current (deployed by CI as $DEPLOY_USER)
  Data:          $DATA_DIR (kanban.db, keys, backups) - never replaced by deploys
  Settings:      $SETTINGS
  Next:          push to main (CI deploys), then 'sudo qatrack-admin set-password'
  Status:        sudo qatrack-admin status
EOF
