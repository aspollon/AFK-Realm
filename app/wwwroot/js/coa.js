// CoA Custom: copy the finished package into the game client and server, build the client patch,
// download a newer package and export the complete client ZIP for other computers.
import { h, T, F, post, get, download, ApiError } from './core.js';
import { dialog, alert, confirm, choose, input, primary, secondary, toggle, runProgress, runTask } from './ui.js';

export async function coaDialog() {
  let state;
  try { state = await post('/api/coa/state'); } catch (e) { return alert(e.message); }
  const packagePath = input(state.package || '', { style: { flex: '1', minWidth: '240px' } });
  const serverPath = input(state.server || '', { style: { flex: '1', minWidth: '240px' } });
  const clientPath = input(state.client || '', { style: { flex: '1', minWidth: '240px' } });
  const status = h('div.small.muted', {}, state.packageRoot ? F('Package: {0}', state.packageRoot) : T('Choose the extracted CoA Custom package.'));
  const playerZip = toggle(T('Create the player ZIP after installing'), true);

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
    if (!await confirm(T('Copy the CoA client files, server DBCs/SQL and settings, install the module add-ons and build the AFK client patch? AFK Realm closes the game if it runs, stops the server, keeps the database running for the race settings and starts the server again afterwards.'))) return;
    await rememberPaths();
    // the server modules this package needs (mod-custom-races with mod-classic-classes; mod-subclass for 1.9.8 and
    // newer) come first: AFK Realm installs them and builds the server, then goes on with CoA Custom
    let st = {};
    try { st = await post('/api/coa/state'); } catch { st = {}; }
    if ((st.missingModules || []).length && !await installNeededModules(st.missingModules)) return;
    const args = { package: packagePath.value, server: serverPath.value, client: clientPath.value, playerZip: playerZip.checked };
    const job = await runProgress(async () => {
      try { return await post('/api/coa/install', args); }
      catch (e) {
        if (e instanceof ApiError && e.confirm && await confirm(e.confirm)) return post('/api/coa/install', Object.assign({ force: true }, args));
        if (e instanceof ApiError && e.confirm) return null;
        throw e;
      }
    }, T('Installing CoA Custom ...'));
    if (job && job.payload && job.payload.url) {
      download(job.payload.url);
      await alert(T('The complete CoA Custom client ZIP is ready:') + ' ' + (job.payload.name || '') + '\n\n' + (job.message || ''));
    } else if (job && job.message) await alert(job.message);
    if (!job || !job.ok) return;

    // CoA Custom brings the client data and the module code, but not AFK's compiled worldserver.
    // Rebuild it once after the install; the engine's build stamp includes the racial patches,
    // so this is a quick no-op when the server already has them, and a real rebuild otherwise.
    status.textContent = T('CoA Custom installed. Building the server ...');
    const build = await runProgress(() => post('/api/engine', { mode: 'Update' }), T('Building the server with the CoA racial scripts ...'));
    if (build && build.ok) {
      await runTask(() => post('/api/server/start', { force: false }), T('Starting the server ...'));
      status.textContent = T('CoA Custom installed and the server was built.');
    } else {
      status.textContent = T('CoA Custom installed, but the server could not be built.');
    }
  }

  async function exportZip() {
    await rememberPaths();
    const job = await runProgress(() => post('/api/coa/export', { package: packagePath.value, server: serverPath.value, client: clientPath.value }), T('Packing the complete CoA Custom client ...'));
    if (job && job.ok && job.payload && job.payload.url) {
      download(job.payload.url);
      await alert(T('The complete CoA Custom client ZIP is ready. Unpack it into the game folder on another computer.'));
    }
  }

  async function removeCoa() {
    if (!await confirm(T('Remove the CoA Custom files this setup installed into the game client? Changed files are restored from their backups; files it created are removed. WTF, Cache, screenshots and your own add-ons are left alone. Close the game first.'))) return;
    await rememberPaths();
    const job = await runProgress(() => post('/api/coa/uninstall', { client: clientPath.value }), T('Removing CoA client files ...'));
    if (job && job.ok) {
      await alert(job.message || T('CoA client files removed.'));
      status.textContent = T('CoA client files removed.');
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
    playerZip,
    status,
    (state.missingModules || []).length ? h('div.row', { style: { alignItems: 'center' } },
      h('span.warn', { style: { flex: '1' } }, T('CoA Custom\'s races need the module mod-custom-races on the server (it brings mod-classic-classes along). Without it nobody can create them.')),
      primary(T('Install it ...'), () => import('./modules.js').then(x => x.installModules(state.missingModules, T('CoA Custom needs these modules on the server.'))))) : null,
    h('p.small.muted', {}, T('A newer CoA package replaces changed files and copies new MPQs, client_root files and add-ons. The module add-ons and the AFK client patch are installed in the same step. The player ZIP is the only ZIP you need to give to players; the same ZIP can also be created from Modules > Game client with "Create player ZIP".')),);

  return dialog({
    title: T('CoA Custom'), icon: 'modules',
    body,
    buttons: [
      { text: T('Close'), value: null },
      { text: T('Check'), onclick: async () => { await check(); return false; } },
      { text: T('Download CoA Custom ...'), onclick: async () => { await downloadCoa(); return false; } },
      ...(state.canUninstall ? [{ text: T('Remove CoA client files ...'), kind: 'danger', onclick: async () => { await removeCoa(); return false; } }] : []),
      { text: T('Create player ZIP ...'), onclick: async () => { await exportZip(); return false; } },
      { text: T('Install / update everything'), kind: 'primary', onclick: async () => { await install(); return false; } }
    ]
  });
}

