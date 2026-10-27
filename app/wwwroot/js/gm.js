// Game master tools: characters on the left, quests and player actions on the right, the console below.
import { h, T, F, meta, post, get, replace, clear } from './core.js';
import { primary, secondary, dialog, alert, prompt, check, input, tabs, table } from './ui.js';

export async function gameMaster() {
  const link = h('div', { style: { fontWeight: '700' } });
  const people = h('div.list', { style: { flex: '1', minHeight: '0' } });
  const offline = check(T('Offline too'), false, () => loadPeople());
  const bots = check(T('Bots too'), false, () => loadPeople());
  const log = h('div.console', { style: { height: '170px' } });
  const cmd = input('', { style: { flex: '1' }, class: 'mono', placeholder: T('GM command, e.g. server info') });
  const history = []; let historyAt = 0;
  let who = null, which = null, questRows = [], questKind = null, ticket = 0, busy = false;
  const actions = [];

  const print = t => { log.appendChild(document.createTextNode(String(t).replace(/\r\n/g, '\n') + '\n')); if (log.textContent.length > 200000) log.textContent = log.textContent.slice(-100000); log.scrollTop = log.scrollHeight; };
  const clean = t => (t || '').replace(/"/g, "'").replace(/[\r\n]+/g, ' ').trim();
  const arg = n => n.includes(' ') ? '"' + n + '"' : n;

  async function showLink() {
    try { const r = await get('/api/gm/link'); link.textContent = r.problem || T('●  Connected to the running server.'); link.className = r.problem ? 'warn' : 'ok'; }
    catch (e) { link.textContent = e.message; link.className = 'bad'; }
  }

  async function loadPeople(thenLog) {
    const keep = who && who.guid;
    try {
      const r = await get('/api/gm/characters', { offline: offline.checked ? 1 : 0, bots: bots.checked ? 1 : 0 });
      who = null;
      replace(people, table([
        { title: T('Character'), render: c => h('span', { class: c.online ? '' : 'muted', title: F('Account {0}', c.account) + (c.bot ? ' ' + T('(random bot)') : '') }, (c.online ? '● ' : '○ ') + c.name) },
        { title: T('Lvl'), key: 'level', cls: 'num' },
        { title: T('Zone'), key: 'zone' },
      ], r.characters, { onpick: c => pick(c), selected: c => c.guid === keep && (who = c) }));
      if (!who && r.characters.length === 1) { who = r.characters[0]; people.querySelector('tbody tr')?.classList.add('sel'); }
      if (!r.characters.length) print(offline.checked ? T('No characters found.') : T('Nobody is online. Tick "Offline too" to see all characters.'));
      if (thenLog && who) showLog(true);
    } catch (e) { print('! ' + e.message); }
  }

  function pick(c) {
    who = c;
    if (questKind === 'search' && questRows.length) searchQuests();
    else showLog(false);
  }

  async function send(command, done) {
    if (busy) return;
    busy = true; actions.forEach(b => b.disabled = true);
    print('> ' + command);
    try {
      const r = await post('/api/gm/command', { command });
      if (r.problem) { showLink(); print('! ' + r.problem); }
      else if (r.error) print('! ' + r.error);
      else { print(r.answer); done && done(); }
    } catch (e) { print('! ' + e.message); }
    finally { busy = false; actions.forEach(b => b.disabled = false); }
  }

  const act = (text, fn) => { const b = secondary(text, fn); b.classList.add('small'); actions.push(b); return b; };
  const needWho = () => { if (!who) { alert(T('Choose a character on the left first.')); return false; } return true; };
  const player = (format, done) => { if (needWho()) send(format.replace('{0}', arg(who.name)), done); };

  // ---- quests
  const questText = input('', { style: { width: '240px' } });
  questText.addEventListener('keydown', e => { if (e.key === 'Enter') { e.preventDefault(); searchQuests(); } });
  const radius = input('150', { type: 'number', min: 20, max: 2000, step: 50, style: { width: '80px' } });
  const levelOnly = check(T('Only quests it can take now'), true);
  const questBox = h('div', { style: { flex: '1', minHeight: '0', overflow: 'auto' } });
  const questHint = h('div.hint');

  function fill(list, kind, hint) {
    questRows = list; questKind = kind; which = null;
    const isLog = kind === 'log';
    const cols = [
      { title: T('Id'), key: 'id', cls: 'num' }, { title: T('Quest'), key: 'title' }, { title: T('Lvl'), render: q => q.level > 0 ? q.level : '', cls: 'num' },
      { title: T('Status'), render: q => q.statusText },
      isLog ? { title: T('Progress'), key: 'progress' } : null,
      { title: T('Given by'), key: 'givers' }, { title: T('Handed in to'), key: 'takers' },
      isLog ? null : { title: T('Yards'), render: q => q.distance >= 0 ? Math.round(q.distance) : '', cls: 'num' },
    ].filter(Boolean);
    replace(questBox, table(cols, list, { onpick: (q, tr) => { which = q; which.tr = tr; } }));
    questHint.textContent = hint;
  }

  async function searchQuests() {
    const text = questText.value.trim();
    if (!text) return;
    ++ticket;
    try {
      const r = await post('/api/gm/search', { text, guid: who ? who.guid : 0 });
      fill(r.quests, 'search', r.quests.length === 0 ? F('Nothing found for "{0}".', text)
        : !who ? F('{0} quest(s). Choose a character to see its status.', r.quests.length)
        : who.online ? F('{0} quest(s). Status for {1} as of the server\'s last save.', r.quests.length, who.name)
        : F('{0} quest(s). Status for {1}.', r.quests.length, who.name));
    } catch (e) { print('! ' + e.message); }
  }

  async function showLog(live) {
    if (!who) return;
    const me = ++ticket, w = who;
    questHint.textContent = F('Reading the quest log of {0} …', w.name);
    try {
      const r = await post('/api/gm/questlog', { guid: w.guid, live: live && w.online });
      if (me !== ticket) return;
      fill(r.quests, 'log', (r.quests.length === 0 ? F('{0} has no quests in its log', w.name) : F('{0} quest(s) in the log of {1}', r.quests.length, w.name)) +
        (!w.online ? '.' : r.live ? T(', as of now.') : T(', as of the server\'s last save. "Quest log" reads it fresh.')));
    } catch (e) { print('! ' + e.message); }
  }

  async function nearQuests() {
    if (!needWho()) return;
    const w = who, me = ++ticket;
    questHint.textContent = F('Looking around {0} …', w.name);
    try {
      const r = await post('/api/gm/near', { guid: w.guid, online: w.online, radius: +radius.value || 150, takeable: levelOnly.checked });
      if (me !== ticket) return;
      fill(r.quests, 'near', (r.quests.length === 0 ? F('No new quests within {0} yards of {1} in {2}', r.radius, r.name, r.zone)
        : F('{0} quest(s) it does not have yet within {1} yards of {2} in {3}', r.quests.length, r.radius, r.name, r.zone)) +
        (r.live ? '.' : ' ' + T('(position as last saved by the server).')) + (levelOnly.checked ? '' : ' ' + T('Quests it cannot take yet are included; "Check" tells why.')));
    } catch (e) { questHint.textContent = e.message; }
  }

  const statusText = { '-1': '', 1: T('complete'), 3: T('in progress'), 5: T('failed'), 100: T('rewarded') };
  function quest(verb) {
    if (!needWho()) return;
    const q = which;
    if (!q) return alert(T('Choose a quest in the list first.'));
    if (verb === 'reward' && q.status !== 1) print(T('Note: Reward only works for a completed quest. If the server refuses, use Complete first.'));
    send('quest ' + verb + ' ' + q.id + ' ' + arg(who.name), () => {
      const s = { add: 3, complete: 1, reward: 100, remove: -1 }[verb];
      if (s !== undefined) { q.status = s; if (q.tr) q.tr.cells[3].textContent = statusText[s] ?? ''; }
    });
  }

  const questTab = h('div', { style: { display: 'flex', flexDirection: 'column', gap: '4px', height: '100%', minHeight: '0' } },
    h('div.row', {}, h('span', {}, T('NPC name, quest title or id')), questText, primary(T('Search'), searchQuests)),
    h('div.row', {}, secondary(T('Quest log'), () => { if (needWho()) showLog(true); }), secondary(T('New quests nearby'), nearQuests),
      h('span', {}, T('within')), radius, h('span', {}, T('yards')), levelOnly),
    questHint, questBox,
    h('div.row', {}, act(T('Give quest'), () => quest('add')), act(T('Complete'), () => quest('complete')), act(T('Reward'), () => quest('reward')), act(T('Remove'), () => quest('remove')), act(T('Check'), () => quest('status'))),
    h('div.hint', {}, T('Choosing a character shows its quest log; "New quests nearby" lists what it could still take around it. Give puts the quest into the character\'s log. Complete marks every objective as done and adds missing quest items. Reward hands a completed quest in: experience, money and the reward (the first one, if there is a choice). Works for offline characters too.')));

  // ---- player
  const announce = input('', { style: { width: '380px' } });
  const ask = async (title, label) => prompt(label, '', { title });
  const playerTab = h('div', {},
    h('p.hint', {}, T('These act on the character chosen on the left.')),
    h('div.row', {},
      act(T('Unstuck (to its inn)'), () => player('unstuck {0} inn')),
      act(T('Revive'), () => player('revive {0}')),
      act(T('Kick …'), async () => { if (!needWho()) return; const r = await ask(T('Kick'), T('Reason shown to the player (optional)')); if (r != null) player('kick {0} ' + clean(r)); })),
    h('div.row', {},
      act(T('Set level …'), async () => {
        if (!needWho()) return;
        const v = await ask(T('Set level'), T('New level')); if (v == null) return;
        const n = parseInt(v.trim(), 10);
        if (!(n >= 1 && n <= 255)) return alert(T('Please enter a level as a number.'));
        player('character level {0} ' + n);
      }),
      act(T('Send gold …'), async () => {
        if (!needWho()) return;
        const v = await ask(T('Send gold'), T('Gold (arrives by in-game mail)')); if (v == null) return;
        const n = parseInt(v.trim(), 10);
        if (!(n >= 1 && n <= 200000)) return alert(T('Please enter the gold as a whole number.'));
        player('send money {0} "Gold" "From your game master." ' + (n * 10000));
      }),
      act(T('Send mail …'), async () => {
        if (!needWho()) return;
        const subject = input(''), text = h('textarea', { rows: 4, style: { width: '100%' } });
        const ok = await dialog({ title: T('Send mail'), icon: 'gamemaster', body: h('div', {}, h('div', {}, T('Subject')), subject, h('div', { style: { marginTop: '8px' } }, T('Text')), text),
          buttons: [{ text: T('Cancel'), value: null }, { text: T('Send'), kind: 'primary', value: true }] });
        if (!ok || !subject.value.trim()) return;
        player('send mail {0} "' + clean(subject.value) + '" "' + clean(text.value) + '"');
      })),
    h('h3', {}, T('At the next login')),
    h('div.row', {}, act(T('Change name'), () => player('character rename {0}')), act(T('Customize appearance'), () => player('character customize {0}')),
      act(T('Change race'), () => player('character changerace {0}')), act(T('Change faction'), () => player('character changefaction {0}'))),
    h('p.hint', {}, T('The character is marked on the login screen the next time its player logs in: "Change name" asks for a new name, "Customize appearance" lets them redo looks, gender and name, "Change race" also lets them pick another race of the same faction, "Change faction" a race of the other faction. A character that is online has to log out to the character list first.')),
    h('h3', {}, T('Message to everyone')),
    h('div.row', {}, announce, act(T('Announce'), () => { const t = clean(announce.value); if (t) { send('announce ' + t); announce.value = ''; } })),
    h('p.hint', {}, T('Everything else works in the console below: type any GM command the server console accepts, for example "server info" or "lookup item sword".')));

  const right = h('div', { style: { display: 'flex', flexDirection: 'column', minHeight: '0', height: '100%' } });
  const tabBodies = [questTab, playerTab];
  const tabArea = h('div', { style: { flex: '1', minHeight: '0', overflow: 'auto' } }, questTab);
  right.append(tabs([T('Quests'), T('Player')], i => replace(tabArea, tabBodies[i])), tabArea);

  const sendInput = () => {
    const c = cmd.value.trim(); if (!c) return;
    const i = history.indexOf(c); if (i >= 0) history.splice(i, 1);
    history.push(c); historyAt = history.length; cmd.value = '';
    send(c);
  };
  cmd.addEventListener('keydown', e => {
    if (e.key === 'Enter') { e.preventDefault(); e.stopPropagation(); sendInput(); }
    else if (e.key === 'ArrowUp' && history.length) { historyAt = Math.max(0, historyAt - 1); cmd.value = history[historyAt]; e.preventDefault(); }
    else if (e.key === 'ArrowDown' && history.length) { historyAt = Math.min(history.length, historyAt + 1); cmd.value = historyAt < history.length ? history[historyAt] : ''; e.preventDefault(); }
  });

  const body = h('div', { style: { display: 'flex', flexDirection: 'column', height: '100%', minHeight: '0', gap: '8px' } },
    link,
    h('div.split', { style: { flex: '1', minHeight: '0', gridTemplateColumns: '360px 1fr' } },
      h('div', { style: { display: 'flex', flexDirection: 'column', minHeight: '0', gap: '4px' } }, h('h3', { style: { margin: '0' } }, T('Characters')),
        h('div.row', {}, offline, bots, secondary(T('Refresh'), () => loadPeople(true))), people),
      right),
    h('div', {}, h('b', {}, T('Server console')), log, h('div.row.nowrap', {}, cmd, secondary(T('Send'), sendInput))));
  showLink(); loadPeople();
  await dialog({ title: meta.product + ' – ' + T('Game master'), icon: 'gamemaster', full: true, body, buttons: [{ text: T('Close'), value: null }], enter: false, focus: false,
    onopen: box => { const db = box.querySelector('.db'); db.style.display = 'flex'; db.style.flexDirection = 'column'; } });
}
