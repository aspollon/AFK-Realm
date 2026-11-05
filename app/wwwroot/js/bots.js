// The Bots section: an AFK-themed dashboard of the random bots, read from the database and, when enabled, from the
// live Playerbots status file. It also has a world map, the chat/loot journals and the full bot cards.
import { h, T, F, get, post, replace, debounce } from './core.js';
import { card, secondary, primary, segmented, tabs, table, input, dialog, alert, confirm, check } from './ui.js';

const nf = new Intl.NumberFormat();
const SVG = 'http://www.w3.org/2000/svg';
const CLASS_COLORS = {
  Warrior: '#c79c6e', Paladin: '#f58cba', Hunter: '#abd473', Rogue: '#fff569', Priest: '#e6e6e6',
  'Death Knight': '#c41f3b', Shaman: '#0070de', Mage: '#69ccf0', Warlock: '#9482c9', Druid: '#ff7d0a',
  Hero: '#a78bfa', Monk: '#00ff96', 'Demon Hunter': '#a330c9', Evoker: '#33937f'
};

function svg(tag, attrs, ...children) {
  const el = document.createElementNS(SVG, tag);
  for (const [k, v] of Object.entries(attrs || {})) {
    if (v == null || v === false) continue;
    if (k === 'onclick') el.addEventListener('click', v);
    else el.setAttribute(k, String(v));
  }
  for (const child of children) if (child != null) el.appendChild(typeof child === 'string' ? document.createTextNode(child) : child);
  return el;
}

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

function classColor(name) { return CLASS_COLORS[name] || 'var(--accent)'; }

function chatKindLabel(kind) {
  return ({ say: T('Say'), yell: T('Yell'), whisper: T('Whisper'), party: T('Party'), guild: T('Guild'), channel: T('Channel'), offscreen: T('Off-screen'), other: T('Other') })[kind] || kind;
}

function inBounds(b, bd) {
  return b && b.x != null && b.y != null && bd && b.x >= Math.min(bd.left, bd.right) && b.x <= Math.max(bd.left, bd.right) &&
    b.y >= Math.min(bd.top, bd.bottom) && b.y <= Math.max(bd.top, bd.bottom);
}

function mapPoint(b, bd) {
  const w = bd.left - bd.right, h = bd.top - bd.bottom;
  // World coordinates are rotated relative to the map: LocLeft/LocRight bound world Y,
  // LocTop/LocBottom bound world X.
  return { x: (bd.left - b.y) / w * 1000, y: (bd.top - b.x) / h * 668 };
}

function contentBounds(cont) {
  const zs = cont.zones || [];
  if (!zs.length) return { x: 0, y: 0, w: 1, h: 1 };
  const pad = 0.015;
  const x1 = Math.max(0, Math.min(...zs.map(z => z.x)) - pad);
  const y1 = Math.max(0, Math.min(...zs.map(z => z.y)) - pad);
  const x2 = Math.min(1, Math.max(...zs.map(z => z.x + z.w)) + pad);
  const y2 = Math.min(1, Math.max(...zs.map(z => z.y + z.h)) + pad);
  return { x: x1, y: y1, w: x2 - x1, h: y2 - y1 };
}

