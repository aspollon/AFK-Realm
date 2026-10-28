// Building blocks of the pages: buttons, cards, fields, dialogs, the folder picker, tables, tabs.
import { h, T, F, api, post, get, clear, replace, lines, on, state, ApiError, upload, fmtBytes, meta } from './core.js';
import { icon } from './icons.js';

export function button(text, onclick, kind = '', ic) {
  const b = h('button.btn', { type: 'button', class: kind, onclick }, ic ? icon(ic) : null, text);
  return b;
}
export const primary = (text, onclick, ic) => button(text, onclick, 'primary', ic);
export const secondary = (text, onclick, ic) => button(text, onclick, '', ic);
export const danger = (text, onclick, ic) => button(text, onclick, 'danger', ic);
export const ghost = (text, onclick, ic) => button(text, onclick, 'ghost', ic);

/** A card of the server management: title, text, an icon and what goes inside. */
export function card(title, text, ic, ...content) {
  return h('section.card', {},
    h('h2', {}, ic ? h('span.ic', {}, icon(ic)) : null, title),
    text ? h('p.lead', {}, ...lines(text)) : null,
    ...content);
}

export function field(label, control, hint) {
  return h('label.field', {}, h('span', {}, label), control, hint ? h('span.hint', {}, hint) : null);
}

export function input(value = '', attrs = {}) { return h('input', Object.assign({ type: 'text', value }, attrs)); }
export function password(attrs = {}) { return h('input', Object.assign({ type: 'password', autocomplete: 'new-password' }, attrs)); }

export function select(options, value, onchange) {
  const s = h('select', { onchange: onchange ? () => onchange(s.value) : null });
  for (const o of options) {
    const [v, t] = Array.isArray(o) ? o : [o, o];
    s.appendChild(h('option', { value: v, selected: String(v) === String(value) }, t));
  }
  return s;
}

/** A switch with its text. */
export function toggle(text, checked, onchange, attrs = {}) {
  const inp = h('input', { type: 'checkbox', checked: !!checked, onchange: () => onchange && onchange(inp.checked), disabled: attrs.disabled });
  const el = h('label.toggle', { title: attrs.title || '' }, inp, h('span.track'), h('span', {}, text));
  el.input = inp;
  Object.defineProperty(el, 'checked', { get: () => inp.checked, set: v => { inp.checked = !!v; } });
  Object.defineProperty(el, 'disabled', { get: () => inp.disabled, set: v => { inp.disabled = !!v; } });
  return el;
}

export function check(text, checked, onchange) {
  const inp = h('input', { type: 'checkbox', checked: !!checked, onchange: () => onchange && onchange(inp.checked) });
  const el = h('label.check', {}, inp, h('span', {}, text));
  Object.defineProperty(el, 'checked', { get: () => inp.checked, set: v => { inp.checked = !!v; } });
  el.input = inp;
  return el;
}

/** Choices side by side, one picked. */
export function segmented(options, value, onchange) {
  const el = h('div.seg');
  const draw = () => {
    clear(el);
    for (const o of options) {
      const [v, t] = Array.isArray(o) ? o : [o, o];
      el.appendChild(h('button', { type: 'button', class: String(v) === String(value) ? 'on' : '', onclick: () => { value = v; draw(); onchange && onchange(v); } }, t));
    }
  };
  draw();
  el.set = v => { value = v; draw(); };
  return el;
}

/** A slider with its value shown beside it. */
export function slider(min, max, step, value, format, onchange) {
  const range = h('input', { type: 'range', min, max, step, value });
  const val = h('span.val', {}, format ? format(+value) : value);
  range.addEventListener('input', () => { val.textContent = format ? format(+range.value) : range.value; onchange && onchange(+range.value, false); });
  range.addEventListener('change', () => onchange && onchange(+range.value, true));
  const el = h('div.slider', {}, range, val);
  el.range = range;
  Object.defineProperty(el, 'value', { get: () => +range.value, set: v => { range.value = v; val.textContent = format ? format(+v) : v; } });
  Object.defineProperty(el, 'disabled', { get: () => range.disabled, set: v => { range.disabled = !!v; } });
  return el;
}

