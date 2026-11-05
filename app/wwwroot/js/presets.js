// Server presets: save the modules and changed settings of a server, or install a complete
// preset on another server. The built-in AFK Realm preset is the baseline for new installs.
import { h, T, F, post, get, replace, download } from './core.js';
import { dialog, alert, confirm, input, primary, secondary, check, runProgress, pickUpload } from './ui.js';

export async function presetsDialog() {
  let data;
  const status = h('div.small.muted');
  const folderNote = h('p.small.muted');
  const listBox = h('div', { style: { maxHeight: '56vh', overflow: 'auto' } });

  async function load() {
    try { data = await get('/api/presets'); }
    catch (e) { await alert(e.message); return; }
    render();
  }

  function render() {
    if (!data) return;
    const items = data.presets.map(p => {
      const buttons = h('div.row', { style: { marginTop: '8px' } },
        primary(T('Apply preset ...'), () => apply(p)),
        secondary(T('Share ...'), () => share(p)));
      if (!p.builtin) {
        buttons.appendChild(secondary(T('Rename ...'), () => rename(p)));
        buttons.appendChild(secondary(T('Delete'), () => remove(p)));
      }
      return h('div', { style: { padding: '10px', border: '1px solid var(--line)', borderRadius: '8px', margin: '8px 0' } },
        h('div', { style: { fontWeight: '700' } }, p.name, p.builtin ? h('span.small.muted', {}, '  ' + T('(built-in)')) : null),
        h('div.small.muted', { style: { margin: '3px 0 6px' } }, p.description || ''),
        h('div.small', {}, F('{0} modules, {1} changed settings', p.modules, p.settings)),
        buttons);
    });
    replace(listBox, ...items);
    status.textContent = '';
    folderNote.textContent = F('Preset files are stored in {0}.', data.folder || '');
  }

  async function askText(title, name, description, overwrite) {
    const n = input(name || '', { style: { width: '100%' }, maxLength: 80 });
    const d = input(description || '', { style: { width: '100%' } });
    const ok = await dialog({
      title, icon: 'modules',
      body: h('div', {},
        h('p.small.muted', {}, T('The preset stores the selected modules and the changed settings. Database passwords, local paths and ports are never stored.')),
        h('div.row', {}, h('span', { style: { width: '110px' } }, T('Name')), n),
        h('div.row', {}, h('span', { style: { width: '110px' } }, T('Description')), d),
        overwrite ? h('p.small.warn', {}, T('A preset with this name exists. Saving replaces it.')) : null),
      buttons: [{ text: T('Cancel'), value: null }, { text: T('Save preset'), kind: 'primary', value: 'save' }]
    });
    return ok === 'save' ? { name: n.value.trim(), description: d.value.trim() } : null;
  }

  async function saveCurrent() {
    const text = await askText(T('Save current server as preset'), '', '');
    if (!text) return;
    try {
      const r = await post('/api/presets/save', { name: text.name, description: text.description });
      status.textContent = r.message;
      await load();
    } catch (e) { await alert(e.message); }
  }

  async function share(preset) {
    try {
      const r = await post('/api/presets/export', { id: preset.id });
      download(r.url);
      status.textContent = F('The preset file "{0}" was created.', r.name);
    } catch (e) { await alert(e.message); }
  }

  async function importPreset() {
    const path = await pickUpload('.json,application/json');
    if (!path) return;
    const overwrite = check(T('Replace an existing preset with the same name'), false);
    const ok = await dialog({
      title: T('Import server preset'), icon: 'modules',
      body: h('div', {},
        h('p.small.muted', {}, T('Import a preset file shared by another server admin. It only contains modules and changed settings - no passwords, paths or ports.')),
        overwrite),
      buttons: [{ text: T('Cancel'), value: false }, { text: T('Import preset'), kind: 'primary', value: true }]
    });
    if (!ok) return;
    try {
      const r = await post('/api/presets/import', { path, overwrite: overwrite.checked });
      status.textContent = r.message;
      await load();
    } catch (e) { await alert(e.message); }
  }

  async function createPreset() {
    const text = await askText(T('Create server preset'), '', '');
    if (!text) return;
    let current, catalog;
    try {
      current = await get('/api/presets/current');
      catalog = await get('/api/modules');
    } catch (e) { return alert(e.message); }

    const modules = (catalog.modules || []).filter(m => m.state !== 'PartOfCoA' && (!m.hidden || m.installed));
    const selected = new Set((current.modules || []).map(m => m.name.toLowerCase()));
    // A new server starts with the AFK baseline rather than an empty preset.
    if (selected.size === 0)
      for (const m of modules) if (m.own) selected.add(m.name.toLowerCase());

    const search = input('', { placeholder: T('Search modules'), style: { width: '260px' } });
    const list = h('div', { style: { maxHeight: '52vh', overflow: 'auto', border: '1px solid var(--line)', borderRadius: '6px', padding: '6px' } });
    let picked = null;

    function paintModules() {
      const q = search.value.toLowerCase();
      const shown = modules.filter(m => !q || m.name.toLowerCase().includes(q) || (m.fullName || '').toLowerCase().includes(q) || (m.description || '').toLowerCase().includes(q));
      const rows = shown.map(m => {
        const box = h('input', { type: 'checkbox', checked: selected.has(m.name.toLowerCase()) });
        box.addEventListener('change', () => { if (box.checked) selected.add(m.name.toLowerCase()); else selected.delete(m.name.toLowerCase()); });
        return h('label', { style: { display: 'block', padding: '3px 2px' } }, box, ' ',
          h('b', {}, m.name), m.installed ? h('span.small.muted', {}, '  ' + T('installed')) : null,
          h('span.small.muted', {}, '  ' + (m.fullName || m.cloneUrl || '')));
      });
      replace(list, ...rows);
    }
    search.addEventListener('input', paintModules);
    paintModules();

    let ok = await dialog({
      title: T('Preset modules'), icon: 'modules', full: true,
      body: h('div', {}, h('p.small.muted', {}, T('Choose the modules the preset should install. The current server selection is already ticked.')),
        h('div.row', {}, search), list),
      buttons: [{ text: T('Cancel'), value: null }, { text: T('Continue'), kind: 'primary', value: 'next' }]
    });
    if (ok !== 'next') return;
    picked = modules.filter(m => selected.has(m.name.toLowerCase()));

    let settings = (current.settings || []).slice();
    if (settings.length > 0) {
      const boxes = settings.map(s => {
        const b = h('input', { type: 'checkbox', checked: true });
        b.addEventListener('change', () => { s._keep = b.checked; });
        s._keep = true;
        return h('label', { style: { display: 'block', padding: '2px 0', fontSize: '12px' } }, b, ' ',
          h('b', {}, s.file + '  ' + s.key), h('span.muted', {}, '  =  ' + s.value));
      });
      ok = await dialog({
        title: T('Preset settings'), icon: 'settings', full: true,
        body: h('div', {}, h('p.small.muted', {}, T('These are the settings that differ from the server defaults. Untick anything the preset should not change.') + ' ' + T('To change a value, edit it in Server settings before creating the preset.')),
          h('div', { style: { maxHeight: '58vh', overflow: 'auto', border: '1px solid var(--line)', borderRadius: '6px', padding: '6px' } }, ...boxes)),
        buttons: [{ text: T('Cancel'), value: null }, { text: T('Continue'), kind: 'primary', value: 'next' }]
      });
      if (ok !== 'next') return;
      settings = settings.filter(s => s._keep);
    }

    const preset = {
      format: 1,
      name: text.name,
      description: text.description,
      requiresCoaCustom: picked.some(m => m.name === 'mod-custom-races' || m.name === 'mod-subclass'),
      modules: picked.map(m => ({ name: m.name, fullName: m.fullName || '', cloneUrl: m.cloneUrl || '' })),
      settings: settings.map(s => ({ file: s.file, key: s.key, value: s.value }))
    };
    try {
      const r = await post('/api/presets/save', { preset });
      status.textContent = r.message;
      await load();
    } catch (e) { await alert(e.message); }
  }

  async function rename(preset) {
    const n = input(preset.name, { style: { width: '100%' }, maxLength: 80 });
    const ok = await dialog({
      title: T('Rename preset'), icon: 'modules',
      body: h('div', {}, h('div.row', {}, h('span', { style: { width: '90px' } }, T('Name')), n)),
      buttons: [{ text: T('Cancel'), value: null }, { text: T('Rename'), kind: 'primary', value: 'rename' }]
    });
    if (ok !== 'rename') return;
    try {
      const r = await post('/api/presets/rename', { id: preset.id, name: n.value.trim() });
      status.textContent = r.message;
      await load();
    } catch (e) { await alert(e.message); }
  }

  async function remove(preset) {
    if (!await confirm(F('Delete the preset "{0}"?', preset.name))) return;
    try { await post('/api/presets/delete', { id: preset.id }); await load(); }
    catch (e) { await alert(e.message); }
  }

  async function apply(preset) {
    let plan;
    try { plan = await post('/api/presets/plan', { id: preset.id, exact: false }); }
    catch (e) { return alert(e.message); }
    const lines = [];
    if (plan.addNames.length) lines.push(T('Install:') + ' ' + plan.addNames.join(', '));
    if (plan.removeNames.length) lines.push(T('Remove:') + ' ' + plan.removeNames.join(', '));
    lines.push(F('Settings: {0}', plan.settings));
    if (plan.warnings.length) lines.push('', T('Warnings:'), ...plan.warnings);
    const ok = await dialog({
      title: T('Apply server preset'), icon: 'modules', wide: true,
      body: h('div', {}, h('p', { style: { fontWeight: '700' } }, preset.name),
        h('pre.small', { style: { whiteSpace: 'pre-wrap' } }, lines.join('\n'))),
      buttons: [{ text: T('Cancel'), value: false }, { text: T('Apply preset'), kind: 'primary', value: true }]
    });
    if (!ok) return;

    if (plan.add.length || plan.remove.length) {
      const job = await runProgress(() => post('/api/engine', { mode: 'Modules', add: plan.add, remove: plan.remove }), F('Applying preset "{0}" ...', preset.name));
      if (!job || !job.ok) return;
    }

    let result;
    try { result = await post('/api/presets/apply-settings', { id: preset.id }); }
    catch (e) { return alert(e.message); }
    let message = F('Preset "{0}" applied.', preset.name) + '\n\n' +
      F('Settings written: {0}', result.saved) +
      (result.warnings.length ? '\n\n' + T('Warnings:') + '\n' + result.warnings.join('\n') : '') +
      (result.worldRunning ? '\n\n' + T('Restart the server so the new settings are used.') : '') +
      '\n\n' + T('Run Game client > Install once so the client patch matches the new modules and settings.');
    await alert(message);
    await load();
    if (preset.requiresCoaCustom && await confirm(T('Install CoA Custom now? It brings the custom races and the matching client data.'))) {
      import('./coa.js').then(x => x.coaDialog());
    }
  }

  const shown = dialog({
    title: T('Server presets'), icon: 'modules', full: true,
    body: h('div', { style: { display: 'flex', flexDirection: 'column', gap: '8px' } },
      h('p.small.muted', {}, T('Install a complete module and settings set with one click, or save the current server as a preset for another installation. The built-in AFK Realm preset installs the modules made for AFK Realm; CoA Custom is downloaded and installed separately.')),
      h('div.row', {}, primary(T('Create preset ...'), () => createPreset()), secondary(T('Save current server ...'), () => saveCurrent()), secondary(T('Import preset ...'), () => importPreset()), status),
      listBox,
      folderNote),
    buttons: [{ text: T('Close'), value: null }]
  });
  await load();
  await shown;
}
