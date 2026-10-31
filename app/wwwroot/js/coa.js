// CoA Custom: copy the finished package into the game client and server, build the client patch,
// download a newer package and export the complete client ZIP for other computers.
import { h, T, F, post, download } from './core.js';
import { dialog, alert, confirm, choose, input, primary, secondary, runProgress } from './ui.js';

export async function coaDialog() {
  let state;
  try { state = await post('/api/coa/state'); } catch (e) { return alert(e.message); }
  async function askToken(first) {
    const key = input('', { type: 'password', style: { width: '100%' }, autocomplete: 'off' });
    const answer = await dialog({
      title: T('CoA Custom GitHub key'), icon: 'modules',
      body: h('div', {},
        h('p', {}, T('The CoA Custom 64 download is in a private repository. Enter the key for it. This key is stored separately from your module-catalog token.')),
        h('div.row.nowrap', {}, h('span', { style: { width: '90px' } }, T('GitHub key')), key),
        h('p.small.muted', {}, T('Fine-grained token: repository ilusixn/CustomRacesCoa, permission Contents: Read-only. Do not enter your GitHub password.'))),
      buttons: first
        ? [{ text: T('Cancel'), value: null }, { text: T('Save key'), kind: 'primary', value: 'save' }]
        : [{ text: T('Cancel'), value: null }, { text: T('Save key'), kind: 'primary', value: 'save' }]
    });
    if (answer !== 'save') return false;
    const value = key.value.trim();
    if (!value) { await alert(T('Please enter the GitHub key.')); return false; }
    try {
      const r = await post('/api/coa/token', { token: value });
      state.tokenSet = !!r.tokenSet;
      return state.tokenSet;
    } catch (e) { await alert(e.message); return false; }
  }

  if (!state.tokenSet && !await askToken(true)) return;

  const packagePath = input(state.package || '', { style: { flex: '1', minWidth: '240px' } });
  const serverPath = input(state.server || '', { style: { flex: '1', minWidth: '240px' } });
  const clientPath = input(state.client || '', { style: { flex: '1', minWidth: '240px' } });
  const status = h('div.small.muted', {}, state.packageRoot ? F('Package: {0}', state.packageRoot) : T('Choose the extracted CoA Custom package.'));

  async function rememberPaths() {
    try { await post('/api/coa/paths', { package: packagePath.value, server: serverPath.value, client: clientPath.value }); }
    catch { /* A path that does not exist yet must not block the dialog. */ }
  }
  for (const field of [packagePath, serverPath, clientPath])
    field.addEventListener('change', () => { rememberPaths(); });

  const browse = (inputEl, title, start) => secondary(T('Browse ...'), async () => {
    const p = await choose({ title, start: inputEl.value || start, newFolder: false });
    if (p) inputEl.value = p;
  }, 'folder');

  async function check() {
    try {
      await rememberPaths();
      await post('/api/coa/check', { package: packagePath.value, server: serverPath.value, client: clientPath.value });
      await alert(T('The package can be copied.'));
    } catch (e) { await alert(e.message); }
  }

  async function install() {
    if (!await confirm(T('Copy the CoA client files, server DBCs/SQL and settings, install the module add-ons and build the AFK client patch? Close the game and stop the server first.'))) return;
    await rememberPaths();
    const job = await runProgress(() => post('/api/coa/install', { package: packagePath.value, server: serverPath.value, client: clientPath.value }), T('Installing CoA Custom ...'));
    if (job && job.message) await alert(job.message);
    status.textContent = job && job.ok ? T('CoA Custom installed.') : status.textContent;
  }

  async function exportZip() {
    await rememberPaths();
    const job = await runProgress(() => post('/api/coa/export', { package: packagePath.value, server: serverPath.value, client: clientPath.value }), T('Packing the complete CoA Custom client ...'));
    if (job && job.ok && job.payload && job.payload.url) {
      download(job.payload.url);
      await alert(T('The complete CoA Custom client ZIP is ready. Unpack it into the game folder on another computer.'));
    }
  }

  async function downloadCoa() {
    const target = await choose({ title: T('Folder for the downloaded CoA Custom package'), start: packagePath.value, newFolder: true });
    if (!target) return;
    const job = await runProgress(() => post('/api/coa/download', { target }), T('Downloading CoA Custom ...'));
    if (job && job.ok && job.payload) {
      packagePath.value = job.payload.package || target;
      await rememberPaths();
      status.textContent = F('Downloaded CoA Custom {0}.', job.payload.version || '');
      await alert(F('CoA Custom {0} downloaded to {1}.', job.payload.version || '', job.payload.package || target));
    }
  }

  const body = h('div', {},
    h('p', {}, T('Copy the finished CoA Custom package. The client files and the server DBCs/SQL are taken as they are; the AFK core and mod-classic-classes stay in place.')),
    h('div.row.nowrap', {}, h('span', { style: { width: '120px' } }, T('CoA package')), packagePath, browse(packagePath, T('Choose the extracted CoA Custom package'))),
    h('div.row.nowrap', {}, h('span', { style: { width: '120px' } }, T('Server folder')), serverPath, browse(serverPath, T('Choose the AFK Realm server folder'))),
    h('div.row.nowrap', {}, h('span', { style: { width: '120px' } }, T('Game client')), clientPath, browse(clientPath, T('Choose the game folder (it contains the "Data" folder)'))),
    status,
    h('p.small.muted', {}, T('A newer CoA package replaces changed files and copies new MPQs, client_root files and add-ons. The module add-ons and the AFK client patch are installed in the same step. The player ZIP is the only ZIP you need to give to players; the Game client page is for AFK-only clients without CoA.')),
    h('div.row', {}, secondary(T('Change CoA GitHub key ...'), async () => { await askToken(false); })));

  return dialog({
    title: T('CoA Custom'), icon: 'modules',
    body,
    buttons: [
      { text: T('Close'), value: null },
      { text: T('Check'), onclick: async () => { await check(); return false; } },
      { text: T('Download CoA Custom ...'), onclick: async () => { await downloadCoa(); return false; } },
      { text: T('Create player ZIP ...'), onclick: async () => { await exportZip(); return false; } },
      { text: T('Install / update everything'), kind: 'primary', onclick: async () => { await install(); return false; } }
    ]
  });
}
