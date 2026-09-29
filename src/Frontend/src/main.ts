/**
 * QATrack board SPA entry point.
 */
import './styles/main.css';
import { App } from './components/app';
import { byId } from './components/dom';
import { renderVersion } from './services/version';

renderVersion(byId('app-version'));

const app = new App(document);
app.start();
