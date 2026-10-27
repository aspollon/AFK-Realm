// The server consoles (the logs of world- and authserver as they grow, and commands to the worldserver) and the log viewer.
import { h, T, F, meta, get, post, replace } from './core.js';
import { dialog, tabs, secondary } from './ui.js';

const MaxChars = 600000;

/** A console that follows one log; print() adds own lines (commands and answers). */
function follower(what) {
  const el = h('div.console', { style: { height: '100%', minHeight: '120px' } });
  let offset = 0, timer = null, stuck = true;
  el.addEventListener('scroll', () => { stuck = el.scrollTop + el.clientHeight >= el.scrollHeight - 30; });
  const add = text => {
    if (!text) return;
    el.appendChild(document.createTextNode(text));
    if (el.textContent.length > MaxChars) el.textContent = el.textContent.slice(-MaxChars / 2);
    if (stuck) el.scrollTop = el.scrollHeight;
  };
  async function poll() {
    if (!el.isConnected && offset > 0) { stop(); return; }
    try {
      const r = await get('/api/log/tail', { what, from: offset });
      if (r.reset) el.textContent = '';
      offset = r.offset;
      add(r.text);
    } catch { }
  }
  function stop() { if (timer) clearInterval(timer); timer = null; }
  el.start = () => { poll(); timer = setInterval(poll, 1000); };
  el.stop = stop;
  el.print = line => add(line + '\n');
  return el;
}

export async function consoles() {
  const world = follower('server'), auth = follower('auth');
  const input = h('input.mono', { type: 'text', style: { flex: '1' }, placeholder: '.server info', autocomplete: 'off', spellcheck: false });
  const history = []; let at = 0, busy = false;
  async function send() {
    const command = input.value.trim();
    if (!command || busy) return;
    const i = history.indexOf(command); if (i >= 0) history.splice(i, 1);
    history.push(command); at = history.length;
    input.value = '';
    world.print('AC> ' + command);
    busy = true;
    try {
      const r = await post('/api/gm/command', { command });
      world.print(r.problem ? '! ' + r.problem : r.error ? '! ' + r.error : r.answer);
    } catch (e) { world.print('! ' + e.message); }
    finally { busy = false; }
  }
  input.addEventListener('keydown', e => {
    if (e.key === 'Enter') { e.preventDefault(); send(); }
    else if (e.key === 'ArrowUp' && history.length) { at = Math.max(0, at - 1); input.value = history[at]; e.preventDefault(); }
    else if (e.key === 'ArrowDown' && history.length) { at = Math.min(history.length, at + 1); input.value = at < history.length ? history[at] : ''; e.preventDefault(); }
  });
  const body = h('div', { style: { display: 'grid', gridTemplateRows: 'auto 2fr auto auto 1fr auto', gap: '6px', height: '100%', minHeight: '0' } },
    h('b', {}, T('Worldserver (game world)')), world,
    h('div.row', { style: { margin: 0, flexWrap: 'nowrap', alignItems: 'center' } }, h('span.mono', {}, 'AC>'), input, secondary(T('Send'), () => send())),
    h('b', { style: { marginTop: '8px' } }, T('Authserver (login)')), auth,
    h('div.small.muted', {}, T('The authserver takes no commands.')));
  world.start(); auth.start();
  await dialog({
    title: meta.product + ' – ' + T('Server consoles'), icon: 'console', full: true, body, enter: false,
    buttons: [{ text: T('Close'), value: null }],
    onopen: b => { const d = b.querySelector('.db'); d.style.display = 'flex'; d.style.flexDirection = 'column'; d.firstChild.style.flex = '1'; setTimeout(() => input.focus(), 50); }
  });
  world.stop(); auth.stop();
}

export async function logsDialog() {
  const logs = [['server-log', 'Server.log'], ['errors-log', 'Errors.log'], ['auth-log', 'Auth.log'], ['install-log', T('Install log')], ['restart-log', T('Scheduled restarts')]];
  const view = h('div.console', { style: { flex: '1', height: 'auto', minHeight: '0' } });
  const path = h('div.small.muted.mono');
  let current = 0;
  async function show(i) {
    current = i;
    view.textContent = T('Reading …');
    try {
      const r = await get('/api/text', { what: logs[i][0] });
      path.textContent = r.path || '';
      view.textContent = r.text || T('(empty)');
      view.scrollTop = view.scrollHeight;
    } catch (e) { view.textContent = e.message; }
  }
  const bar = tabs(logs.map(l => l[1]), i => show(i));
  const body = h('div', { style: { display: 'flex', flexDirection: 'column', height: '100%', minHeight: '0', gap: '6px' } }, bar, view, path);
  show(0);
  await dialog({
    title: meta.product + ' – ' + T('Logs'), icon: 'file', full: true, body, enter: false,
    left: [secondary(T('Refresh'), () => show(current))],
    buttons: [{ text: T('Close'), value: null }],
    onopen: b => { const d = b.querySelector('.db'); d.style.display = 'flex'; d.style.flexDirection = 'column'; d.firstChild.style.flex = '1'; }
  });
}
