# Hosting QATrack on DigitalOcean (Linux)

QATrack runs on an Ubuntu 24.04 droplet. GitHub Actions tests every change and deploys `main` automatically.

```
visitor / AI agent / in-app reporter
        │  HTTPS
        ▼
Cloudflare (proxy, DNS)  ──HTTPS (Full strict)──▶  Caddy :443 on the droplet (Let's Encrypt certificate)
                                                        │  http://127.0.0.1:5080
                                                        ▼
                                                  qatrack.service (ASP.NET Core 8)
                                                        │
                                                        ▼
                                                  /var/lib/qatrack/kanban.db (+ keys/, backups/)
```

| What | Where on the droplet |
|---|---|
| App (replaced by every deploy) | `/opt/qatrack/current` → `/opt/qatrack/releases/<version>-<commit>` |
| Database, sign-in keys, backups (never replaced) | `/var/lib/qatrack/` |
| Settings and secrets (API keys, password hash) | `/etc/qatrack/appsettings.Production.json` |
| Service | `qatrack.service` (systemd), log: `sudo qatrack-admin logs` |
| Web server / HTTPS | Caddy, `/etc/caddy/Caddyfile` |
| Admin tool | `sudo qatrack-admin` (status, keys, set-password, import, backup) |

## 1. Cloudflare

1. **DNS:** an `A` record for `qatrack` pointing at the droplet's IP, **Proxied** (orange cloud).
2. **SSL/TLS → Overview:** encryption mode **Full (strict)**.
3. **SSL/TLS → Edge Certificates:** **Always Use HTTPS: Off**. Caddy already redirects HTTP to HTTPS, and Cloudflare's own redirect would stop Let's Encrypt from checking the site over HTTP (first certificate and every renewal).
4. **Security:** leave **Bot Fight Mode** and **"I'm Under Attack" mode** off, or add a WAF *skip* rule for `/api/*`. Both challenge non-browser clients, which would block the AI agents and the in-app reporters.

Until the first deploy, Cloudflare shows error **521** (nothing answering on the droplet). Error **526** means Caddy has no valid certificate yet (see Troubleshooting).

## 2. Set up the droplet (once)

From a machine that can SSH to the droplet as root, copy the `deploy/linux` folder and run the setup:

```bash
scp -r deploy/linux root@<droplet-ip>:/root/qatrack-setup
```

```bash
ssh root@<droplet-ip> "cd /root/qatrack-setup && bash setup-server.sh --domain qatrack.xmlbridge.work --email you@example.com --deploy-key '$(cat ~/.ssh/qatrack_deploy.pub)'"
```

- `--email` is for Let's Encrypt expiry notices (optional).
- `--deploy-key` is the public half of the key GitHub Actions deploys with (see step 3).
- It prints a new **X-API-Key** and **X-Reporter-Key** once. Moving from IIS? Ignore them: step 5 imports your existing keys.
- Re-running it is safe: existing settings, data and keys are kept.

The setup installs the ASP.NET Core 8 runtime, Caddy, sqlite3, a 1 GB swap file, the firewall (SSH, HTTP, HTTPS only), automatic security updates and a daily database backup at 03:30 UTC (14 kept). It creates two users: `qatrack` runs the app and can only write `/var/lib/qatrack`; `qatrack-deploy` is the user CI logs in as, and may only run `sudo qatrack-deploy`.

## 3. GitHub repository and pipeline

1. Create a deploy key pair (no passphrase; it is used only by CI and can only install releases):
   ```bash
   ssh-keygen -t ed25519 -N "" -C "github-actions qatrack deploy" -f ~/.ssh/qatrack_deploy
   ```
2. In the repository: **Settings → Environments → New environment** `production`. Optionally add yourself as a required reviewer to approve each deploy.
3. In that environment add:
   - variable `DEPLOY_HOST` = the droplet's IP
   - variable `PUBLIC_URL` = `https://qatrack.xmlbridge.work`
   - secret `DEPLOY_SSH_KEY` = the contents of `~/.ssh/qatrack_deploy` (the private key)
   - secret `DEPLOY_KNOWN_HOSTS` = the output of `ssh-keyscan -t ed25519 <droplet-ip>`
4. **Settings → Branches:** protect `main` and require the **Verify (tests + build)** check, so nothing untested reaches it.

With the GitHub CLI, steps 2–3 are:

```bash
gh secret set DEPLOY_SSH_KEY --env production < ~/.ssh/qatrack_deploy
```

```bash
ssh-keyscan -t ed25519 <droplet-ip> | gh secret set DEPLOY_KNOWN_HOSTS --env production
```

```bash
gh variable set DEPLOY_HOST --env production --body "<droplet-ip>"
```

**On Windows (PowerShell):**

- PowerShell has no `<` redirect. Hand the key file to `cmd` instead:
  ```powershell
  cmd /c "gh secret set DEPLOY_SSH_KEY --env production < %USERPROFILE%\.ssh\qatrack_deploy"
  ```
