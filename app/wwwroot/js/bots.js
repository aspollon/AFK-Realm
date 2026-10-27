// The Bots section: counts and distributions of the random bots, read from the database every 30 seconds.
import { h, T, F, get, replace } from './core.js';
import { card, secondary, segmented } from './ui.js';

const nf = new Intl.NumberFormat();

function bars(items, total, colors) {
  if (!items.length) return h('div.muted', {}, T('No bots to count.'));
  const max = Math.max(1, ...items.map(i => i.value));
  return h('div.bars', {}, items.map((it, i) => h('div.barrow', {},
    h('span.name', { title: it.name }, it.name),
    h('span.track', {}, h('i', { style: { width: it.value <= 0 ? '0' : Math.max(1.5, 100 * it.value / max) + '%', background: (colors && colors[i]) || (it.other ? 'var(--muted)' : 'var(--accent)') } })),
    h('span.num', {}, nf.format(it.value) + (total > 0 ? '  ' + Math.round(100 * it.value / total) + ' %' : '')))));
}

export function dashboard() {
  let all = false, loading = false, timer = null;
  const tile = caption => { const v = h('div.tv', {}, '–'); return [h('div.stat', {}, v, h('div.tc', {}, caption)), v]; };
  const [t1, world] = tile(T('bots in the world')), [t2, players] = tile(T('players in the world')), [t3, level] = tile(T('average level')), [t4, sides] = tile(T('Alliance : Horde'));
  const state = h('span.small.muted');
  const mode = segmented([[0, T('Bots in the world')], [1, T('All bots')]], 0, v => { all = +v === 1; refresh(); });
  const boxes = { faction: h('div'), classes: h('div'), races: h('div'), levels: h('div'), zones: h('div') };
  const root = h('div', {},
    card(T('The random bots'), T('How many bots there are and how they are spread. The numbers come from the database; the server writes positions and levels of the bots every few minutes, so they can be a little behind.'), 'bots',
      h('div.stats', {}, t1, t2, t3, t4),
      h('div.row', { style: { alignItems: 'center' } }, h('span.muted', {}, T('Count:')), mode, secondary(T('Refresh'), () => refresh()), state)),
    card(T('Factions'), null, null, boxes.faction),
    card(T('Classes'), null, null, boxes.classes),
    card(T('Races'), null, null, boxes.races),
    card(T('Levels'), null, null, boxes.levels),
    card(T('Zones'), T('Where the bots are, the fifteen zones with the most of them.'), 'globe', boxes.zones));

  async function refresh() {
    if (!root.isConnected && timer) { clearInterval(timer); timer = null; return; }
    if (loading) return;
    loading = true;
    state.textContent = T('Reading …');
    try {
      const s = await get('/api/bots/stats', all ? { all: 1 } : undefined);
      world.textContent = nf.format(s.inWorld) + ' / ' + nf.format(s.total);
      players.textContent = nf.format(s.players);
      level.textContent = s.average === '–' ? '–' : Number(s.average).toLocaleString(undefined, { minimumFractionDigits: 1, maximumFractionDigits: 1 });
      sides.textContent = nf.format(s.alliance) + ' : ' + nf.format(s.horde);
      replace(boxes.faction, bars([{ name: T('Alliance'), value: s.alliance }, { name: T('Horde'), value: s.horde }], s.counted, ['#2e60b2', '#b22e2e']));
      replace(boxes.classes, bars(s.classes, s.counted));
      replace(boxes.races, bars(s.races, s.counted));
      replace(boxes.levels, bars(s.levels, s.counted));
      replace(boxes.zones, bars(s.zones, s.counted));
      state.textContent = F('Updated {0} · every 30 s', s.time);
    } catch (e) { state.textContent = e.message; }
    finally { loading = false; }
  }
  setTimeout(refresh, 0);
  timer = setInterval(refresh, 30000);
  return root;
}
