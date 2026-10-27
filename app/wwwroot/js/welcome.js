// The first pages: welcome, folder, server name, database password, summary - then the installation runs.
import { h, T, F, meta, post, get, replace, lines, debounce, state } from './core.js';
import { primary, secondary, ghost, input, password, check, field, choose, alert, confirm } from './ui.js';
import { icon } from './icons.js';
import { languageMenu, themeMenu, show } from './app.js';

const wiz = { path: '', name: 'AzerothCore', password: '', password2: '', installed: false, shortcut: true };

export function header() {
  const lang = h('button', { type: 'button', title: T('Language') }, icon('language'), (meta.languages.find(l => l.code === meta.language) || { name: 'English' }).name);
  lang.addEventListener('click', () => languageMenu(lang));
  const theme = h('button', { type: 'button', title: T('Theme') }, icon(document.documentElement.dataset.theme === 'dark' ? 'moon' : 'sun'));
  theme.addEventListener('click', () => themeMenu(theme));
  return h('header', {}, h('img', { src: '/logo.png', alt: '' }),
    h('div', {}, h('div.t', {}, meta.product), h('div.s', {}, meta.tagline)),
    h('div.spacer'), h('div.headtools', {}, lang, theme));
}

function page(title, ...content) {
  return h('div.wizard', {}, header(), h('div.sheet', {}, h('h1', {}, title), ...content));
}

function footer(...buttons) { return h('footer', {}, ...buttons); }

export function welcome(step) {
  if (step === 'folder') return folderPage();
  if (step === 'name') return namePage();
  if (step === 'password') return passwordPage();
  if (step === 'summary') return summaryPage();
  const tile = (title, text, ic, onclick) => h('button.tile', { type: 'button', onclick }, h('span.ic', {}, icon(ic)), h('span', {}, h('b', {}, title), h('span', {}, text)));
  return page(T('Welcome'),
    h('p', {}, T('This program builds your own Conquest of Azeroth server with Playerbots, straight from the current source code. It downloads every tool it needs, compiles the server and sets up the database including the CoA world data.')),
    h('p.hint', {}, meta.windows
      ? T('You need: Windows 10 or 11 (64-bit), about 40 GB of free disk space, an internet connection and some patience. Bring your own CoA game client; it is not included.')
      : T('You need: a 64-bit Linux (Ubuntu, Debian, Fedora, Arch …), about 40 GB of free disk space, an internet connection and some patience. The build tools of the distribution are installed once with one command that AFK Realm shows. Bring your own CoA game client; it is not included.')),
    tile(T('Install a new server'), T('Set everything up from scratch.'), 'server', () => show('welcome', 'folder')),
    tile(T('Manage an existing server'), T('Start, stop, updates, accounts, map data …'), 'settings', openExisting),
    knownServers());
}

function knownServers() {
  const box = h('div');
  get('/api/servers').then(r => {
    const list = r.list.filter(s => s.installed);
    if (!list.length) return;
    replace(box, h('h3', { style: { marginTop: '22px' } }, T('Your servers')),
      ...list.map(s => h('button.tile', { type: 'button', onclick: () => open(s.root) }, h('span.ic', {}, icon('server')), h('span', {}, h('b', {}, s.name), h('span.mono', {}, s.root)))));
  }).catch(() => { });
  return box;
}

async function open(root) {
  try { await post('/api/servers/open', { root }); show('manager'); }
  catch (e) { alert(e.message); }
}

async function openExisting() {
  const root = await choose({ title: T('Choose your server folder (the one containing "Server", "DB" and "Dependencies").'), newFolder: false });
  if (!root) return;
  open(root);
}

function folderPage() {
  const path = input(wiz.path, { style: { width: '100%' }, spellcheck: false });
  const checkLine = h('div.hint.pre');
  let lastError = null;
  const evaluate = async () => {
    try {
      const r = await post('/api/setup/check', { path: path.value });
      lastError = r.error; wiz.installed = r.installed;
      checkLine.className = 'hint pre ' + (r.error ? 'bad' : '');
      checkLine.textContent = r.error || r.notes.join('\n');
    } catch (e) { checkLine.textContent = e.message; }
  };
  const later = debounce(evaluate, 300);
  path.addEventListener('input', () => { wiz.path = path.value; later(); });
  if (!wiz.path) get('/api/setup/suggest').then(r => { wiz.path = path.value = r.path; evaluate(); }); else evaluate();
  const browse = secondary(T('Browse …'), async () => {
    const p = await choose({ title: T('Choose a folder for the server'), start: path.value.replace(/[\\/][^\\/]*$/, '') });
    if (p) { path.value = wiz.path = p; evaluate(); }
  }, 'folder');
  return page(T('Where should the server go?'),
    h('p', {}, meta.windows
      ? T('Everything goes into this folder: tools, source code, database and the finished server. Best is a short path without special characters, e.g. C:\\CoA-Server.')
      : T('Everything goes into this folder: tools, source code, database and the finished server. Best is a folder in your home folder without spaces or special characters, e.g. ~/CoA-Server.')),
    h('div.row.nowrap', {}, path, browse), checkLine,
    footer(secondary(T('Back'), () => show('welcome')), primary(T('Next'), async () => {
      await evaluate();
      if (lastError) return alert(lastError);
      if (wiz.installed && !await confirm(T('A server is already installed here. Recompile it completely?\n\nDatabase, characters and map data are kept.'))) return;
      show('welcome', wiz.installed ? 'password' : 'name');
    })));
}

