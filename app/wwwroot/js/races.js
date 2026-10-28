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

  function raceRow(r) {
    const t = toggleEl(r.name, r.on, v => act({ action: 'toggle', key: r.key, on: v }));
    t.style.minWidth = '230px';
    const base = select(view.bases.map(b => [b.key, b.name]), r.base, v => act({ action: 'base', key: r.key, value: v }));
    const out = [h('div.row', { style: { alignItems: 'center', margin: '6px 0 2px' } }, t, h('span.small.muted', {}, T('leans on')), base,
      h('span.small.muted', { style: { minWidth: '170px' } }, r.number),
      r.canFree ? ghost(T('Free its number'), async () => {
        if (!await confirm(F('Give up the race number of the {0}? Its characters stay their base race for good, and another race can take the number.', r.name))) return;
        act({ action: 'free', key: r.key });
      }) : null,
      r.canMove ? ghost(T('Move to a free number'), () => act({ action: 'move', key: r.key, step: 0 })) : null),
      small(r.about, 50)];
    const free = out[0].querySelector('button');
    if (r.canFree && free) free.disabled = !r.freeEnabled;
    if (r.pack) out.push(h('div', { style: { margin: '0 0 10px 46px' } }, ghost(T('Delete from the race folder'), async () => {
      if (!await confirm(F('Delete {0} from the race folder?', r.name) + '\n\n' + T('Its files are removed from disk; if the race is switched on, it is switched off first, its race number becomes free for another race, and its characters play their base race from now on. This cannot be undone.'))) return;
      act({ action: 'delete', key: r.key });
    })));
    if (r.alt) {
      out.push(h('div', { style: { margin: '0 0 4px 46px' } }, toggleEl(r.alt.text, r.alt.on, v => act({ action: 'alt', key: r.key, on: v }))));
      out.push(h('div', { style: { margin: '0 0 10px 46px' } }, ghost(T('Delete these looks from the race folder'), async () => {
        if (!await confirm(F('Delete the race folder looks of {0}?', r.name) + '\n\n' + T('Its files are removed from disk; where the race used the folder looks, it shows the looks of your game client again. This cannot be undone.'))) return;
        act({ action: 'deleteAlt', key: r.key });
      })));
    }
    return out;
  }

  function paint() {
    master.checked = view.enable;
    changes.textContent = view.unsaved === 0 ? '' : view.unsaved === 1 ? T('1 change not saved yet') : F('{0} changes not saved yet', view.unsaved);
    changes.className = 'small ' + (view.unsaved ? 'warn' : 'muted');
    const scroll = content.parentElement ? content.parentElement.scrollTop : 0;
    replace(content,
      card(T('Which races can be played'), view.intro, 'accounts',
        view.problem ? small(view.problem) : h('div', { style: { fontWeight: '700', margin: '4px 0 8px', color: view.usageWarn ? 'var(--warn)' : '' } }, view.usage),
        view.knownError ? h('div.small.bad', {}, view.knownError) : null),
      card(T('Import races'), T('Choose the game client or repack once. AFK Realm finds its races and the files it needs for your game client, takes the races from that client and keeps the required files up to date when the client patch is installed.'), 'client',
        h('div.row', {}, primary(T('Import races …'), () => importRaces()), secondary(view.coaButton, () => downloadCoa()), secondary(T('Show race IDs …'), () => raceIds()), secondary(T('Check the races …'), () => checkRaces())),
        small(T('A custom race keeps the race number its client and Esteria appearance use. Stock-copy rows 51-61 (NightElf2, Gnome2, ...) are placeholders and are replaced, not moved. “Show race IDs” lists the actual mapping of your client.')),
        small(view.import)),
      card(T('Race numbers above 31'), T('The 64-race patch and the high race numbers of CoA Custom are switched on by the import. They stay visible here so they can be switched off if a client really has no 64-race dinput8.dll.'), 'client',
        h('div', {}, toggleEl(T('Use the 64-race patch (dinput8.dll)'), view.more, v => act({ action: 'flag', option: 'CustomRaces.MoreRaces', on: v }))),
        small(T('Needed for race numbers 32 to 63. AFK Realm copies the DLL next to Ascension.exe when the client patch is installed.'), 50),
        h('div', {}, toggleEl(T('Use CoA Custom\'s race numbers above 63'), view.high, v => act({ action: 'flag', option: 'CustomRaces.HighRaces', on: v }))),
        small(T('CoA Custom\'s own exe knows its races up to 127, including Highmountain, Earthen, Haranir and the others.'), 50)),
      view.imported.length ? card(T('Imported races'), T('These races come from your race folder. They are the ones you added to the game; switch them on here.'), 'client', view.imported.map(raceRow)) : null,
      view.client.length ? card(T('Races already in your game client'), T('These races are part of your game client. You can switch them on, or move one to a free number when an imported race needs its number.'), 'client', view.client.map(raceRow)) : null,
      card(T('After a change'), null, 'client', small(view.after)));
    if (content.parentElement) content.parentElement.scrollTop = scroll;
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
      await alert(T('The races were taken from the selected client.') + '\n\n' + T('The imported races are now at the top. Switch on the ones you want, save, then install the client patch under Modules > Game client.'));
    }
  }

  async function save() {
    try { const r = await post('/api/races/save'); view = r.view; paint(); await alert(r.message); return true; }
    catch (e) { await alert(e.message); return false; }
  }

  paint();
  if (!data.exists) setTimeout(() => alert(F('{0} was not found in the server\'s configs\\modules folder. Build the server with the module first.', 'mod_custom_races.conf')), 50);
  post('/api/races/coa/check').then(x => { view = x.view; paint(); }).catch(() => { });
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
