// "Problem? Get help": AFK Realm gathers every log, crash report and dump of the server and the game client into one
// ZIP in the Downloads folder and says where it is - the user only has to send that file on Discord.
import { h, T, F, meta, post } from './core.js';
import { dialog, runProgress, toast } from './ui.js';

export async function helpBundle() {
  const go = await dialog({ title: T('Problem? Get help'), icon: 'info', body: h('div', { style: { maxWidth: '560px' } },
    h('p', {}, T('Something does not work - the server does not build, crashes, the game shows errors? AFK Realm now collects everything needed to find the cause: the logs of the build and the server, crash reports and dumps, the settings (without passwords) and the logs of your game client.')),
    h('p', {}, T('You get one ZIP file. Send it on Discord together with a few words about what happened - that is all.'))),
    buttons: [{ text: T('Cancel'), value: null }, { text: T('Collect the files'), kind: 'primary', value: true }] });
  if (!go) return;
  const j = await runProgress(() => post('/api/support/bundle'), T('Collecting the files for help …'));
  if (!j || !j.ok || !j.payload) return;
  const r = j.payload;
  const copy = async text => { try { await navigator.clipboard.writeText(text); toast(T('Copied.')); } catch { } return false; };
  const steps = h('ol', { style: { margin: '6px 0 10px', paddingLeft: '20px', lineHeight: '1.6' } },
    h('li', {}, meta.remote ? T('Download the file with the button below.') : T('Open the folder with the button below - the file is selected there.')),
    h('li', {}, T('Drag the file into the AFK Realm channel on Discord (or attach it with the + button).')),
    h('li', {}, T('Write in a few words what you did and what went wrong.')));
  const body = h('div', { style: { maxWidth: '600px' } },
    h('p', { style: { fontWeight: '700' } }, T('Done! Your help file is ready.')),
    h('div.small.mono', { style: { userSelect: 'all', wordBreak: 'break-all', margin: '0 0 6px' } }, r.zip),
    h('div.small.muted', {}, F('{0} MB, {1} files', r.mb, r.files)),
    h('p', { style: { margin: '12px 0 0', fontWeight: '600' } }, T('Next steps:')), steps,
    r.mb > 9.5 ? h('p.small.warn', {}, T('The file is larger than Discord allows without Nitro (10 MB). Upload it to Google Drive, OneDrive or a similar service and post the link instead.')) : null,
    r.dumps ? h('p.small.muted', {}, F('Crash dumps were found as well ({0} MB). They are in a second file next to it ({1}); send it only when you are asked for it - it is usually too large for Discord.', r.dumpsMb, r.dumpsName)) : null);
  const buttons = [];
  if (meta.remote) {
    buttons.push({ text: T('Download'), kind: 'primary', onclick: () => { location.href = r.download; return false; } });
    if (r.dumpsDownload) buttons.unshift({ text: T('Download the crash dumps'), onclick: () => { location.href = r.dumpsDownload; return false; } });
  } else {
    buttons.push({ text: T('Copy the path'), onclick: () => copy(r.zip) });
    buttons.push({ text: T('Open the folder'), kind: 'primary', onclick: async () => { await post('/api/fs/reveal', { path: r.zip }).catch(() => { }); return false; } });
  }
  buttons.push({ text: T('Close'), value: true });
  await dialog({ title: T('Problem? Get help'), icon: 'check', body, buttons });
}

/** The button for the side bar: hard to miss. */
export function helpButton() {
  return h('button.helpbtn', { type: 'button', onclick: () => helpBundle(), title: T('Collects all logs and crash files into one ZIP for Discord') },
    h('span', {}, '🛟'), h('span', {}, T('Problem? Get help')));
}
