// Server settings: popular options and every option of every config file, with the descriptions of their templates.
import { h, T, F, meta, post, get, replace, clear, lines, debounce } from './core.js';
import { primary, secondary, dialog, alert, confirm, select, input, toggle, check } from './ui.js';

/** The value as shown: On/Off for switches, "value – meaning" for choices. */
export function display(o, value) {
  if (o.kind === 'bool') return value === '1' ? T('On') : value === '0' ? T('Off') : value;
  if (o.kind === 'choice') { const c = o.choices.find(c => c.value === value); if (c) return value + ' – ' + c.text; }
  return value;
}

/** Checks a value typed by the user; returns an error or null (the program checks again when saving). */
export function invalid(o, v) {
  v = (v ?? '').trim();
  if (/[\r\n]/.test(v)) return T('The value must be on one line.');
  if (o.kind === 'bool' && v !== '0' && v !== '1') return F('{0} can only be 0 (off) or 1 (on).', o.key);
  if ((o.kind === 'number' || o.kind === 'choice') && (v === '' || isNaN(Number(v)))) return F('{0} needs a number (use a dot for decimals, e.g. 1.5).', o.key);
  if (o.quoted && v.includes('"')) return T('The value must not contain quotation marks.');
  return null;
}

const same = (a, b) => {
  const n = x => { x = (x ?? '').trim(); const d = Number(x); return x !== '' && !isNaN(d) ? String(d) : x; };
  return n(a) === n(b);
};

