// The basics every page uses: texts in the chosen language, talking to AFK Realm, events, building elements.

export const meta = { strings: {} };

/** A text in the language shown (looked up by its English original, like L.T in the program). */
export function T(english) {
  if (english == null) return '';
  const s = meta.strings[english];
  if (s) return s;
  const key = english.replace(/\r\n/g, '\n').trim();
  if (key !== english && meta.strings[key]) {
    const lead = english.length - english.trimStart().length, trail = english.length - english.trimEnd().length;
    return english.substring(0, lead) + meta.strings[key] + english.substring(english.length - trail);
  }
  return english;
}

/** A text with places for values ({0}, {1} ...). */
export function F(english, ...args) {
  const t = T(english);
  return t.replace(/\{(\d+)(?::([^}]*))?\}/g, (m, i, fmt) => {
    let v = args[+i];
    if (v === undefined) return m;
    if (typeof v === 'number' && fmt) {
      if (/^N(\d*)$/.test(fmt)) { const d = +(fmt.slice(1) || 0); return v.toLocaleString(meta.language === 'en' ? 'en-US' : meta.language, { minimumFractionDigits: d, maximumFractionDigits: d }); }
    }
    return String(v);
  });
}

/** A short text that means different things in different places ("context|text"). */
export function C(context, english) { return meta.strings[context + '|' + english] || english; }

// ---- talking to AFK Realm ----

export class ApiError extends Error {
  constructor(message, confirm) { super(message); this.confirm = confirm; }
}

export async function api(path, body) {
  const options = body === undefined ? { method: 'GET' } : {
    method: 'POST', headers: { 'Content-Type': 'application/json', 'X-AFK': '1' }, body: JSON.stringify(body ?? {})
  };
  let res;
  try { res = await fetch(path, options); }
  catch (e) { throw new ApiError(T('AFK Realm does not answer. Is it still running?')); }
  if (res.status === 401) { location.reload(); throw new ApiError(T('Not logged in.')); }
  let data = null;
  try { data = await res.json(); } catch { }
  if (res.status === 409 && data && data.confirm) throw new ApiError(data.confirm, true);
  if (!res.ok) throw new ApiError((data && data.error) || (T('The request failed') + ' (' + res.status + ')'));
  return data;
}

export const get = (path, params) => api(params ? path + '?' + new URLSearchParams(params) : path);
export const post = (path, body) => api(path, body || {});

/** Sends a file of the browser to AFK Realm; the answer is the path it was put at. */
export async function upload(file) {
  const res = await fetch('/api/files/upload?name=' + encodeURIComponent(file.name), { method: 'POST', headers: { 'X-AFK': '1' }, body: file });
  if (!res.ok) throw new ApiError(T('The file could not be sent.'));
  return (await res.json()).path;
}

/** Lets the browser save a file AFK Realm offers (a link from the server). */
export function download(url) {
  const a = document.createElement('a');
  a.href = url; a.download = ''; document.body.appendChild(a); a.click(); a.remove();
}

// ---- events ----

const listeners = {};
export const state = { status: null, job: null };

export function on(type, fn) { (listeners[type] ||= new Set()).add(fn); return () => listeners[type].delete(fn); }
function emit(type, data) { (listeners[type] || []).forEach(fn => { try { fn(data); } catch (e) { console.error(e); } }); }

export function connect() {
  let es;
  const open = () => {
    es = new EventSource('/api/events');
    for (const type of ['status', 'job', 'joblog', 'alert', 'console', 'gm']) {
      es.addEventListener(type, e => {
        let data = null;
        try { data = JSON.parse(e.data); } catch { }
        if (type === 'status') state.status = data;
        if (type === 'job') state.job = data;
        emit(type, data);
      });
    }
    es.onerror = () => { emit('offline', true); };
    es.onopen = () => { emit('offline', false); };
  };
  open();
}

// ---- elements ----

/** Builds an element: h('div.card#x', {onclick, title, ...}, children...). */
export function h(spec, attrs, ...children) {
  if (attrs == null || typeof attrs !== 'object' || attrs instanceof Node || Array.isArray(attrs)) { if (attrs != null) children.unshift(attrs); attrs = {}; }
  const m = /^([a-z0-9]+)?((?:[.#][\w-]+)*)$/i.exec(spec) || [];
  const el = document.createElement(m[1] || 'div');
  for (const part of (m[2] || '').match(/[.#][\w-]+/g) || []) {
    if (part[0] === '.') el.classList.add(part.slice(1)); else el.id = part.slice(1);
  }
  for (const [k, v] of Object.entries(attrs)) {
    if (v == null || v === false) continue;
    if (k.startsWith('on') && typeof v === 'function') el.addEventListener(k.slice(2), v);
    else if (k === 'class') String(v).split(' ').filter(Boolean).forEach(c => el.classList.add(c));
    else if (k === 'style' && typeof v === 'object') Object.assign(el.style, v);
    else if (k === 'html') el.innerHTML = v;
    else if (k in el && k !== 'list' && k !== 'form') { try { el[k] = v; } catch { el.setAttribute(k, v); } }
    else el.setAttribute(k, v === true ? '' : v);
  }
  add(el, children);
  return el;
}

function add(el, children) {
  for (const c of children) {
    if (c == null || c === false) continue;
    if (Array.isArray(c)) add(el, c);
    else if (c instanceof Node) el.appendChild(c);
    else el.appendChild(document.createTextNode(String(c)));
  }
}

export function clear(el) { while (el.firstChild) el.removeChild(el.firstChild); return el; }
export function replace(el, ...children) { clear(el); add(el, children); return el; }

/** Text with line breaks kept. */
export function lines(text) {
  const out = [];
  String(text ?? '').split('\n').forEach((l, i) => { if (i) out.push(h('br')); out.push(l); });
  return out;
}

export const sleep = ms => new Promise(r => setTimeout(r, ms));

export function debounce(fn, ms) {
  let t;
  return (...a) => { clearTimeout(t); t = setTimeout(() => fn(...a), ms); };
}

export function fmtBytes(n) {
  if (n == null) return '';
  if (n < 1024) return n + ' B';
  if (n < 1048576) return (n / 1024).toFixed(0) + ' KB';
  if (n < 1073741824) return (n / 1048576).toFixed(1) + ' MB';
  return (n / 1073741824).toFixed(1) + ' GB';
}

export function clock(seconds) {
  seconds = Math.max(0, seconds | 0);
  const p = n => String(n).padStart(2, '0');
  return p(Math.floor(seconds / 3600)) + ':' + p(Math.floor(seconds / 60) % 60) + ':' + p(seconds % 60);
}