// Races the game client offers that the server's race settings leave off: switch them on, or keep them off.
export async function missingRacesDialog(banner) {
  const v = await dialog({
    title: T('Races the server cannot create'), icon: 'warn', once: 'missing-races',
    body: h('div.pre', {}, banner.text.replace(T('Click here to switch them on.'), '').trim() + '\n\n' +
      T('Switch them on in the race settings? Each race gets its settings as the CoA Custom install writes them; the races you have stay as they are. The server uses them after its next start.')),
    buttons: [
      { text: T('Keep them off'), value: 'leave' },
      { text: T('Not now'), value: null },
      { text: T('Switch them on'), kind: 'primary', value: 'add', default: true }]
  });
  if (v === 'leave') { await post('/api/coa/races/leave', {}); return; }
  if (v !== 'add') return;
  const r = await post('/api/coa/races/add', {});
  if (!r.ok) return;
  await alert(T('Switched on:') + ' ' + (r.added || []).join(', ') + '\n\n' +
    (r.running ? T('Restart the server (Stop server, then Start server) so the new races can be created.') : T('The new races can be created the next time the server starts.')));
}

// Installs the server modules a CoA Custom package needs, with the modules they need in turn, and waits for the
// server build. True when it went through.
async function installNeededModules(names) {
  let data;
  try { data = await get('/api/modules'); } catch (e) { await alert(e.message); return false; }
  const wanted = names.map(n => n.toLowerCase());
  const add = data.modules.filter(m => m.state === 'Available' && wanted.includes((m.name || '').toLowerCase())).map(m => m.id);
  if (!add.length) { await alert(F('{0} is not in the module list. Open Modules > Manage modules and add it by its address.', names.join(', '))); return false; }
  let plan;
  try { plan = await post('/api/modules/plan', { add, remove: [] }); } catch (e) { await alert(e.message); return false; }
  if (!await confirm(T('CoA Custom needs these modules on the server. AFK Realm installs them first and builds the server (this takes a while), then installs CoA Custom:') + '\n\n' + plan.text)) return false;
  const job = await runProgress(() => post('/api/engine', { mode: 'Modules', add: plan.add, remove: plan.remove }), T('Installing the modules CoA Custom needs ...'));
  return !!(job && job.ok);
}
