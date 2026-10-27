// The accounts of real players: password, access level, deleting, export and import.
import { h, T, F, meta, post, get, replace, download, state } from './core.js';
import { primary, secondary, danger, dialog, alert, confirm, table, select, password, field, runTask, pickUpload, setNote, note } from './ui.js';

const levels = () => [T('Player'), T('Moderator (GM 1)'), T('Game Master (GM 2)'), T('Administrator (GM 3)')];

export async function accountsDialog() {
  const listBox = h('div');
  const status = note();
  let selected = null, data = null;
  const level = select(levels().map((t, i) => [i, t]), 0);
  const actions = [];
  const del = danger(T('Delete account …'), async () => {
    const a = selected; if (!a) return;
    if (data.worldRunning) return alert(T('Stop the server first. Accounts can only be deleted while the worldserver is not running, so no character is still in use.'));
    const chars = a.characterCount === 0 ? T('It has no characters.') : a.characterCount === 1 ? F('Its character ({0}) will be deleted as well.', a.characters) : F('Its {0} characters ({1}) will be deleted as well.', a.characterCount, a.characters);
    const purge = data.purges ? T("The worldserver removes the characters' remaining data (items, mail, guild membership …) at its next start.") : T('Note: CharDelete.KeepDays is 0 in your settings, so the worldserver never purges deleted characters. They stay hidden in the database.');
    if (!await confirm(F('Delete the account "{0}"?', a.name) + '\n\n' + chars + '\n' + purge + '\n\n' + T('This cannot be undone.'), T('Delete'), null, null, 'danger')) return;
    try { await post('/api/accounts/delete', { id: a.id }); load(F('Account {0} was deleted.', a.name)); } catch (e) { alert(e.message); }
  });
  const pw = secondary(T('New password …'), async () => {
    const a = selected; if (!a) return;
    const p1 = password(), p2 = password();
    const ok = await dialog({ title: F('New password for {0}', a.name), icon: 'accounts', body: h('div', {}, field(T('New password'), p1), field(T('Repeat password'), p2)),
      buttons: [{ text: T('Cancel'), value: null }, { text: T('Set password'), kind: 'primary', onclick: async () => { await post('/api/accounts/password', { id: a.id, name: a.name, password: p1.value, password2: p2.value }); return true; } }] });
    if (ok) load(F('New password set for {0}.', a.name));
  });
  const setLevel = secondary(T('Set access level'), async () => {
    const a = selected; if (!a) return;
    const lvl = +level.value;
    try { await post('/api/accounts/level', { id: a.id, level: lvl }); load(a.online ? F('{0} is now: {1} (applies at the next login).', a.name, levels()[lvl]) : F('{0} is now: {1}.', a.name, levels()[lvl])); }
    catch (e) { alert(e.message); }
  });
  const exp = secondary(T('Export …'), async () => {
    const a = selected; if (!a) return;
    if (data.worldRunning && !await confirm(T('The server is running. Characters that are online right now are exported as they were last saved.') + '\n\n' + T('Export anyway?'))) return;
    const j = await runTask(() => post('/api/accounts/export', { name: a.name }));
    if (!j || !j.ok) return;
    download(j.payload.download);
    await alert(F('{0} was exported to', a.name) + ' ' + a.name + '.afkaccount\n\n' +
      (j.payload.characters.length === 0 ? T('The account has no characters.') : T('Characters:') + ' ' + j.payload.characters.join(', ') + '\n' + T('Items:') + ' ' + j.payload.items) +
      '\n\n' + T('On the new server, open "Manage player accounts" and choose "Import account file".'));
  }, 'download');
  actions.push(del, pw, level, setLevel, exp);
  const enable = () => actions.forEach(x => x.disabled = !selected);

  async function load(message) {
    try {
      data = await get('/api/accounts');
      const keep = selected && selected.id; selected = null;
      replace(listBox, table([
        { title: T('Account'), key: 'name' },
        { title: T('Access level'), render: a => levels()[Math.max(0, Math.min(3, a.level))] },
        { title: T('Characters'), render: a => a.characterCount === 0 ? '–' : a.characters + (a.characterCount > 1 ? '  (' + a.characterCount + ')' : '') },
        { title: T('Last login'), render: a => a.lastLogin || T('never') },
        { title: T('Online'), render: a => a.online ? h('span.ok', {}, T('yes')) : '' },
      ], data.accounts, { height: '360px', onpick: a => { selected = a; level.value = String(a.level); enable(); }, selected: a => a.id === keep && (selected = a) }));
      setNote(status, message || (data.accounts.length === 1 ? T('1 player account') : F('{0} player accounts', data.accounts.length)), message ? 'ok' : '');
      enable();
    } catch (e) { setNote(status, T('The accounts could not be loaded:') + '\n' + e.message, 'bad'); }
  }

  const importBtn = secondary(T('Import account file …'), async () => {
    if (data && data.worldRunning) return alert(T('Stop the server first. Characters can only be imported while the worldserver is not running.'));
    const path = await pickUpload('.afkaccount');
    if (!path) return;
    let m;
    try { m = await post('/api/accounts/inspect', { path }); } catch (e) { return alert(e.message); }
    if (!await confirm(F('Import the account {0}?', m.accounts) + '\n\n' + T('Characters:') + ' ' + (m.characters || T('none')) + '\n' + T('Exported:') + ' ' + m.created + '\n\n' +
      T('If an account with this name exists here, the characters are added to it and its password becomes the one from the file. Guild, group and arena team memberships are not carried over.'))) return;
    const j = await runTask(() => post('/api/accounts/import', { path }));
    if (!j || !j.ok) return;
    load(F('Imported {0}.', m.accounts));
    alert(T('Import finished.') + '\n\n' + T('Account:') + ' ' + j.payload.accounts.join(', ') + '\n' + T('Characters:') + '\n  ' + (j.payload.characters.length ? j.payload.characters.join('\n  ') : T('none')) + '\n' + T('Items:') + ' ' + j.payload.items);
  }, 'uploadIc');

  load();
  enable();
  await dialog({
    title: meta.product + ' – ' + T('Player accounts'), icon: 'accounts', wide: true,
    body: h('div', {},
      h('p.hint', {}, T('Accounts of real players. The Playerbots accounts are not listed and are managed with "Reset random bots".'), h('br'), T('Select an account to delete it, give it a new password, change its access level or export it for another server.')),
      listBox, h('div.row', { style: { marginTop: '10px' } }, del, pw, level, setLevel, exp), status),
    left: [importBtn, secondary(T('Refresh'), () => load())],
    buttons: [{ text: T('Close'), value: null }]
  });
}
