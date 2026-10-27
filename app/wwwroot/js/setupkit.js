// The frame of a module setup page: a header with the module's switch, tabs, cards with switches and sliders, and a
// footer to save. Everything is read from and written to the module's .conf file, so the file and the page agree.
import { h, T, F, meta, post, replace, clear } from './core.js';
import { icon } from './icons.js';
import { primary, secondary, ghost, dialog, alert, confirm, toggle as toggleEl, slider as sliderEl, segmented, tabs as tabsEl } from './ui.js';

const unquote = v => (v != null && v.length >= 2 && v[0] === '"' && v[v.length - 1] === '"') ? v.slice(1, -1) : v;
export const num = (v, decimals) => Number(v).toFixed(decimals);
const decimalsOf = step => { const s = String(step); return s.includes('.') ? s.split('.')[1].length : 0; };

/** Loads config files and keeps the page's changes to them. */
export class Conf {
  constructor(file) {
    this.name = file.name; this.path = file.path; this.exists = file.exists;
    this.values = new Map(); this.defaults = new Map(); this.quoted = new Set(); this.keys = [];
    for (const o of file.options) {
      this.keys.push(o.key);
      if (o.has) this.values.set(o.key, o.value);
      if (o.defaultValue != null) this.defaults.set(o.key, o.defaultValue);
      if (o.quoted) this.quoted.add(o.key);
    }
    this.pending = new Map(); this.removed = new Set();
  }
  raw(key, fallback) {
    if (this.pending.has(key)) return this.pending.get(key);
    if (this.removed.has(key)) return fallback;
    if (this.values.has(key)) return this.values.get(key);
    if (this.defaults.has(key)) return this.defaults.get(key);
    return fallback;
  }
  has(key) { if (this.pending.has(key)) return true; if (this.removed.has(key)) return false; return this.values.has(key); }
  set(key, value) {
    const bare = unquote(String(value));
    const current = this.values.has(key) ? this.values.get(key) : this.defaults.get(key);
    this.removed.delete(key);
    if (this.values.has(key) && current === bare) this.pending.delete(key); else this.pending.set(key, bare);
  }
  unset(key) { this.pending.delete(key); if (this.values.has(key)) this.removed.add(key); }
  get unsaved() { return this.pending.size + this.removed.size; }
}

export async function readConfs(names) {
  const r = await post('/api/conf/read', { files: names });
  return r.files.map(f => new Conf(f));
}

/**
 * Opens a setup page. opts: {file, title, subtitle, icon, masterKey, masterText, also: [files], build(page), reset(page), beforeSave(page), saved(page)}
 * build adds tabs and cards; it may be async (for data the page needs first).
 */
