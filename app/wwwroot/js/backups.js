// Backups: the server snapshots, keeping, restoring (whole or parts), deleting.
import { h, T, F, meta, post, get, replace } from './core.js';
import { primary, secondary, dialog, alert, confirm, table, check, choose, toast } from './ui.js';

export async function backups() {
  let data;
  try { data = await get('/api/backups'); } catch (e) { return alert(e.message); }
  let selected = data.list[0] || null;
  let closeIt;
  const listBox = h('div');
  const restoreBtn = primary(T('Restore …'), () => restore());
  const partsBtn = secondary(T('Restore only parts …'), () => selected && parts(selected.name, selected.created));
  const deleteBtn = secondary(T('Delete'), () => remove());

  function fill() {
    if (selected && !data.list.some(b => b.name === selected.name)) selected = data.list[0] || null;
    replace(listBox, data.list.length === 0 ? h('div.list', { style: { padding: '14px' } }, h('span.muted', {}, T('No backups yet')))
      : table([
        { title: T('Created'), key: 'created' }, { title: T('Made'), key: 'made' }, { title: T('CoA core'), key: 'core', cls: 'mono' },
        { title: 'Playerbots', key: 'bots', cls: 'mono' }, { title: T('Size'), key: 'size' }
      ], data.list, { onpick: b => { selected = b; buttons(); }, ondouble: b => { selected = b; restore(); }, selected: b => selected && b.name === selected.name, height: '46vh' }));
    buttons();
  }
  function buttons() { restoreBtn.disabled = partsBtn.disabled = deleteBtn.disabled = !selected; }

  const keep = h('input', { type: 'number', min: data.min, max: data.max, value: data.keep, style: { width: '70px', textAlign: 'center' } });
  keep.addEventListener('change', async () => {
    try { const r = await post('/api/backups/keep', { keep: Number(keep.value) }); keep.value = r.keep; toast(T('Saved.')); } catch (e) { alert(e.message); }
  });

  async function restore() {
    if (!selected) return;
    if (!await confirm(F('Go back to the backup from {0}?', selected.created) + '\n\n' +
      T('The server programs, settings and all databases are replaced by the state of that moment. Everything that happened on the server since then is lost: character progress, new characters and accounts, changed settings.') + '\n\n' +
      T('The server is stopped for this.'))) return;
    closeIt(null);
    (await import('./manager.js')).engine('Restore', null, { snapshot: selected.name });
  }

  async function remove() {
    if (!selected || !await confirm(F('Delete the backup from {0}?', selected.created))) return;
    try { await post('/api/backups/delete', { name: selected.name }); data = await get('/api/backups'); fill(); } catch (e) { alert(e.message); }
  }

  async function parts(folder, created, other) {
    const accounts = check(T('Accounts'), false), characters = check(T('Characters'), false), settings = check(T('Settings'), false);
    const text = t => h('div.small.muted', { style: { margin: '0 0 10px 26px' } }, t);
    const r = await dialog({
      title: T('Restore only parts'), icon: 'database',
      body: h('div', {}, h('p', {}, h('b', {}, F('From the backup of {0}, take:', created))),
        accounts, text(T('Every account with its password and GM rights. The realm list (name, address, port) stays that of this server.')),
        characters, text(T('Every character with everything it has - bags, bank, mail, guilds - and the Playerbots data. Characters belong to their accounts by number: take the accounts along, unless the backup comes from this very server.')),
        settings, text(T('The settings of the server and of the modules it has. The programs stay those of this server, and so do the database password and the folders.')),
        h('p.small.warn', {}, T('What you take replaces what this server has now; everything else stays. The parts are brought up to this server\'s version afterwards, so a backup of an older server works too. The server is stopped for this.'))),
      buttons: [{ text: T('Cancel'), value: null }, {
        text: T('Restore'), kind: 'primary', onclick: async () => {
          const chosen = [accounts.checked && 'accounts', characters.checked && 'characters', settings.checked && 'settings'].filter(Boolean);
          if (chosen.length === 0) { await alert(T('Choose at least one part.')); return false; }
          if (characters.checked && !accounts.checked && other &&
            !await confirm(T('The characters of another server belong to its accounts. Without them, characters whose account number this server does not have cannot be played. Restore only the characters anyway?'))) return false;
          return chosen;
        }
      }]
    });
    if (!r) return;
    closeIt(null);
    (await import('./manager.js')).engine('RestoreParts', null, { snapshot: folder, parts: r });
  }

  async function fromOther() {
    const folder = await choose({ title: T('The folder of a backup of another AFK Realm server (it holds manifest.txt)') });
    if (!folder) return;
    try { const r = await post('/api/backups/other', { folder }); parts(r.folder, r.created, true); } catch (e) { alert(e.message); }
  }

  fill();
  await dialog({
    title: meta.product + ' – ' + T('Backups'), icon: 'database', wide: true,
    body: h('div', {},
      h('p.muted', {}, F('Before every update {0} saves the server: programs, settings, all databases and the versions they were built from. If an update causes problems, restore the backup made before it.', meta.product)),
      h('div.row', { style: { alignItems: 'center' } }, h('span', {}, T('Backups to keep:')), keep, h('span.small.muted', {}, T('Older ones are removed with the next backup.'))),
      listBox,
      h('div.small.muted.mono', { style: { marginTop: '6px' } }, data.folder)),
    left: [secondary(T('Back up now'), async () => { closeIt(null); (await import('./manager.js')).engine('Backup'); }), restoreBtn, partsBtn,
      secondary(T('From another server …'), () => fromOther()), deleteBtn],
    buttons: [{ text: T('Open folder'), onclick: async () => { await post('/api/fs/reveal', { path: data.folder }); return false; } }, { text: T('Close'), value: null }],
    onopen: (b, c) => { closeIt = c; }
  });
}
