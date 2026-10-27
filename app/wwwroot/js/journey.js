// The setup page of World Journey: the ranges of the parts of the world, zones and dungeons, the level window,
// the difficulty and what follows the journey.
import { h, T, F, meta, get, replace } from './core.js';
import { toggle as toggleEl, slider as sliderEl, segmented } from './ui.js';
import { setupPage, parseRange } from './setupkit.js';

const COLORS = { old: '#56a06e', outland: '#c46e3c', northrend: '#4682c8', end: '#6c4cc4' };

/** The rules of mod-world-journey (src/JourneyRules.cpp), so the page can show where zones and dungeons land. */
class Rules {
  constructor() { Object.assign(this, { cf: 1, ct: 35, of: 30, ot: 43, nf: 40, nt: 55, endgame: 60 }); this.zones = new Map(); this.instances = new Map(); }
  static outlandI = new Set([269, 540, 542, 543, 545, 546, 547, 552, 553, 554, 555, 556, 557, 558, 560, 585, 532, 534, 544, 548, 550, 564, 565, 568, 580]);
  static northrendI = new Set([574, 575, 576, 578, 595, 599, 600, 601, 602, 604, 608, 619, 632, 650, 658, 668, 249, 533, 603, 615, 616, 624, 631, 649, 724]);
  static classicRaids = new Set([309, 409, 469, 509, 531]);
  clamp(v) { return Math.max(1, Math.min(63, Math.sign(v) * Math.round(Math.abs(v)))); }
  lay(l, lo, hi, from, to) { return from + (l - lo) * (to - from) / (hi - lo); }
  classic(l) { return this.clamp(this.lay(l, 1, 55, this.cf, this.ct)); }
  outland(l) { return l < 58 ? this.classic(l) : this.clamp(this.lay(l, 58, 67, this.of, this.ot)); }
  northrend(l) { return l < 68 ? this.classic(l) : Math.min(this.clamp(this.lay(l, 68, 77, this.nf, this.nt)), this.endgame); }
  zone(zone, part, original) {
    if (this.zones.has(zone)) return Math.max(1, Math.min(this.endgame, this.zones.get(zone)));
    let entry = original <= 1 ? 1 : part === 1 ? this.outland(Math.max(original, 58)) : part === 2 ? this.northrend(Math.max(original, 68)) : this.classic(original);
    if (zone === 4080) entry = this.endgame - 5;
    return Math.max(1, Math.min(entry, Math.max(1, this.endgame - 5)));
  }
  instance(map, kind, original, content) {
    if (this.instances.has(map)) return Math.max(1, Math.min(this.endgame, this.instances.get(map)));
    const o = Rules.outlandI.has(map), n = Rules.northrendI.has(map);
    if (kind === 2 && Rules.classicRaids.has(map)) return original;
    if (kind !== 0) return this.endgame;
    let level = Math.max(content, original), entry;
    if (o) { if (map === 560) level = 66; entry = Math.max(this.of, this.outland(Math.min(level, 70) - 3)); }
    else if (n) entry = Math.max(this.nf, this.northrend(Math.min(level, 80) - 3));
    else entry = this.classic(Math.min(original + 3, 55));
    return Math.max(1, Math.min(entry, this.endgame));
  }
}

function journeyBar(rules) {
  const el = h('div.jbar');
  const X = l => ((l - 1) / 59 * 100);
  const lanes = [[T('Old world'), rules.cf, rules.ct, COLORS.old], [T('Outland'), rules.of, rules.ot, COLORS.outland],
    [T('Northrend'), rules.nf, rules.nt, COLORS.northrend], [T('Raids and heroics'), rules.endgame, rules.endgame, COLORS.end]];
  for (const [name, a, b, color] of lanes) {
    const lane = h('div.lane');
    if (a !== b) {
      lane.appendChild(h('i.light', { style: { left: X(a) + '%', width: (100 - X(a)) + '%', background: color } }));
      lane.appendChild(h('i.solid', { style: { left: X(a) + '%', width: Math.max(2, X(b) - X(a)) + '%', background: color } }));
      const label = h('span', { style: { left: 'calc(' + X(a) + '% + 6px)' } }, name + '  ' + a + '–' + b);
      lane.appendChild(label);
    } else {
      lane.appendChild(h('i.dot', { style: { left: 'calc(' + X(a) + '% - 10px)', background: color } }));
      lane.appendChild(h('span', { style: { right: 'calc(' + (100 - X(a)) + '% + 16px)', color } }, name + '  ' + a));
    }
    el.appendChild(lane);
  }
  el.appendChild(h('div.axis', {}, [1, 10, 20, 30, 40, 50, 60].map(l => h('b', { style: { left: X(l) + '%' } }, String(l)))));
  return el;
}

