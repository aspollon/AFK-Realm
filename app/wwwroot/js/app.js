// AFK Realm: which page is shown, the language and the look.
import { meta, get, post, connect, on, state, h, T, replace } from './core.js';
import { welcome } from './welcome.js';
import { progress } from './progress.js';
import { manager } from './manager.js';
import { menu, alert, prompt, toast } from './ui.js';

const root = document.getElementById('app');
export const view = { name: null, close: null };

async function loadMeta() {
  const m = await get('/api/meta');
  Object.assign(meta, m);
  document.documentElement.lang = meta.language || 'en';
  applyTheme();
  if (meta.font) { document.body.style.setProperty('--lang-font', '"' + meta.font + '"'); document.body.classList.add('lang-font'); }
  document.title = meta.product;
}

const dark = window.matchMedia('(prefers-color-scheme: dark)');
export function applyTheme() {
  const want = meta.theme === 'dark' || (meta.theme !== 'light' && dark.matches) ? 'dark' : 'light';
  document.documentElement.dataset.theme = want;
}
dark.addEventListener('change', applyTheme);

/** Shows a page: 'welcome' (with a step), 'progress' or 'manager'. */
export function show(name, arg) {
  if (view.close) { try { view.close(); } catch { } }
  view.name = name; view.close = null;
  const el = name === 'progress' ? progress(arg) : name === 'manager' ? manager(arg) : welcome(arg);
  if (el && el.close) view.close = el.close;
  replace(root, el);
  window.scrollTo(0, 0);
}

/** The page that fits the state: a running (or just finished) engine task, the open server, or the welcome. */
export function route() {
  const j = state.job;
  if (j && j.kind === 'engine' && (j.running || view.name === 'progress' || view.name === null)) return show('progress');
  if (state.status && state.status.root) return show('manager');
  return show('welcome');
}

// ---- the language and the look, from every page ----

export function languageMenu(at) {
  const items = [{ text: T('Automatic (as the system)'), check: meta.languageChoice === 'auto', onclick: () => setLanguage('auto') }, 'sep'];
  for (const l of meta.languages) items.push({ text: l.name + (l.english && l.english !== l.name ? '  ·  ' + l.english : ''), check: meta.languageChoice === l.code, onclick: () => setLanguage(l.code) });
  items.push('sep', { text: T('Add a language …'), onclick: addLanguage });
  menu(at, items);
}

async function setLanguage(code) {
  try {
    const m = await post('/api/language', { code });
    Object.assign(meta, m);
    location.reload();
  } catch (e) { alert(e.message); }
}

async function addLanguage() {
  const code = await prompt(T('Code of the language (for example ja, ko, it, pl, tr):'), '', { title: T('Add a language') });
  if (!code) return;
  try {
    const r = await post('/api/language/add', { code: code.trim() });
    await alert(T('A file with every text of AFK Realm was written. Fill in the translations, then restart AFK Realm:') + '\n\n' + r.file);
  } catch (e) { alert(e.message); }
}

export function themeMenu(at) {
  const sys = dark.matches ? T('Dark') : T('Light');
  menu(at, [
    { text: T('Automatic (as the system)') + '  ·  ' + sys, check: meta.theme !== 'light' && meta.theme !== 'dark', onclick: () => setTheme('auto') },
    { text: T('Light'), check: meta.theme === 'light', onclick: () => setTheme('light') },
    { text: T('Dark'), check: meta.theme === 'dark', onclick: () => setTheme('dark') },
  ]);
}

async function setTheme(choice) {
  meta.theme = choice; applyTheme();
  try { await post('/api/theme', { choice }); } catch { }
}

// ---- start ----

(async () => {
  try {
    await loadMeta();
    const s = await get('/api/state');
    state.status = s.status; state.job = s.job;
    connect();
    on('status', st => {
      // The server was opened or closed elsewhere (another page, the command line).
      const open = !!(st && st.root);
      if (view.name === 'manager' && !open) route();
      else if (view.name === 'welcome' && open && !(state.job && state.job.running)) route();
    });
    on('job', j => {
      if (j && j.kind === 'engine' && j.running && view.name !== 'progress') show('progress');
    });
    on('alert', a => alert(a.text, a.title));
    route();
    if (meta.justUpdated) toast(meta.justUpdated, 6000);
  } catch (e) {
    console.error(e && e.stack); replace(root, h('div.boot', {}, e.message || String(e)));
  }
})();
