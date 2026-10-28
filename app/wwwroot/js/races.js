// The setup page of mod-custom-races: which races of the game client and of the race folder can be played.
import { h, T, F, meta, post, replace } from './core.js';
import { icon } from './icons.js';
import { primary, secondary, ghost, dialog, alert, confirm, toggle as toggleEl, select, choose, runProgress } from './ui.js';

export async function racesDialog() {
  let data;
  try { data = await post('/api/races/open'); } catch (e) { return alert(e.message); }
  let view = data.view, closeIt;
  const content = h('div.setup-cards');
  const changes = h('span.small');
  const master = toggleEl(T('Custom races on'), view.enable, v => act({ action: 'flag', option: 'CustomRaces.Enable', on: v }));

  async function act(body) {
    let r;
    try { r = await post('/api/races/act', body); } catch (e) { await alert(e.message); paint(); return null; }
    if (r.ask) {
      const answer = await dialog({ title: meta.product, icon: 'warn', body: h('div.pre', {}, r.ask.text),
        buttons: r.ask.answers.map((x, i) => ({ text: x.text, value: x.value, kind: i === r.ask.answers.length - 1 ? 'primary' : '' })) });
      if (!answer || answer === 'cancel') { paint(); return null; }
      if (body.action === 'move') return act(Object.assign({}, body, { step: +answer }));
      return act(Object.assign({}, body, { answer }));
    }
    view = r.view; paint();
    if (r.message) await alert(r.message);
    return r;
  }

  const card = (title, text, ic, ...kids) => h('div.card.setup', {}, h('h2', {}, ic ? h('span.ic', {}, icon(ic)) : null, title), text ? h('p.lead', {}, text) : null, ...kids);
  const small = (t, indent) => h('div.small.muted', { style: { margin: '0 0 10px ' + (indent || 0) + 'px' } }, t);

  const side = x => x === 'alliance' ? T('Alliance') : x === 'horde' ? T('Horde') : T('Both sides');

  // One line per race: on/off, side, men or women only, how it starts, where its looks come from, and its state.
  function raceLine(r) {
    const t = toggleEl(r.name, r.on, v => act({ action: 'toggle', key: r.key, on: v }));
    const startsLike = select(view.bases.map(b => [b.key, b.name]), r.base, v => act({ action: 'base', key: r.key, value: v }));
    startsLike.title = T('Start area, reputation, first quests and abilities of this race');
    const line = h('div.race-line', {}, t,
      h('span.meta', {}, [r.sex, F('from {0}', r.from)].filter(Boolean).join('  ·  ')),
      h('span.grow'),
      h('span.meta', {}, T('starts like')), startsLike,
      h('span', { style: { minWidth: '170px', textAlign: 'right' } }, r.stateText ? h('span.chip.' + r.state, { title: r.state === 'decide' ? T('See the box at the top of the page') : '' }, r.stateText) : null));
    const out = [line];
    if (r.variant) out.push(h('div.small.muted', { style: { margin: '-2px 0 8px 46px' } }, r.variant.text + '  ',
      h('a', { href: '#', onclick: e => { e.preventDefault(); act({ action: 'alt', key: r.key, on: !r.variant.on }); } }, r.variant.swap)));
    return out;
  }

  // What AFK Realm cannot decide alone, in plain words, the recommended way first.
  function decision(c) {
    return h('div.decision', {}, h('h3', {}, c.title), h('p', {}, c.text),
      h('div.row', {}, (c.answers || []).map(a => (a.primary ? primary : secondary)(a.text, () => answer(a.value)))));
  }

  async function answer(value) {
    if (value === 'coa-download') return downloadCoa();
    if (value === 'coa-folder') return importRaces();
    if (value === 'more') return act({ action: 'flag', option: 'CustomRaces.MoreRaces', on: true });
    if (value.startsWith('off:')) return act({ action: 'toggle', key: value.substring(4), on: false });
  }

  // The tools for those who know what race numbers are: closed unless opened.
  function advanced() {
    const rows = view.list.filter(r => r.pack || r.canFree || r.canMove || r.variant).map(r => h('div.row', { style: { alignItems: 'center', margin: '4px 0' } },
      h('span', { style: { minWidth: '200px' } }, r.name), h('span.small.muted', { style: { minWidth: '170px' } }, r.number),
      r.canFree ? (() => { const b = ghost(T('Free its number'), async () => {
        if (!await confirm(F('Give up the race number of the {0}? Its characters stay their base race for good, and another race can take the number.', r.name))) return;
        act({ action: 'free', key: r.key });
      }); b.disabled = !r.freeEnabled; return b; })() : null,
      r.canMove ? ghost(T('Move to a free number'), () => act({ action: 'move', key: r.key, step: 0 })) : null,
      r.pack ? ghost(T('Delete from the race folder'), async () => {
        if (!await confirm(F('Delete {0} from the race folder?', r.name) + '\n\n' + T('Its files are removed from disk; if the race is switched on, it is switched off first, its race number becomes free for another race, and its characters play their base race from now on. This cannot be undone.'))) return;
        act({ action: 'delete', key: r.key });
      }) : null,
      r.variant ? ghost(T('Delete these looks from the race folder'), async () => {
        if (!await confirm(F('Delete the race folder looks of {0}?', r.name) + '\n\n' + T('Its files are removed from disk; where the race used the folder looks, it shows the looks of your game client again. This cannot be undone.'))) return;
        act({ action: 'deleteAlt', key: r.key });
      }) : null));
    return h('details.advanced', {}, h('summary', {}, T('Advanced: race numbers and race files')),
      small(T('AFK Realm gives every race its number and switches the 64-race patch and CoA Custom\'s game on by itself. Change these only if you know why.')),
      h('div', {}, toggleEl(T('Use the 64-race patch (dinput8.dll)'), view.more, v => act({ action: 'flag', option: 'CustomRaces.MoreRaces', on: v }))),
      h('div', {}, toggleEl(T('Use CoA Custom\'s race numbers above 63'), view.high, v => act({ action: 'flag', option: 'CustomRaces.HighRaces', on: v }))),
      h('div.row', { style: { margin: '8px 0' } }, secondary(T('Show race IDs …'), () => raceIds())),
      small(view.import), ...rows);
  }

  function paint() {
    master.checked = view.enable;
    changes.textContent = view.unsaved === 0 ? '' : view.unsaved === 1 ? T('1 change not saved yet') : F('{0} changes not saved yet', view.unsaved);
    changes.className = 'small ' + (view.unsaved ? 'warn' : 'muted');
    const scroll = content.parentElement ? content.parentElement.scrollTop : 0;
    const groups = ['alliance', 'horde', ''].map(sd => ({ sd, items: view.list.filter(r => r.side === sd) })).filter(g => g.items.length);
    replace(content,
      card(T('Races in your game'), T('Switch on the races you want. Each one starts like the race you choose under "starts like" (start area, reputation, first quests) and keeps its own looks. AFK Realm takes care of race numbers, files and repairs; when something needs you, it says so at the top.'), 'accounts',
        view.problem ? small(view.problem) : h('div', { style: { fontWeight: '700', margin: '4px 0 8px' } }, F('{0} of {1} races on', view.onCount, view.capacity)),
        view.knownError ? h('div.small.bad', {}, view.knownError) : null,
        h('div.row', {}, primary(T('Add races …'), () => setup()), view.hasCoa ? null : secondary(view.coaButton, () => downloadCoa()), secondary(T('Check the races …'), () => checkRaces()))),
      view.conflicts.length ? card(T('Please decide'), T('AFK Realm cannot decide these alone. The first button is the recommended way.'), 'warn', ...view.conflicts.map(decision)) : null,
      card(T('All races'), null, 'client', ...groups.map(g => [h('div.side-head', {}, side(g.sd)), ...g.items.map(raceLine)]).flat(2)),
      card(T('After a change'), null, 'client', small(T('Save, then open Modules › Game client and choose Install: AFK Realm checks and repairs every race on the way and builds the game patch. Other players need the new ZIP. A race you switch off keeps its characters as the race it starts like until you switch it on again.')), advanced()));
    if (content.parentElement) content.parentElement.scrollTop = scroll;
  }

  // The guided way in: where the races come from, which ones, what needs a decision, into the game.
  async function setup() {
    const steps = [T('1 Get races'), T('2 Choose'), T('3 Check'), T('4 Into the game')];
    const bar = n => h('div.wizard-steps', {}, steps.map((x, i) => h('span' + (i === n ? '.now' : ''), {}, x)));
    // 1: where the races come from
    if (!view.list.some(r => r.pack || r.variant)) {
      const how = await dialog({ title: T('Add races'), icon: 'races', body: h('div', {}, bar(0),
        h('p', {}, T('Most extra races come from CoA Custom, a free race package for Conquest of Azeroth. AFK Realm downloads it once and takes the races from it - nothing else changes in your game.')),
        h('p.small.muted', {}, T('Already have CoA Custom or another client with races? Choose its folder instead.'))),
        buttons: [{ text: T('Cancel'), value: null }, ...(view.list.length ? [{ text: T('Only the races of my game'), value: 'skip' }] : []),
          { text: T('Choose a folder …'), value: 'folder' }, { text: T('Download CoA Custom (recommended)'), kind: 'primary', value: 'coa' }] });
      if (!how) return;
      if (how === 'coa') await downloadCoa(); else if (how === 'folder') await importRaces();
      if (how !== 'skip' && !view.list.some(r => r.pack || r.variant)) return;
    }
    // 2: which races
    const boxes = view.list.map(r => ({ r, box: h('input', { type: 'checkbox', checked: r.on }) }));
    const count = h('div.small', {});
    const recount = () => { const n = boxes.filter(b => b.box.checked).length; count.textContent = F('{0} of {1} races chosen', n, view.capacity); count.className = 'small ' + (n > view.capacity ? 'warn' : 'muted'); };
    boxes.forEach(b => b.box.addEventListener('change', recount)); recount();
    const pick = await dialog({ title: T('Add races'), icon: 'races', wide: true, body: h('div', {}, bar(1),
      h('p', {}, T('Tick the races players may choose. You can change this any time.')), count,
      h('div.list', { style: { maxHeight: '48vh', padding: '6px 10px' } }, ['alliance', 'horde', ''].map(sd => {
        const items = boxes.filter(b => b.r.side === sd);
        return items.length ? [h('div.side-head', {}, side(sd)), ...items.map(({ r, box }) => h('label.check', { style: { display: 'flex', margin: '3px 0' } }, box, h('span', {}, r.name + (r.sex ? '   (' + r.sex + ')' : ''))))] : null;
      }).flat())),
      buttons: [{ text: T('Cancel'), value: null }, { text: T('Next'), kind: 'primary', value: true }] });
    if (!pick) return;
    for (const { r, box } of boxes) if (box.checked !== r.on) { const res = await act({ action: 'toggle', key: r.key, on: box.checked }); if (res === null) break; }
    // 3: what needs a decision
    while (view.conflicts.length) {
      const c = view.conflicts[0];
      const v = await dialog({ title: T('Add races'), icon: 'warn', body: h('div', {}, bar(2), h('h3', {}, c.title), h('p', {}, c.text)),
        buttons: [{ text: T('Later'), value: null }, ...(c.answers || []).slice().reverse().map(a => ({ text: a.text, value: a.value, kind: a.primary ? 'primary' : '' }))] });
      if (!v) break;
      await answer(v);
    }
    // 4: save and into the game
    if (!await save(true)) return;
    const go = await dialog({ title: T('Add races'), icon: 'races', body: h('div', {}, bar(3),
      h('p', {}, view.conflicts.length ? T('Saved. Some races still need your decision (see the top of the page); the others can go into the game now.') : T('Saved. Now the races go into your game: AFK Realm checks and repairs every race on the way.')),
      h('p.small.muted', {}, T('Close the game first. Restart the server afterwards so it knows the races too.'))),
      buttons: [{ text: T('Later'), value: null }, { text: T('Install into the game now'), kind: 'primary', value: true }] });
    if (!go) return;
    if (!view.clientFolder) return alert(T('Choose your game client first (Game client).'));
    const j = await runProgress(() => post('/api/client/install', { folder: view.clientFolder, clearCache: true }), T('Installing into your game …'));
    if (j && j.message) await alert(j.message);
  }

  async function raceIds() {
    let r;
    try { r = await post('/api/races/ids'); } catch (e) { return alert(e.message); }
    dialog({ title: T('Race IDs of this realm'), icon: 'races', wide: true, body: h('pre.small', { style: { maxHeight: '60vh', overflow: 'auto', whiteSpace: 'pre', tabSize: 8 } }, r.text),
      buttons: [{ text: T('Copy'), onclick: async () => { try { await navigator.clipboard.writeText(r.text); } catch { } return false; } }, { text: T('Close'), kind: 'primary', value: true }] });
  }

  async function checkRaces() {
    let r;
    try { r = await post('/api/races/check'); } catch (e) { return alert(e.message); }
    dialog({ title: T('Race check'), icon: 'races', wide: true,
      body: h('div', {}, h('p.small.muted', {}, T('Every race that is switched on, both sexes, against what your game client reads now: model, skins, faces, HD rows, textures, helmets and Esteria numbers. Install the client patch first after a change.')),
        h('pre.small', { style: { maxHeight: '60vh', overflow: 'auto', whiteSpace: 'pre-wrap' } }, r.text)),
      buttons: [{ text: T('Copy'), onclick: async () => { try { await navigator.clipboard.writeText(r.text); } catch { } return false; } }, { text: T('Close'), kind: 'primary', value: true }] });
  }

  async function downloadCoa() {
    let q;
    try { q = await post('/api/races/coa/question'); } catch (e) { return alert(e.message); }
    if (!await confirm(q.text)) return;
    const j = await runProgress(() => post('/api/races/coa/download'), q.title);
    if (j && j.ok && j.payload) { await reopen(); importRaces(j.payload.root); }
  }

  async function reopen() {
    try { const r = await post('/api/races/open'); view = r.view; paint(); post('/api/races/coa/check').then(x => { view = x.view; paint(); }).catch(() => { }); } catch (e) { alert(e.message); }
  }

  async function importRaces(picked) {
    if (!picked) {
      picked = await choose({ title: T('The game client or repack with the race archives') });
      if (!picked) return;
    }
    let r;
    try { r = await post('/api/races/pack/open', { folder: picked }); } catch (e) { return alert(e.message); }
    view = r.view; paint();
    const boxes = r.races.map(x => ({ x, box: h('input', { type: 'checkbox' }) }));
    const status = h('div.small.muted.pre', {}, r.status);
    let made = 0;
    await dialog({
      title: meta.product + ' – ' + T('Race packs from another client'), icon: 'races', wide: true,
      body: h('div', {}, h('p.small.muted', {}, r.intro), h('div.small.mono', { style: { margin: '0 0 8px' } }, r.source),
        h('div.list', { style: { maxHeight: '44vh', padding: '6px 10px' } }, boxes.map(({ x, box }) => h('label.check', { style: { display: 'flex', margin: '3px 0' } }, box, h('span', {}, x.name + (x.notes ? '   (' + x.notes + ')' : ''))))),
        status),
      buttons: [{ text: T('Close'), value: null }, {
        text: T('Create race packs'), kind: 'primary', onclick: async () => {
          const chosen = boxes.filter(b => b.box.checked);
          if (!chosen.length) return false;
          const again = chosen.filter(b => b.x.inFolder).map(b => b.x.name);
          if (again.length && !await confirm(again.length === 1 ? F('Your race folder has {0} already. Replace it with the race of this client?', again[0]) : F('Your race folder has {0} already. Replace them with the races of this client?', again.join(', ')))) return false;
          const j = await runProgress(() => post('/api/races/pack/create', { indices: chosen.map(b => b.x.index) }), T('Create race packs'), { quiet: true });
          if (j) { status.textContent = j.message || ''; if (j.payload) made += j.payload.made; }
          chosen.forEach(b => { b.box.checked = false; b.x.inFolder = true; });
          return false;
        }
      }]
    });
    await post('/api/races/pack/close').catch(() => { });
    if (made > 0) {
      try { await post('/api/races/save'); } catch (e) { await alert(e.message); }
      await reopen();
      await alert(T('The races were taken from the selected client. Switch on the ones you want.'));
    }
  }

  async function save(quiet) {
    try { const r = await post('/api/races/save'); view = r.view; paint(); if (!quiet) await alert(r.message); return true; }
    catch (e) { await alert(e.message); return false; }
  }

  paint();
  if (!data.exists) setTimeout(() => alert(F('{0} was not found in the server\'s configs\\modules folder. Build the server with the module first.', 'mod_custom_races.conf')), 50);
  post('/api/races/coa/check').then(x => { view = x.view; paint(); }).catch(() => { });
  if (data.exists && view.setupNeeded) setTimeout(() => setup(), 50);
  const head = h('div.setup-head', {}, h('div.row', { style: { alignItems: 'center', flexWrap: 'nowrap' } },
    h('span.bigic', {}, icon('races')),
    h('div', { style: { flex: '1' } }, h('h1', { style: { margin: 0, fontSize: '21px' } }, T('Custom races')), h('div.small.muted', {}, T('mod-custom-races  ·  modules\\mod_custom_races.conf'))),
    master));
  const body = h('div', { style: { display: 'flex', flexDirection: 'column', height: '100%', minHeight: '0', gap: '10px' } },
    head, h('div.setup-scroll', { style: { flex: '1', overflow: 'auto', minHeight: '0' } }, content));
  await dialog({
    title: meta.product + ' – ' + T('Custom races'), icon: 'races', full: true, body, enter: false,
    left: [ghost(T('Reset to defaults'), async () => {
      if (await confirm(T('Set every option on this page back to the value the module comes with? Nothing is written until you save.'))) act({ action: 'reset' });
    }), changes],
    buttons: [{
      text: T('Cancel'), onclick: async () => {
        if (!view.unsaved) return null;
        const r = await dialog({ title: meta.product, icon: 'warn', body: h('p', {}, T('Save your changes before closing?')),
          buttons: [{ text: T('Cancel'), value: 'cancel' }, { text: T('No'), value: 'no' }, { text: T('Yes'), kind: 'primary', value: 'yes' }] });
        if (r === 'cancel' || r === null) return false;
        if (r === 'yes' && !await save()) return false;
        return null;
      }
    }, { text: T('Save'), kind: 'primary', onclick: async () => (await save()) ? null : false }],
    onopen: (b, c) => { closeIt = c; const d = b.querySelector('.db'); d.style.display = 'flex'; d.style.flexDirection = 'column'; }
  });
}