function windowPreview(on, below, above, level) {
  const span = 12, cells = [];
  for (let l = level - span; l <= level + span; l++) {
    const inWin = on ? l >= level - below && l <= level + above : l === level - 2;
    const elite = on && l === level + above + 1;
    cells.push(h('div.wcell', { class: l === level ? 'me' : inWin ? 'in' : elite ? 'elite' : '' }, h('i'), h('b', {}, l > 0 && (l % 5 === 0 || l === level) ? String(l) : '')));
  }
  return h('div', {}, h('div.wrow', {}, cells),
    h('div.small.muted', {}, on ? F('A character of level {0} meets the weakest creatures of a zone at {1}, the strongest at {2}, elites and rares up to {3}.', level, level - below, level + above, level + above + 1)
      : T('Without the window every lifted creature stands at the same level below the character (DestinyWeaver.Scaling.Offset).')));
}

const Parts = ['OldWorld', 'Outland', 'Northrend', 'Endgame'];
const Ranks = ['Normal', 'Elite', 'RareElite', 'WorldBoss', 'Rare'];
const Stats = ['Health', 'Damage', 'SpellDamage', 'Armor'];
const normal = [[0.85, 0.85, 0.85, 1.0], [1.0, 1.0, 1.0, 1.0], [1.2, 1.15, 1.15, 1.05], [1.5, 1.3, 1.3, 1.15]];
const rare = [[1.25, 1.1, 1.1, 1.0], [1.5, 1.25, 1.25, 1.0], [1.75, 1.4, 1.4, 1.05], [2.0, 1.6, 1.6, 1.15]];
const two = v => String(Math.round(v * 100) / 100);
const times = v => '×' + two(v);
const CoaSwitch = 'CoA.CreatureScaling.Enable';