export function tabs(names, onchange, active = 0) {
  const el = h('div.tabs');
  const buttons = names.map((n, i) => h('button', { type: 'button', class: i === active ? 'active' : '', onclick: () => select(i) }, n));
  buttons.forEach(b => el.appendChild(b));
  function select(i) { buttons.forEach((b, k) => b.classList.toggle('active', k === i)); onchange(i); }
  el.select = select;
  return el;
}

/** A table: columns [{title, key|render, cls}], rows; onpick(row, tr) makes rows clickable. */
export function table(columns, rows, opts = {}) {
  const tbody = h('tbody');
  const t = h('table.grid', {}, h('thead', {}, h('tr', {}, columns.map(c => h('th', { class: c.cls || '' }, c.title)))), tbody);
  let selected = null;
  for (const row of rows) {
    if (row && row.__group) { tbody.appendChild(h('tr.group', {}, h('td', { colSpan: columns.length }, row.__group))); continue; }
    const tr = h('tr', { class: opts.onpick ? 'pick' : '' }, columns.map(c => {
      const v = c.render ? c.render(row) : row[c.key];
      return h('td', { class: c.cls || '' }, v instanceof Node || Array.isArray(v) ? v : (v == null ? '' : String(v)));
    }));
    if (opts.onpick) tr.addEventListener('click', () => { if (selected) selected.classList.remove('sel'); selected = tr; tr.classList.add('sel'); opts.onpick(row, tr); });
    if (opts.ondouble) tr.addEventListener('dblclick', () => opts.ondouble(row, tr));
    if (opts.selected && opts.selected(row)) { selected = tr; tr.classList.add('sel'); }
    tbody.appendChild(tr);
  }
  return h('div.list', { style: opts.height ? { maxHeight: opts.height } : {} }, t);
}

// ---- dialogs ----

const layer = () => document.getElementById('layer');

/**
 * A dialog: dialog({title, icon, body, buttons:[{text, kind, value|onclick}], wide, full, onclose}) - resolves with the
 * value of the button pressed (or null when closed). A button's onclick may return false to keep the dialog open.
 */
export function dialog(opts) {
  return new Promise(resolve => {
    let done = false;
    const close = v => {
      if (done) return;
      done = true;
      back.remove();
      document.removeEventListener('keydown', key);
      if (opts.onclose) opts.onclose(v);
      resolve(v);
    };
    const foot = h('div.df');
    const left = h('div.left');
    if (opts.left) { (Array.isArray(opts.left) ? opts.left : [opts.left]).forEach(x => left.appendChild(x)); foot.appendChild(left); }
    for (const b of opts.buttons || [{ text: T('OK'), kind: 'primary', value: true }]) {
      const btn = button(b.text, async () => {
        if (b.onclick) {
          btn.disabled = true;
          try { const r = await b.onclick(); if (r === false) return; close(r === undefined ? b.value : r); }
          catch (e) { await alert(e.message || String(e)); }
          finally { btn.disabled = false; }
          return;
        }
        close(b.value);
      }, b.kind || '', b.icon);
      if (b.default) btn.dataset.default = '1';
      foot.appendChild(btn);
    }
    const box = h('div.dialog', { class: (opts.wide ? 'wide ' : '') + (opts.full ? 'full' : ''), role: 'dialog' },
      h('div.dh', {}, opts.icon ? h('span.ic', {}, icon(opts.icon)) : null, h('h2', {}, opts.title || meta.product || 'AFK Realm'),
        opts.noClose ? null : h('button.x', { type: 'button', title: T('Close'), onclick: () => close(null) }, '×')),
      h('div.db', {}, opts.body),
      (opts.buttons && opts.buttons.length === 0) ? null : foot);
    const back = h('div.backdrop', {}, box);
    const key = e => {
      if (e.key === 'Escape' && !opts.noClose) { e.preventDefault(); close(null); }
      if (e.key === 'Enter' && opts.enter !== false && e.target.tagName !== 'TEXTAREA') {
        const d = foot.querySelector('[data-default]') || foot.querySelector('.btn.primary');
        if (d && e.target.tagName !== 'BUTTON') { e.preventDefault(); d.click(); }
      }
    };
    document.addEventListener('keydown', key);
    layer().appendChild(back);
    box.close = close;
    if (opts.onopen) opts.onopen(box, close);
    const first = box.querySelector('.db input, .db select, .db textarea');
    if (first && opts.focus !== false) setTimeout(() => first.focus(), 30);
  });
}

