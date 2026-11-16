// "Problem? Get help": AFK Realm gathers every log, crash report and dump of the server and the game client into one
// ZIP in the Downloads folder and says where it is - the user only has to send that file on Discord.
import { h, T, F, meta, post } from './core.js';
import { dialog, runProgress, toast } from './ui.js';

// The AFK Realm help channel on Discord, where the help ZIPs go.
const HELP_CHANNEL = 'https://discord.com/channels/1552281581396164608/1571912710176637088';

export async function helpBundle(context) {
  const area = (ph, rows) => h('textarea', { rows: rows || 3, placeholder: ph, style: { width: '100%', boxSizing: 'border-box', resize: 'vertical' } });
  const did = area(T('For example: I installed a CoA server, then clicked "Create map data" and chose my game folder.'));
  const wrong = area(T('For example: the build stopped with an error, or the worldserver closes right after starting. Paste the error message here if there is one.'), 4);
  const more = area(T('Anything else: what you already tried, your game client (where it came from, which version) …'), 2);
  if (context) wrong.value = context;
  const go = await dialog({ title: T('Problem? Get help'), icon: 'info', body: h('div', { style: { maxWidth: '600px' } },
    h('p', {}, T('Something does not work - the server does not build, crashes, the game shows errors? AFK Realm now collects everything needed to find the cause: the logs of the build and the server, crash reports and dumps, the settings (without passwords) and the logs of your game client.')),
    h('p', {}, T('Tell us in a few words what happened. It goes into the ZIP, so you do not need to write it again on Discord. English is best, but any language works.')),
    h('div.small', { style: { fontWeight: '600', marginTop: '8px' } }, T('What did you do?')), did,
    h('div.small', { style: { fontWeight: '600', marginTop: '8px' } }, T('What went wrong?')), wrong,
    h('div.small', { style: { fontWeight: '600', marginTop: '8px' } }, T('Anything else?')), more),
    buttons: [{ text: T('Cancel'), value: null }, { text: T('Collect the files'), kind: 'primary', value: true }] });
  if (!go) return;
  const report = [['What I did', did.value], ['What went wrong', wrong.value], ['Anything else', more.value]]
    .filter(([, v]) => v.trim()).map(([k, v]) => k + ':\n' + v.trim()).join('\n\n');
  const j = await runProgress(() => post('/api/support/bundle', { report }), T('Collecting the files for help …'));
  if (!j || !j.ok || !j.payload) return;
  const r = j.payload;
  const copy = async text => { try { await navigator.clipboard.writeText(text); toast(T('Copied.')); } catch { } return false; };
  const steps = h('ol', { style: { margin: '6px 0 10px', paddingLeft: '20px', lineHeight: '1.6' } },
    h('li', {}, T('Download the file with the button below.')),
    h('li', {}, T('Drag the file into the AFK Realm help channel on Discord (or attach it with the + button):') + ' ',
      h('a', { href: HELP_CHANNEL, target: '_blank', rel: 'noopener' }, T('open the help channel'))),
    h('li', {}, report ? T('Your description is in the file; a short "please help" is enough.') : T('Write in a few words what you did and what went wrong.')));
  const body = h('div', { style: { maxWidth: '600px' } },
    h('p', { style: { fontWeight: '700' } }, T('Done! Your help file is ready.')),
    h('div.small.mono', { style: { userSelect: 'all', wordBreak: 'break-all', margin: '0 0 6px' } }, r.zip),
    h('div.small.muted', {}, F('{0} MB, {1} files', r.mb, r.files)),
    h('p', { style: { margin: '12px 0 0', fontWeight: '600' } }, T('Next steps:')), steps,
    r.mb > 9.5 ? h('p.small.warn', {}, T('The file is larger than Discord allows without Nitro (10 MB). Upload it to Google Drive, OneDrive or a similar service and post the link instead.')) : null,
    r.dumps ? h('p.small.muted', {}, F('Crash dumps were found as well ({0} MB). They are in a second file next to it ({1}); send it only when you are asked for it - it is usually too large for Discord.', r.dumpsMb, r.dumpsName)) : null);
  const buttons = [];
  buttons.push({ text: T('Download'), kind: 'primary', onclick: () => { location.href = r.download; return false; } });
  if (r.dumpsDownload) buttons.push({ text: T('Download the crash dumps'), onclick: () => { location.href = r.dumpsDownload; return false; } });
  if (!meta.remote) {
    buttons.push({ text: T('Copy the path'), onclick: () => copy(r.zip) });
    buttons.push({ text: T('Open the folder'), onclick: async () => { await post('/api/fs/reveal', { path: r.zip }).catch(() => { }); return false; } });
  }
  buttons.push({ text: T('Open the help channel'), onclick: () => { window.open(HELP_CHANNEL, '_blank', 'noopener'); return false; } });
  buttons.push({ text: T('Close'), value: true });
  await dialog({ title: T('Problem? Get help'), icon: 'check', body, buttons });
}

/** The button for the side bar: hard to miss. */
export function helpButton() {
  return h('button.helpbtn', { type: 'button', onclick: () => helpBundle(), title: T('Collects all logs and crash files into one ZIP for Discord') },
    h('span', {}, '🛟'), h('span', {}, T('Problem? Get help')));
}
