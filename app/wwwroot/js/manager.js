// The server management: side bar, the bar with the state of the server, and its sections.
import { h, T, F, meta, post, get, replace, clear, lines, on, state } from './core.js';
import { primary, secondary, ghost, card, field, input, password, select, toggle, confirm, alert, act, runTask, menu, choose, check, dialog, note, setNote, toast } from './ui.js';
import { icon } from './icons.js';
import { show, languageMenu, themeMenu } from './app.js';
import { helpButton } from './help.js';

const sections = [
  ['server', () => T('Server'), 'server'],
  ['bots', () => T('Bots'), 'bots'],
  ['settings', () => T('Settings'), 'settings'],
  ['modules', () => T('Modules'), 'modules'],
  ['gm', () => T('Game master'), 'gamemaster'],
  ['accounts', () => T('Accounts'), 'accounts'],
  ['database', () => T('Database'), 'database'],
  ['maintenance', () => T('Maintenance'), 'maintenance'],
];

let current = sessionStorage.getItem('afk-section') || 'server';

export function manager() {
  const nav = h('div.nav');
  const content = h('div.content');
  const pickerName = h('span'), pickerPath = h('div.path');
  const picker = h('button.picker', { type: 'button', onclick: () => serverMenu(picker) }, h('div.name', {}, pickerName, icon('chevron')), pickerPath);
  const lang = h('button', { type: 'button', onclick: () => languageMenu(lang) }, icon('language'), (meta.languages.find(l => l.code === meta.language) || { name: 'English' }).name);
  const theme = h('button', { type: 'button', title: T('Theme'), onclick: () => themeMenu(theme) }, icon('sun'));
  const sidebar = h('aside.sidebar', {}, picker, nav,
    h('div.sidefoot', {}, helpButton(), h('div.tools', {}, lang, theme), h('div', {}, meta.product + ' ' + meta.version)));

  // the bar at the top
  const pills = { db: pill(T('Database')), auth: pill(T('Login (authserver)')), world: pill(T('Game world (worldserver)')) };
  const startBtn = primary(T('Start server'), () => startServer(), 'play');
  const stopBtn = secondary(T('Stop server'), async () => {
    if (!await confirm(T('Stop the server now? All players are saved and logged out.'))) return;
    runTask(() => post('/api/server/stop'));
  }, 'stop');
  const restartBtn = secondary(T('Restart server'), async () => {
    if (!await confirm(T('Restart the server now? All players are saved and logged out; they can log in again once the world has loaded.'))) return;
    runTask(() => post('/api/server/restart'));
  }, 'restart');
  const consolesBtn = secondary(T('Open server consoles …'), () => import('./consoles.js').then(m => m.consoles()), 'console');
  const uptime = h('div.hint', { style: { margin: '6px 0 0' } });
  const notice = h('div.notice.pre');
  const banners = h('div');
  const bar = h('div.statusbar', {}, h('div.pills', {}, pills.db.el, pills.auth.el, pills.world.el),
    h('div.row', { style: { marginTop: '12px' } }, startBtn, stopBtn, restartBtn, consolesBtn), uptime, notice, banners);

  const main = h('div.main', {}, bar, content);
  const el = h('div.frame', {}, sidebar, main);

  let offerShown = false, sectionEls = {};
  const s = () => state.status || {};

  function drawNav() {
    clear(nav);
    const badges = s().badges || {};
    for (const [key, name, ic] of sections) {
      nav.appendChild(h('button', { type: 'button', class: key === current ? 'active' : '', onclick: () => selectSection(key) },
        icon(ic), h('span', {}, name()), badges[key] ? h('span.badge', {}, badges[key]) : null));
    }
  }

  function selectSection(key) {
    current = key;
    sessionStorage.setItem('afk-section', key);
    drawNav();
    const name = sections.find(x => x[0] === key)[1]();
    replace(content, h('h1', {}, name), ...build(key));
    content.scrollTop = 0;
  }

  function build(key) {
    sectionEls = {};
    switch (key) {
      case 'server': return serverSection();
      case 'bots': { const box = h('div'); import('./bots.js').then(m => replace(box, m.dashboard())); return [box]; }
      case 'settings': return settingsSection();
      case 'modules': return modulesSection();
      case 'gm': return [card(T('Game master tools'), T('Help players on the running server: give, complete and reward quests (found by NPC name or around a character), unstuck, revive, level, gold and mail, announcements, and a console for every other GM command.'), 'gamemaster',
        primary(T('Open game master tools …'), () => { if (!s().db) return alert(T('The database is not running. Start the server first.')); import('./gm.js').then(m => m.gameMaster()); }))];
      case 'accounts': return accountsSection();
      case 'database': return [
        card(T('Database editor'), T('Look into the server\'s tables, change values and run SQL without a separate program. Read-only until you allow changes; meant for people who know what the tables are for - a wrong change can break the server, so back it up first.'), 'database',
          primary(T('Open the database editor …'), () => { if (!s().db) return alert(T('The database is not running. Start the server first.')); import('./database.js').then(m => m.databaseEditor()); })),
        card(T('Backups'), F('Before every update and module change {0} saves the whole server: programs, settings, all databases. Restore one when something went wrong.', meta.product), 'database',
          secondary(T('Backups …'), () => import('./backups.js').then(m => m.backups()))),
      ];
      case 'maintenance': return maintenanceSection();
    }
    return [];
  }

  // ---- server ----
  function serverSection() {
    const st = s();
    const plan = st.plan || { mode: 0, hours: 6, daily: '05:00', memory: 8, warn: 10 };
    const mode = select([[0, T('Off')], [1, T('After the server has run for')], [2, T('Every day at')], [3, T('When the worldserver uses more than')]], plan.mode, () => fields());
    const value = input('', { style: { width: '90px' } });
    const unit = h('span');
    const warn = input(String(plan.warn), { style: { width: '60px' } });
    const planState = h('div.hint.pre');
    const postpone = secondary(T('Postpone by an hour'), async () => { try { await post('/api/plan/postpone'); } catch (e) { alert(e.message); } });
    sectionEls.planState = planState; sectionEls.postpone = postpone;
    function fields() {
      const m = +mode.value;
      value.disabled = warn.disabled = m === 0;
      value.value = m === 1 ? String(plan.hours) : m === 2 ? plan.daily : m === 3 ? String(plan.memory) : '';
      unit.textContent = m === 1 ? T('hours') : m === 2 ? T('(24-hour clock)') : m === 3 ? T('GB of memory') : '';
    }
    fields();
    const apply = secondary(T('Apply'), async () => {
      try { await post('/api/plan', { mode: +mode.value, value: value.value, warn: warn.value }); toast(T('Saved.')); }
      catch (e) { alert(e.message); }
    });
    const mapState = h('div');
    sectionEls.mapState = mapState;
    const out = [
      meta.windows ? null : card(T('Linux: faster databases'), T('On Linux, the database and world server often run noticeably faster with jemalloc, and Redis helps with caching. Install them once; this is a recommendation, not a requirement.'), 'server',
        h('pre.small', {}, 'Ubuntu / Debian:\nsudo apt update && sudo apt install libjemalloc2 redis-server\n(optional build headers: sudo apt install build-essential libjemalloc-dev)\n\nCentOS / RHEL / Rocky Linux:\nsudo dnf install epel-release\nsudo dnf install redis\nsudo dnf install jemalloc')),
      card(T('Scheduled restart'), F('A world that has run for many hours can grow slow and use more and more memory; a restart gives it back. {0} can do that on its own: players are told in the game beforehand, then the server is stopped cleanly and started again. This works while the server runs and {0} stays open.', meta.product), 'server',
        h('div.row', {}, mode, value, unit),
        h('div.row', {}, h('span', {}, T('Tell players')), warn, h('span', {}, T('minutes before')), apply),
        planState, postpone),
      card(T('Map data'), T('The worldserver needs the maps, vmaps and mmaps of the game and CoA\'s own DBC tables. They are made once from your game client.'), 'globe',
        mapState, secondary(T('Create map data from the game client …'), mapDataDialog)),
    ];
    paintServer();
    return out;
  }

  function paintServer() {
    const st = s();
    if (sectionEls.planState) {
      sectionEls.planState.textContent = st.planState || '';
      sectionEls.planState.className = 'hint pre ' + (st.planWarn ? 'warn' : '');
      sectionEls.postpone.classList.toggle('hidden', !st.postpone);
    }
    if (sectionEls.mapState) {
      sectionEls.mapState.textContent = st.mapText || '';
      sectionEls.mapState.className = 'hint ' + (st.maps && st.dbc ? 'ok' : 'warn');
    }
  }

  async function mapDataDialog() {
    const st = s();
    const folder = input('', { style: { flex: '1' } });
    get('/api/mapdata/client').then(r => { if (r.folder && !folder.value) folder.value = r.folder; }).catch(() => { });
    const mmaps = check(T('Also create mmaps (pathfinding for bots and creatures, highly recommended; may take a long time)'), true);
    const dbcOnly = check(T('Only refresh the CoA DBC tables (for a server that already has maps)'), false, v => { mmaps.input.disabled = v; });
    const r = await dialog({
      title: T('Create map data'), icon: 'globe',
      body: h('div', {}, h('p', {}, T('Choose the folder of your CoA game client (the one with the "Data" folder). When the extraction finishes, the data is moved into the server automatically. Keep the game and its launcher closed meanwhile.')),
        h('div.row.nowrap', {}, folder, secondary(T('Browse …'), async () => { const p = await choose({ title: T('Choose the game folder (it contains the "Data" folder)'), start: folder.value, newFolder: false }); if (p) folder.value = p; }, 'folder')),
        mmaps, st.needsClientDbc ? dbcOnly : null),
      buttons: [{ text: T('Cancel'), value: null }, { text: T('Start extraction'), kind: 'primary', value: true }]
    });
    if (!r) return;
    const job = await runTaskWithLog(() => post('/api/mapdata', { folder: folder.value, mmaps: mmaps.checked, dbcOnly: dbcOnly.checked }), T('Create map data'));
    if (job && job.ok) alert(job.message);
  }

  // ---- settings ----
  function settingsSection() {
    const name = input('', { maxLength: 32, style: { width: '240px' } });
    const nameNote = note();
    const addr = input('', { style: { width: '240px' } });
    get('/api/realm').then(r => { if (r.name != null) name.value = r.name; if (r.address != null) addr.value = r.address; }).catch(() => { });
    return [
      card(T('Server settings'), T('XP and drop rates, Playerbots, convenience options and every other setting of the worldserver and its modules, each with its description.'), 'settings',
        primary(T('Open server settings …'), () => import('./settings.js').then(m => m.settingsDialog()))),
      card(T('Server name'), T('The name players see in the realm list when they log in. The game client keeps each character\'s interface settings in a folder named after the server (WTF\\Account\\<account>\\<server name>), so after renaming it starts with fresh ones unless you rename that folder too.'), 'server',
        h('div.row', {}, h('span', { style: { width: '190px' } }, T('Server name')), name, secondary(T('Rename'), async () => {
          const j = await runTask(() => post('/api/realm/name', { name: name.value }));
          if (j && j.ok) setNote(nameNote, j.message, 'ok');
        })), nameNote),
      card(T('Play with others'), meta.windows
        ? F('Enter the address other players use to reach your PC, for example your Radmin VPN or Hamachi IP (26.x.x.x / 25.x.x.x) or your LAN IP. {0} then adjusts everything needed and allows the servers through the Windows Firewall. You keep playing via 127.0.0.1 yourself. Other players put this address into their realmlist.wtf.', meta.product)
        : F('Enter the address other players use to reach this computer, for example its LAN IP or a VPN address. {0} then adjusts everything needed. You keep playing via 127.0.0.1 yourself. Other players put this address into their realmlist.wtf.', meta.product), 'globe',
        h('div.row', {}, h('span', { style: { width: '190px' } }, T('Realm address')), addr, secondary(T('Apply'), async () => {
          const j = await runTask(() => post('/api/realm/address', { address: addr.value }));
          if (j && j.ok) alert(j.message);
        }))),
    ];
  }

  // ---- modules ----
  function modulesSection() {
    const st = s();
    const m = st.modules || {};
    const setup = (file, fn) => () => import('./' + file).then(x => x[fn]());
    const moduleCard = (on, title, text, ic, btn) => on ? h('div', {}, (() => { const c = card(title, text, ic, btn); c.classList.add('stripe'); return c; })()) : null;
    const clientState = h('div.hint.warn');
    sectionEls.clientState = clientState;
    const clientBtn = secondary(st.clientButton || T('Game client …'), () => import('./client.js').then(x => x.clientDialog()), 'client');
    sectionEls.clientBtn = clientBtn;
    const coaBtn = primary(st.coaButton || T('Install / update CoA Custom ...'), () => import('./coa.js').then(x => x.coaDialog()), 'client');
    sectionEls.coaBtn = coaBtn;
    const racesBtn = secondary(st.racesButton || T('Base races …'), () => import('./races.js').then(x => x.racesDialog()));
    sectionEls.racesBtn = racesBtn;
    const out = [
      card(T('Manage modules'), T('Add or remove modules: those made for this server and the AzerothCore module catalog. The server is backed up and rebuilt; database changes of modules installed here are recorded, so removing a module undoes them.'), 'modules',
        primary(T('Manage modules …'), () => import('./modules.js').then(x => x.modulesDialog()))),
      moduleCard(m.journey, T('World Journey'), T('The old world, Outland and Northrend as one journey from 1 to 60: where each part begins, how the level window works, how hard creatures are and what follows the journey.'), 'journey',
        primary(T('Set up World Journey …'), setup('journey.js', 'journeySetup'))),
      moduleCard(m.auction, T('Auction house bots'), T('How the bots of mod-playerbots-auctions trade: how often they come, what they sell and buy, their prices, crafting, deals by chat, the Trading Post and their chatter.'), 'auction',
        primary(T('Set up the auction house bots …'), setup('setups.js', 'auctionSetup'))),
      moduleCard(m.classic, T('Classic classes'), T('Which classic classes - Warrior to Druid - can be played next to the classes of Conquest of Azeroth, by players and bots.'), 'gamemaster',
        primary(T('Set up the classic classes …'), setup('setups.js', 'classicSetup'))),
      moduleCard(m.races, T('CoA Custom'), T('Install or update the CoA Custom client and server package. The CoA UI owns race and class selection; only the base race of each CoA custom race is configured here.'), 'races', h('div', { class: 'row' }, coaBtn, racesBtn)),
      moduleCard(m.flying, T('Azeroth flying'), T('Optional flying mounts in Eastern Kingdoms and Kalimdor: off by default, with a configurable no-fly list for the old capitals. Old Azeroth was not built for flying; use it at your own risk.'), 'journey',
        primary(T('Set up Azeroth flying …'), setup('setups.js', 'flyingSetup'))),
      moduleCard(m.immersion, T('Immersion Life'), T('The population of cities and towns, questing, legendary residents, PvP Life and Fishing Life for mod-playerbots-immersion. PvP Life and Fishing Life are off by default; the legendary residents are on.'), 'accounts',
        primary(T('Set up Immersion Life ...'), setup('setups.js', 'immersionSetup'))),
      card(T('Game client'), F('Some modules bring an add-on or a patch for the game client (for example World Journey\'s map levels and tooltips, or the class choice of the classic classes). {0} puts them into your game and clears its cache when a module asks for it; for other players it packs them into a ZIP.', meta.product), 'client',
        clientState, clientBtn),
    ];
    paintModules();
    return out;
  }

  function paintModules() {
    const st = s();
    if (sectionEls.clientState) { sectionEls.clientState.textContent = st.clientState || ''; sectionEls.clientState.classList.toggle('hidden', !st.clientState); }
    if (sectionEls.clientBtn && st.clientButton) sectionEls.clientBtn.lastChild.textContent = st.clientButton;
    if (sectionEls.coaBtn && st.coaButton) sectionEls.coaBtn.lastChild.textContent = st.coaButton;
    if (sectionEls.racesBtn && st.racesButton) sectionEls.racesBtn.lastChild.textContent = st.racesButton;
  }

  // ---- accounts ----
  function accountsSection() {
    const name = input('', { style: { width: '220px' }, maxLength: 16, autocomplete: 'off' });
    const p1 = password({ style: { width: '220px' } }), p2 = password({ style: { width: '220px' } });
    const level = select([[0, T('Player')], [1, T('Moderator (GM 1)')], [2, T('Game Master (GM 2)')], [3, T('Administrator (GM 3)')]], 0);
    const created = note();
    return [
      card(T('Create account'), T('A new login for the game, with the access level you choose.'), 'accounts',
        field(T('Account name'), name), field(T('Password'), p1), field(T('Repeat password'), p2), field(T('Access level'), level),
        primary(T('Create account'), async () => {
          const j = await runTask(() => post('/api/accounts/create', { name: name.value.trim(), password: p1.value, password2: p2.value, level: +level.value }));
          if (j && j.ok) { setNote(created, j.message, 'ok'); p1.value = p2.value = ''; }
        }), created),
      card(T('Player accounts'), T('The accounts of real players with their characters: a new password, the access level, deleting, and moving an account to another server (export and import).'), 'accounts',
        secondary(T('Manage player accounts …'), () => import('./accounts.js').then(m => m.accountsDialog()))),
    ];
  }

  // ---- maintenance ----
  function maintenanceSection() {
    return [
      card(T('Updates'), T('Checks the CoA core, Playerbots and your modules for new versions; the server is backed up first and rebuilt only when something changed.'), 'maintenance',
        primary(T('Check for updates and install'), () => engine('Update', T('Check for updates?\n\nIf there are new versions of CoA or Playerbots, the current server is backed up first and then recompiled, which can take a while. A running server is stopped cleanly first.')))),
      card(T('Repair and reset'), T('"Repair setup" sets up the database and configuration again without recompiling; characters and accounts are kept. "Reset random bots" deletes every random bot and lets the server create new ones.'), 'maintenance',
        h('div.row', {},
          secondary(T('Repair setup'), () => engine('Setup', T('Set up the database and configuration again (without recompiling)?\n\nCharacters and accounts are kept.'))),
          secondary(T('Reset random bots …'), () => resetBots()))),
      meta.remote ? null : (() => { const box = h('div'); import('./remote.js').then(m => replace(box, m.remoteCard())); return box; })(),
      card(T('Files'), T('The server\'s folder and its logs, or another server.'), 'server',
        h('div.row', {},
          meta.remote ? null : secondary(T('Open folder'), () => post('/api/open', { what: 'folder' }).then(r => { if (!r.ok) alert(r.path); })),
          meta.remote ? null : secondary(T('Open server logs'), () => post('/api/open', { what: 'logs' }).then(r => { if (!r.ok) alert(r.path); })),
          secondary(T('Show the logs …'), () => import('./consoles.js').then(m => m.logsDialog())),
          secondary(T('Other server / new install'), async () => { await post('/api/servers/close'); show('welcome'); }))),
    ];
  }

  // ---- the bar ----
  function paintBar() {
    const st = s();
    pickerName.textContent = (st.root && serverName) || '…';
    pickerPath.textContent = st.root ? '\u200E' + st.root + '\u200E' : '';
    pills.db.set(st.db, true); pills.auth.set(st.auth, true); pills.world.set(st.world, st.worldReady);
    startBtn.disabled = !st.canStart || st.busy; stopBtn.disabled = !st.canStop || st.busy; restartBtn.disabled = !st.canRestart || st.busy;
    uptime.textContent = st.uptime || ''; uptime.classList.toggle('hidden', !st.uptime);
    notice.textContent = st.notice || ''; notice.className = 'notice pre ' + (st.noticeKind === 'ok' ? 'ok' : st.noticeKind === 'bad' ? 'bad' : st.noticeKind === 'warn' ? 'warn' : 'muted');
    clear(banners);
    if (st.serverBanner) banners.appendChild(h('button.banner', { type: 'button', title: (st.serverBanner.news || []).join('\n') + ((st.serverBanner.warnings || []).length ? '\n\n' + st.serverBanner.warnings.join('\n') : '') + (st.serverBanner.limited ? '\n' + st.serverBanner.limited : ''), onclick: serverUpdate }, st.serverBanner.text));
    if (st.toolBanner) banners.appendChild(h('button.banner', { type: 'button', onclick: () => toolUpdate(st.toolBanner) }, st.toolBanner.text));
    if (st.clientBanner) banners.appendChild(h('button.banner', { type: 'button', onclick: () => import('./client.js').then(x => x.clientDialog()) }, st.clientBanner));
    if (st.coaBanner) banners.appendChild(h('button.banner', { type: 'button', onclick: () => import('./coa.js').then(x => x.coaDialog()) }, st.coaBanner));
    drawNav();
    paintServer(); paintModules();
    offers();
  }

  async function offers() {
    const st = s();
    if (offerShown || (!st.offerClient && !(st.botOffer && st.botOffer.length))) return;
    offerShown = true;
    const client = st.offerClient, bots = st.botOffer || [];
    await post('/api/offers/shown', { client, bots: bots.length > 0 }).catch(() => { });
    if (client) {
      const m = await import('./client.js');
      await m.clientDialog(T('A module brings a new or changed add-on or client patch for the game client (or one is no longer needed).'));
    }
    if (bots.length) resetBots(T('A new module changes what the random bots are made of:') + '\n\n' + bots.map(b => '• ' + b.key + (b.why ? ': ' + b.why : '')).join('\n') + '\n\n');
  }

  async function serverUpdate() {
    const b = s().serverBanner || {};
    const warnings = b.warnings || [];
    let q = T('Install the server update now?') + '\n\n';
    if (warnings.length) q += '⚠ ' + (warnings.length === 1 ? T('Checked beforehand: a patch of a module does not fit the new code:') : T('Checked beforehand: some patches of modules do not fit the new code:')) + '\n  ' + warnings.join('\n  ') + '\n' +
      (warnings.length === 1 ? T('You can update anyway - the server then runs without that change - or wait until the module is adapted.') : T('You can update anyway - the server then runs without these changes - or wait until the module is adapted.')) + '\n\n';
    if ((b.news || []).length) q += T('New:') + '\n  ' + b.news.join('\n  ') + '\n' + (b.limited ? b.limited + '\n' : '') + '\n';
    q += T('The current server is backed up first, then rebuilt with the newest code, which can take a while. A running server is stopped cleanly first.');
    engine('Update', q);
  }

  async function toolUpdate(b) {
    if (!b.download) { window.open(b.url, '_blank'); return; }
    if (!await confirm(F('Update {0} to version {1} now?', meta.product, b.version) + '\n\n' + F('The new version is downloaded from GitHub, then {0} restarts. A running server keeps running.', meta.product))) return;
    const j = await runTask(() => post('/api/tool/update'));
    if (j && j.ok) { toast(T('Restarting …')); setTimeout(() => location.reload(), 6000); }
    else if (j && b.url && await confirm(T('Open the download page instead?'))) window.open(b.url, '_blank');
  }

  const off = [on('status', () => paintBar())];
  let serverName = '';
  get('/api/servers').then(r => { const me = r.list.find(x => x.root === r.current); serverName = me ? me.name : ''; paintBar(); }).catch(() => { });
  selectSection(sections.some(x => x[0] === current) ? current : 'server');
  paintBar();
  el.close = () => off.forEach(f => f());
  return el;
}