export async function setupPage(opts) {
  let confs;
  try { confs = await readConfs([opts.file, ...(opts.also || [])]); } catch (e) { return alert(e.message); }
  const conf = confs[0];
  const byName = new Map(confs.map(c => [c.name, c]));
  const loaders = [], refreshers = [], pages = [];
  let loading = 0, closeIt;
  const changes = h('span.small.muted');
  const shown = h('div.setup-cards');
  const tabNames = [];

  const page = {
    conf, confs, also: n => byName.get(n),
    get loading() { return loading > 0; },
    raw: (key, fallback, c = conf) => c.raw(key, fallback),
    has: (key, c = conf) => c.has(key),
    number: (key, fallback, c = conf) => { const v = parseFloat(c.raw(key, '')); return isNaN(v) ? fallback : v; },
    flag: (key, fallback, c = conf) => { const v = c.raw(key, fallback ? '1' : '0'); return v === '1' || String(v).toLowerCase() === 'true'; },
    defaultOf: (key, fallback, c = conf) => c.defaults.has(key) ? c.defaults.get(key) : fallback,
    set: (key, value, c = conf) => { if (loading) return; c.set(key, value); changed(); },
    unset: (key, c = conf) => { if (loading) return; c.unset(key); changed(); },
    loader: fn => loaders.push(fn),
    onChange: fn => refreshers.push(fn),
    reload,
    changed,
    tab(name) { tabNames.push(name); pages.push([]); },
    card(title, text, ic) {
      const body = h('div.setup-body');
      const el = h('div.card.setup', {}, h('h2', {}, ic ? h('span.ic', {}, icon(ic)) : null, title), text ? h('p.lead', {}, text) : null, body);
      el.add = (...x) => { x.forEach(c => c && body.appendChild(c)); return x[0]; };
      pages[pages.length - 1].push(el);
      return el;
    },
    note(text) { pages[pages.length - 1].push(h('p.small.muted', { style: { margin: '0 4px 14px', maxWidth: '860px' } }, text)); },
    small: (text, indent) => h('div.small.muted', { style: { margin: '0 0 10px ' + (indent || 0) + 'px' } }, text),
    close: v => closeIt && closeIt(v),

    toggle(card, key, text, hint) {
      const t = toggleEl(text, false, v => page.set(key, v ? '1' : '0'));
      card.add(h('div', {}, t));
      if (hint) card.add(page.small(hint, 50));
      loaders.push(() => { t.checked = page.flag(key, true); });
      return t;
    },
    slider(card, key, label, min, max, step, format, hint) {
      const d = decimalsOf(step);
      const s = sliderEl(min, max, step, min, format || (v => num(v, d)), v => { if (key) page.set(key, num(v, d)); });
      card.add(row(label, s));
      if (hint) card.add(page.small(hint, 258));
      if (key) loaders.push(() => { s.value = page.number(key, min); });
      return s;
    },
    range(card, key, label, min, max, hint, swatch) {
      const s = dual(min, max, 1, v => String(Math.round(v)), (a, b) => page.set(key, Math.round(a) + '-' + Math.round(b)));
      card.add(row(label, s, swatch));
      if (hint) card.add(page.small(hint, 258));
      loaders.push(() => {
        let r = parseRange(page.raw(key, ''));
        if (!r) r = parseRange(page.defaultOf(key, '')) || [min, max];
        s.set(r[0], r[1]);
      });
      return s;
    },
    pair(card, keyLow, keyHigh, label, min, max, step, format, hint) {
      const d = decimalsOf(step);
      const s = dual(min, max, step, format || (v => num(v, d)), (a, b) => { page.set(keyLow, num(a, d)); page.set(keyHigh, num(b, d)); });
      card.add(row(label, s));
      if (hint) card.add(page.small(hint, 258));
      loaders.push(() => s.set(page.number(keyLow, min), page.number(keyHigh, max)));
      return s;
    },
    choice(card, key, label, values, texts, hint) {
      const seg = segmented(values.map((v, i) => [v, texts[i]]), values[0], v => page.set(key, v));
      card.add(row(label, seg));
      if (hint) card.add(page.small(hint, 258));
      loaders.push(() => { const v = page.raw(key, values[0]); seg.set(values.includes(v) ? v : values[0]); });
      return seg;
    },
    text(card, key, label, hint) {
      const box = h('input', { type: 'text', style: { width: '100%' } });
      box.addEventListener('input', () => { if (!box.value.includes('"')) page.set(key, box.value.trim()); });
      card.add(row(label, box));
      if (hint) card.add(page.small(hint, 258));
      loaders.push(() => { box.value = page.raw(key, ''); });
      return box;
    },
    flags(card, key, values, names, columns = 3, sep = ',') {
      const toggles = values.map((v, i) => toggleEl(names[i], false, () => write()));
      card.add(h('div', { style: { display: 'grid', gridTemplateColumns: `repeat(${columns}, 1fr)`, gap: '2px 8px', margin: '2px 0 8px' } }, toggles));
      const split = () => page.raw(key, '').split(sep).map(x => x.trim()).filter(Boolean);
      function write() {
        const other = split().filter(x => !values.map(String).includes(x));
        const keep = values.filter((v, i) => toggles[i].checked).map(String);
        page.set(key, [...keep, ...other].sort((a, b) => (parseInt(a) || 999) - (parseInt(b) || 999)).join(sep));
      }
      loaders.push(() => { const on = new Set(split()); values.forEach((v, i) => { toggles[i].checked = on.has(String(v)); }); });
      return toggles;
    },
  };

  function row(label, control, swatch) {
    return h('div.setup-row', {}, h('label', {}, swatch ? h('i.swatch', { style: { background: swatch } }) : null, label), control);
  }

  function unsaved() { return confs.reduce((n, c) => n + c.unsaved, 0); }
  function changed() {
    const n = unsaved();
    changes.textContent = n === 0 ? '' : n === 1 ? T('1 change not saved yet') : F('{0} changes not saved yet', n);
    changes.className = 'small ' + (n ? 'warn' : 'muted');
    refreshers.forEach(r => { try { r(); } catch (e) { console.error(e); } });
  }
  function reload() {
    loading++;
    try { loaders.forEach(l => { try { l(); } catch (e) { console.error(e); } }); }
    finally { loading--; }
    changed();
  }

  const master = toggleEl(opts.masterText, false, v => page.set(opts.masterKey, v ? '1' : '0'));
  loaders.push(() => { master.checked = page.flag(opts.masterKey, true); });
  page.master = master;

  await opts.build(page);

  async function save() {
    if (opts.beforeSave) await opts.beforeSave(page);
    const files = confs.filter(c => c.unsaved > 0).map(c => ({ name: c.name, set: Object.fromEntries(c.pending), unset: [...c.removed] }));
    let r;
    try { r = await post('/api/conf/save', { files }); }
    catch (e) { await alert(T('The settings could not be saved:') + '\n' + e.message); return false; }
    for (const c of confs) {
      for (const [k, v] of c.pending) c.values.set(k, v);
      for (const k of c.removed) c.values.delete(k);
      c.pending.clear(); c.removed.clear();
    }
    if (opts.saved) await opts.saved(page);
    changed();
    await alert(r.message);
    return true;
  }

  const showPage = i => { replace(shown, pages[i] || []); shown.parentElement && (shown.parentElement.scrollTop = 0); };
  const bar = tabsEl(tabNames, i => showPage(i));
  reload();
  showPage(0);
  if (!conf.exists) setTimeout(() => alert(F('{0} was not found in the server\'s configs\\modules folder. Build the server with the module first.', opts.file.split('/').pop())), 50);

  const head = h('div.setup-head', {},
    h('div.row', { style: { alignItems: 'center', flexWrap: 'nowrap' } },
      h('span.bigic', {}, icon(opts.icon || 'modules')),
      h('div', { style: { flex: '1' } }, h('h1', { style: { margin: 0, fontSize: '21px' } }, opts.title), h('div.small.muted', {}, opts.subtitle)),
      master),
    bar);
  const body = h('div', { style: { display: 'flex', flexDirection: 'column', height: '100%', minHeight: '0' } },
    head, h('div.setup-scroll', { style: { flex: '1', overflow: 'auto', minHeight: '0' } }, shown));
  await dialog({
    title: meta.product + ' – ' + opts.title, icon: opts.icon || 'modules', full: true, body, enter: false,
    left: [ghost(T('Reset to defaults'), async () => {
      if (!await confirm(T('Set every option on this page back to the value the module comes with? Nothing is written until you save.'))) return;
      if (opts.reset) opts.reset(page); else for (const [k, v] of conf.defaults) conf.set(k, v);
      reload();
    }), changes],
    buttons: [{
      text: T('Cancel'), onclick: async () => {
        if (unsaved() === 0) return null;
        const r = await dialog({ title: meta.product, icon: 'warn', body: h('p', {}, T('Save your changes before closing?')),
          buttons: [{ text: T('Cancel'), value: 'cancel' }, { text: T('No'), value: 'no' }, { text: T('Yes'), kind: 'primary', value: 'yes' }] });
        if (r === 'cancel' || r === null) return false;
        if (r === 'yes' && !await save()) return false;
        return null;
      }
    }, { text: T('Save'), kind: 'primary', onclick: async () => (await save()) ? null : false }],
    onopen: (b, c) => { closeIt = c; const d = b.querySelector('.db'); d.style.display = 'flex'; d.style.flexDirection = 'column'; }
  });
}

