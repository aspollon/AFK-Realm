// The first pages: welcome, folder, server name, database password, summary - then the installation runs.
import { h, T, F, meta, post, get, replace, lines, debounce, state } from './core.js';
import { primary, secondary, ghost, input, password, check, field, choose, alert, confirm } from './ui.js';
import { icon } from './icons.js';
import { languageMenu, themeMenu, show } from './app.js';

const wiz = { path: '', name: 'AzerothCore', password: '', password2: '', installed: false, shortcut: true, kind: 'coa',
  clientMode: '', clientFolder: '', modules: [], preset: '', coaCustom: false, voices: false, hd: false, resume: false };

/** "Try again" after a failed first installation: the wizard once more, from the game client on, for the same folder. */
export function restartWizard(info) {
  Object.assign(wiz, { path: info.root, kind: info.kind, name: info.name || wiz.name, installed: false, resume: !!info.hasPassword });
  if (info.client && !wiz.clientFolder) { wiz.clientMode = 'have'; wiz.clientFolder = info.client; }
  if (info.preset) wiz.preset = info.preset;
  if (info.coaCustom) wiz.coaCustom = true;
  if (info.voices) wiz.voices = true;
  if (info.hd) wiz.hd = true;
  show('welcome', wiz.resume || wiz.password ? 'client' : 'password');
}

// Where the game clients come from when the user has none yet.
const clientLinks = {
  coa: { url: 'https://coa-development.org/download', name: 'CoA launcher' },
  azerothcore: { url: 'https://www.chromiecraft.com/en/downloads/', name: 'ChromieCraft' },
};

// The kinds of server AFK Realm can build, by their official names.
// group: 'standard' = the plain WotLK core, 'custom' = projects built on top of it (more follow there).
const kinds = () => [
  { id: 'coa', group: 'custom', name: 'Conquest of Azeroth', tags: [T('21 classes'), T('Modules by AFK Realm')], text: T('The Conquest of Azeroth core with its 21 classes, Playerbots and the CoA world data. All modules by AFK Realm work here. Needs the CoA game client.') },
  { id: 'esteria', group: 'custom', name: 'EsteriaWoW', tags: [T('64 races'), T('Eluna'), T('Playerbots')], text: T('EsteriaWoW with its new races, the third faction, Eluna and its own modules, Playerbots and Dungeon Clear among them, set up as its realm runs. Needs the EsteriaWoW game client.') },
  { id: 'azerothcore', group: 'standard', name: 'AzerothCore', tags: [T('WotLK 3.3.5a'), T('Playerbots'), T('Module catalog')], text: T('AzerothCore (WotLK 3.3.5a) with Playerbots and Dungeon Clear, the bots can be switched off. The modules by AFK Realm are made for CoA and are not offered; the AzerothCore module catalog is. Needs a normal 3.3.5a game client (build 12340).') },
];
const kindName = id => (kinds().find(k => k.id === id) || kinds()[0]).name;

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
  if (step === 'kind') return kindPage();
  if (step === 'name') return namePage();
  if (step === 'password') return passwordPage();
  if (step === 'client') return clientPage();
  if (step === 'extras') return extrasPage();
  if (step === 'summary') return summaryPage();
  const tile = (title, text, ic, onclick) => h('button.tile', { type: 'button', onclick }, h('span.ic', {}, icon(ic)), h('span', {}, h('b', {}, title), h('span', {}, text)));
  return page(T('Welcome'),
    h('p', {}, T('This program builds your own World of Warcraft server - Conquest of Azeroth, EsteriaWoW or AzerothCore - straight from the current source code. It downloads every tool it needs, compiles the server and sets up the database.')),
    h('p.hint', {}, meta.windows
      ? T('You need: Windows 10 or 11 (64-bit), about 40 GB of free disk space, an internet connection and some patience. Bring your own game client; it is not included.')
      : T('You need: a 64-bit Linux (Ubuntu, Debian, Fedora, Arch …), about 40 GB of free disk space, an internet connection and some patience. The build tools of the distribution are installed once with one command that AFK Realm shows. Bring your own game client; it is not included.')),
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
      show('welcome', wiz.installed ? 'password' : 'kind');
    })));
}

