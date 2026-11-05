// The Bots section: an AFK-themed dashboard of the random bots, read from the database and, when enabled, from the
// live Playerbots status file. It replaces the former count-only page with a roster and live activity.
import { h, T, F, get, post, replace, debounce } from './core.js';
import { card, secondary, primary, segmented, tabs, table, input, dialog, alert, confirm } from './ui.js';

const nf = new Intl.NumberFormat();

function bars(items, total, colors) {
  if (!items.length) return h('div.muted', {}, T('No bots to count.'));
  const max = Math.max(1, ...items.map(i => i.value));
  return h('div.bars', {}, items.map((it, i) => h('div.barrow', {},
    h('span.name', { title: it.name }, it.name),
    h('span.track', {}, h('i', { style: { width: it.value <= 0 ? '0' : Math.max(1.5, 100 * it.value / max) + '%', background: (colors && colors[i]) || (it.other ? 'var(--muted)' : 'var(--accent)') } })),
    h('span.num', {}, nf.format(it.value) + (total > 0 ? '  ' + Math.round(100 * it.value / total) + ' %' : '')))));
}

function health(value) {
  if (value == null || value < 0) return h('span.muted', {}, '—');
  const p = Math.max(0, Math.min(100, value));
  return h('span.health-cell', { title: F('{0} % health', p) },
    h('span.health', {}, h('i', { class: p < 30 ? 'low' : p < 60 ? 'mid' : '', style: { width: p + '%' } })),
    h('span.health-num', {}, p + ' %'));
}

function activity(b) {
  if (b.live) return b.task || '—';
  return b.online ? T('Online · live status off') : T('Offline');
}