function pill(name) {
  const st = h('span'), dot = h('span.dot');
  const el = h('div.pill', {}, dot, h('div', {}, h('b', {}, name), st));
  return {
    el, set(running, ready) {
      const color = !running ? 'var(--muted)' : ready ? 'var(--ok)' : 'var(--warn)';
      el.style.setProperty('--dot', color);
      st.textContent = !running ? T('stopped') : ready ? T('running') : T('loading …');
    }
  };
}

export async function startServer() {
  await runTask(force => post('/api/server/start', { force: !!force }), T('Start server'));
}

export async function engine(mode, question, extra) {
  if (question && !await confirm(question)) return;
  try {
    const job = await post('/api/engine', Object.assign({ mode }, extra || {}));
    state.job = job;
    show('progress');
  } catch (e) { alert(e.message); }
}

export async function resetBots(reason) {
  if (!await confirm((reason || '') + T('Delete all random bots and create new ones?') + '\n\n' +
    T('This removes every random bot account with its characters, guilds, arena teams and mail. Your own accounts and characters are kept, including bots you created on your own accounts.') + '\n\n' +
    T('The server is stopped first. Deleting can take a while; the new bots are created at the next server start.') +
    (reason ? '\n\n' + T('Not now? "Reset random bots …" under Maintenance does the same later.') : ''))) return;
  const j = await runTask(() => post('/api/bots/reset'));
  if (j && j.ok && await confirm(T('All random bots were deleted.\n\nStart the server now? The new bots are created while it starts, which takes a bit longer than usual.'))) startServer();
}