function kindPage() {
  const box = h('div');
  const groups = [
    ['standard', T('Standard'), T('The plain World of Warcraft 3.3.5a core, as the AzerothCore project publishes it.')],
    ['custom', T('Custom'), T('Projects built on AzerothCore with their own classes, races and content. They need their own game client.')],
  ];
  const tag = t => h('span', { style: { display: 'inline-block', fontSize: '11px', fontWeight: '600', padding: '1px 8px', marginRight: '6px', marginTop: '6px',
    borderRadius: '999px', background: 'var(--accent-soft)', color: 'var(--accent-text)' } }, t);
  const tile = k => h('button.tile' + (wiz.kind === k.id ? '.on' : ''), {
    type: 'button', 'aria-pressed': wiz.kind === k.id ? 'true' : 'false',
    style: wiz.kind === k.id ? { outline: '2px solid var(--accent)', outlineOffset: '-2px' } : {},
    onclick: () => { wiz.kind = k.id; paint(); }
  }, h('span.ic', {}, icon(wiz.kind === k.id ? 'check' : 'server')),
    h('span', {}, h('b', {}, k.name), h('span', {}, k.text), h('span', { style: { display: 'block' } }, ...k.tags.map(tag))));
  const paint = () => replace(box, ...groups.map(([id, title, text]) => h('section', { style: { marginTop: '14px' } },
    h('div', { style: { fontSize: '12px', fontWeight: '700', letterSpacing: '.08em', textTransform: 'uppercase', color: 'var(--muted)' } }, title),
    h('div.small.muted', { style: { margin: '2px 0 8px' } }, text),
    ...kinds().filter(k => k.group === id).map(tile))));
  paint();
  return page(T('Which server?'),
    h('p', {}, T('Choose the kind of server. It is set once for this server folder; for the other kind, install a second server in another folder.')),
    box,
    footer(secondary(T('Back'), () => show('welcome', 'folder')), primary(T('Next'), () => show('welcome', 'name'))));
}

function namePage() {
  const name = input(wiz.name, { maxLength: 32, style: { width: '300px' } });
  name.addEventListener('input', () => { wiz.name = name.value; });
  return page(T('Name your server'),
    h('p', {}, T('This is the name players see in the realm list when they log in. You can change it later in the server management.')),
    field(T('Server name'), name),
    h('p.hint', {}, T('Up to 32 characters: English letters, digits, spaces, hyphen, apostrophe and dot. It has to start with a letter or digit.')),
    footer(secondary(T('Back'), () => show('welcome', 'kind')), primary(T('Next'), async () => {
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
      show('welcome', wiz.installed ? 'summary' : 'client');
    })));
}

// The game client: there already (checked against the server kind), downloaded first by AFK Realm (CoA), or later.
export function clientChooser(kind, initial, onChange) {
  const st = Object.assign({ mode: '', folder: '' }, initial || {});
  const box = h('div');
  const line = h('div.hint.pre');
  const tile = (mode, title, text, ic) => h('button.tile' + (st.mode === mode ? '.on' : ''), {
    type: 'button', style: st.mode === mode ? { outline: '2px solid var(--accent)', outlineOffset: '-2px' } : {},
    onclick: () => { st.mode = mode; line.textContent = ''; line.className = 'hint pre'; paint(); onChange(st); }
  }, h('span.ic', {}, icon(st.mode === mode ? 'check' : ic)), h('span', {}, h('b', {}, title), h('span', {}, text)));
  const check = async () => {
    if (!st.folder) return false;
    try {
      const r = await post('/api/setup/client', { kind, mode: st.mode, folder: st.folder });
      st.folder = r.folder; line.className = 'hint pre'; line.textContent = st.mode === 'download' ? F('The game client will be downloaded into {0} before the server is built (about 43 GB).', r.folder) : F('{0} fits this server.', r.folder);
      onChange(st); return true;
    } catch (e) { line.className = 'hint pre bad'; line.textContent = e.message; return false; }
  };
  const folderRow = (title) => {
    const path = input(st.folder, { style: { width: '100%' }, spellcheck: false });
    path.addEventListener('change', () => { st.folder = path.value.trim(); check(); });
    const browse = secondary(T('Browse …'), async () => {
      const p = await choose({ title, start: st.folder || undefined });
      if (p) { path.value = st.folder = p; check(); }
    }, 'folder');
    return h('div', { style: { marginTop: '10px' } }, h('div.row.nowrap', {}, path, browse), line);
  };
  const link = clientLinks[kind];
  const paint = () => replace(box,
    tile('have', T('Yes, I have it'), T('Choose its folder. AFK Realm checks that it is the right client for this server.'), 'folder'),
    st.mode === 'have' ? folderRow(T('Choose the folder of your game client (the one with the "Data" folder)')) : null,
    tile('download', T('No, I still need it'), kind === 'coa'
      ? T('AFK Realm can download the CoA client for you before it builds the server, or you get it from the official launcher.')
      : T('Get it first; AFK Realm cannot download this client.'), 'download'),
    st.mode === 'download' ? h('div', { style: { margin: '10px 0 0 4px' } },
      kind === 'coa' ? h('p', {}, T('Download it with AFK Realm into this folder:')) : null,
      kind === 'coa' ? folderRow(T('Choose a folder for the game client')) : null,
      link ? h('p.hint', {}, F('Or get it from the official {0}:', link.name) + ' ', h('a', { href: link.url, target: '_blank', rel: 'noopener' }, link.url),
        kind === 'coa' ? ' ' + T('(then choose "Yes, I have it" once it is installed)') : '')
        : h('p.hint', {}, T('The EsteriaWoW client comes from the EsteriaWoW project (see its Discord). Install it, then choose "Yes, I have it".'))) : null,
    tile('later', T('Later'), T('Only the server is built now. Create the map data from your client afterwards in the server management.'), 'info'));
  paint();
  return { el: box, check, state: st };
}