export function dashboard() {
  let all = false, loading = false, timer = null, data = null, query = '', view = 0;

  const tile = caption => { const v = h('div.tv', {}, '–'); return [h('div.stat', {}, v, h('div.tc', {}, caption)), v]; };
  const [t1, world] = tile(T('bots in the world')), [t2, players] = tile(T('players in the world')), [t3, level] = tile(T('average level')), [t4, sides] = tile(T('Alliance : Horde'));
  const state = h('span.small.muted');
  const liveState = h('span.small.muted');
  const mode = segmented([[0, T('Bots in the world')], [1, T('All bots')]], 0, v => { all = +v === 1; refresh(); });
  const boxes = { faction: h('div'), classes: h('div'), races: h('div'), levels: h('div'), zones: h('div') };

  const overview = h('div.dash-grid', {},
    card(T('Factions'), null, null, boxes.faction),
    card(T('Classes'), null, null, boxes.classes),
    card(T('Races'), null, null, boxes.races),
    card(T('Levels'), null, null, boxes.levels),
    h('div.dash-wide', {}, card(T('Zones'), T('Where the bots are, the fifteen zones with the most of them.'), 'globe', boxes.zones)));

  const search = input('', {
    placeholder: T('Search name, class, race or zone …'),
    style: { flex: '1', minWidth: '220px' },
    oninput: debounce(() => { query = search.value.trim().toLowerCase(); drawRoster(); }, 120)
  });
  const count = h('span.small.muted');
  const rosterNote = h('div.note.hidden');
  const liveButton = primary(T('Turn on live status'), async () => {
    if (!await confirm(T('AFK Realm will write the Playerbots live status to the server\'s logs folder. The change needs a server restart or .reload config. Turn it on now?'), T('Turn on live status'), T('Cancel'))) return;
    liveButton.disabled = true;
    try {
      const r = await post('/api/bots/live');
      await alert(r.note);
      await refresh();
    } catch (e) { await alert(e.message || String(e)); }
    finally { liveButton.disabled = false; }
  }, 'play');
  liveButton.style.display = 'none';
  const rosterBox = h('div');
  const rosterCard = card(T('Bot roster'), T('Every bot AFK Realm can see, with its class, level, zone and – when live status is on – its health, group and what it is doing.'), 'accounts',
    h('div.row', { style: { alignItems: 'center', flexWrap: 'wrap' } }, search, secondary(T('Refresh'), () => refresh()), liveButton),
    h('div.row', { style: { alignItems: 'center' } }, count),
    rosterNote,
    rosterBox);
  rosterCard.style.maxWidth = 'none';

  const tabsBar = tabs([T('Overview'), T('Bot roster')], i => { view = i; paint(); });
  const viewBox = h('div');
  const root = h('div.bots-dash', {},
    card(T('Bots dashboard'), T('How many random bots are in the world, who they are and what they are doing. The numbers come from the database; live activity comes from the Playerbots status file.'), 'bots',
      h('div.stats', {}, t1, t2, t3, t4),
      h('div.row', { style: { alignItems: 'center', flexWrap: 'wrap' } }, h('span.muted', {}, T('Count:')), mode, secondary(T('Refresh'), () => refresh()), state),
      h('div.row', { style: { alignItems: 'center' } }, liveState)),
    tabsBar,
    viewBox);

  function paint() {
    replace(viewBox, view === 0 ? overview : rosterCard);
  }

  function drawRoster() {
    if (!data) {
      replace(rosterBox, h('div.muted', {}, T('Reading …')));
      return;
    }
    let bots = data.bots || [];
    if (!all) bots = bots.filter(b => b.online);
    if (query) bots = bots.filter(b => [b.name, b.className, b.raceName, b.zone, b.task].join(' ').toLowerCase().includes(query));
    count.textContent = F('{0} bots', nf.format(bots.length));
    replace(rosterBox, bots.length ? table([
      { title: T('Bot'), render: b => h('span.bot-name', {}, h('span.bot-dot', { class: b.dead ? 'dead' : b.combat ? 'combat' : b.live ? 'live' : b.online ? 'online' : '' }), b.name) },
      { title: T('Level'), key: 'level', cls: 'num' },
      { title: T('Class'), key: 'className' },
      { title: T('Race'), key: 'raceName' },
      { title: T('Zone'), render: b => b.zone || '—' },
      { title: T('Activity'), render: b => activity(b) },
      { title: T('Health'), render: b => b.live ? health(b.health) : h('span.muted', {}, '—') },
      { title: T('Group'), render: b => b.group > 1 ? String(b.group) : '' },
      { title: T('Kills'), render: b => b.live ? b.kills : '—', cls: 'num' }
    ], bots, { height: '520px', onpick: showBot }) : h('div.muted', {}, T('No bots found.')));

    replace(rosterNote);
    liveButton.style.display = data.live || data.configured ? 'none' : '';
    if (!data.live) {
      rosterNote.className = 'note warn';
      rosterNote.appendChild(document.createTextNode(data.note || T('Live status is off.')));
    } else {
      rosterNote.className = 'note hidden';
    }
  }

  function showBot(b) {
    const rows = [
      [T('Level'), b.level],
      [T('Class'), b.className || '—'],
      [T('Race'), b.raceName || '—'],
      [T('Zone'), b.zone || '—'],
      [T('Activity'), activity(b)],
      [T('Health'), b.live ? health(b.health) : '—'],
      [T('Power'), b.live ? (b.power + ' %') : '—'],
      [T('Group'), b.group > 1 ? F('{0} members', b.group) + (b.leader ? ' · ' + b.leader : '') : '—'],
      [T('Kills'), b.live ? nf.format(b.kills) : '—'],
      [T('Position'), b.live ? F('Map {0} · {1}, {2}', b.map, Math.round(b.x), Math.round(b.y)) : '—']
    ];
    const detail = h('table.grid', {}, h('tbody', {}, rows.map(r => h('tr', {}, h('th', { style: { width: '130px' } }, r[0]), h('td', {}, r[1])))));
    const body = h('div', {}, detail,
      b.live && b.quests && b.quests.length ? h('div', { style: { marginTop: '14px' } }, h('h3', {}, T('Quests')), h('div.small', {}, b.quests.join(' · '))) : null);
    dialog({ title: b.name, icon: 'bots', body, buttons: [{ text: T('OK'), kind: 'primary', value: true }] });
  }

  async function refresh() {
    if (loading) return;
    loading = true;
    state.textContent = T('Reading …');
    try {
      const [s, r] = await Promise.all([
        get('/api/bots/stats', all ? { all: 1 } : undefined),
        get('/api/bots/roster', all ? { all: 1 } : undefined)
      ]);
      data = r;
      world.textContent = nf.format(s.inWorld) + ' / ' + nf.format(s.total);
      players.textContent = nf.format(s.players);
      level.textContent = s.average === '–' ? '–' : Number(s.average).toLocaleString(undefined, { minimumFractionDigits: 1, maximumFractionDigits: 1 });
      sides.textContent = nf.format(s.alliance) + ' : ' + nf.format(s.horde);
      replace(boxes.faction, bars([{ name: T('Alliance'), value: s.alliance }, { name: T('Horde'), value: s.horde }], s.counted, ['#2e60b2', '#b22e2e']));
      replace(boxes.classes, bars(s.classes, s.counted));
      replace(boxes.races, bars(s.races, s.counted));
      replace(boxes.levels, bars(s.levels, s.counted));
      replace(boxes.zones, bars(s.zones, s.counted));
      liveState.textContent = r.live
        ? F('Live · {0}', new Date((r.liveAt || 0) * 1000).toLocaleTimeString())
        : (r.configured ? T('Live status: waiting for the first file') : T('Database only · live status is off'));
      state.textContent = F('Updated {0} · every 30 s', s.time);
      drawRoster();
    } catch (e) { state.textContent = e.message; }
    finally { loading = false; }
  }

  paint();
  drawRoster();
  setTimeout(refresh, 0);
  timer = setInterval(() => { if (!root.isConnected && timer) { clearInterval(timer); timer = null; return; } refresh(); }, 30000);
  return root;
}
