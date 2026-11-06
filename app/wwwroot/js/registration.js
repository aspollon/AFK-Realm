// The public registration site: set up a second, small web server that only has the player page and its downloads.
// It is intentionally separate from this admin page, so a player can never reach the admin API through it.
import { h, T, F, meta, get, post, replace } from './core.js';
import { card, primary, secondary, toggle, input, field, alert, toast, runProgress } from './ui.js';

export function registrationCard() {
  const body = h('div', {}, h('div.muted.small', {}, T('Reading ...')));
  const el = card(T('Public registration'), F('Let players create an account from a browser, without installing {0}. The public site is a separate listener: it only has the registration page, the account request and the realm-file downloads. It never exposes the admin page or its API.', meta.product), 'globe', body);
  load();

  async function load() {
    let r;
    try { r = await get('/api/registration'); } catch (e) { replace(body, h('div.small.bad', {}, e.message)); return; }
    paint(r);
  }

  function paint(r) {
    const on = toggle(T('Open public registration'), r.enabled);
    const port = input(String(r.port), { style: { width: '90px' }, inputMode: 'numeric' });
    const address = input(r.address || '', { style: { width: '220px' }, placeholder: T('e.g. play.example.com or 26.1.2.3') });
    const publicUrl = input(r.publicUrl || '', { style: { width: '100%' }, placeholder: T('optional: the address players should use') });
    const clientUrl = input(r.clientUrl || '', { style: { width: '100%' }, placeholder: 'https://coa-development.org/download' });
    const max = input(String(r.maxAccountsPerIp), { style: { width: '65px' }, inputMode: 'numeric' });
    const window = input(String(r.windowMinutes), { style: { width: '65px' }, inputMode: 'numeric' });
    const save = primary(T('Save and apply'), async () => {
      save.disabled = true;
      try {
        const res = await post('/api/registration', {
          enabled: on.checked, port: Number(port.value) || 0,
          address: address.value.trim(), publicUrl: publicUrl.value.trim(), clientUrl: clientUrl.value.trim(),
          maxAccountsPerIp: Number(max.value) || 0, windowMinutes: Number(window.value) || 0
        });
        paint(res);
        toast(res.running ? T('Public registration is on.') : T('Public registration is off.'));
      } catch (e) { await alert(e.message); }
      finally { save.disabled = false; }
    });

    const parts = [
      h('div', {}, on),
      h('div.row', { style: { alignItems: 'flex-end', flexWrap: 'wrap' } },
        field(T('Public port'), port),
        field(T('Advertised realm address'), address),
        field(T('Registrations per address'), max),
        field(T('Within minutes'), window),
        save),
      h('div', { style: { marginTop: '4px' } },
        field(T('Public address (optional)'), publicUrl,
          T('Use this when players reach the site through a host name, a tunnel or a port forward. Leave it empty to show the direct IP addresses below.'))),
      h('div', { style: { marginTop: '4px' } },
        field(T('Client download link'), clientUrl,
          T('Use a direct client download or launcher page. Leave it empty to show the official CoA download page.'))),
      h('div.small.muted', {},
        T('The realm address is written into realmlist.wtf and Config.wtf. The authserver sends the address from Settings > Play with others, so set the same address there too; players cannot log in with 127.0.0.1.')),
      h('div.small.muted', {},
        T('Behind a tunnel or reverse proxy, all players may look like one address to the registration limit; set it to 0 to allow unlimited registrations.'))
    ];

    if (r.running) {
      parts.push(h('p', { style: { margin: '12px 0 4px', fontWeight: '600' } }, T('The public page is running. Open one of these addresses in a player\'s browser:')));
      const urls = (r.addresses || []).slice();
      if (r.publicUrl) urls.unshift({ url: r.publicUrl, network: T('configured public address') });
      parts.push(urls.length ? h('div', {}, urls.map(a => h('div.row', { style: { alignItems: 'center', margin: '2px 0', flexWrap: 'nowrap' } },
        h('a', { href: a.url, target: '_blank', rel: 'noopener', style: { userSelect: 'all' } }, a.url),
        h('span.small.muted', { style: { overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' } }, a.network || ''),
        secondary(T('Copy'), async () => { try { await navigator.clipboard.writeText(a.url); toast(T('Copied.')); } catch { } }))))
        : h('div.small.muted', {}, T('This computer has no network address right now.')));
      parts.push(h('div.small.muted', { style: { marginTop: '8px' } },
        T('For players outside your network, forward the public port on the router (or use a VPN/tunnel) and use the IP or host name they can reach. The site uses plain HTTP; put it behind HTTPS yourself if you want encrypted player passwords.')));
      if (!meta.windows) parts.push(h('div.small.muted', {},
        T('If a firewall is on (ufw), allow the public port once:') + ' sudo ufw allow ' + r.port + '/tcp'));
    } else if (r.enabled) {
      parts.push(h('div.small.bad', {}, r.error || T('The setting is on, but the public page is not running. Another program may already use the port, or the app was just started. Save again to retry.')));
    }

    if (!r.patch && !r.clientPack) parts.push(h('div.small.muted', {}, T('No client patch is available yet. Build the client patch under Client first; players can still download realmlist.wtf and Config.wtf.')));
    const packState = r.clientPack
      ? F('Player client pack ready ({0}, {1}).', (r.clientPackSize / 1024 / 1024).toFixed(1) + ' MB', r.clientPackAt || '')
      : T('No player client pack built yet. The portal falls back to the client patch ZIP.');
    const buildPack = secondary(r.clientPack ? T('Rebuild player client pack') : T('Build player client pack'), async () => {
      const job = await runProgress(() => post('/api/registration/client-pack'), T('Building player client pack ...'));
      if (job && job.ok) load();
    });
    parts.push(h('div', { style: { marginTop: '14px', paddingTop: '12px', borderTop: '1px solid #343140' } },
      h('div', { style: { fontWeight: '600' } }, T('Player client pack')),
      h('div.small.muted', {}, packState),
      buildPack,
      h('div.small.muted', {}, T('Build or rebuild this after changing modules, races or the client. It contains the module add-ons, the generated client patch and the current subclass patches; the pack can be several hundred MB. Close the game first.'))));
    if (r.publicUrl) parts.push(h('div.small.muted', {}, T('The configured public address is only a label for players; it does not change where the server listens.')));
    if (r.running) parts.push(h('div.small.muted', {}, F('While public registration is on, {0} keeps running in the background when you close its window, so players can still register. Start {0} again to see the window here; turn this off first if you want {0} to end with its window.', meta.product)));
    replace(body, ...parts);
  }

  return el;
}
