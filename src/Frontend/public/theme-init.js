/*
 * Applies the saved theme before first paint to avoid a light/dark flash.
 * Kept as a tiny external file (not inline) so the strict
 * Content-Security-Policy (script-src 'self') still holds.
 * The full logic lives in src/components/themeSwitcher.ts.
 */
(function () {
  var pref = 'auto';
  try {
    var stored = window.localStorage.getItem('kanban_theme_preference');
    if (stored === 'light' || stored === 'dark' || stored === 'auto') pref = stored;
  } catch (e) { /* storage blocked: fall back to auto */ }
  var dark = pref === 'dark' ||
    (pref === 'auto' && window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches);
  var root = document.documentElement;
  root.classList.toggle('dark', dark);
  root.setAttribute('data-theme-preference', pref);
})();