function clientPage() {
  const chooser = clientChooser(wiz.kind, { mode: wiz.clientMode, folder: wiz.clientFolder }, st => { wiz.clientMode = st.mode; wiz.clientFolder = st.folder; });
  return page(T('Your game client'),
    h('p', {}, F('The server needs data from the game client ({0}): maps and data tables. Do you already have the client on this PC?', kindName(wiz.kind))),
    h('p.hint', {}, T('With a client, AFK Realm creates the map data by itself right after the build - nothing left to do by hand.')),
    chooser.el,
    footer(secondary(T('Back'), () => show('welcome', wiz.resume ? 'folder' : 'password')), primary(T('Next'), async () => {
      const st = chooser.state;
      if (!st.mode) return alert(T('Please choose one of the three.'));
      if (st.mode === 'download' && wiz.kind !== 'coa') { wiz.clientMode = 'later'; wiz.clientFolder = ''; }
      else if (st.mode !== 'later') {
        if (!st.folder) return alert(T('Please choose the folder.'));
        if (!await chooser.check()) return;
      }
      show('welcome', wiz.kind === 'coa' ? 'extras' : 'summary');
    })));
}

function extrasPage() {
  const box = h('div', {}, h('p.hint', {}, T('Loading …')));
  post('/api/setup/extras', { kind: wiz.kind }).then(r => {
    const preset = r.presets[0];
    const boxes = [];
    const presetCheck = preset ? check(preset.name, wiz.preset === preset.id, v => {
      wiz.preset = v ? preset.id : '';
      if (v) for (const [url, c] of boxes) if (preset.urls.includes(url)) { c.querySelector('input').checked = true; if (!wiz.modules.includes(url)) wiz.modules.push(url); }
    }) : null;
    const list = r.modules.map(m => {
      const c = check(m.name, wiz.modules.includes(m.url), v => { wiz.modules = v ? [...new Set([...wiz.modules, m.url])] : wiz.modules.filter(u => u !== m.url); });
      boxes.push([m.url, c]);
      return h('div', { style: { margin: '6px 0' } }, c, h('div.small.muted', { style: { marginLeft: '26px' } }, m.text));
    });
    const coa = check(T('CoA Custom (the custom races)'), wiz.coaCustom, v => { wiz.coaCustom = v; });
    replace(box,
      h('section', {}, h('h3', {}, T('CoA Custom')), coa,
        h('div.small.muted', { style: { marginLeft: '26px' } }, T('Dracthyr, Worgen, Vulpera, Sethrak, High elves and many more races. AFK Realm downloads the package, builds the modules it needs in with the server and installs it into the server and your game client afterwards (that part needs a game client).'))),
      h('section', { style: { marginTop: '18px' } }, h('h3', {}, T('For your game client')),
        check(T('Voices (quests read aloud, bots speak in chat)'), wiz.voices, v => { wiz.voices = v; }),
        h('div.small.muted', { style: { marginLeft: '26px' } }, T('AFK Realm downloads and installs them into your game client after the build. Needs a game client.')),
        check(T('HD pack (fueryin\'s Ultra-HD graphics)'), wiz.hd, v => { wiz.hd = v; }),
        h('div.small.muted', { style: { marginLeft: '26px' } }, T('Needs CoA Custom and a good graphics card. The pack is on Google Drive, so you download it yourself: open its page now and save the ZIP into your Downloads folder while the server builds. AFK Realm finds it, unpacks it and installs it afterwards.'), ' ',
          h('a', { href: 'https://github.com/fueryin/Conquest-of-Azeroth-Ultra-HD-Graphics-Modpack/tree/CoA-HD-PATCH', target: '_blank', rel: 'noopener' }, T('Open the download page of the HD pack')))),
      preset ? h('section', { style: { marginTop: '18px' } }, h('h3', {}, T('Preset')), presetCheck, h('div.small.muted', { style: { marginLeft: '26px' } }, preset.text)) : null,
      h('h3', { style: { marginTop: '18px' } }, T('Modules by AFK Realm')), ...list,
      h('p.hint', { style: { marginTop: '14px' } }, T('Modules from the AzerothCore catalog can be added later in the server management, and so can the voices and the HD pack.')));
  }).catch(e => replace(box, h('p.bad', {}, e.message)));
  return page(T('Modules'),
    h('p', {}, T('Choose what is built in right away. Everything chosen here is compiled with the server in one go; adding modules later means building again.')),
    box,
    footer(secondary(T('Back'), () => show('welcome', 'client')), primary(T('Next'), () => show('welcome', 'summary'))));
}

