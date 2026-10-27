// The game client: add-ons and the client patch of the modules, the ZIP for other players, the client download.
import { h, T, F, meta, post, replace, download } from './core.js';
import { primary, secondary, dialog, alert, confirm, input, toggle, table, choose, runProgress } from './ui.js';

export async function clientDialog(reason) {
  const folder = input('', { style: { flex: '1', minWidth: '300px' } });
  const listBox = h('div');
  const state = h('div.small');
  const clearCache = toggle(T('Clear the game\'s cache (recommended: the game then forgets the old levels and values of items, creatures and quests)'), false);
  const installBtn = primary(T('Install add-ons'), () => install());
  const exportBtn = secondary(T('Export as ZIP …'), () => exportZip());
  const downloadBtn = secondary(T('Download / update client …'), () => downloadClient());
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
    exportBtn.disabled = !look.canExport;
    installBtn.disabled = !look.canInstall;
    installBtn.lastChild.textContent = look.installText;
    state.textContent = look.state;
    state.className = 'small ' + (look.stateWarn ? 'warn' : 'muted');
    post('/api/client/version', { folder: look.folder }).then(r => { downloadBtn.lastChild.textContent = r.text; }).catch(() => { });
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
    const j = await runProgress(() => post('/api/client/export', { folder: folder.value.trim() }), T('Making the ZIP for the other players …'));
    if (j && j.ok && j.payload) {
      download(j.payload.url);
      await alert(T('Saved:') + ' ' + j.payload.name + '\n\n' + j.message);
    }
  }

  async function downloadClient() {
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
    h('p.muted', {}, F('Some modules bring an add-on for the game client, or change files of the client. {0} copies the add-ons into Interface\\AddOns of your game and builds the client patch (Data\\{1}) from your own game client, keeps both up to date and takes them out again when their module is removed. Other players can get everything as a ZIP.', meta.product, 'patch-ZZZZZ.MPQ')),
    meta.remote ? h('p.small.warn', {}, F('The game folder is a folder on the computer {0} runs on.', meta.product)) : null,
    h('div.row', { style: { alignItems: 'center' } }, h('span', {}, T('Game folder')), folder, secondary(T('Browse …'), () => browse()), downloadBtn),
    listBox, h('div', { style: { margin: '8px 0 4px' } }, clearCache), state);
  refresh();
  await dialog({
    title: meta.product + ' – ' + T('Game client'), icon: 'client', wide: true, body,
    left: [installBtn, exportBtn],
    buttons: [{ text: T('Close'), value: null }]
  });
}