export async function journeySetup() {
  let places = [];
  try { places = (await get('/api/setups/journey/places')).places; } catch { }
  const rules = new Rules();
  return setupPage({
    file: 'modules/mod_world_journey.conf', also: ['modules/coa.conf'], title: T('World Journey'), subtitle: T('mod-world-journey  ·  modules\\mod_world_journey.conf'),
    icon: 'journey', masterKey: 'Journey.Enable', masterText: T('World Journey on'),
    reset(p) {
      for (const [k, v] of p.conf.defaults) p.conf.set(k, v);
      for (const pl of places) p.conf.unset(key(pl));
      for (let part = 0; part < 4; part++) for (let r = 0; r < 5; r++) for (let c = 0; c < 4; c++) p.conf.unset(diffKey(part, r, c));
      for (const part of Parts) p.conf.unset('Journey.Quests.XpRate.' + part);
      const coa = p.also('modules/coa.conf');
      if (coa && coa.keys.includes(CoaSwitch)) coa.set(CoaSwitch, '0');
    },
    build(p) {
      const readRules = () => {
        let r;
        if ((r = parseRange(p.raw('Journey.Classic.Range', '1-35')))) [rules.cf, rules.ct] = r;
        if ((r = parseRange(p.raw('Journey.Outland.Range', '30-43')))) [rules.of, rules.ot] = r;
        if ((r = parseRange(p.raw('Journey.Northrend.Range', '40-55')))) [rules.nf, rules.nt] = r;
        rules.endgame = Math.max(50, Math.min(60, p.number('Journey.Endgame.Level', 60)));
        rules.zones.clear(); rules.instances.clear();
        for (const pl of places) if (p.has(key(pl))) (pl.instance ? rules.instances : rules.zones).set(pl.id, p.number(key(pl), pl.original));
      };
      const levelOf = pl => pl.instance ? rules.instance(pl.id, pl.kind, pl.original, pl.content) : rules.zone(pl.id, pl.part, pl.original);

      // ---- the journey
      p.tab(T('The journey'));
      const shape = p.card(T('Where each part of the world begins'), T('The zones of a part open over its range, in their original order; every zone stays worth playing until 60, because CoA lifts its creatures to a character. The ranges may overlap: that makes the journey a choice instead of a tube.'), 'journey');
      const barBox = shape.add(h('div'));
      p.range(shape, 'Journey.Classic.Range', T('Old world (was 1–55)'), 1, 60, null, COLORS.old);
      p.range(shape, 'Journey.Outland.Range', T('Outland (was 58–67)'), 1, 60, null, COLORS.outland);
      p.range(shape, 'Journey.Northrend.Range', T('Northrend (was 68–77)'), 1, 60, null, COLORS.northrend);
      const end = p.card(T('The end of the journey'), T('The raids and heroic dungeons of Outland and Northrend are played at the end, after those of the old world, which stay as they are.'), 'gamemaster');
      p.slider(end, 'Journey.Endgame.Level', T('Level of raids and heroics'), 50, 60, 1);
      p.slider(end, 'Journey.Endgame.BossLevels', T('Their bosses stand higher by'), 0, 3, 1, v => '+' + Math.trunc(v));
      p.range(end, 'Journey.Endgame.Outland.ItemLevels', T('Item levels of Outland\'s rewards'), 60, 200, T('The raids of the old world give 66 to 92: Outland\'s rewards come next on the ladder, Northrend\'s last.'));
      p.range(end, 'Journey.Endgame.Northrend.ItemLevels', T('Item levels of Northrend\'s rewards'), 60, 200);
      const guard = p.card(T('The rules of the realm'), null, 'settings');
      p.toggle(guard, 'Journey.Scaling.Forced', T('Every character plays the scaled world'), T('CoA\'s level scaling is on for everyone. The question at character creation still appears, but "off" no longer counts. Off: each character keeps the choice it made at creation.'));
      p.toggle(guard, 'Journey.Guard.Enable', T('Keep the realm at 60'), T('The realm runs with the highest level 60, the Dungeon Finder with the dungeons of Outland and Northrend and Wintergrasp from 60, whatever worldserver.conf says.'));

      // ---- zones and dungeons
      p.tab(T('Zones and dungeons'));
      const where = p.card(T('Zones and dungeons'), T('Where each one opens on the journey, worked out from the ranges. Set one by hand to move it: its creatures and quests move with it, and the bots and the world map follow.'), 'globe');
      let kind = 0, current = null, filling = false;
      const kindSeg = segmented([[0, T('Zones')], [1, T('Dungeons and raids')]], 0, v => { kind = +v; current = null; fillPlaces(); });
      const listBox = h('div.list', { style: { height: '330px' } });
      const placeName = h('div', { style: { fontWeight: '700', margin: '8px 0 2px' } });
      const byHand = toggleEl(T('Set this one by hand'), false, () => { if (!filling) setHand(); });
      const handLevel = sliderEl(1, 60, 1, 1, v => String(v), () => { if (!filling && byHand.checked) setHand(); });
      where.add(kindSeg, listBox, placeName, h('div', {}, byHand), h('div.setup-row', {}, h('label', {}, T('Opens at level')), handLevel));
      if (!places.length) p.note(T('The list of zones comes with the module (data\\journey-ui.json). Update World Journey to see it here.'));
      const partOf = pl => !pl.instance ? [T('Old world'), T('Outland'), T('Northrend'), T('Raids and heroics')][pl.part] : pl.kind === 2 ? T('Raid') : pl.kind === 1 ? T('Heroic') : T('Dungeon');
      function fillPlaces() {
        const list = places.filter(pl => pl.instance === (kind === 1)).sort((a, b) => levelOf(a) - levelOf(b) || a.name.localeCompare(b.name));
        const tbody = h('tbody');
        for (const pl of list) {
          const tr = h('tr.pick', { class: current === pl ? 'sel' : '', onclick: () => { current = pl; listBox.querySelectorAll('tr').forEach(x => x.classList.toggle('sel', x === tr)); showPlace(); } },
            h('td', {}, pl.name), h('td', {}, partOf(pl)), h('td', { style: { textAlign: 'right' } }, String(pl.original)), h('td.lvl', { style: { textAlign: 'right' } }, ''));
          tr.place = pl;
          tbody.appendChild(tr);
        }
        replace(listBox, h('table.grid', {}, h('thead', {}, h('tr', {}, h('th', {}, T('Name')), h('th', {}, T('Part')), h('th', {}, T('Originally')), h('th', {}, T('On the journey')))), tbody));
        refreshLevels(); showPlace();
      }
      function refreshLevels() {
        listBox.querySelectorAll('tbody tr').forEach(tr => {
          const pl = tr.place, manual = p.has(key(pl));
          tr.querySelector('.lvl').textContent = levelOf(pl) + (manual ? '  ' + T('(by hand)') : '');
          tr.style.color = manual ? 'var(--accent-text)' : '';
        });
      }
      function showPlace() {
        filling = true;
        const pl = current;
        byHand.disabled = !pl;
        placeName.textContent = !pl ? T('Choose a zone or dungeon in the list to move it.') : F('{0} – opens at {1} on the journey (originally {2})', pl.name, levelOf(pl), pl.original);
        byHand.checked = !!pl && p.has(key(pl));
        handLevel.value = pl ? levelOf(pl) : 1;
        handLevel.disabled = !pl || !byHand.checked;
        filling = false;
      }
      function setHand() {
        const pl = current;
        if (!pl) return;
        if (byHand.checked) p.set(key(pl), String(Math.trunc(handLevel.value))); else p.unset(key(pl));
        readRules();
        filling = true; handLevel.disabled = !byHand.checked; if (!byHand.checked) handLevel.value = levelOf(pl); filling = false;
        refreshLevels();
        placeName.textContent = F('{0} – opens at {1} on the journey (originally {2})', pl.name, levelOf(pl), pl.original);
      }

      // ---- level window
      p.tab(T('Level window'));
      const win = p.card(T('The level window'), T('Creatures lifted to a character keep their place in their zone: the weakest stand a little below the character, the strongest a little above, elites and rares one more. A creature is never set below its own level.'), 'server');
      const winOn = p.toggle(win, 'Journey.Window.Enable', T('Use the level window'));
      const below = p.slider(win, 'Journey.Window.Below', T('Weakest creatures below'), 0, 10, 1, v => '−' + Math.trunc(v));
      const above = p.slider(win, 'Journey.Window.Above', T('Strongest creatures above'), 0, 10, 1, v => '+' + Math.trunc(v));
      const previewBox = win.add(h('div', { style: { margin: '6px 0' } }));
      const tryLevel = p.slider(win, null, T('Try it with a character of level'), 10, 60, 1, v => String(v));
      tryLevel.value = 40;
      const showWindow = () => replace(previewBox, windowPreview(winOn.checked, Math.trunc(below.value), Math.trunc(above.value), Math.trunc(tryLevel.value)));
      tryLevel.range.addEventListener('input', showWindow);
      p.onChange(showWindow);
      p.note(F('The level window and the forced scaling need World Journey\'s change to the core, which {0} brings in when it builds the server.', meta.product));

      // ---- difficulty
      p.tab(T('Difficulty'));
      const coa = p.also('modules/coa.conf');
      const coaHasIt = !!coa && coa.keys.includes(CoaSwitch);
      const presets = p.card(T('How hard'), T('A starting point for the multipliers below. They come on top of the creatures\' base stats and of the Rate.Creature.* settings of the server.'), 'gamemaster');
      const preset = segmented([[0, T('Relaxed')], [1, T('Standard')], [2, T('Challenging')], [3, T('Hard')]], -1, v => applyPreset(+v));
      const extraHard = toggleEl(T('Extra hard'), false, v => p.set(CoaSwitch, v ? '1' : '0', coa));
      extraHard.disabled = !coaHasIt;
      presets.add(h('div.row', { style: { alignItems: 'center' } }, preset, extraHard),
        p.small(T('Relaxed: a bit softer than the original. Standard: as the module comes, rares stronger. Challenging and Hard: everything hits harder and lasts longer.')));
      const extraText = presets.add(p.small(''));
      p.loader(() => { extraHard.checked = coaHasIt && p.raw(CoaSwitch, '0', coa) === '1'; });
      p.onChange(() => {
        if (!coaHasIt) { extraText.textContent = T('Extra hard needs a CoA core from October 2026 or later, which has creature multipliers of its own. Update the server to use it.'); return; }
        const x = (k, fb) => '×' + p.raw('CoA.CreatureScaling.' + k, fb, coa);
        extraText.textContent = F('Extra hard adds CoA\'s own creature multipliers (coa.conf) on top of the level chosen here: in the open world creatures have {0} health and hit players and pets {1} as hard, in dungeons {2} health (some maps more) and {3} damage, in raids {4} health. The values themselves are set in coa.conf (Settings).',
          x('World.Health', '2.5'), x('World.Damage', '2.0'), x('Dungeon.Health', '2.5'), x('Dungeon.Damage', '1.5'), x('Raid.Health', '2.2'));
        extraText.className = 'small ' + (extraHard.checked ? 'bad' : 'muted');
      });

      const grid = p.card(T('Multipliers by rank'), null, 'settings');
      let diffPart = -1;
      const partSeg = segmented([[-1, T('All parts')], [0, T('Old world')], [1, T('Outland')], [2, T('Northrend')], [3, T('Raids and heroics')]], -1, v => { diffPart = +v; showDiff(); });
      const partOwn = toggleEl(T('Own values for this part of the world'), false, v => { if (!filling) setPartOwn(v); });
      const ownBox = h('div', {}, partOwn);
      const sliders = [];
      const table = h('div.diffgrid', {}, h('span'), [T('Health'), T('Damage'), T('Spell damage'), T('Armor')].map(s => h('span.small.muted', {}, s)));
      const rankNames = [T('Normal'), T('Elite'), T('Rare elite'), T('World boss'), T('Rare')];
      for (let r = 0; r < 5; r++) {
        table.appendChild(h('span', {}, rankNames[r]));
        sliders[r] = [];
        for (let c = 0; c < 4; c++) {
          const s = sliderEl(0.25, 3, 0.05, 1, times, v => setDiff(r, c, v));
          sliders[r][c] = s;
          table.appendChild(s);
        }
      }
      grid.add(partSeg, ownBox, table);
      const general = (r, c) => p.number(diffKey(-1, r, c), 1);
      const partHasOwn = part => { for (let r = 0; r < 5; r++) for (let c = 0; c < 4; c++) if (p.has(diffKey(part, r, c))) return true; return false; };
      function showDiff() {
        filling = true;
        ownBox.style.display = diffPart >= 0 ? '' : 'none';
        const own = diffPart >= 0 && partHasOwn(diffPart);
        partOwn.checked = own;
        for (let r = 0; r < 5; r++) for (let c = 0; c < 4; c++) {
          sliders[r][c].value = diffPart >= 0 && own ? p.number(diffKey(diffPart, r, c), general(r, c)) : general(r, c);
          sliders[r][c].disabled = !(diffPart < 0 || own);
        }
        filling = false;
      }
      function setDiff(r, c, v) {
        if (filling || p.loading) return;
        if (diffPart >= 0 && !partOwn.checked) return;
        p.set(diffKey(diffPart, r, c), two(v));
        preset.set(matching());
      }
      function setPartOwn(on) {
        if (diffPart < 0) return;
        for (let r = 0; r < 5; r++) for (let c = 0; c < 4; c++) if (on) p.set(diffKey(diffPart, r, c), two(general(r, c))); else p.unset(diffKey(diffPart, r, c));
        showDiff();
      }
      function matching() {
        for (let part = 0; part < 4; part++) if (partHasOwn(part)) return -1;
        for (let w = 0; w < 4; w++) {
          let all = true;
          for (let r = 0; r < 5 && all; r++) for (let c = 0; c < 4 && all; c++) all = Math.abs(general(r, c) - ((r === 2 || r === 4) ? rare[w][c] : normal[w][c])) < 0.001;
          if (all) return w;
        }
        return -1;
      }
      function applyPreset(w) {
        if (filling || w < 0) return;
        for (let r = 0; r < 5; r++) for (let c = 0; c < 4; c++) p.set(diffKey(-1, r, c), two((r === 2 || r === 4) ? rare[w][c] : normal[w][c]));
        for (let part = 0; part < 4; part++) for (let r = 0; r < 5; r++) for (let c = 0; c < 4; c++) p.unset(diffKey(part, r, c));
        diffPart = -1; partSeg.set(-1);
        showDiff();
        const m = matching(); preset.set(m < 0 ? w : m);
      }

      const creatures = p.card(T('Creatures on the way'), null, 'server');
      p.choice(creatures, 'Journey.Creatures.BaseStats', T('Base stats of Outland and Northrend'), ['journey', 'original'], [T('The old world\'s (recommended)'), T('Their own game\'s')],
        T('Those of the old world rise evenly to 60; the tables of Outland and Northrend have gaps and jumps below 60. Raids and heroics always keep those of their game.'));
      const xp = p.card(T('Quest experience'), T('On top of Rate.XP.Quest of the server.'), 'journey');
      p.slider(xp, 'Journey.Quests.XpRate', T('All quests'), 0, 3, 0.05, times);
      const alone = p.card(T('Dungeons alone'), T('A player who ticks "Go alone" in the Dungeon Finder (or types .journey solo) gets his dungeon at once, just for him; without the tick the Dungeon Finder waits for a group of five as always. Only in a dungeon entered that way do the creatures get the factors below - other instances of the same dungeon stay as they are.'), 'gamemaster');
      p.toggle(alone, 'Journey.SoloDungeon.Enable', T('Players may go into dungeons alone'));
      p.slider(alone, 'Journey.SoloDungeon.Health', T('Health of the creatures'), 0.1, 1.5, 0.05, times);
      p.slider(alone, 'Journey.SoloDungeon.Damage', T('Their melee damage'), 0.1, 1.5, 0.05, times);
      p.slider(alone, 'Journey.SoloDungeon.SpellDamage', T('Their spell damage'), 0.1, 1.5, 0.05, times);
      p.slider(alone, 'Journey.SoloDungeon.Boss.Health', T('Health of the bosses'), 0.1, 1.5, 0.05, times);
      p.slider(alone, 'Journey.SoloDungeon.Boss.Damage', T('Melee damage of the bosses'), 0.1, 1.5, 0.05, times);
      p.slider(alone, 'Journey.SoloDungeon.Boss.SpellDamage', T('Spell damage of the bosses'), 0.1, 1.5, 0.05, times);
      alone.add(p.small(F('For comparison: mod-autobalance gives a single player about ×0.66 of health and damage. The tick in the Dungeon Finder needs the add-on ZoneLevels, which {0} puts into your game; the Dungeon Finder needs World Journey\'s change to the core, which {0} brings in when it builds the server.', meta.product)));

      // ---- what follows
      p.tab(T('What follows'));
      const follows = p.card(T('What follows the journey'), T('Switch off what should stay as it came.'), 'modules');
      p.toggle(follows, 'Journey.Items.Enable', T('Items: required level, item level, stats, armor, damage and price'));
      p.toggle(follows, 'Journey.Consumables.Enable', T('Potions, food, elixirs, flasks and the spells of trinkets and procs'));
      p.toggle(follows, 'Journey.Enchantments.Enable', T('Gems, socket bonuses and the enchantments of Outland\'s and Northrend\'s professions'));
      p.toggle(follows, 'Journey.Trainers.Enable', T('Riding and the professions (riding at 13 and 26, flying at 33 and 47)'));
      p.toggle(follows, 'Journey.Battlegrounds.Enable', T('Eye of the Storm, Strand of the Ancients, Isle of Conquest and Wintergrasp open at 60'));
      const client = p.card(T('In the game client'), F('Needs the add-on ZoneLevels, which {0} puts into your game (Modules › Game client add-ons).', meta.product), 'client');
      p.toggle(client, 'Journey.Map.Enable', T('The world map shows the zone levels of the journey'));
      p.toggle(client, 'Journey.Tooltips.Enable', T('Tooltips show the real values of items\' spells, gems and enchantments'));
      const debug = p.card(T('For testing'), null, 'maintenance');
      p.toggle(debug, 'Journey.Debug', T('More detail in the server log'));

      p.loader(() => { readRules(); fillPlaces(); showDiff(); preset.set(matching()); });
      p.onChange(() => { readRules(); replace(barBox, journeyBar(rules)); if (!filling) refreshLevels(); });
    }
  });
}

function key(pl) { return (pl.instance ? 'Journey.Instance.' : 'Journey.Zone.') + pl.id; }
function diffKey(part, rank, stat) { return 'Journey.Difficulty.' + (part < 0 ? '' : Parts[part] + '.') + Ranks[rank] + '.' + Stats[stat]; }
