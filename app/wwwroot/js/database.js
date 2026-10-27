// The database editor: browse and edit tables, run SQL, export, import, undo the last change.
import { h, T, F, meta, post, get, replace, clear, debounce, state, download, ApiError } from './core.js';
import { primary, secondary, dialog, alert, confirm, input, toggle, select, tabs, pickUpload, choose } from './ui.js';

const world = () => !!(state.status && state.status.world);

/** Asked before every change while the worldserver runs. Resolves with {stop} or null (cancelled). */
async function liveOk(databases) {
  if (!world()) return { stop: false };
  const players = (databases || []).some(d => d === 'acore_characters' || d === 'acore_auth');
  const r = await dialog({
    title: meta.product, icon: 'warn',
    body: h('div.pre', {}, T('The server is running. Changing its database now is risky.') + '\n\n' +
      (players ? T('This changes characters or accounts: the worldserver keeps the characters that are online in its memory and writes them back, so your change can be overwritten a moment later, or a character can end up half changed.\n\n')
        : T('The worldserver has read these tables when it started and goes on working with what it read; the change only shows after a restart, and until then the server and the database do not agree.\n\n')) +
      T('Stop the server first? All players are saved and logged out.')),
    buttons: [{ text: T('Cancel'), value: null }, { text: T('Go on while the server is running'), value: 'go' }, { text: T('Stop the server, then go on'), kind: 'primary', value: 'stop', default: true }]
  });
  if (!r) return null;
  return { stop: r === 'stop' };
}

/** A small "please wait" window around a call. */
async function busyWith(text, fn) {
  let closeIt;
  dialog({ title: meta.product, icon: 'database', body: h('div', {}, h('div.muted', {}, text), h('div.bar.thin.busy', {}, h('i'))), buttons: [], noClose: true, onopen: (b, c) => { closeIt = c; } });
  try { return await fn(); } finally { closeIt && closeIt(); }
}