- Windows' own `ssh-keyscan` may fail against Ubuntu 24.04 (`choose_kex: unsupported KEX method`) and print nothing, which stores an **empty** `DEPLOY_KNOWN_HOSTS`. Run the `ssh-keyscan` line in Git Bash instead, and check the secret isn't empty: the deploy job stops with "Set the DEPLOY_KNOWN_HOSTS secret" if it is.
- To confirm the scanned key is the droplet's own, compare `ssh-keyscan -t ed25519 <droplet-ip> | ssh-keygen -lf -` with `ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub` run on the droplet.

**What the pipeline does** (`.github/workflows/ci-cd.yml`):

- **Every push and pull request:** typecheck, frontend unit tests, backend tests, production build, the Playwright end-to-end suite (Chromium) and a lint of the server scripts.
- **A push to `main` that passes:** packages `qatrack-<version>-<commit>.tar.gz`, uploads it to the droplet and runs `qatrack-deploy`, which:
  1. refuses a package that contains a database file,
  2. backs up the live database (`/var/lib/qatrack/backups/pre-deploy-*.db`, 20 kept),
  3. switches `/opt/qatrack/current` to the new release and restarts the service,
  4. waits for `/api/version` to report the new commit, and **switches back to the previous release** if it doesn't.
- Finally it checks the public URL reports the new commit.

The database is never replaced by a deploy. Its changes are forward-only additive migrations, so the previous release also runs on a database the new release has upgraded (that's what makes the automatic switch-back safe).

## 4. First deploy and password

Push to `main` (or run the workflow by hand: **Actions → CI/CD → Run workflow**). Then set the board password on the droplet:

```bash
ssh -t root@<droplet-ip> sudo qatrack-admin set-password
```

## 5. Moving the board from IIS

This carries over every card, comment, image, file and the history, plus the existing AI agent key, reporter key and board password, so agents and in-app reporters keep working once they point at the new address.

1. **On the IIS server:** stop the site so the database is closed cleanly (IIS Manager → Sites → QATrack → Stop, or `Stop-Website QATrack`).
2. Copy two files from the site folder (e.g. `C:\inetpub\QATrack`) to the droplet:
   - `App_Data\kanban.db` (and `App_Data\kanban.db-wal` if there is one)
   - `appsettings.Production.json`

   ```bash
   scp kanban.db appsettings.Production.json root@<droplet-ip>:/root/
   ```
3. **On the droplet:**
   ```bash
   sudo qatrack-admin import-settings /root/appsettings.Production.json
   ```
   ```bash
   sudo qatrack-admin import-db /root/kanban.db
   ```
   `import-db` checks the file's integrity first, and refuses to replace a board that already has cards unless you add `--replace` (a backup is taken first either way).
4. Delete the copies afterwards (`rm /root/kanban.db /root/appsettings.Production.json`): the settings file holds the keys.
5. Point the AI agents and the programs under test at `https://qatrack.xmlbridge.work` instead of the old address. Their keys don't change.
6. Everyone signs in to the board once more (sign-in sessions don't move between servers).

Keep the IIS site stopped (or remove it) afterwards, so nobody keeps writing to the old copy.

## Day-to-day

| Task | Command (on the droplet) |
|---|---|
| Is it running? Which version? | `sudo qatrack-admin status` |
| Application log | `sudo qatrack-admin logs 200` |
| Show the API keys | `sudo qatrack-admin keys` |
| Change the board password | `sudo qatrack-admin set-password` |
| Replace a key | `sudo qatrack-admin set-key agent` / `set-key reporter` |
| Back up now | `sudo qatrack-admin backup` |
| Caddy log | `sudo tail -f /var/log/caddy/qatrack.log` |

**Roll back by hand** to an earlier release (the last five are kept):

```bash
ls /opt/qatrack/releases
```

```bash
sudo ln -sfn /opt/qatrack/releases/<release> /opt/qatrack/current && sudo systemctl restart qatrack
```

The next push to `main` deploys again as usual.

**Restore a database backup** (only if data was really lost; stops the board briefly):

```bash
sudo qatrack-admin import-db /var/lib/qatrack/backups/<backup>.db --replace
```

DigitalOcean's droplet backups (enable them in the droplet's **Backups** tab) also copy the whole disk.

## Troubleshooting

| Symptom | Meaning / fix |
|---|---|
| Cloudflare **521** | Nothing answering on the droplet: `sudo systemctl status caddy qatrack`. |
| Cloudflare **522** | The droplet can't be reached: firewall (`sudo ufw status`) or droplet off. |
| Cloudflare **525/526** | No valid certificate on the droplet yet. Check `sudo journalctl -u caddy -n 50`: usually "Always Use HTTPS" is on, or a Cloudflare security feature is challenging Let's Encrypt. |
| **502** from Caddy | The app is down or starting: `sudo qatrack-admin logs`. |
| Deploy job: `Permission denied (publickey)` | The `DEPLOY_SSH_KEY` secret doesn't match the key given to `--deploy-key`. Re-run setup with the right public key. |
| Deploy job: `Host key verification failed` | Refresh `DEPLOY_KNOWN_HOSTS` (`ssh-keyscan -t ed25519 <droplet-ip>`). |
| Agents get **403/challenge pages** | A Cloudflare security feature is blocking API clients; see step 1.4. |
