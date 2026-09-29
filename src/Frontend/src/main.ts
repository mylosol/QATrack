/**
 * QATrack board SPA entry point.
 */
import './styles/main.css';
import { App } from './components/app';
import { byId } from './components/dom';
import { UpdateNotifier } from './components/updateNotifier';
import { renderVersion } from './services/version';

renderVersion(byId('app-version'));

const app = new App(document);
app.start();

// Mounted once, globally: tells a long-open tab that a newer build is deployed.
new UpdateNotifier({ container: byId('update-notifier-root') }).start();