export function alert(text, title) {
  return dialog({ title: title || meta.product, icon: 'info', body: h('div.pre', {}, text), buttons: [{ text: T('OK'), kind: 'primary', value: true }] });
}

export async function confirm(text, yes, no, title, kind) {
  const v = await dialog({ title: title || meta.product, icon: 'warn', body: h('div.pre', {}, text),
    buttons: [{ text: no || T('No'), value: false }, { text: yes || T('Yes'), kind: kind || 'primary', value: true, default: true }] });
  return v === true;
}

export async function prompt(text, value = '', opts = {}) {
  const inp = opts.multiline ? h('textarea', { value, rows: 5, style: { width: '100%' } }) : input(value, { style: { width: '100%' }, type: opts.password ? 'password' : 'text' });
  const v = await dialog({ title: opts.title || meta.product, icon: opts.icon || 'info', body: h('div', {}, h('p', {}, ...lines(text)), inp),
    buttons: [{ text: T('Cancel'), value: null }, { text: opts.ok || T('OK'), kind: 'primary', onclick: () => inp.value }] });
  return v;
}

export function toast(text, ms = 3500) {
  const t = h('div.toast', {}, text);
  layer().appendChild(t);
  setTimeout(() => t.remove(), ms);
}

/** Runs an action; a refusal or error is shown, a question (409) is asked and the action sent again with force. */
export async function act(fn) {
  try { return await fn(false); }
  catch (e) {
    if (e instanceof ApiError && e.confirm) {
      if (await confirm(e.confirm)) {
        try { return await fn(true); } catch (e2) { await alert(e2.message); }
      }
      return null;
    }
    await alert(e.message || String(e));
    return null;
  }
}

/** Waits for the task with this id to end; returns its last state. */
export function waitJob(job) {
  return new Promise(resolve => {
    if (!job) return resolve(null);
    if (!job.running) return resolve(job);
    const off = on('job', j => {
      if (j && j.id === job.id && !j.running) { off(); resolve(j); }
    });
  });
}

/**
 * Starts a task (an API call that answers with a job) and shows its status lines in a small dialog until it ends;
 * returns the finished job. Errors are shown.
 */
/** A job that went wrong: its message, and the way to the help files right there. */
export async function failed(message) {
  const v = await dialog({ title: meta.product, icon: 'warn', body: h('div', {}, h('div.pre', {}, message),
    h('p.small.muted', { style: { marginTop: '12px' } }, T('Stuck? "Get help" collects all logs and crash files into one ZIP you can send on Discord.'))),
    buttons: [{ text: T('Get help …'), value: 'help' }, { text: T('OK'), kind: 'primary', value: true }] });
  if (v === 'help') import('./help.js').then(m => m.helpBundle());
}