function namePage() {
  const name = input(wiz.name, { maxLength: 32, style: { width: '300px' } });
  name.addEventListener('input', () => { wiz.name = name.value; });
  return page(T('Name your server'),
    h('p', {}, T('This is the name players see in the realm list when they log in. You can change it later in the server management.')),
    field(T('Server name'), name),
    h('p.hint', {}, T('Up to 32 characters: English letters, digits, spaces, hyphen, apostrophe and dot. It has to start with a letter or digit.')),
    footer(secondary(T('Back'), () => show('welcome', 'folder')), primary(T('Next'), async () => {
      const r = await post('/api/setup/name', { name: name.value.trim() });
      if (r.error) return alert(r.error);
      wiz.name = name.value.trim();
      show('welcome', 'password');
    })));
}

function passwordPage() {
  const p1 = password({ value: wiz.password, style: { width: '300px' } }), p2 = password({ value: wiz.password2, style: { width: '300px' } });
  const showIt = check(T('Show password'), false, v => { p1.type = p2.type = v ? 'text' : 'password'; });
  p1.addEventListener('input', () => wiz.password = p1.value);
  p2.addEventListener('input', () => wiz.password2 = p2.value);
  return page(T('Database password'),
    h('p', {}, F('The server gets its own private MySQL database. Choose a password for it. You will not need it day to day; {0} keeps it in the server settings.', meta.product)),
    field(T('Password'), p1), field(T('Repeat'), p2), showIt,
    h('p.hint', {}, T('At least 10 characters. Not allowed: semicolon ; quote " and backslash \\ (AzerothCore cannot handle them in its configuration).')),
    footer(secondary(T('Back'), () => show('welcome', wiz.installed ? 'folder' : 'name')), primary(T('Next'), () => {
      const a = p1.value, b = p2.value;
      if (a.length < 10) return alert(T('The password needs at least 10 characters.'));
      if (/[;"\\\r\n]/.test(a)) return alert(T('Please do not use a semicolon, quote or backslash.'));
      if (a !== b) return alert(T('The two passwords do not match.'));
      show('welcome', 'summary');
    })));
}

const phaseTitles = () => meta.windows
  ? [T('Check tools (Git, CMake)'), T('Visual Studio C++ Build Tools'), T('Libraries (OpenSSL, Boost)'), T('Portable MySQL database'), T('Download the server source code'), T('Prepare the build (CMake)'), T('Compile the server'), T('Set up the database'), T('Import the CoA world data'), T('Configuration and final checks')]
  : [T('Check tools (Git, CMake)'), T('Compiler and libraries'), T('Portable MySQL database'), T('Download the server source code'), T('Prepare the build (CMake)'), T('Compile the server'), T('Set up the database'), T('Import the CoA world data'), T('Configuration and final checks')];

function summaryPage() {
  const shortcut = check(meta.windows ? F('Create a "{0}" shortcut on the desktop', 'AFK Realm') : F('Add "{0}" to the application menu', 'AFK Realm'), wiz.shortcut, v => wiz.shortcut = v);
  const understood = check(T('I understand the installation may take a long time, depending on my PC.'), false);
  return page(T('Ready to install'),
    h('p', {}, T('Target:') + ' ', h('span.mono', {}, wiz.path), wiz.installed ? null : [h('br'), T('Server name:') + ' ' + wiz.name]),
    h('h3', {}, T('What happens now')),
    h('ul', {}, phaseTitles().map(t => h('li', {}, t))),
    h('p.hint', {}, meta.windows
      ? T('If Visual Studio is missing, its installer window opens along the way. That is normal; please do not close it. Compiling puts a heavy load on your PC. Close games and other large programs if you can.')
      : T('If build tools of your distribution are missing, AFK Realm stops and shows the one command that installs them (it needs sudo once). Compiling puts a heavy load on your PC.')),
    shortcut, understood,
    footer(secondary(T('Back'), () => show('welcome', 'password')), primary(T('Install'), async () => {
      if (!understood.checked) return alert(T('Please confirm that you have the time.'));
      try {
        const job = await post('/api/setup/install', { path: wiz.path, name: wiz.name, password: wiz.password, password2: wiz.password2, shortcut: wiz.shortcut });
        state.job = job;
        show('progress');
      } catch (e) { alert(e.message); }
    })));
}
