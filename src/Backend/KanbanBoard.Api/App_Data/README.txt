QATrack data directory
======================

The SQLite database (kanban.db) is created here automatically on first start
and schema migrations are applied in place on every start (forward-only,
non-destructive).

 * Never delete or replace kanban.db during a redeploy - deploy-iis.ps1
   deliberately skips this directory's *.db, *.db-wal and *.db-shm files.
 * The IIS web.config hides this folder (hiddenSegments), so it can never be
   downloaded over HTTP.
 * The IIS AppPool identity needs Modify rights here (deploy-iis.ps1 grants it).