export async function runTask(call, title) {
  let job;
  try { job = await call(); }
  catch (e) {
    if (e instanceof ApiError && e.confirm) { if (!await confirm(e.confirm)) return null; return runTask(() => call(true), title); }
    await alert(e.message); return null;
  }
  if (!job || !job.id) return job;
  if (!job.running) { if (!job.ok) await failed(job.message); return job; }
  const status = h('div.muted', {}, job.status || T('Working …'));
  const bar = h('div.bar.thin.busy', {}, h('i'));
  let closeIt;
  dialog({ title: title || job.title, icon: 'server', body: h('div', {}, status, bar), buttons: [], noClose: true, onopen: (b, c) => { closeIt = c; } });
  const off = on('job', j => { if (j && j.id === job.id) status.textContent = j.status || status.textContent; });
  const end = await waitJob(job);
  off();
  closeIt && closeIt();
  if (end && !end.ok) await failed(end.message);
  return end;
}

// ---- folders and files ----

/**
 * Lets the user choose a folder (or a file with one of the extensions) on the computer AFK Realm runs on.
 * Resolves with the path, or null.
 */
export function choose({ title, start, files, mustExist = true, newFolder = true, hint } = {}) {
  let path = start || '';
  let picked = null;
  const crumbs = h('div.row.small.mono');
  const pathInput = input('', { style: { flex: '1', minWidth: '200px' } });
  const listBox = h('div.list.picker-list');
  const go = async p => {
    try {
      const r = await get('/api/fs/list', { path: p || '', files: files || '' });
      path = r.path; picked = null;
      pathInput.value = path;
      clear(listBox);
      if (r.parent !== null && r.parent !== undefined) listBox.appendChild(item('up', '..', () => go(r.parent)));
      for (const d of r.dirs) listBox.appendChild(item('folder', d.name, () => go(d.path), d.path));
      for (const f of r.files) {
        const it = item('file', f.name + '   ' + fmtBytes(f.size) + (f.modified ? '   ' + f.modified : ''), () => { picked = f.path; listBox.querySelectorAll('.item').forEach(x => x.classList.remove('sel')); it.classList.add('sel'); pathInput.value = f.path; }, f.path);
        it.addEventListener('dblclick', () => { picked = f.path; box && box.close(f.path); });
        listBox.appendChild(it);
      }
      if (!r.dirs.length && !r.files.length) listBox.appendChild(h('div.item.muted', {}, T('(empty)')));
    } catch (e) {
      // A path typed by hand that does not exist yet: go to the nearest folder that does.
      if (p && p.length > 1) {
        const parent = p.replace(/[\\/][^\\/]*[\\/]?$/, '');
        if (parent && parent !== p) return go(parent);
      }
      await alert(e.message);
    }
  };
  const item = (ic, text, onclick, title) => {
    const el = h('div.item', { title: title || '', onclick }, icon(ic), h('span', {}, text));
    if (ic === 'folder' || ic === 'up') el.addEventListener('dblclick', onclick);
    return el;
  };
  let box;
  pathInput.addEventListener('keydown', e => { if (e.key === 'Enter') { e.preventDefault(); e.stopPropagation(); go(pathInput.value); } });
  const body = h('div', {}, hint ? h('p.hint', {}, hint) : null,
    h('div.row.nowrap', {}, ghost(T('Places'), () => go('')), pathInput, secondary(T('Go'), () => go(pathInput.value))),
    listBox);
  const p = dialog({
    title: title || (files ? T('Choose a file') : T('Choose a folder')), icon: files ? 'file' : 'folder', wide: true, body, focus: false, enter: false,
    left: newFolder && !files ? [secondary(T('New folder …'), async () => {
      const name = await prompt(T('Name of the new folder:'));
      if (!name) return;
      try { const r = await post('/api/fs/mkdir', { path, name }); go(r.path); } catch (e) { alert(e.message); }
    }, 'plus')] : [],
    buttons: [{ text: T('Cancel'), value: null }, {
      text: files ? T('Open') : T('Choose this folder'), kind: 'primary', onclick: () => {
        if (files) { const v = picked || pathInput.value; if (!v || v === path) { alert(T('Choose a file first.')); return false; } return v; }
        return pathInput.value || path;
      }
    }],
    onopen: b => { box = b; }
  });
  go(path);
  return p;
}