export async function databaseEditor() {
  let info;
  try { info = await busyWith(T('Reading the databases …'), () => get('/api/db/tables')); } catch (e) { return alert(e.message); }

  let databases = info.databases, undoInfo = info.undo;
  let db = null, table = null, columns = [], rows = [], page = 0, more = false, busy = false;
  let deleted = [];          // original rows
  let allowed = false, asked = false, wantBackup = false;
  let results = [];

  // ---- top
  const allow = toggle(T('Allow changes'), false, v => onAllow(v));
  const backupBtn = secondary(T('Back up the server first …'), async () => {
    if (await confirm((dirty() ? T('There are changes that were not saved; they are lost.') + ' ' : '') + T('Back up the server now? This window closes; the backup shows its progress and can be restored under "Backups …".'))) { wantBackup = true; closeAll(); }
  });
  const top = h('div.row', { style: { alignItems: 'center', flexWrap: 'nowrap', marginBottom: '8px' } }, allow, backupBtn,
    h('div.small.warn', { style: { flex: '1' } }, T('This is the server\'s own data. A wrong value or a statement without WHERE can break the world, characters or accounts - if you do not know what a table is for, leave it alone. Looking is safe: nothing can be changed until "Allow changes" is ticked.')));

  // ---- left: databases and tables
  const find = input('', { placeholder: T('Find a table'), style: { width: '100%' } });
  find.addEventListener('input', debounce(() => fillTree(), 150));
  const tree = h('div.list.dbtree', { style: { flex: '1', minHeight: '0', padding: '4px 0' } });
  const exportBtn = secondary(T('Export …'), () => doExport());
  const importBtn = secondary(T('Import SQL file …'), () => doImport());
  const undoBtn = secondary(T('Undo last change …'), () => doUndo());
  const open = new Set(['acore_world']);
  const left = h('div', { style: { display: 'flex', flexDirection: 'column', gap: '8px', minHeight: '0', width: '270px', flex: 'none' } },
    find, tree, h('div.row', { style: { gap: '6px' } }, exportBtn, importBtn, undoBtn));

  function fillTree() {
    const q = find.value.trim().toLowerCase();
    const out = [];
    for (const d of databases) {
      const matches = d.tables.filter(t => !q || t.toLowerCase().includes(q));
      if (q && matches.length === 0) continue;
      const expanded = open.has(d.name) || (q && matches.length <= 60);
      out.push(h('div.dbnode', { title: d.friendly, onclick: () => { if (open.has(d.name)) open.delete(d.name); else open.add(d.name); fillTree(); } },
        h('span.tw', {}, expanded ? '▾' : '▸'), d.name, h('span.muted.small', {}, '  ' + d.tables.length)));
      if (expanded) for (const t of matches)
        out.push(h('div.dbleaf', { class: db === d.name && table === t ? 'sel' : '', onclick: () => openTable(d.name, t) }, t));
    }
    replace(tree, out.length ? out : h('div.muted.small', { style: { padding: '8px' } }, T('No table found.')));
  }

  // ---- the table tab
  const tableName = h('b', {}, T('Choose a table on the left.'));
  const where = input('', { class: 'mono', style: { width: '330px' }, placeholder: 'entry = 1234' });
  where.addEventListener('keydown', e => { if (e.key === 'Enter') { e.preventDefault(); page = 0; loadPage(); } });
  const applyBtn = secondary(T('Filter'), () => { page = 0; loadPage(); });
  const prevBtn = secondary(T('< Previous'), () => { if (page > 0) { page--; loadPage(); } });
  const nextBtn = secondary(T('Next >'), () => { if (more) { page++; loadPage(); } });
  const addBtn = secondary(T('Add row'), () => addRow());
  const delBtn = secondary(T('Delete row'), () => deleteRows());
  const saveBtn = primary(T('Save changes'), () => save());
  const discardBtn = secondary(T('Discard'), () => loadPage(true));
  const pageInfo = h('span.small.muted');
  const tableHint = h('div.small.muted', { style: { minHeight: '18px' } });
  const gridBox = h('div.list.dbgrid', { style: { flex: '1', minHeight: '0' } });
  const tablePane = h('div.dbpane', {},
    h('div.row', { style: { alignItems: 'center' } }, tableName, h('span.small.muted', {}, 'WHERE'), where, applyBtn),
    h('div.row', { style: { alignItems: 'center' } }, prevBtn, nextBtn, addBtn, delBtn, saveBtn, discardBtn, pageInfo),
    gridBox, tableHint);

  // ---- the SQL tab
  const sql = h('textarea.mono', { rows: 8, spellcheck: false, style: { width: '100%', minHeight: '140px' }, placeholder: 'SELECT * FROM acore_world.creature_template WHERE entry = 1234;' });
  sql.addEventListener('keydown', e => {
    if (e.key === 'F5' || (e.ctrlKey && e.key === 'Enter')) { e.preventDefault(); runSql(); }
    if (e.key === 'Tab') { e.preventDefault(); const s = sql.selectionStart; sql.setRangeText('  ', s, sql.selectionEnd, 'end'); }
  });
  const runBtn = primary(T('Run (F5)'), () => runSql());
  const resultPick = h('select', { style: { maxWidth: '640px', display: 'none' }, onchange: () => showResult(Number(resultPick.value)) });
  const sqlHint = h('div.small.muted', { style: { minHeight: '18px' } }, T('Type any SQL; several statements are separated by \';\'. Name the database with the table: acore_world.creature_template.'));
  const resultBox = h('div.list.dbgrid', { style: { flex: '1', minHeight: '0' } });
  const sqlPane = h('div.dbpane', { style: { display: 'none' } }, sql, h('div.row', { style: { alignItems: 'center' } }, runBtn, resultPick), resultBox, sqlHint);

  let sqlShown = false;
  const tabBar = tabs([T('Table'), 'SQL'], i => { sqlShown = i === 1; tablePane.style.display = sqlShown ? 'none' : 'flex'; sqlPane.style.display = sqlShown ? 'flex' : 'none'; if (sqlShown) sql.focus(); });
  const hint = () => sqlShown ? sqlHint : tableHint;

  const note = h('div.small.muted', {}, T('The worldserver reads most world tables only when it starts. A change shows in the game after a restart of the server, or after the matching ".reload …" command in Game master tools (for example "reload creature_template 1234"). Characters that are online are written back by the server: change those while they are logged out.'));
  const right = h('div', { style: { display: 'flex', flexDirection: 'column', flex: '1', minWidth: '0', minHeight: '0' } }, tabBar, tablePane, sqlPane, note);

  // ---- state helpers
  const hasKey = () => columns.some(c => c.key);
  const isDirty = r => r.original === null || r.changed.size > 0;
  const dirty = () => deleted.length > 0 || rows.some(isDirty);
  const canEdit = () => allowed && table !== null && hasKey() && !busy;

  function buttons() {
    const edit = canEdit();
    addBtn.disabled = delBtn.disabled = !edit;
    saveBtn.disabled = discardBtn.disabled = !(edit && dirty());
    prevBtn.disabled = busy || table === null || page === 0;
    nextBtn.disabled = busy || table === null || !more;
    applyBtn.disabled = busy || table === null;
    runBtn.disabled = busy;
    exportBtn.disabled = importBtn.disabled = busy || databases.length === 0;
    undoBtn.disabled = busy || !undoInfo || !undoInfo.count;
    undoBtn.title = undoInfo && undoInfo.count ? undoInfo.what + (undoInfo.at ? ', ' + undoInfo.at : '') : '';
    gridBox.querySelectorAll('td[data-c]').forEach(td => {
      const c = columns[Number(td.dataset.c)];
      td.contentEditable = edit && c && !c.binary ? 'plaintext-only' : 'false';
    });
  }

  async function work(text, fn) {
    busy = true; buttons();
    if (text) hint().textContent = text;
    try { return await fn(); }
    finally { busy = false; buttons(); }
  }

  function tableHintText() {
    return !hasKey() ? T('This table has no primary key, so rows cannot be told apart: it can be read here and changed in the SQL tab.')
      : allowed ? T('Click a cell to change it; type NULL for an empty value where the column allows it. Nothing is written until "Save changes".')
      : T('Tick "Allow changes" at the top to edit. The primary key is shown in bold.');
  }

  // ---- reading
  async function openTable(d, t) {
    if (busy || (d === db && t === table)) return;
    if (dirty() && !await confirm(F('There are changes that were not saved; they are lost. Open {0} anyway?', t))) return;
    db = d; table = t; page = 0; where.value = '';
    fillTree();
    if (sqlShown) tabBar.select(0);
    loadPage(true);
  }

  async function loadPage(force) {
    if (table === null || busy) return;
    if (!force && dirty() && !await confirm(T('There are changes that were not saved; they are lost. Read the table again?'))) return;
    tableName.textContent = db + '.' + table;
    try {
      const r = await work(F('Reading {0} …', table), () => post('/api/db/page', { db, table, where: where.value.trim(), page }));
      columns = r.columns; more = r.more; deleted = [];
      rows = r.rows.map(v => ({ original: v, values: v.slice(), changed: new Set() }));
      renderGrid();
      const at = page * r.pageSize;
      pageInfo.textContent = rows.length === 0 ? T('no rows') : F('rows {0} to {1}', at + 1, at + rows.length) + (more ? T(', more follow') : '');
      tableHint.textContent = tableHintText();
    } catch (e) { tableHint.textContent = T('Not read:') + ' ' + e.message; }
    buttons();
  }

  function cellText(td, v) {
    td.classList.toggle('null', v === null);
    td.textContent = v === null ? 'NULL' : v;
  }

  function renderGrid() {
    const head = h('tr', {}, h('th.rn', {}, '#'), columns.map(c => h('th', {
      title: c.type + (c.key ? T(', primary key') : '') + (c.nullable ? T(', may be NULL') : '') + (c.auto ? T(', counts up by itself') : ''),
      style: { fontWeight: c.key ? '800' : '', color: c.key ? 'var(--text)' : '' }
    }, c.name)));
    const body = h('tbody');
    rows.forEach((r, i) => body.appendChild(rowEl(r, i)));
    replace(gridBox, h('table.grid.cells', {}, h('thead', {}, head), body));
  }

  function rowEl(r, i) {
    const tr = h('tr', { class: r.original === null ? 'added' : '' });
    tr.row = r;
    tr.appendChild(h('td.rn', { title: T('Click to select the row'), onclick: () => { tr.classList.toggle('sel'); } }, String(i + 1)));
    columns.forEach((c, k) => {
      const td = h('td', { 'data-c': k });
      cellText(td, r.values[k]);
      if (r.changed.has(k) && r.original !== null) td.classList.add('changed');
      td.addEventListener('focus', () => { if (td.classList.contains('null')) { td.classList.remove('null'); } gridBox.lastRow = tr; });
      td.addEventListener('keydown', e => {
        if (e.key === 'Enter') { e.preventDefault(); td.blur(); }
        if (e.key === 'Escape') { e.preventDefault(); e.stopPropagation(); cellText(td, r.values[k]); td.blur(); }
      });
      td.addEventListener('blur', () => {
        if (td.contentEditable !== 'true' && td.contentEditable !== 'plaintext-only') return;
        let text = td.textContent;
        let v = c.nullable && text === 'NULL' ? null : text;
        r.values[k] = v;
        if (r.original === null) r.changed.add(k);
        else if (v === r.original[k]) { r.changed.delete(k); td.classList.remove('changed'); }
        else { r.changed.add(k); td.classList.add('changed'); }
        cellText(td, v);
        buttons();
      });
      tr.appendChild(td);
    });
    return tr;
  }

  // ---- editing
  function addRow() {
    const r = { original: null, values: columns.map(() => ''), changed: new Set() };
    rows.push(r);
    const tbody = gridBox.querySelector('tbody');
    const tr = rowEl(r, rows.length - 1);
    tbody.appendChild(tr);
    buttons();
    const first = tr.querySelector('td[data-c]');
    first.scrollIntoView({ block: 'nearest' }); first.focus();
    tableHint.textContent = T('Fill in the new row. Cells left empty get the column\'s default value.');
  }

  async function deleteRows() {
    let trs = [...gridBox.querySelectorAll('tbody tr.sel')];
    if (trs.length === 0 && gridBox.lastRow && gridBox.lastRow.isConnected) trs = [gridBox.lastRow];
    if (trs.length === 0) return alert(T('Click the number at the start of a row to select it, then press "Delete row".'));
    if (!await confirm(F('Delete {0} row(s)? They are removed from the table when you press "Save changes".', trs.length))) return;
    for (const tr of trs) {
      if (tr.row.original !== null) deleted.push(tr.row.original);
      rows.splice(rows.indexOf(tr.row), 1);
      tr.remove();
    }
    buttons();
  }

  async function save() {
    if (busy || !dirty()) return;
    if (document.activeElement && document.activeElement.blur) document.activeElement.blur();
    const changed = rows.filter(r => r.original !== null && r.changed.size > 0).map(r => ({ original: r.original, values: r.values, columns: [...r.changed] }));
    const added = rows.filter(r => r.original === null).map(r => r.values);
    const summary = [changed.length ? F('{0} row(s) changed', changed.length) : null, added.length ? F('{0} added', added.length) : null, deleted.length ? F('{0} deleted', deleted.length) : null].filter(Boolean).join(', ');
    const live = await liveOk([db]);
    if (!live) return;
    if (!await confirm(F('Write to {0}: {1}?', db + '.' + table, summary))) return;
    try {
      const r = await work(T('Saving …'), () => post('/api/db/save', { db, table, deleted, changed, added, stop: live.stop }));
      undoInfo = r.undo; deleted = [];
      await loadPage(true);
    } catch (e) { tableHint.textContent = T('Not saved.'); await alert(e.message); }
  }

  // ---- SQL
  async function runSql(force) {
    const selected = sql.value.substring(sql.selectionStart, sql.selectionEnd);
    const text = selected.trim() ? selected : sql.value;
    if (busy || !text.trim()) return;
    let stop = false;
    if (allowed && !readsOnly(text)) {
      const live = await liveOk(databasesNamed(text));
      if (!live) return;
      stop = live.stop;
    }
    const was = sqlHint.textContent;
    try {
      const r = await work(T('Running …'), () => post('/api/db/sql', { text, allow: allowed, force: !!force, stop }));
      results = r.results; undoInfo = r.undo;
      replace(resultPick, results.map((x, i) => h('option', { value: i }, x.label)));
      resultPick.style.display = results.length > 1 ? '' : 'none';
      if (results.length) { resultPick.value = results.length - 1; showResult(results.length - 1); } else clear(resultBox);
      sqlHint.textContent = r.hint;
      if (r.changedSomething) refreshTree();
    } catch (e) {
      if (e instanceof ApiError && e.confirm) { sqlHint.textContent = was; if (await confirm(e.confirm)) return runSql(true); return; }
      sqlHint.textContent = e.message.split('\n')[0];
      await alert(e.message);
    }
  }

  function showResult(i) {
    const t = results[i];
    if (!t) return;
    const body = h('tbody');
    for (const r of t.rows) body.appendChild(h('tr', {}, t.columns.map((c, k) => h('td', { class: r[k] === null ? 'null' : '' }, r[k] === null ? 'NULL' : r[k]))));
    replace(resultBox, h('table.grid.cells', {}, h('thead', {}, h('tr', {}, t.columns.map(c => h('th', {}, c)))), body));
  }

  async function refreshTree() {
    try { const r = await get('/api/db/tables'); databases = r.databases; undoInfo = r.undo; fillTree(); buttons(); } catch { }
  }

  // ---- export
  async function doExport() {
    if (busy) return;
    const ticked = new Map();       // db -> Set of tables
    if (db && table) ticked.set(db, new Set([table]));
    const count = h('div.small.muted');
    const ok = { text: T('Export …'), kind: 'primary', onclick: () => [...ticked.entries()].filter(([, s]) => s.size > 0) };
    const box = h('div.list', { style: { maxHeight: '52vh', padding: '4px 0' } });
    const openHere = new Set(db ? [db] : []);
    function fill() {
      const out = [];
      for (const d of databases) {
        const set = ticked.get(d.name) || new Set();
        const all = set.size === d.tables.length && d.tables.length > 0;
        const cb = h('input', { type: 'checkbox', checked: all, onclick: e => e.stopPropagation(), onchange: () => { ticked.set(d.name, cb.checked ? new Set(d.tables) : new Set()); fill(); } });
        cb.indeterminate = set.size > 0 && !all;
        out.push(h('div.dbnode', { onclick: () => { if (openHere.has(d.name)) openHere.delete(d.name); else openHere.add(d.name); fill(); } },
          h('span.tw', {}, openHere.has(d.name) ? '▾' : '▸'), cb, ' ', d.friendly));
        if (openHere.has(d.name)) for (const t of d.tables) {
          const c2 = h('input', { type: 'checkbox', checked: set.has(t), onchange: () => { const s = ticked.get(d.name) || new Set(); if (c2.checked) s.add(t); else s.delete(t); ticked.set(d.name, s); fill(); } });
          out.push(h('label.dbleaf', {}, c2, ' ', t));
        }
      }
      const scroll = box.scrollTop;
      replace(box, out); box.scrollTop = scroll;
      const n = [...ticked.values()].reduce((a, s) => a + s.size, 0);
      count.textContent = n === 0 ? T('Nothing ticked yet.') : F('{0} table(s) ticked.', n);
    }
    fill();
    const picked = await dialog({
      title: meta.product + ' – ' + T('Export'), icon: 'database', wide: true,
      body: h('div', {}, h('p', {}, T('Tick what you want to export: a whole database, or open it and tick single tables. Everything goes into one file that can be imported again here or on another server.')), box, count),
      buttons: [{ text: T('Cancel'), value: null }, ok]
    });
    if (!picked || picked.length === 0) return;
    const chosen = picked.map(([name, set]) => { const d = databases.find(x => x.name === name); return set.size === d.tables.length ? { db: name } : { db: name, tables: [...set] }; });
    try {
      const r = await work(T('Exporting … large databases take a while.'), () => busyWith(T('Exporting … large databases take a while.'), () => post('/api/db/export', { chosen })));
      hint().textContent = F('Exported to {0} ({1}).', r.name, r.size);
      download(r.url);
    } catch (e) { hint().textContent = T('Not exported.'); await alert(e.message); }
  }

  // ---- import
  async function doImport() {
    if (busy) return;
    if (!allowed) return alert(T('Importing changes the database. Tick "Allow changes" at the top first.'));
    if (dirty() && !await confirm(T('There are changes in the table that were not saved; they are lost. Go on?'))) return;
    const how = await dialog({ title: T('Choose the SQL file to import'), icon: 'database', body: h('p', {}, T('Where is the file?')),
      buttons: [{ text: T('Cancel'), value: null }, { text: T('On the server computer …'), value: 'here' }, { text: T('Send it from this browser …'), kind: 'primary', value: 'up' }] });
    if (!how) return;
    let file, name;
    if (how === 'up') {
      file = await pickUpload('.sql,text/plain');
      if (!file) return;
    } else {
      file = await choose({ title: T('Choose the SQL file to import'), files: '.sql' });
      if (!file) return;
    }
    name = file.split(/[\\/]/).pop();
    let plan;
    try { plan = await busyWith(T('Reading the file …'), () => post('/api/db/import/scan', { file })); } catch (e) { return alert(e.message); }
    const pick = select(plan.databases.map(d => [d.name, d.friendly]), plan.guess);
    const r = await dialog({
      title: meta.product + ' – ' + T('Import'), icon: 'database', wide: true,
      body: h('div', {},
        h('b', {}, F('Import {0}', plan.name) + '  (' + plan.size + ')'),
        plan.choose ? [h('p', { style: { margin: '10px 0 4px' } }, T('Which database is this file for?')), pick,
          h('div.small.muted', {}, plan.fits || T('The file does not say. The place you got it from usually does (world, characters or auth); most module files are for the world.'))]
          : h('p', {}, T('Goes into:') + ' ' + plan.named.join(', ')),
        h('p', {}, plan.summary),
        plan.details ? h('div.pre.small.list', { style: { padding: '8px', maxHeight: '160px' } }, plan.details) : null,
        h('p.small.warn', {}, T('The whole file is carried out. Before that, AFK Realm keeps a copy of the tables the file touches, so "Undo last change" can put them back as they were. Still: only import files from a source you trust.'))),
      buttons: [{ text: T('Cancel'), value: null }, { text: T('Import'), kind: 'primary', value: true }]
    });
    if (!r) return;
    const target = plan.choose ? pick.value : plan.named[0];
    const live = await liveOk(plan.choose ? [target] : plan.named);
    if (!live) return;
    try {
      const res = await work(F('Keeping a copy of what the file touches, then importing {0} …', name),
        () => busyWith(F('Keeping a copy of what the file touches, then importing {0} …', name), () => post('/api/db/import', { file, name: plan.name, target, stop: live.stop })));
      undoInfo = res.undo;
      await alert(res.message);
      hint().textContent = res.hint;
    } catch (e) { hint().textContent = T('Nothing was imported.'); await alert(e.message); }
    await refreshTree();
    if (table) loadPage(true);
  }

  // ---- undo
  async function doUndo() {
    if (busy || !undoInfo || !undoInfo.count) return;
    if (!allowed) return alert(T('Undoing a change changes the database. Tick "Allow changes" at the top first.'));
    const live = await liveOk(undoInfo.databases);
    if (!live) return;
    if (!await confirm(F('Undo the last change: {0}?', undoInfo.what + (undoInfo.at ? ', ' + undoInfo.at : '')) + '\n\n' +
      (undoInfo.rows ? T('The rows you changed, added or deleted are put back as they were before.')
        : T('The tables it touched are put back exactly as they were before. Whatever else changed in these tables since then is lost as well.')) +
      (world() && !live.stop ? '\n\n' + T('The server is running: restart it afterwards for this to show in the game.') : '') +
      (undoInfo.count > 1 ? '\n\n' + F('{0} earlier change(s) can be undone after this one.', undoInfo.count - 1) : ''))) return;
    if (dirty() && !await confirm(T('There are changes in the table that were not saved; they are lost. Go on?'))) return;
    try {
      const r = await work(T('Putting it back …'), () => busyWith(T('Putting it back …'), () => post('/api/db/undo', { stop: live.stop })));
      undoInfo = r.undo; hint().textContent = r.hint;
    } catch (e) { hint().textContent = T('Undoing stopped with an error.'); await alert(e.message); }
    await refreshTree();
    if (table) loadPage(true);
  }

  // ---- the switch
  async function onAllow(v) {
    if (v && !asked) {
      const r = await dialog({
        title: meta.product, icon: 'warn',
        body: h('div.pre', {}, T('From here on you can change and delete the server\'s data.') + '\n\n' +
          T('A mistake can break quests, creatures, characters or accounts, in the worst case the whole server. "Undo last change" can take back what you change in this window, but it is no substitute for a backup.') + '\n\n' +
          T('Back up the server first? The backup holds all databases and can be restored under "Backups …".')),
        buttons: [{ text: T('Cancel'), value: null }, { text: T('Go on without a backup'), value: 'no' }, { text: T('Back up now (this window closes)'), kind: 'primary', value: 'yes', default: true }]
      });
      if (!r) { allow.input.checked = false; return; }
      if (r === 'yes') { allow.input.checked = false; wantBackup = true; closeAll(); return; }
      asked = true;
    }
    allowed = v;
    if (table) tableHint.textContent = tableHintText();
    buttons();
  }

  let closeAll = () => { };
  fillTree(); buttons();
  tableHint.textContent = F('{0} database(s). Choose a table on the left.', databases.length);
  const body = h('div', { style: { display: 'flex', flexDirection: 'column', height: '100%', minHeight: '0' } }, top,
    h('div', { style: { display: 'flex', gap: '14px', flex: '1', minHeight: '0' } }, left, right));
  await dialog({
    title: meta.product + ' – ' + T('Database'), icon: 'database', full: true, body, enter: false,
    buttons: [{
      text: T('Close'), onclick: async () => {
        if (!wantBackup && dirty() && !await confirm(T('There are changes that were not saved. Close anyway?'))) return false;
        return null;
      }
    }],
    onopen: (box, close) => {
      closeAll = () => close(null);
      const d = box.querySelector('.db'); d.style.display = 'flex'; d.style.flexDirection = 'column';
      box.addEventListener('keydown', e => { if (e.key === 'F5' && !sqlShown) { e.preventDefault(); loadPage(); } });
    }
  });
  if (wantBackup) {
    const m = await import('./manager.js');
    m.engine('Backup');
  }
}

/** Mirrors the server's check: does this text only look things up? */
function readsOnly(text) {
  const bare = text.replace(/'(?:[^'\\]|\\.|'')*'|"(?:[^"\\]|\\.|"")*"|`[^`]*`|\/\*[\s\S]*?\*\/|--[^\r\n]*|#[^\r\n]*/g, ' ');
  for (const st of bare.split(';')) {
    const s = st.trim();
    if (!s) continue;
    if (!/^\(*\s*(select|show|describe|desc|explain|use|with|table|values)\b/i.test(s)) return false;
    if (/\binto\s+(outfile|dumpfile)\b|\bfor\s+update\b/i.test(s)) return false;
  }
  return true;
}

function databasesNamed(text) {
  const found = new Set();
  for (const m of text.matchAll(/\b(acore_[a-z]+|afk_modules)\s*`?\./gi)) found.add(m[1].toLowerCase());
  return [...found];
}