export function parseRange(text) {
  const p = String(text || '').split('-');
  if (p.length !== 2) return null;
  const a = parseInt(p[0].trim()), b = parseInt(p[1].trim());
  return !isNaN(a) && !isNaN(b) && a >= 1 && b >= a ? [a, b] : null;
}

/** A slider with two thumbs; onchange(low, high). */
export function dual(min, max, step, format, onchange) {
  const lo = h('input', { type: 'range', min, max, step, value: min });
  const hi = h('input', { type: 'range', min, max, step, value: max });
  const val = h('span.val', { style: { minWidth: '110px' } });
  const fill = h('i.dual-fill');
  const paint = () => {
    const a = +lo.value, b = +hi.value;
    val.textContent = format(a) + ' – ' + format(b);
    const pa = (a - min) / (max - min) * 100, pb = (b - min) / (max - min) * 100;
    fill.style.left = pa + '%'; fill.style.width = Math.max(0, pb - pa) + '%';
  };
  const fire = () => { onchange && onchange(+lo.value, +hi.value); };
  lo.addEventListener('input', () => { if (+lo.value > +hi.value) lo.value = hi.value; paint(); fire(); });
  hi.addEventListener('input', () => { if (+hi.value < +lo.value) hi.value = lo.value; paint(); fire(); });
  const el = h('div.slider', {}, h('div.dual', {}, h('i.dual-track'), fill, lo, hi), val);
  el.set = (a, b) => { hi.value = b; lo.value = a; paint(); };
  Object.defineProperty(el, 'low', { get: () => +lo.value });
  Object.defineProperty(el, 'high', { get: () => +hi.value });
  Object.defineProperty(el, 'disabled', { set: v => { lo.disabled = hi.disabled = !!v; } });
  paint();
  return el;
}
