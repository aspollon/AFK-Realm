// fueryin's Ultra-HD pack and LuxVelum in a CoA Custom game: install and switch the parts on or off.
import { h, T, F, meta, post, replace } from './core.js';
import { primary, secondary, dialog, alert, confirm, toggle, input, choose, select, runProgress } from './ui.js';

export async function hdDialog(folder) {
  let v;
  try { v = await post('/api/hd/look', { folder }); } catch (e) { return alert(e.message); }
  const pack = input(v.pack, { style: { flex: '1', minWidth: '300px' } });
  const state = h('div.small');
  const warnings = h('div');
  const world = toggle(T('HD world: buildings, objects and terrain (about 8.5 GB)'), v.world);
  const orcs = toggle(T('Upright orcs (male orc model)'), v.orcs);
  const shaders = toggle(T('Better shaders and fonts'), v.shaders);
  const effects = toggle(T('Better spell effects'), v.effects);
  const sounds = toggle(T('Better sounds and music'), v.sounds);
  const blood = toggle(T('More blood when hitting'), v.blood);
  const cursor = toggle(T('Retail mouse cursors'), v.cursor);
  const visuals = select([
    [0, T('No graphics effect (best for weak computers)')],
    [1, T('Lumenveil (standard)')],
    [2, T('LuxVelum: fog, light and water (Ctrl+F7 in game for its settings)')]
  ], v.visuals ?? 1);
  const visualsRow = h('div', { style: { display: 'flex', gap: '6px', alignItems: 'center', margin: '6px 0' } },
    h('span.small', {}, T('Graphics effect')), visuals);
  const addons = toggle(T('The pack\'s add-ons: dance music, Bloody Screen, CoA Tooltip, battleground scoreboard, Leatrix Plus'), v.addons);

  function show(x) {
    replace(state, x.state); state.className = x.ready ? 'small muted' : 'small warn';
    replace(warnings, ...(x.warnings || []).map(w => h('p.small.warn', {}, w)));
    if (x.pack && !pack.value.trim()) pack.value = x.pack;
  }
  show(v);

  const values = () => ({
    folder, pack: pack.value.trim(), world: world.checked, orcs: orcs.checked, shaders: shaders.checked,
    effects: effects.checked, sounds: sounds.checked, blood: blood.checked, cursor: cursor.checked,
    visuals: Number(visuals.value), addons: addons.checked
  });

  async function refresh() {
    try { v = await post('/api/hd/look', { folder, pack: pack.value.trim() }); show(v); } catch { }
  }
  pack.addEventListener('change', refresh);

  async function browse() {
    const p = await choose({ title: T('Choose the unpacked HD pack folder (it contains the "Data" folder)'), start: pack.value.trim() || '' });
    if (p) { pack.value = p; await refresh(); }
  }

  async function install() {
    const j = await runProgress(() => post('/api/hd/install', values()), T('Installing the HD pack …'));
    if (j && j.ok) await alert(j.message);
    await refresh();
  }

  async function remove() {
    if (!await confirm(T('Remove the HD pack, LuxVelum and the pack\'s add-ons from your game?'))) return;
    try { const r = await post('/api/hd/remove', { folder }); await alert(r.message); } catch (e) { await alert(e.message); }
    await refresh();
  }

  const body = h('div', {},
    h('p.muted', {}, T('fueryin\'s Ultra-HD pack for Conquest of Azeroth. Download it from its page (Google Drive), unpack it, then choose its folder here. Its archives get names that do not clash with CoA Custom; its own CoA64.exe, d3d9.dll and Config.wtf are left out. Every part can be switched off again, for example on a weak laptop.')),
    h('p', {}, h('a', { href: v.page, target: '_blank', rel: 'noopener' }, T('Open the download page of the HD pack'))),
    h('div', { style: { display: 'flex', gap: '6px', alignItems: 'center', margin: '6px 0' } },
      h('span.small', {}, T('Pack folder')), pack, meta.remote ? null : secondary(T('Browse …'), browse)),
    world, orcs, shaders, effects, sounds, blood, cursor, h('div', { style: { height: '8px' } }), visualsRow, addons,
    h('p.small.muted', {}, T('ReShade (the pack\'s screen filter) is optional and not installed here. Races without their own dance song get the song of the race they come from; /dancemusic test plays yours.')),
    warnings, state);
  const installBtn = primary(T('Install / apply'), install);
  installBtn.disabled = !v.ready;
  await dialog({
    title: meta.product + ' – ' + T('HD pack'), icon: 'client', body,
    left: [installBtn, secondary(T('Remove'), remove)],
    buttons: [{ text: T('Close'), value: null }]
  });
}