/** Lets the user pick a file in the browser and sends it to AFK Realm; resolves with its path there, or null. */
export function pickUpload(accept) {
  return new Promise(resolve => {
    const inp = h('input', { type: 'file', accept: accept || '', style: { display: 'none' } });
    inp.addEventListener('change', async () => {
      const f = inp.files && inp.files[0];
      inp.remove();
      if (!f) return resolve(null);
      try { resolve(await upload(f)); } catch (e) { await alert(e.message); resolve(null); }
    });
    document.body.appendChild(inp);
    inp.click();
  });
}

/** A menu at an element: items [{text, sub, onclick, disabled}] or 'sep'. */
export function menu(at, items) {
  const r = at.getBoundingClientRect();
  const m = h('div.menu');
  const back = h('div', { style: { position: 'fixed', inset: '0', pointerEvents: 'auto' }, onclick: () => back.remove() }, m);
  for (const it of items) {
    if (it === 'sep') { m.appendChild(h('hr')); continue; }
    if (!it) continue;
    m.appendChild(h('button', { type: 'button', disabled: !!it.disabled, onclick: e => { e.stopPropagation(); back.remove(); it.onclick && it.onclick(); } },
      it.check !== undefined ? h('span', { style: { width: '16px' } }, it.check ? '✓' : '') : null,
      h('span', {}, it.text, it.sub ? h('div.sub', {}, it.sub) : null)));
  }
  layer().appendChild(back);
  const top = Math.min(r.bottom + 4, window.innerHeight - m.offsetHeight - 10);
  m.style.left = Math.max(8, Math.min(r.left, window.innerWidth - m.offsetWidth - 8)) + 'px';
  m.style.top = Math.max(8, top) + 'px';
}

/** A note line that can be filled later. */
export function note(kind) { return h('div', { class: 'note ' + (kind || '') + ' hidden' }); }
export function setNote(el, text, kind) {
  el.className = 'note ' + (kind || '') + (text ? '' : ' hidden');
  replace(el, ...lines(text || ''));
}

/**
 * Like runTask, with a progress bar (the job's "sub", 0..1), a detail line (its "action") and, for a task that can be
 * stopped, a Cancel button. Resolves with the finished job; a failure is shown unless quiet.
 */
export async function runProgress(call, title, { quiet = false } = {}) {
  let job;
  try { job = await call(); }
  catch (e) { await alert(e.message); return null; }
  if (!job || !job.id) return job;
  const status = h('div', { style: { fontWeight: '600' } }, job.status || T('Working …'));
  const fill = h('i', { style: { width: '0%' } });
  const bar = h('div.bar', {}, fill);
  const detail = h('div.small.muted.pre', { style: { minHeight: '34px' } });
  let closeIt;
  const paint = j => {
    status.textContent = j.status || status.textContent;
    detail.textContent = j.action || '';
    if (j.sub >= 0) { bar.classList.remove('busy'); fill.style.width = Math.round(j.sub * 1000) / 10 + '%'; }
    else bar.classList.add('busy');
  };
  paint(job);
  const buttons = job.cancellable ? [{ text: T('Cancel'), onclick: async () => { await post('/api/job/cancel'); status.textContent = T('Stopping ...'); return false; } }] : [];
  dialog({ title: title || job.title, icon: 'server', body: h('div', { style: { minWidth: '420px' } }, status, bar, detail), buttons, noClose: true, onopen: (b, c) => { closeIt = c; } });
  const off = on('job', j => { if (j && j.id === job.id) paint(j); });
  const end = job.running ? await waitJob(job) : job;
  off();
  closeIt && closeIt();
  if (end && !end.ok && !quiet) await failed(end.message);
  return end;
}
