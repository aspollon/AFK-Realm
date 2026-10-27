// Module manager: the AzerothCore module catalog with the installed modules ticked; applying rebuilds the server.
import { h, T, F, meta, post, get, replace, debounce } from './core.js';
import { primary, secondary, dialog, alert, input, select, check, toast } from './ui.js';

export async function modulesDialog() {
  let data;
  let all = [], ticked = new Set(), current = null, closeIt;

  const search = input('', { placeholder: T('Search'), style: { width: '240px' } });
  search.addEventListener('input', debounce(() => fill(), 150));
  const hideOld = check(T('Hide modules not changed for 2 years'), true, () => fill());
  const onlyInstalled = check(T('Installed only'), false, () => fill());
  const sort = select([[0, T('Recommended')], [1, T('Name A–Z')], [2, T('Name Z–A')], [3, T('Newest first')], [4, T('Oldest first')], [5, T('Most stars')]], 0, () => fill());
  const reload = secondary(T('Reload list'), () => load(true));
  const own = h('div');
  const listBox = h('div.list', { style: { flex: '1', minHeight: '0' } });
  const detail = h('div', { style: { width: '340px', flex: 'none', overflow: 'auto', padding: '2px 4px' } });
  const status = h('span.small.muted');
  const applyBtn = primary(T('Apply changes …'), () => apply());
  const customUrl = input('', { placeholder: 'https://github.com/azerothcore/mod-transmog', style: { width: '380px' } });
  const token = input('', { type: 'password', style: { width: '300px' }, autocomplete: 'off' });
  token.addEventListener('change', () => post('/api/modules/token', { token: token.value }).catch(e => alert(e.message)));

  async function load(refresh) {
    replace(status, T('Loading the module list from GitHub …'));
    try { data = await get('/api/modules' + (refresh ? '?refresh=1' : '')); }
    catch (e) { status.textContent = e.message; status.className = 'small warn'; return; }
    all = data.modules;
    ticked = new Set(all.filter(m => m.installed || m.state === 'PartOfCoA').map(m => m.id));
    token.value = data.token || '';
    status.textContent = data.status; status.className = 'small ' + (data.warn ? 'warn' : 'muted');
    current = null;
    fillOwn(); fill(); showDetails();
  }

  function statusText(m) {
    const tick = ticked.has(m.id);
    switch (m.state) {
      case 'PartOfCoA': return m.playerbots ? T('included') : T('part of CoA');
      case 'Managed':
        if (!tick) return T('will be removed');
        return m.status === 'sql-failed' ? T('database step failed') : m.status === 'missing' ? T('folder missing') : T('installed');
      case 'ByHand': return tick ? T('added by hand') : T('will be removed');
      default: return tick ? T('will be installed') : '';
    }
  }

  const clash = m => all.find(o => o !== m && ticked.has(o.id) && o.name.toLowerCase() === m.name.toLowerCase());
  const clashText = (m, c) => F('Another module named "{0}" ({1}) is already chosen. Only one module of the same name can be installed.', m.name, c.fullName || c.cloneUrl);

  function setTick(m, on, box) {
    if (m.state === 'PartOfCoA') { box.checked = true; return; }
    if (on && !m.installed) { const c = clash(m); if (c) { box.checked = false; alert(clashText(m, c)); return; } }
    if (on) ticked.add(m.id); else ticked.delete(m.id);
    updateApply();
  }

  function tickBox(m, after) {
    const box = h('input', { type: 'checkbox', checked: ticked.has(m.id), disabled: m.state === 'PartOfCoA', onclick: e => e.stopPropagation(),
      onchange: () => { setTick(m, box.checked, box); after(); } });
    return box;
  }

  function fillOwn() {
    const mine = all.filter(m => m.own).sort((a, b) => a.name.localeCompare(b.name));
    if (!mine.length) return replace(own);
    replace(own, h('h3', { style: { margin: '4px 0 6px', color: 'var(--accent-text)' } }, F('Modules by {0}', meta.product)),
      h('div.list', { style: { maxHeight: '28vh', marginBottom: '10px' } }, h('table.grid', {}, h('tbody', {}, mine.map(m => {
        const st = h('td.small.muted', {}, statusText(m));
        const tr = h('tr.pick', { class: current === m ? 'sel' : '', onclick: () => pick(m) },
          h('td', { style: { width: '24px' } }, tickBox(m, () => { st.textContent = statusText(m); pick(m); })),
          h('td', {}, h('b', {}, m.name)), h('td', {}, m.description), st);
        tr.mod = m;
        return tr;
      })))));
  }

  function fill() {
    const q = search.value.trim().toLowerCase();
    let shown = all.filter(m => !m.own && (!hideOld.checked || m.installed || ticked.has(m.id) || !m.old) && (!onlyInstalled.checked || m.installed) &&
      (!q || m.name.toLowerCase().includes(q) || m.description.toLowerCase().includes(q) || (m.raw || '').toLowerCase().includes(q) || m.fullName.toLowerCase().includes(q)));
    const by = (f, d = 1) => (a, b) => (f(a) < f(b) ? -d : f(a) > f(b) ? d : 0);
    const name = m => m.name.toLowerCase();
    switch (Number(sort.value)) {
      case 1: shown.sort(by(name)); break;
      case 2: shown.sort(by(name, -1)); break;
      case 3: shown.sort(by(m => m.pushed, -1)); break;
      case 4: shown.sort(by(m => m.pushed || '9999')); break;
      case 5: shown.sort((a, b) => b.stars - a.stars || name(a).localeCompare(name(b))); break;
      default: shown.sort((a, b) => (b.installed - a.installed) || (ticked.has(b.id) - ticked.has(a.id)) || (b.stars - a.stars) || name(a).localeCompare(name(b)));
    }
    const tbody = h('tbody');
    for (const m of shown) {
      const st = h('td.small', {}, statusText(m));
      const tr = h('tr.pick', { class: current === m ? 'sel' : '', onclick: () => pick(m), style: { color: m.state === 'PartOfCoA' ? 'var(--muted)' : m.status === 'sql-failed' || m.status === 'missing' ? 'var(--bad)' : '' } },
        h('td', { style: { width: '24px' } }, tickBox(m, () => { st.textContent = statusText(m); })),
        h('td', {}, h('b', {}, m.name)), h('td.small', {}, m.description), h('td.small', { style: { textAlign: 'right' } }, m.stars ? String(m.stars) : ''), h('td.small', {}, m.pushed), st);
      tr.mod = m;
      tbody.appendChild(tr);
    }
    replace(listBox, h('table.grid', {}, h('thead', {}, h('tr', {}, h('th', {}, ''), h('th', {}, T('Module')), h('th', {}, T('Description')), h('th', {}, '★'), h('th', {}, T('Last change')), h('th', {}, T('Status')))), tbody));
    updateApply();
  }

  function pick(m) {
    current = m;
    [own, listBox].forEach(x => x.querySelectorAll('tr').forEach(tr => tr.classList.toggle('sel', tr.mod === m)));
    showDetails();
  }

  function updateApply() {
    const n = all.filter(m => m.state !== 'PartOfCoA' && m.installed !== ticked.has(m.id)).length;
    applyBtn.disabled = n === 0;
    applyBtn.textContent = n === 1 ? T('Apply 1 change …') : n > 1 ? F('Apply {0} changes …', n) : T('Apply changes …');
  }

  const mark = level => level < 0 ? ['ⓘ', 'muted'] : level === 0 ? ['✔', 'ok'] : level === 1 ? ['⚠', 'warn'] : ['✖', 'bad'];
  function checkRow(level, text) {
    const [m, cls] = mark(level);
    return h('div', { style: { display: 'flex', gap: '6px', margin: '4px 0' } }, h('b', { class: cls }, m), h('span.small', { class: level === 2 ? 'bad' : '' }, text));
  }

  async function showDetails() {
    const m = current;
    if (!m) return replace(detail, h('div.muted.small', {}, T('Choose a module to see what it does.')));
    const checks = h('div');
    const readme = secondary(T('Show README'), () => showReadme(m));
    readme.style.display = 'none';
    replace(detail, h('h2', { style: { margin: '0 0 4px', wordBreak: 'break-word' } }, m.name),
      h('div', {}, m.description), m.fullName ? h('div.small.muted.mono', {}, m.fullName) : null,
      m.url && m.url.startsWith('http') ? h('a.small', { href: m.url, target: '_blank', rel: 'noopener' }, T('Open on GitHub')) : null,
      h('div', { style: { marginTop: '8px' } }, checks), readme);
    const add = (l, t) => checks.appendChild(checkRow(l, t));
    if (m.playerbots) {
      add(0, F('Playerbots is already included: {0} always builds the CoA version (Zyth45/mod-playerbots, branch coa) and keeps it up to date with every server update.', meta.product));
      if (m.fullName && m.fullName.toLowerCase() !== 'zyth45/mod-playerbots') add(-1, F('This entry is a different version ({0}) that does not fit CoA. Nothing needs to be installed here.', m.fullName));
      return;
    }
    if (m.state === 'PartOfCoA') return add(0, T('This module is already part of the CoA core.'));
    if (m.own) add(0, F('Made for {0} and the CoA server it builds.', meta.product));
    if (m.state === 'Managed') {
      add(0, (m.installedOn ? F('Installed through {0} on {1}.', meta.product, m.installedOn) : F('Installed through {0}.', meta.product)) + ' ' + T('Removing it undoes its recorded database changes.'));
      if (m.createdTables.length) add(1, T('Removing it deletes its own tables and the data in them:') + ' ' + m.createdTables.join(', '));
      if (m.manualTables.length) add(1, F('Changes to {0} cannot be undone automatically; a backup from before the module restores them.', m.manualTables.join(', ')));
      if (m.status === 'sql-failed') add(2, T('One of its database files failed. Removing the module undoes what was applied.'));
      if (m.status === 'missing') add(2, T('Its folder is missing. Removing it undoes its database changes.'));
    }
    if (m.state === 'ByHand') add(1, F('Added by hand, not through {0}. Removing it takes the module out, but its database changes stay.', meta.product));
    if (m.installed) { if (m.fullName) readme.style.display = ''; return; }
    const loading = h('div.small.muted', {}, T('Checking the module …'));
    checks.appendChild(loading);
    let r;
    try { r = await post('/api/modules/analyze', { id: m.id }); } catch (e) { r = { error: e.message }; }
    if (current !== m) return;
    loading.remove();
    if (r.error) return add(m.own ? -1 : 1, (m.own ? T('The module could not be checked online right now:') : T('The module could not be checked:')) + ' ' + r.error);
    for (const c of r.checks) add(c.level, c.text);
    add(-1, T('Check the README for anything you need to set up yourself, for example in the game.'));
    if (r.readme) readme.style.display = '';
  }

  async function showReadme(m) {
    let r;
    try { r = await post('/api/modules/readme', { id: m.id }); } catch (e) { return alert(e.message); }
    dialog({ title: r.title, icon: 'info', wide: true, body: h('div.pre.small', { style: { maxHeight: '64vh', overflow: 'auto' } }, r.text), buttons: [{ text: T('Close'), kind: 'primary', value: true }] });
  }

  async function addCustom() {
    let r;
    try { r = await post('/api/modules/custom', { url: customUrl.value.trim() }); } catch (e) { return alert(e.message); }
    const m = r.module;
    let known = all.find(x => x.id === m.id);
    if (!known) { all.push(m); known = m; }
    customUrl.value = '';
    if (!known.installed && known.state !== 'PartOfCoA' && !clash(known)) ticked.add(known.id);
    if (known.own) { search.value = ''; fillOwn(); } else search.value = known.name;
    fill(); pick(known);
  }

  async function apply() {
    const add = all.filter(m => m.state === 'Available' && ticked.has(m.id)).map(m => m.id);
    const remove = all.filter(m => m.installed && !ticked.has(m.id)).map(m => m.id);
    if (!add.length && !remove.length) return;
    applyBtn.disabled = true;
    status.textContent = T('Checking the chosen modules …'); status.className = 'small muted';
    let plan;
    try { plan = await post('/api/modules/plan', { add, remove }); } catch (e) { updateApply(); status.textContent = ''; return alert(e.message); }
    updateApply(); status.textContent = '';
    const ok = await dialog({ title: T('Apply module changes'), icon: plan.blocking ? 'warn' : 'info', wide: true,
      body: h('div.pre.small', { style: { maxHeight: '60vh', overflow: 'auto' } }, plan.text),
      buttons: [{ text: T('Cancel'), value: false }, { text: plan.blocking ? T('Install anyway') : T('Apply changes'), kind: plan.blocking ? 'danger' : 'primary', value: true }] });
    if (!ok) return;
    closeIt(null);
    (await import('./manager.js')).engine('Modules', null, { add: plan.add, remove: plan.remove });
  }

  showDetails();
  const body = h('div', { style: { display: 'flex', flexDirection: 'column', height: '100%', minHeight: '0', gap: '8px' } },
    h('p.small.muted', { style: { margin: 0 } }, F('Tick a module to install it, untick it to remove it, then choose "Apply changes". {0} backs up the server, downloads the modules and rebuilds the server. Database changes of modules installed here are recorded, so removing a module also undoes them. "Modules by {0}" are made for this server; the rest of the list comes from the AzerothCore module catalog, and not every module there works with CoA.', meta.product)),
    h('div.row', { style: { alignItems: 'center' } }, search, hideOld, onlyInstalled, h('span', {}, T('Sort by')), sort, reload),
    h('div', { style: { display: 'flex', gap: '14px', flex: '1', minHeight: '0' } },
      h('div', { style: { display: 'flex', flexDirection: 'column', flex: '1', minWidth: '0', minHeight: '0' } }, own, h('h3', { style: { margin: '0 0 6px' } }, T('AzerothCore module catalog')), listBox),
      detail),
    h('div.row', { style: { alignItems: 'center' } }, h('span', {}, T('Not in the list? Git address:')), customUrl, secondary(T('Add'), () => addCustom()), status),
    h('div.row', { style: { alignItems: 'center' } }, h('span', {}, T('GitHub token (optional):')), token, h('span.small.muted', {}, T('GitHub allows 60 requests an hour without a token, 5000 with one.'))));
  const shown = dialog({
    title: meta.product + ' – ' + T('Modules'), icon: 'modules', full: true, body, enter: false,
    buttons: [{ text: T('Close'), value: null }, { text: T('Apply changes …'), kind: 'primary', onclick: async () => { await apply(); return false; } }],
    onopen: (b, c) => { closeIt = c; const d = b.querySelector('.db'); d.style.display = 'flex'; d.style.flexDirection = 'column';
      // the dialog's own apply button mirrors ours
      const foot = b.querySelector('.df .btn.primary'); if (foot) { foot.replaceWith(applyBtn); } }
  });
  load(false);
  await shown;
}