export function dashboard() {
  let all = false, loading = false, data = null, query = '', view = 0;
  let mapData = null, mapContinent = null, mapZone = 0, mapLoading = false;
  let chatData = null, lootData = null, logData = null, chatKind = '', chatQuery = '', chatLoading = false;
  let pvpData = null, auctionData = null, pvpLoading = false, auctionLoading = false;
  let armoryData = null, armoryLoading = false;

  const tile = caption => { const v = h('div.tv', {}, '–'); return [h('div.stat', {}, v, h('div.tc', {}, caption)), v]; };
  const [t1, world] = tile(T('bots in the world')), [t2, players] = tile(T('players in the world')), [t3, level] = tile(T('average level')), [t4, sides] = tile(T('Alliance : Horde'));
  const state = h('span.small.muted');
  const liveState = h('span.small.muted');
  const mode = segmented([[0, T('Bots in the world')], [1, T('All bots')]], 0, v => { all = +v === 1; refresh(); });
  const boxes = { faction: h('div'), classes: h('div'), races: h('div'), levels: h('div'), zones: h('div') };
  const liveBox = h('div');

  const overview = h('div.dash-grid', {},
    card(T('Factions'), null, null, boxes.faction),
    card(T('Classes'), null, null, boxes.classes),
    card(T('Races'), null, null, boxes.races),
    card(T('Levels'), null, null, boxes.levels),
    card(T('Zones'), T('Where the bots are, the fifteen zones with the most of them.'), 'globe', boxes.zones),
    h('div.dash-wide', {}, card(T('Live activity'), T('What the online bots are doing right now, from the Playerbots status file.'), 'bots', liveBox)));

  // ---- world map ----
  const mapSub = h('div.small.muted');
  const mapTabs = h('div.tabs');
  const mapHolder = h('div.map-holder');
  const mapSide = h('div.map-side');
  const mapElsewhere = h('div.small.muted');
  const mapCard = card(T('World map'), T('Every online bot on the map, with its zone and last known position. With live status on, the dots and cards follow the bots every few seconds.'), 'globe',
    mapSub, mapTabs, h('div.map-layout', {}, mapHolder, mapSide), mapElsewhere);

  // ---- roster ----
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

  // ---- chat and loot ----
  const chatSub = h('span.small.muted');
  const chatBox = h('div.chat-feed');
  const chatSearch = input('', {
    placeholder: T('Search speaker or text …'),
    style: { flex: '1', minWidth: '180px' },
    oninput: debounce(() => { chatQuery = chatSearch.value.trim(); loadChat(); }, 250)
  });
  const chatKindTabs = tabs([T('All'), T('Say'), T('Yell'), 'Whisper', T('Channels')], i => {
    chatKind = ['', 'say,yell', 'yell', 'whisper', 'channel,party,guild'][i] || '';
    loadChat();
  });
  const lootBox = h('div');
  const logsBox = h('div');
  const chatCard = card(T('Chat & Loot'), T('What the bots say and the rare items they find, from the server journals.'), 'chat',
    h('div.row', { style: { alignItems: 'center', flexWrap: 'wrap' } }, chatKindTabs, chatSearch, secondary(T('Refresh'), () => loadChatLoot())),
    chatSub, chatBox,
    h('h3', { style: { marginTop: '18px' } }, T('Recent finds')),
    lootBox,
    h('h3', { style: { marginTop: '18px' } }, T('Journals')),
    logsBox);

  const pvpBox = h('div');
  const pvpCard = card(T('PvP'), T('Battlegrounds and arenas of the last 24 hours, from the server\'s own statistics.'), 'pvp', pvpBox);
  const auctionBox = h('div');
  const auctionCard = card(T('Auction house'), T('What is on offer right now. This is a read-only view; the bots trade through mod-playerbots-auctions.'), 'auction', auctionBox);

  const armorySearch = input('', {
    placeholder: T('Search a character or bot by name …'),
    style: { flex: '1', minWidth: '220px' },
    oninput: debounce(() => loadArmory(true), 250)
  });
  const armoryBox = h('div');
  const armoryCard = card(T('Armory'), T('Find a bot or player and look at their equipped gear, item level and stats.'), 'accounts',
    h('div.row', {}, armorySearch, secondary(T('Refresh'), () => loadArmory(false))), armoryBox);

  const tabsBar = tabs([T('Overview'), T('World map'), T('Bot roster'), T('Chat & Loot'), T('PvP'), T('Auction'), T('Armory')], i => {
    view = i;
    paint();
    if (i === 1) ensureMap();
    if (i === 3) loadChatLoot();
    if (i === 4) loadPvp();
    if (i === 5) loadAuction();
    if (i === 6) loadArmory(false);
  });
  const viewBox = h('div');
  const root = h('div.bots-dash', {},
    card(T('Bots dashboard'), T('How many random bots are in the world, who they are and what they are doing. The numbers come from the database; live activity comes from the Playerbots status file.'), 'bots',
      h('div.stats', {}, t1, t2, t3, t4),
      h('div.row', { style: { alignItems: 'center', flexWrap: 'wrap' } }, h('span.muted', {}, T('Count:')), mode, secondary(T('Refresh'), () => refresh()), state),
      h('div.row', { style: { alignItems: 'center' } }, liveState)),
    tabsBar,
    viewBox);

  function paint() {
    replace(viewBox, view === 0 ? overview : view === 1 ? mapCard : view === 2 ? rosterCard : view === 3 ? chatCard : view === 4 ? pvpCard : view === 5 ? auctionCard : armoryCard);
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
    drawLive();
  }

  function drawLive() {
    if (!data) { replace(liveBox, h('div.muted', {}, T('Reading …'))); return; }
    const online = (data.bots || []).filter(b => b.online);
    const live = online.filter(b => b.live);
    if (!live.length) {
      replace(liveBox, h('div.muted', {}, T('No live activity. Turn on live status above to see what the bots are doing.')));
      return;
    }
    const tasks = new Map();
    for (const b of live) tasks.set(b.task || T('Idle'), (tasks.get(b.task || T('Idle')) || 0) + 1);
    const top = live.slice().sort((a, b) => (b.kills || 0) - (a.kills || 0) || b.level - a.level).slice(0, 10);
    const tile = (value, label) => h('div.stat', {}, h('div.tv', {}, nf.format(value)), h('div.tc', {}, label));
    replace(liveBox, h('div', {},
      h('div.stats', {}, tile(live.length, T('bots with live data')), tile(live.filter(b => b.combat).length, T('in combat')), tile(live.filter(b => b.dead).length, T('dead')), tile(live.filter(b => b.group > 1).length, T('in a group'))),
      h('div.dash-grid', {},
        h('div', {}, h('h3', {}, T('What bots are doing')), bars([...tasks.entries()].sort((a, b) => b[1] - a[1]).slice(0, 10).map(([name, value]) => ({ name, value })), live.length)),
        h('div', {}, h('h3', {}, T('Top bots by kills')), table([
          { title: T('Bot'), key: 'name' }, { title: T('Level'), key: 'level', cls: 'num' }, { title: T('Kills'), key: 'kills', cls: 'num' }
        ], top, { onpick: showBot })))));
  }

  async function ensureMap() {
    if (mapData) { drawMap(); return; }
    if (mapLoading) return;
    mapLoading = true;
    mapSub.textContent = T('Reading the map …');
    try {
      mapData = await get('/worldmap.json', { v: 'afk-northrend-1' });
      mapContinent = Object.keys(mapData.continents || {})[0] || null;
      drawMap();
    } catch (e) {
      replace(mapHolder, h('div.note.warn', {}, e.message || String(e)));
      mapSub.textContent = '';
    } finally { mapLoading = false; }
  }

  function drawMap() {
    if (!data) {
      replace(mapHolder, h('div.muted', {}, T('Reading …')));
      return;
    }
    if (!mapData) { ensureMap(); return; }
    const continents = mapData.continents || {};
    const keys = Object.keys(continents).sort((a, b) => Number(a) - Number(b));
    if (!mapContinent || !continents[mapContinent]) mapContinent = keys[0];
    replace(mapTabs, keys.map(k => h('button', {
      type: 'button', class: k === mapContinent ? 'active' : '',
      onclick: () => { mapContinent = k; mapZone = 0; drawMap(); }
    }, continents[k].name || ('Map ' + k))));
    const cont = continents[mapContinent];
    if (!cont) { replace(mapHolder, h('div.muted', {}, T('No map data.'))); return; }
    const online = (data.bots || []).filter(b => b.online);
    const inCont = online.filter(b => String(b.map) === String(mapContinent) && inBounds(b, cont.bounds));
    const zoneCount = {};
    for (const b of inCont) if (b.zoneId) zoneCount[b.zoneId] = (zoneCount[b.zoneId] || 0) + 1;
    const zones = (cont.zones || []).slice().sort((a, b) => (zoneCount[b.areaId] || 0) - (zoneCount[a.areaId] || 0) || String(a.title || a.name).localeCompare(String(b.title || b.name)));
    const selected = zones.find(z => z.areaId === mapZone);
    const shown = selected ? inCont.filter(b => b.zoneId === selected.areaId) : inCont;

    const crop = contentBounds(cont);
    const box = svg('svg', {
      class: 'map-svg', role: 'img', 'aria-label': cont.name || T('World map'),
      viewBox: (crop.x * 1000).toFixed(1) + ' ' + (crop.y * 668).toFixed(1) + ' ' + Math.max(1, crop.w * 1000).toFixed(1) + ' ' + Math.max(1, crop.h * 668).toFixed(1)
    });
    box.appendChild(svg('rect', { x: 0, y: 0, width: 1000, height: 668, fill: 'var(--canvas)' }));
    for (const z of zones) {
      const id = z.areaId;
      const active = id === mapZone;
      const count = zoneCount[id] || 0;
      const r = svg('rect', {
        x: (z.x * 1000).toFixed(1), y: (z.y * 668).toFixed(1), width: Math.max(2, z.w * 1000).toFixed(1), height: Math.max(2, z.h * 668).toFixed(1),
        class: 'map-zone' + (active ? ' active' : '') + (count ? ' has' : ''),
        onclick: () => { mapZone = active ? 0 : id; drawMap(); }
      });
      r.appendChild(svg('title', {}, (z.title || z.name) + ' · ' + count));
      box.appendChild(r);
    }
    for (const b of shown) {
      const p = mapPoint(b, cont.bounds);
      if (p.x < 0 || p.y < 0 || p.x > 1000 || p.y > 668) continue;
      const dot = svg('circle', {
        cx: p.x.toFixed(1), cy: p.y.toFixed(1), r: selected ? 4.5 : 3.6, class: 'map-bot',
        fill: classColor(b.className), 'data-name': b.name, onclick: () => showBot(b)
      });
      dot.appendChild(svg('title', {}, b.name + (b.zone ? ' · ' + b.zone : '') + (b.task ? ' · ' + b.task : '')));
      box.appendChild(dot);
    }
    replace(mapHolder, box);

    const rows = zones.filter(z => zoneCount[z.areaId] > 0 || z.areaId === mapZone);
    const list = [h('button', { type: 'button', class: 'map-zone-row' + (mapZone === 0 ? ' active' : ''), onclick: () => { mapZone = 0; drawMap(); } },
      h('span.zn', {}, T('All zones')), h('span.zc', {}, String(inCont.length)))];
    for (const z of rows) {
      list.push(h('button', { type: 'button', class: 'map-zone-row' + (z.areaId === mapZone ? ' active' : ''), onclick: () => { mapZone = z.areaId; drawMap(); } },
        h('span.zn', {}, z.title || z.name), h('span.zc', {}, String(zoneCount[z.areaId] || 0))));
    }
    replace(mapSide, h('div.map-zone-list', {}, list));
    mapSub.textContent = F('{0} bots on {1}', shown.length, cont.name || T('this map'));
    const elsewhere = online.length - inCont.length;
    mapElsewhere.textContent = elsewhere > 0 ? F('{0} more bots are on another map or between maps.', elsewhere) : '';
  }

  function traitWord(value, low, mid, high) {
    return value < 0.333 ? T(low) : value < 0.666 ? T(mid) : T(high);
  }

  function traitRow(label, value, low, mid, high) {
    const p = Math.max(0, Math.min(100, Math.round((value || 0) * 100)));
    return h('div.trait-row', {},
      h('span.trait-name', {}, label),
      h('span.trait-bar', {}, h('i', { style: { width: p + '%' } })),
      h('span.trait-word', {}, traitWord(value, low, mid, high)));
  }

  function showBot(b) {
    const chatBox = h('div.small.muted', {}, T('Reading …'));
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
      [T('Position'), b.live || (b.x || b.y) ? F('Map {0} · {1}, {2}', b.map, Math.round(b.x), Math.round(b.y)) : '—']
    ];
    const detail = h('table.grid', {}, h('tbody', {}, rows.map(r => h('tr', {}, h('th', { style: { width: '130px' } }, r[0]), h('td', {}, r[1])))));
    const persona = b.persona ? b.persona + (b.personaStreak ? ' · ' + T('with a streak of') + ' ' + b.personaStreak : '') : T('not stored yet');
    const character = h('div', { style: { marginTop: '14px' } },
      h('h3', {}, T('Character')),
      h('div.small.muted', { style: { marginBottom: '8px' } }, T('Chat personality') + ': ' + persona),
      traitRow(T('Auction house'), b.trading, 'rarely there', 'sometimes there', 'lives there'),
      traitRow(T('Crafting'), b.crafting, 'little', 'sometimes', 'for profit'),
      traitRow(T('Order'), b.order, 'collects', 'balanced', 'tidies up'),
      traitRow(T('Patience'), b.patience, 'sells fast', 'balanced', 'waits for a price'),
      traitRow(T('Market knowledge'), b.knowledge, 'by feeling', 'some', 'knows the market'),
      traitRow(T('Talk'), b.talk, 'quiet', 'sometimes', 'talkative'));
    const body = h('div', {}, detail, character,
      b.live && b.quests && b.quests.length ? h('div', { style: { marginTop: '14px' } }, h('h3', {}, T('Quests')), h('div.small', {}, b.quests.join(' · '))) : null,
      h('h3', { style: { marginTop: '14px' } }, T('Recent chat')), chatBox);
    dialog({ title: b.name, icon: 'bots', body, buttons: [{ text: T('OK'), kind: 'primary', value: true }] });
    get('/api/bots/chat', { bot: b.name, limit: 10 }).then(r => {
      const lines = (r && r.lines) || [];
      replace(chatBox, lines.length ? lines.map(l => h('div.chat-line', {},
        h('span.chat-time', {}, (l.at || '').slice(11, 19)), h('span.chat-speaker', {}, l.speaker),
        h('span.chat-kind', {}, l.channel || chatKindLabel(l.kind)), h('span.chat-text', {}, l.text))) : h('span.muted', {}, T('No recent chat.')));
    }).catch(() => replace(chatBox, h('span.muted', {}, T('Chat could not be read.'))));
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
      if (view === 1) drawMap();
    } catch (e) { state.textContent = e.message; }
    finally { loading = false; }
  }

  async function loadChatLoot() {
    await Promise.allSettled([loadChat(), loadLoot(), loadLogs()]);
  }

  async function loadChat() {
    if (chatLoading) return;
    chatLoading = true;
    try {
      const params = { limit: 100 };
      if (chatKind) params.kind = chatKind;
      if (chatQuery) params.q = chatQuery;
      chatData = await get('/api/bots/chat', params);
      drawChat();
    } catch (e) {
      replace(chatBox, h('div.note.warn', {}, e.message || String(e)));
    } finally { chatLoading = false; }
  }

  function drawChat() {
    const lines = (chatData && chatData.lines) || [];
    chatSub.textContent = F('{0} lines', nf.format(lines.length));
    if (!lines.length) {
      replace(chatBox, h('div.muted', {}, (chatData && chatData.note) || T('No chat lines found.')));
      return;
    }
    replace(chatBox, lines.map(l => h('div.chat-line.chat-' + (l.kind || 'other'), {},
      h('span.chat-time', { title: l.at || '' }, (l.at || '').slice(11, 19)),
      h('span.chat-speaker', {}, l.speaker),
      h('span.chat-kind', {}, l.channel || chatKindLabel(l.kind)),
      h('span.chat-text', {}, (l.target ? h('span.muted', {}, T('to') + ' ' + l.target + ': ') : null), l.text))));
  }

  async function loadLoot() {
    try {
      lootData = await get('/api/bots/loot', { limit: 30 });
      drawLoot();
    } catch (e) {
      replace(lootBox, h('div.note.warn', {}, e.message || String(e)));
    }
  }

  function drawLoot() {
    const rows = (lootData && lootData.rows) || [];
    if (!rows.length) {
      replace(lootBox, h('div.muted', {}, (lootData && lootData.note) || T('No rare finds yet.')));
      return;
    }
    replace(lootBox, h('div.loot-list', {}, rows.map(l => h('div.loot-line', {},
      h('span.loot-time', {}, (l.at || '').slice(11, 19)),
      h('span.loot-item', { class: 'q' + Math.min(5, l.quality) }, l.item + (l.count > 1 ? ' ×' + l.count : '')),
      h('span.loot-bot', {}, l.bot)))));
  }

  async function loadLogs() {
    try {
      logData = await get('/api/bots/logs');
      drawLogs();
    } catch (e) {
      replace(logsBox, h('div.note.warn', {}, e.message || String(e)));
    }
  }

  async function loadPvp() {
    if (pvpLoading) return;
    pvpLoading = true;
    replace(pvpBox, h('div.muted', {}, T('Reading …')));
    try { pvpData = await get('/api/bots/pvp'); drawPvp(); }
    catch (e) { replace(pvpBox, h('div.note.warn', {}, e.message || String(e))); }
    finally { pvpLoading = false; }
  }

  function drawPvp() {
    if (!pvpData) { replace(pvpBox, h('div.muted', {}, T('Reading …'))); return; }
    const parts = [];
    if (pvpData.note) parts.push(h('div.note.warn', {}, pvpData.note));
    const smallTable = (title, columns, rows, empty) => {
      if (!rows.length) return h('div', {}, h('h3', {}, title), h('div.muted', {}, empty));
      return h('div', {}, h('h3', {}, title), table(columns, rows, {}));
    };
    parts.push(smallTable(T('Battlegrounds'), [
      { title: T('Type'), key: 'name' }, { title: T('Games'), key: 'games', cls: 'num' },
      { title: T('Alliance'), key: 'alliance', cls: 'num' }, { title: T('Horde'), key: 'horde', cls: 'num' },
      { title: T('Other'), key: 'other', cls: 'num' }
    ], pvpData.bg || [], T('No battlegrounds in the last 24 hours.')));
    parts.push(smallTable(T('Arenas'), [
      { title: T('Type'), key: 'name' }, { title: T('Fights'), key: 'fights', cls: 'num' },
      { title: T('Average'), render: r => r.averageSeconds + ' s', cls: 'num' },
      { title: T('Wins'), key: 'wins', cls: 'num' }, { title: T('Losses'), key: 'losses', cls: 'num' }
    ], pvpData.arena || [], T('No arenas in the last 24 hours.')));
    parts.push(smallTable(T('Arena teams'), [
      { title: T('Type'), key: 'name' }, { title: T('Teams'), key: 'count', cls: 'num' },
      { title: T('Average rating'), key: 'averageRating', cls: 'num' },
      { title: T('Games'), key: 'games', cls: 'num' }, { title: T('Wins'), key: 'wins', cls: 'num' }
    ], pvpData.teams || [], T('No arena teams.')));
    replace(pvpBox, parts);
  }

  async function loadAuction() {
    if (auctionLoading) return;
    auctionLoading = true;
    replace(auctionBox, h('div.muted', {}, T('Reading …')));
    try { auctionData = await get('/api/bots/auction'); drawAuction(); }
    catch (e) { replace(auctionBox, h('div.note.warn', {}, e.message || String(e))); }
    finally { auctionLoading = false; }
  }

  const gold = copper => (copper / 10000).toLocaleString(undefined, { maximumFractionDigits: 2 }) + ' g';

  function drawAuction() {
    if (!auctionData) { replace(auctionBox, h('div.muted', {}, T('Reading …'))); return; }
    const parts = [];
    if (auctionData.note) parts.push(h('div.note.warn', {}, auctionData.note));
    parts.push(h('div.stats', {}, h('div.stat', {}, h('div.tv', {}, nf.format(auctionData.total || 0)), h('div.tc', {}, T('auctions')))));
    if ((auctionData.houses || []).length) parts.push(table([
      { title: T('House'), key: 'name' }, { title: T('Auctions'), key: 'count', cls: 'num' },
      { title: T('From bots'), key: 'bots', cls: 'num' }, { title: T('Other'), key: 'other', cls: 'num' }
    ], auctionData.houses, {}));
    parts.push(h('h3', {}, T('Sellers')));
    if ((auctionData.sellers || []).length) parts.push(table([
      { title: T('Seller'), render: r => r.name + (r.bot ? ' · ' + T('bot') : '') },
      { title: T('Auctions'), key: 'count', cls: 'num' },
      { title: T('Buyout'), render: r => gold(r.buyout), cls: 'num' }
    ], auctionData.sellers, {}));
    else parts.push(h('div.muted', {}, T('No auctions on offer.')));
    parts.push(h('h3', {}, T('Most expensive')));
    if ((auctionData.top || []).length) parts.push(table([
      { title: T('Item'), key: 'item' }, { title: T('Seller'), key: 'seller' },
      { title: T('Buyout'), render: r => gold(r.buyout), cls: 'num' }
    ], auctionData.top, {}));
    else parts.push(h('div.muted', {}, T('No auctions on offer.')));
    replace(auctionBox, parts);
  }

  async function loadArmory(filter) {
    if (armoryLoading) return;
    const q = armorySearch.value.trim();
    if (q.length < 2) {
      replace(armoryBox, h('div.muted', {}, T('Type at least two letters of a character name.')));
      return;
    }
    armoryLoading = true;
    replace(armoryBox, h('div.muted', {}, T('Reading …')));
    try {
      armoryData = await get('/api/bots/armory', { q });
      const rows = armoryData.characters || [];
      replace(armoryBox, rows.length ? table([
        { title: T('Name'), key: 'name' }, { title: T('Level'), key: 'level', cls: 'num' },
        { title: T('Class'), key: 'className' }, { title: T('Race'), key: 'raceName' },
        { title: T('Zone'), render: r => r.zone || '—' },
        { title: T('Type'), render: r => r.bot ? T('bot') : T('player') }
      ], rows, { onpick: openArmory }) : h('div.muted', {}, armoryData.note || T('No character by that name.')));
    } catch (e) { replace(armoryBox, h('div.note.warn', {}, e.message || String(e))); }
    finally { armoryLoading = false; }
  }

  async function openArmory(row) {
    let sheet;
    try { sheet = await get('/api/bots/armory/character', { name: row.name }); }
    catch (e) { await alert(e.message || String(e)); return; }
    const slots = ['Head', 'Neck', 'Shoulder', 'Shirt', 'Chest', 'Waist', 'Legs', 'Feet', 'Wrists', 'Hands', 'Finger 1', 'Finger 2', 'Trinket 1', 'Trinket 2', 'Back', 'Main hand', 'Off hand', 'Ranged', 'Tabard'];
    const gear = (sheet.gear || []).map(g => ({ ...g, slotName: slots[g.slot] || String(g.slot) }));
    const body = h('div', {},
      h('div.small.muted', {}, F('{0} · {1} · {2} · {3}', sheet.raceName, sheet.className, F('Level {0}', sheet.level), sheet.zone || '—')),
      h('div.small.muted', {}, F('Average item level: {0}', sheet.averageItemLevel || '—')),
      gear.length ? table([
        { title: T('Slot'), key: 'slotName' },
        { title: T('Item'), render: g => h('span', { class: 'q' + Math.min(5, g.quality) }, g.item + (g.count > 1 ? ' ×' + g.count : '')) },
        { title: T('Item level'), key: 'itemLevel', cls: 'num' }
      ], gear, {}) : h('div.muted', {}, T('No equipped gear could be read.')),
      sheet.stats ? h('div', { style: { marginTop: '14px' } }, h('h3', {}, T('Stats')), h('div.small', {}, [
        F('Health {0}', nf.format(sheet.stats.maxHealth)), F('Mana {0}', nf.format(sheet.stats.maxPower)),
        F('Strength {0}', sheet.stats.strength), F('Agility {0}', sheet.stats.agility),
        F('Stamina {0}', sheet.stats.stamina), F('Intellect {0}', sheet.stats.intellect),
        F('Spirit {0}', sheet.stats.spirit), F('Armor {0}', nf.format(sheet.stats.armor))
      ].join(' · '))) : null);
    dialog({ title: sheet.name, icon: 'accounts', wide: true, body, buttons: [{ text: T('OK'), kind: 'primary', value: true }] });
  }

  function drawLogs() {
    if (!logData) { replace(logsBox, h('div.muted', {}, T('Reading …'))); return; }
    const missing = logData.missing || [];
    const box = h('div', {});
    if (missing.length) {
      box.appendChild(h('div.note.warn', {}, F('{0} journal lines are missing in worldserver.conf.', missing.length)));
      box.appendChild(h('div.small.muted', {}, missing.join(' · ')));
    } else {
      box.appendChild(h('div.small.muted', {}, T('The bot journals are switched on in worldserver.conf.')));
    }
    if (!logData.chatPresent) box.appendChild(h('div.small.muted', {}, T('Chat.log has not been written yet.')));
    if (!logData.lootPresent) box.appendChild(h('div.small.muted', {}, T('BotLoot.log has not been written yet.')));
    const players = check(T('Also log player chat'), false);
    const turnOn = primary(T('Turn on bot journals'), async () => {
      if (!await confirm(T('AFK Realm will add the journal appenders to worldserver.conf after making a backup. Restart the worldserver afterwards. Continue?'), T('Turn on bot journals'), T('Cancel'))) return;
      turnOn.disabled = true;
      try {
        const r = await post('/api/bots/logs', { players: players.checked });
        await alert(r.note + '\n\n' + F('Backup: {0}', r.backup));
        await loadLogs();
      } catch (e) { await alert(e.message || String(e)); }
      finally { turnOn.disabled = false; }
    }, 'play');
    box.appendChild(h('div.row', { style: { alignItems: 'center', flexWrap: 'wrap' } }, players, turnOn));
    replace(logsBox, box);
  }

  paint();
  drawRoster();
  setTimeout(refresh, 0);
  let timer = null, chatTimer = null;
  timer = setInterval(() => {
    if (!root.isConnected) {
      if (timer) clearInterval(timer);
      if (chatTimer) clearInterval(chatTimer);
      timer = chatTimer = null;
      return;
    }
    refresh();
  }, 30000);
  chatTimer = setInterval(() => {
    if (!root.isConnected) return;
    if (view === 3) loadChat();
  }, 6000);
  return root;
}
