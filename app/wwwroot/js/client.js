// The game client: add-ons and the client patch of the modules, the ZIP for other players, the client download.
import { h, T, F, meta, post, replace, download } from './core.js';
import { primary, secondary, dialog, alert, confirm, input, toggle, table, choose, runProgress, check } from './ui.js';

export async function clientDialog(reason) {
  const folder = input('', { style: { flex: '1', minWidth: '300px' } });
  const listBox = h('div');
  const state = h('div.small');
  const clearCache = toggle(T('Clear the game\'s cache (recommended: the game then forgets the old levels and values of items, creatures and quests)'), false);
  const installBtn = primary(T('Install add-ons'), () => install());
  const exportBtn = secondary(T('Export AFK-only ZIP …'), () => exportZip());
  const downloadBtn = secondary(T('Download / update client …'), () => downloadClient());
  const sharedBtn = secondary(T('Add-ons for your players …'), () => sharedAddons());
  let look = null, checked = null;

  async function refresh(value) {
    replace(state, T('Checking what your game needs …')); state.className = 'small muted';
    installBtn.disabled = exportBtn.disabled = true;
    try { look = await post('/api/client/look', value === undefined ? {} : { folder: value }); }
    catch (e) { state.textContent = e.message; state.className = 'small bad'; return; }
    checked = look.folder;
    if (folder.value.trim() !== look.folder) folder.value = look.folder;
    replace(listBox, look.rows.length ? table([
      { title: T('Add-on or patch'), render: r => h('span', { class: r.warn ? 'warn' : '' }, r.name) },
      { title: T('Module'), key: 'module' }, { title: T('In your game'), render: r => h('span', { class: r.warn ? 'warn' : '' }, r.state) },
      { title: T('What it does'), key: 'why', cls: 'small' }
    ], look.rows, { height: '40vh' }) : h('div'));
    clearCache.style.display = look.cacheShown ? '' : 'none';
    clearCache.checked = look.cacheChecked;
    // With CoA Custom installed the AFK-only export is not the right ZIP. The same button now makes the
    // complete CoA player ZIP, so it must not disappear after the CoA setup.
    exportBtn.disabled = !(look.canExport || look.coa);
    exportBtn.style.display = '';
    exportBtn.lastChild.textContent = look.coa ? T('Create player ZIP …') : T('Export AFK-only ZIP …');
    installBtn.disabled = !look.canInstall;
    installBtn.lastChild.textContent = look.installText;
    state.textContent = look.state;
    state.className = 'small ' + (look.stateWarn ? 'warn' : 'muted');
    downloadBtn.style.display = look.serverCoa || look.clientPage ? '' : 'none';
    if (look.clientPage) downloadBtn.lastChild.textContent = T('Get a 3.3.5a client …');
    if (look.serverCoa) post('/api/client/version', { folder: look.folder }).then(r => { downloadBtn.lastChild.textContent = r.text; }).catch(() => { });
  }

  folder.addEventListener('change', () => { if (folder.value.trim() !== checked) refresh(folder.value.trim()); });

  async function browse() {
    const p = await choose({ title: T('Choose your game folder (it contains the "Data" folder)'), start: look && look.client || folder.value.trim() || '' });
    if (p) { folder.value = p; refresh(p); }
  }

  async function install() {
    const j = await runProgress(() => post('/api/client/install', { folder: folder.value.trim(), clearCache: clearCache.checked }), T('Installing into your game …'));
    await refresh(folder.value.trim());
    if (j && j.ok) await alert(j.message);
  }

  async function exportZip() {
    const coa = !!(look && look.coa);
    const url = coa ? '/api/coa/export' : '/api/client/export';
    const body = coa ? { client: folder.value.trim() } : { folder: folder.value.trim() };
    const j = await runProgress(() => post(url, body), coa ? T('Packing the complete CoA Custom client ...') : T('Making the ZIP for the other players ...'));
    if (j && j.ok && j.payload && j.payload.url) {
      download(j.payload.url);
      await alert(T('Saved:') + ' ' + (j.payload.name || '') + '\n\n' + j.message);
    }
  }

  // Add-ons of the owner's own game that go to the players with the client pack and the AFK Client.
  async function sharedAddons() {
    let r;
    try { r = await post('/api/client/shared', { folder: folder.value.trim() }); } catch (e) { return alert(e.message); }
    if (!r.client) return alert(T('Choose your game folder, the one with the "Data" folder.'));
    const boxes = r.addons.map(a => ({ a, box: check(a.name + (a.module ? ' (' + T('a module brings it') + ')' : ''), a.chosen || a.module) }));
    boxes.filter(b => b.a.module).forEach(b => { b.box.input.disabled = true; });
    const list = h('div', { style: { maxHeight: '45vh', overflow: 'auto', columns: '2', margin: '8px 0' } }, ...boxes.map(b => h('div', {}, b.box)));
    const ok = await dialog({
      title: T('Add-ons for your players'), icon: 'client',
      body: h('div', {}, h('p.muted', {}, T('The add-ons ticked here are taken from your game and handed out to your players: in the client pack of the registration page and by the AFK Client, which keeps them up to date. Add-ons of the installed modules are handed out anyway. Blizzard\'s own interface parts are not listed.')),
        r.addons.length ? list : h('p', {}, T('Your game has no add-ons of its own.'))),
      buttons: [{ text: T('Cancel'), value: false }, { text: T('Save'), kind: 'primary', value: true }]
    });
    if (!ok) return;
    const chosen = boxes.filter(b => !b.a.module && b.box.checked).map(b => b.a.name);
    await post('/api/client/shared', { folder: folder.value.trim(), names: chosen });
    alert(chosen.length ? F('{0} add-on(s) will go to your players with the next player package (Client build).', chosen.length) : T('No add-on of your own is handed out.'));
  }

  async function downloadClient() {
    // A stock AzerothCore server: a clean 3.3.5a client from ChromieCraft's download page.
    if (look && look.clientPage) { window.open(look.clientPage, '_blank'); return; }
    let target = look && look.client ? look.client : folder.value.trim();
    if (!target) {
      target = await choose({ title: T('Choose the folder for the game client. It can be empty; about 43 GB will be downloaded into it.'), hint: T('Choose the folder for the game client. It can be empty; about 43 GB will be downloaded into it.') });
      if (!target) return;
    }
    let check;
    try { check = await post('/api/client/download/check', { folder: target }); } catch (e) { return alert(e.message); }
    if (check.foreign && !await confirm(check.foreign)) return;
    if (!await confirm(check.question)) return;
    const j = await runProgress(() => post('/api/client/download', { target: check.target }), T('Download game client'));
    if (j && j.ok) { folder.value = check.target; await refresh(check.target); await alert(j.message); }
  }

  const body = h('div', {},
    reason ? h('p', { style: { fontWeight: '700', color: 'var(--accent-text)' } }, reason) : null,
    h('p.muted', {}, F('Some modules bring an add-on for the game client, or change files of the client. {0} copies the add-ons into Interface\\AddOns of your game and builds the client patch (Data\\{1}) from your own game client, keeps both up to date and takes them out again when their module is removed. On a CoA server the same button creates the complete CoA player ZIP, including CoA, the module add-ons and the client patch.', meta.product, 'patch-ZZZZZZZZ.MPQ')),
    meta.remote ? h('p.small.warn', {}, F('The game folder is a folder on the computer {0} runs on.', meta.product)) : null,
    h('div.row', { style: { alignItems: 'center' } }, h('span', {}, T('Game folder')), folder, secondary(T('Browse …'), () => browse()), downloadBtn),
    listBox, h('div', { style: { margin: '8px 0 4px' } }, clearCache), state);
  refresh();
  await dialog({
    title: meta.product + ' – ' + T('Game client'), icon: 'client', wide: true, body,
    left: [installBtn, exportBtn, sharedBtn],
    buttons: [{ text: T('Close'), value: null }]
  });
}