const phaseTitles = () => {
  const world = wiz.kind === 'coa' || wiz.installed ? [T('Import the CoA world data')] : [];
  const first = !wiz.installed && wiz.clientMode === 'download' ? [T('Download the game client')] : [];
  const mods = !wiz.installed && wiz.modules.length ? [F('Download the chosen modules ({0})', wiz.modules.length)] : [];
  const last = [...(!wiz.installed && wiz.coaCustom ? [T('Install CoA Custom into the server and the game client')] : []),
    ...(!wiz.installed && wiz.clientMode && wiz.clientMode !== 'later' ? [T('Create the map data from the game client')] : []),
    ...(!wiz.installed && wiz.voices ? [T('Install the voices into the game client')] : []),
    ...(!wiz.installed && wiz.hd ? [T('Unpack and install the HD pack')] : [])];
  if (!wiz.installed && wiz.coaCustom) first.push(T('Download CoA Custom'));
  return meta.windows
    ? [...first, T('Check tools (Git, CMake)'), T('Visual Studio C++ Build Tools'), T('Libraries (OpenSSL, Boost)'), T('Portable MySQL database'), T('Download the server source code'), ...mods, T('Prepare the build (CMake)'), T('Compile the server'), T('Set up the database'), ...world, T('Configuration and final checks'), ...last]
    : [...first, T('Check tools (Git, CMake)'), T('Compiler and libraries'), T('Portable MySQL database'), T('Download the server source code'), ...mods, T('Prepare the build (CMake)'), T('Compile the server'), T('Set up the database'), ...world, T('Configuration and final checks'), ...last];
};

function summaryPage() {
  const shortcut = check(meta.windows ? F('Create a "{0}" shortcut on the desktop', 'AFK Realm') : F('Add "{0}" to the application menu', 'AFK Realm'), wiz.shortcut, v => wiz.shortcut = v);
  const understood = check(T('I understand the installation may take a long time, depending on my PC.'), false);
  return page(T('Ready to install'),
    h('p', {}, T('Target:') + ' ', h('span.mono', {}, wiz.path), wiz.installed ? null : [h('br'), T('Server:') + ' ' + kindName(wiz.kind), h('br'), T('Server name:') + ' ' + wiz.name,
      h('br'), T('Game client:') + ' ' + (wiz.clientMode === 'have' ? wiz.clientFolder : wiz.clientMode === 'download' ? F('download into {0}', wiz.clientFolder) : T('later')),
      wiz.modules.length ? [h('br'), T('Modules:') + ' ' + wiz.modules.map(u => u.replace(/^.*\/|\.git$/g, '')).join(', ')] : null,
      wiz.coaCustom ? [h('br'), T('CoA Custom: yes')] : null,
      wiz.voices || wiz.hd ? [h('br'), T('For the game client:') + ' ' + [wiz.voices ? T('voices') : '', wiz.hd ? T('HD pack') : ''].filter(Boolean).join(', ')] : null]),
    h('h3', {}, T('What happens now')),
    h('ul', {}, phaseTitles().map(t => h('li', {}, t))),
    h('p.hint', {}, meta.windows
      ? T('If Visual Studio is missing, its installer window opens along the way. That is normal; please do not close it. Compiling puts a heavy load on your PC. Close games and other large programs if you can.')
      : T('If build tools of your distribution are missing, AFK Realm stops and shows the one command that installs them (it needs sudo once). Compiling puts a heavy load on your PC.')),
    shortcut, understood,
    footer(secondary(T('Back'), () => show('welcome', wiz.installed ? 'password' : wiz.kind === 'coa' ? 'extras' : 'client')), primary(T('Install'), async () => {
      if (!understood.checked) return alert(T('Please confirm that you have the time.'));
      try {
        const job = await post('/api/setup/install', { path: wiz.path, name: wiz.name, password: wiz.password, password2: wiz.password2, shortcut: wiz.shortcut, kind: wiz.kind,
          clientMode: wiz.clientMode, clientFolder: wiz.clientFolder, modules: wiz.modules, preset: wiz.preset, coaCustom: wiz.coaCustom, voices: wiz.voices, hd: wiz.hd });
        state.job = job;
        show('progress');
      } catch (e) { alert(e.message); }
    })));
}
