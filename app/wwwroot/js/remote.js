// Access from other computers: switched on here, with a password; shows the addresses to open on the laptop.
import { h, T, F, meta, get, post, replace } from './core.js';
import { card, primary, secondary, toggle, input, password, field, alert, toast } from './ui.js';

export function remoteCard() {
  const body = h('div', {}, h('div.muted.small', {}, T('Reading …')));
  const el = card(T('Access from other computers'),
    F('Manage this server from your laptop or another PC in your home network or VPN, in the browser. {0} asks for a password there. It is meant for your own networks, not for the internet.', meta.product), 'globe', body);
  load();

  async function load() {
    let r;
    try { r = await get('/api/remote'); } catch (e) { replace(body, h('div.small.bad', {}, e.message)); return; }
    paint(r);
  }

  function paint(r) {
    const on = toggle(T('Allow access from other computers'), r.enabled, null, { disabled: r.byCommandLine });
    const pw = password({ style: { width: '220px' }, placeholder: r.hasPassword ? T('(unchanged)') : '' });
    const port = input(String(r.port), { style: { width: '90px' }, inputMode: 'numeric' });
    const save = primary(T('Apply'), async () => {
      save.disabled = true;
      try {
        const res = await post('/api/remote', { enabled: on.checked, password: pw.value, port: Number(port.value) || 0 });
        pw.value = '';
        paint(res);
        toast(res.running ? T('Access from other computers is on.') : T('Access from other computers is off.'));
      } catch (e) { await alert(e.message); }
      finally { save.disabled = false; }
    });
    const parts = [h('div', {}, on)];
    if (r.byCommandLine) parts.push(h('div.small.muted', {}, T('AFK Realm was started with --listen; that start decides about the access from other computers.')));
    else parts.push(h('div.row', { style: { alignItems: 'flex-end' } },
      field(r.hasPassword ? T('New password (optional)') : T('Password for the access'), pw),
      field(T('Port'), port), save));
    if (r.error) parts.push(h('div.small.bad', {}, r.error));
    if (r.running) {
      parts.push(h('p', { style: { margin: '12px 0 4px', fontWeight: '600' } }, T('Open one of these addresses in the browser of the other computer:')));
      const list = h('div', {}, r.addresses.length ? r.addresses.map(a => h('div.row', { style: { alignItems: 'center', margin: '2px 0', flexWrap: 'nowrap' } },
        h('code', { style: { fontSize: '14px', userSelect: 'all' } }, a.address),
        h('span.small.muted', { style: { overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' } }, a.network),
        secondary(T('Copy'), async () => { try { await navigator.clipboard.writeText(a.address); toast(T('Copied.')); } catch { } }))) : h('div.small.muted', {}, T('This computer has no network address right now.')));
      parts.push(list);
      parts.push(h('div.small.muted', { style: { marginTop: '8px' } },
        T('Use the address of the network the other computer is in: usually 192.168.… at home, the address of the VPN adapter (e.g. 26.… for Radmin VPN) from elsewhere. Log in there with the password.')));
      parts.push(h('div.small.muted', {}, F('While the access is on, {0} keeps running in the background when you close its window, so the other computer stays connected. Start {0} again to see the window here.', meta.product)));
      if (!r.windows) parts.push(h('div.small.muted', {}, F('If a firewall is on (ufw), allow the port once: sudo ufw allow {0}/tcp', r.port)));
    }
    replace(body, ...parts);
  }

  return el;
}