/** A task whose output is worth watching (map data): its lines in a console until it ends. */
export async function runTaskWithLog(call, title) {
  let job;
  try { job = await call(); } catch (e) { await alert(e.message); return null; }
  if (!job || !job.running) return job;
  const status = h('div.muted', {}, job.status || '');
  const log = h('div.console');
  let closeIt;
  dialog({ title, icon: 'server', wide: true, body: h('div', {}, status, log), buttons: [], noClose: true, onopen: (b, c) => { closeIt = c; } });
  const offs = [
    on('job', j => { if (j && j.id === job.id) status.textContent = j.status || status.textContent; }),
    on('joblog', l => { if (l.id !== job.id) return; log.appendChild(document.createTextNode(l.line + '\n')); log.scrollTop = log.scrollHeight; }),
  ];
  const { waitJob } = await import('./ui.js');
  const end = await waitJob(job);
  offs.forEach(f => f());
  closeIt && closeIt();
  if (end && !end.ok) await alert(end.message);
  return end;
}

async function serverMenu(at) {
  let r;
  try { r = await get('/api/servers'); } catch (e) { return alert(e.message); }
  const items = r.list.map(k => ({
    text: k.name + '   —   ' + k.root + (k.installed ? '' : '   ' + T('(not found)')),
    check: k.root === r.current, disabled: k.root === r.current || !k.installed,
    onclick: async () => {
      const st = state.status || {};
      if ((st.world || st.auth) && !await confirm(F('This server is running and keeps running. {0} then looks after the other server: the scheduled restart of this one pauses until you switch back.', meta.product) + '\n\n' + T('Two servers cannot run at the same time on the same ports: stop this one before you start the other.') + '\n\n' + T('Switch anyway?'))) return;
      try { await post('/api/servers/open', { root: k.root }); show('manager'); } catch (e) { alert(e.message); }
    }
  }));
  items.push('sep',
    { text: T('Add an existing server …'), onclick: async () => { const p = await choose({ title: T('Choose the server folder (the one containing "Server", "DB" and "Dependencies").'), newFolder: false }); if (!p) return; try { await post('/api/servers/open', { root: p }); show('manager'); } catch (e) { alert(e.message); } } },
    { text: T('Install a new server …'), onclick: async () => { await post('/api/servers/close'); show('welcome', 'folder'); } });
  const others = r.list.filter(k => k.root !== r.current);
  if (others.length) {
    items.push('sep');
    for (const k of others) items.push({ text: T('Remove from this list') + ': ' + k.name, sub: k.root, onclick: () => post('/api/servers/forget', { root: k.root }) });
  }
  menu(at, items);
}
