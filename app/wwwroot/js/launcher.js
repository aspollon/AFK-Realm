// The launcher builder: builds the per-file manifest, launcher config and player package for AFK Client.
import { h, T, F, meta, get, post } from './core.js';
import { card, primary, secondary, alert, toast, runProgress } from './ui.js';

export function launcherCard() {
  const body = h('div', {}, h('div.muted.small', {}, T('Reading ...')));
  const el = card(T('Launcher builder'), T('Build the player-side AFK Client from the server\'s working client: a manifest with a SHA-256 for every add-on, patch and client file, a launcher config with the realm and portal already set, and a ZIP for your players.'), 'download', body);
  load();

  async function load() {
    let r;
    try { r = await get('/api/launcher'); } catch (e) { body.replaceChildren(h('div.small.bad', {}, e.message)); return; }
    paint(r);
  }

  function paint(r) {
    const status = r.built
      ? F('Ready: {0} files, {1}.', r.files, fmt(r.bytes)) + (r.version ? '  ' + r.version : '')
      : T('No launcher package built yet.');
    const manifest = r.manifestUrl ? h('div.row', { style: { alignItems: 'center', flexWrap: 'nowrap' } },
      h('span.small.muted', {}, T('Manifest URL')),
      h('code', { style: { fontSize: '13px', userSelect: 'all', overflow: 'hidden', textOverflow: 'ellipsis' } }, r.manifestUrl),
      secondary(T('Copy'), async () => { try { await navigator.clipboard.writeText(r.manifestUrl); toast(T('Copied.')); } catch { } })) : null;
    const build = primary(T('Build manifest and launcher'), async () => {
      const job = await runProgress(() => post('/api/launcher/build'), T('Building the AFK Client ...'));
      if (job && job.ok) { await load(); }
    }, 'download');
    const packageBtn = r.package
      ? secondary(T('Download player package'), () => { window.open('/api/launcher/package', '_blank'); }, 'download')
      : null;
    const clientNote = r.clientExe
      ? h('div.small.ok', {}, F('The package includes AFK-Client.exe ({0}).', fmt(r.clientExeSize)))
      : h('div.small.warn', {}, T('AFK-Client.exe is not on this PC yet. The package contains only launcher.json; download the client from the release page and put it in Dependencies\\launcher.'));
    const parts = [
      h('div', { style: { fontWeight: '600', marginBottom: '4px' } }, status),
      manifest,
      h('div.row', { style: { marginTop: '10px', flexWrap: 'wrap' } }, build, packageBtn,
        r.package ? secondary(T('Copy package link'), async () => { try { await navigator.clipboard.writeText(location.origin + '/api/launcher/package'); toast(T('Copied.')); } catch { } }) : null),
      r.stale ? h('div.small.warn', {}, T('The manifest is older than the player client pack. Build it again.')) : null,
      clientNote,
      r.package ? h('div.small.muted', {}, F('Player package ready ({0}, {1}).', fmt(r.packageSize), r.packageAt)) : null,
    ];
    if (!r.clientExe) parts.push(secondary(T('Download AFK Client'), () => window.open(r.clientExeUrl, '_blank'), 'download'));
    body.replaceChildren(...parts);
  }

  function fmt(n) {
    if (n == null) return '0 B';
    if (n < 1024) return n + ' B';
    if (n < 1048576) return (n / 1024).toFixed(0) + ' KB';
    if (n < 1073741824) return (n / 1048576).toFixed(1) + ' MB';
    return (n / 1073741824).toFixed(1) + ' GB';
  }

  return el;
}
