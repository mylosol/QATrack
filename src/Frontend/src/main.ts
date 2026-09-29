/**
 * QATrack board SPA entry point.
 */
import './styles/main.css';
import { AccessGate } from './components/accessGate';
import { App } from './components/app';
import { byId } from './components/dom';
import { UpdateNotifier } from './components/updateNotifier';
import { renderVersion } from './services/version';

renderVersion(byId('app-version'));

// Shared-password gate: the board only starts once this browser is signed in
// (or immediately when no password is configured). A later 401 re-opens the
// sign-in dialog on top of whatever is open.
let gate: AccessGate | undefined;
const app = new App(document, {
  onUnauthorized: () => gate?.lock('Your sign-in has expired or the password was changed. Sign in again to continue.'),
});
gate = new AccessGate({
  dialog: byId<HTMLDialogElement>('access-dialog'),
  api: app.api,
  announcer: app.announcer,
  signOutButton: byId<HTMLButtonElement>('sign-out'),
  onUnlocked: (firstTime) => (firstTime ? app.start() : void app.refresh()),
});
void gate.init();

// Mounted once, globally: tells a long-open tab that a newer build is deployed.
new UpdateNotifier({ container: byId('update-notifier-root') }).start();