export async function settingsDialog(startFile) {
  let data;
  try { data = await get('/api/settings'); } catch (e) { return alert(e.message); }
  const pending = new Map();                      // "file|key" -> value
  const id = (f, o) => f + '|' + o.key;
  const all = [];
  for (const f of data.files) for (const o of f.options) { o.file = f.name; all.push(o); }
  const byId = new Map(all.map(o => [id(o.file, o), o]));
  const popularName = T('★  Popular settings');
  const fileBox = select([['', popularName], ...data.files.map(f => [f.name, f.name])], startFile || '', () => fill());
  const search = input('', { placeholder: T('Search'), style: { width: '240px' } });
  search.addEventListener('input', debounce(() => fill(), 200));
  const changedOnly = check(T('Only changed'), false, () => fill());
  const listBox = h('div.list', { style: { height: '100%' } });
  const detail = h('div', { style: { overflow: 'auto', padding: '4px 4px 4px 8px' } });
  const pendingLabel = h('span.muted');
  let current = null;

  const edited = o => pending.has(id(o.file, o)) ? pending.get(id(o.file, o)) : o.value;
  const differs = o => o.defaultValue != null && !same(edited(o), o.defaultValue);

  function matching() {
    const q = search.value.trim().toLowerCase();
    let items;
    if (q) items = all.filter(o => o.key.toLowerCase().includes(q) || (o.label && o.label.toLowerCase().includes(q)) || o.description.toLowerCase().includes(q));
    else if (!fileBox.value) items = data.popular.map(p => { const o = byId.get(p.file + '|' + p.key); if (o) { o.popularSection = p.section; o.popularLabel = p.label; } return o; }).filter(Boolean);
    else items = all.filter(o => o.file === fileBox.value);
    if (changedOnly.checked) items = items.filter(o => differs(o) || pending.has(id(o.file, o)));
    return items;
  }

  function fill() {
    const searching = search.value.trim().length > 0;
    const popular = !fileBox.value && !searching;
    const tbody = h('tbody');
    let group = null, first = null, keep = null;
    for (const o of matching()) {
      const g = popular ? o.popularSection : searching ? o.file + '  ›  ' + o.section : o.section;
      if (g !== group) { group = g; tbody.appendChild(h('tr.group', {}, h('td', { colSpan: 2 }, g))); }
      const tr = h('tr.pick', { title: o.key, onclick: () => showOption(o, tr) },
        h('td', { style: { fontWeight: differs(o) || pending.has(id(o.file, o)) ? '700' : '', color: pending.has(id(o.file, o)) ? 'var(--accent-text)' : o.locked ? 'var(--muted)' : '' } }, popular && o.popularLabel ? o.popularLabel : o.key),
        h('td', {}, display(o, edited(o))));
      tr.option = o;
      tbody.appendChild(tr);
      if (!first) first = tr;
      if (o === current) keep = tr;
    }
    replace(listBox, h('table.grid', {}, h('thead', {}, h('tr', {}, h('th', {}, T('Setting')), h('th', {}, T('Value')))), tbody));
    const sel = keep || first;
    if (sel) { sel.classList.add('sel'); showOption(sel.option, sel); sel.scrollIntoView({ block: 'nearest' }); }
    else showOption(null);
  }

  function showOption(o, tr) {
    listBox.querySelectorAll('tr.sel').forEach(x => x.classList.remove('sel'));
    if (tr) tr.classList.add('sel');
    current = o;
    if (!o) { replace(detail, h('h2', {}, T('No setting found'))); return; }
    const msg = h('div.small');
    const value = edited(o);
    let editor;
    if (o.kind === 'bool') editor = toggle(T('On'), value === '1', v => edit(o, v ? '1' : '0', msg));
    else if (o.kind === 'choice') {
      editor = h('div.row');
      const sel = select([...o.choices.map(c => [c.value, c.value + ' – ' + c.text]), ...(o.choices.some(c => c.value === value) ? [] : [[value, value]])], value, v => { inp.value = v; edit(o, v, msg); });
      const inp = input(value, { style: { width: '90px' }, title: T('Another value') });
      inp.addEventListener('input', () => edit(o, inp.value, msg));
      editor.append(sel, inp);
    } else {
      editor = input(value, { style: { width: o.kind === 'number' ? '160px' : '100%' } });
      editor.addEventListener('input', () => edit(o, editor.value, msg));
    }
    if (o.locked) { (editor.input || editor).disabled = true; editor.querySelectorAll && editor.querySelectorAll('input,select').forEach(x => x.disabled = true); }
    replace(detail,
      h('h2', { style: { margin: '0 0 2px', fontSize: '17px', wordBreak: 'break-word' } }, o.popularLabel && !fileBox.value && !search.value ? o.popularLabel : o.key),
      h('div.hint', {}, (o.popularLabel ? o.key + '   ·   ' : '') + o.file + '   ·   ' + o.section),
      h('div.pre.small', { style: { margin: '8px 0', lineHeight: '1.5' } }, o.description || T('No description in the template.')),
      o.defaultValue != null ? h('div.hint', {}, T('Default:') + ' ' + display(o, o.defaultValue)) : null,
      h('div.row', {}, editor, !o.locked && o.defaultValue != null ? secondary(T('Reset to default'), () => { edit(o, o.defaultValue, msg); showOption(o, tr); fill(); }) : null),
      o.locked ? h('div.warn.small', {}, F('{0} manages this setting itself, so it cannot be changed here.', meta.product)) : msg);
  }

  function edit(o, v, msg) {
    if (o.locked) return;
    const err = invalid(o, v);
    msg.className = 'small ' + (err ? 'bad' : '');
    msg.textContent = err || '';
    if (err) return;
    const k = id(o.file, o);
    if (same(v, o.value) && o.raw != null) pending.delete(k); else pending.set(k, v.trim());
    // the row in the list
    listBox.querySelectorAll('tr').forEach(tr => {
      if (tr.option !== o) return;
      tr.cells[1].textContent = display(o, edited(o));
      tr.cells[0].style.fontWeight = differs(o) || pending.has(k) ? '700' : '';
      tr.cells[0].style.color = pending.has(k) ? 'var(--accent-text)' : '';
    });
    updatePending();
  }

  const saveBtn = primary(T('Save changes'), () => save());
  function updatePending() {
    const n = pending.size;
    saveBtn.disabled = n === 0;
    pendingLabel.textContent = n === 0 ? T('Changes take effect after restarting the worldserver.') : n === 1 ? T('1 unsaved change') : F('{0} unsaved changes', n);
    pendingLabel.className = n === 0 ? 'muted' : '';
    pendingLabel.style.color = n ? 'var(--accent-text)' : '';
  }

  async function save() {
    const changes = [...pending.entries()].map(([k, v]) => { const [file] = k.split('|'); return { file, key: k.slice(file.length + 1), value: v }; });
    try {
      const r = await post('/api/settings/save', { changes });
      for (const [k, v] of pending) { const o = byId.get(k); if (o) { o.value = v; o.raw = v; } }
      pending.clear(); fill(); updatePending();
      await alert(r.worldRunning ? T('Saved. Restart the server (Stop server, then Start server) so the worldserver uses the new settings.') : T('Saved. The new settings are used the next time the server starts.'));
      return true;
    } catch (e) { await alert(T('The settings could not be saved:') + '\n' + e.message); return false; }
  }

  fill(); updatePending();
  const body = h('div', { style: { display: 'flex', flexDirection: 'column', height: '100%', minHeight: '0' } },
    h('div.row', {}, h('span', {}, T('Show')), fileBox, search, changedOnly),
    h('div.split', { style: { flex: '1', gridTemplateColumns: 'minmax(320px, 46%) 1fr', minHeight: '0' } }, listBox, detail));
  await dialog({
    title: meta.product + ' – ' + T('Server settings'), icon: 'settings', full: true, body, enter: false,
    left: [pendingLabel],
    buttons: [saveBtn && { text: T('Save changes'), kind: 'primary', onclick: async () => { await save(); return false; } }, {
      text: T('Close'), onclick: async () => {
        if (pending.size === 0) return null;
        const r = await dialog({ title: meta.product, icon: 'warn', body: h('p', {}, F('Save your {0} unsaved change(s)?', pending.size)),
          buttons: [{ text: T('Cancel'), value: 'cancel' }, { text: T('No'), value: 'no' }, { text: T('Yes'), kind: 'primary', value: 'yes' }] });
        if (r === 'cancel' || r === null) return false;
        if (r === 'yes' && !await save()) return false;
        return null;
      }
    }].filter(Boolean),
    onopen: box => { box.querySelector('.db').style.display = 'flex'; box.querySelector('.db').style.flexDirection = 'column'; }
  });
}
