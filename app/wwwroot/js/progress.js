// A running engine task (installing, updating, a backup, modules) and its result.
import { h, T, F, meta, post, get, replace, clear, lines, on, state, clock } from './core.js';
import { primary, secondary, confirm, alert, prompt } from './ui.js';
import { header } from './welcome.js';
import { show, route } from './app.js';

const titles = {
  Update: () => T('Update complete'), Backup: () => T('Backup complete'), Restore: () => T('Backup restored'), RestoreParts: () => T('Backup restored'),
  Modules: () => T('Modules changed'), Setup: () => T('Setup complete'),
};

export function progress() {
  const sheet = h('div.sheet');
  const el = h('div.wizard', {}, header(), sheet);
  let offs = [], timer = null, logBox = null, logShown = false, lastJobId = null;

  const render = job => {
    if (!job) { route(); return; }
    if (!job.running) return result(job);
    if (job.id !== lastJobId) { lastJobId = job.id; logBox = null; build(job); }
    update(job);
  };

  let phaseList, overall, overallBar, action, sub, subBar, elapsed, toggleLog, logWrap;
  function build(job) {
    phaseList = h('ul.phases');
    overallBar = h('i'); overall = h('div.bar', {}, overallBar);
    subBar = h('i'); sub = h('div.bar.thin', {}, subBar);
    action = h('div.hint', {}, T('Starting …'));
    elapsed = h('div.hint');
    logWrap = h('div.console.hidden');
    toggleLog = h('a', { href: '#', onclick: e => { e.preventDefault(); logShown = !logShown; logWrap.classList.toggle('hidden', !logShown); toggleLog.textContent = logShown ? T('Hide details') : T('Show details'); if (logShown) loadLog(); } }, T('Show details'));
    replace(sheet, h('h1', {}, job.title),
      h('p.hint', {}, T('You can leave this window open in the background. Please do not let the PC go to sleep.') + (meta.windows ? '' : ' ' + T('Closing this window does not stop the task; open AFK Realm again to see it.'))),
      phaseList, overall, action, sub, elapsed, h('div.row', {}, toggleLog), logWrap,
      h('footer', {}, secondary(T('Cancel'), async () => {
        if (!await confirm(T('Really cancel?\n\nThe current step is terminated. You can start the installation again later; completed steps are skipped.'))) return;
        try { await post('/api/job/cancel'); } catch (e) { alert(e.message); }
      })));
  }

  async function loadLog() {
    try {
      const r = await get('/api/job/log');
      logWrap.textContent = r.lines.join('\n') + '\n';
      logWrap.scrollTop = logWrap.scrollHeight;
    } catch { }
  }

  function update(job) {
    clear(phaseList);
    const idx = (job.phases || []).findIndex(p => p.id === job.phase);
    (job.phases || []).forEach((p, i) => {
      const cls = i < idx ? 'done' : i === idx ? 'now' : '';
      phaseList.appendChild(h('li', { class: cls }, h('span.m', {}, i < idx ? '✔' : i === idx ? '►' : '○'), p.title));
    });
    overallBar.style.width = Math.round(Math.max(0, job.overall) * 100) + '%';
    if (job.action) action.textContent = job.action;
    sub.classList.toggle('busy', job.sub < 0);
    subBar.style.width = job.sub < 0 ? '' : Math.round(job.sub * 100) + '%';
    elapsed.textContent = T('Elapsed:') + ' ' + clock(job.elapsed);
  }

  function result(job) {
    if (timer) { clearInterval(timer); timer = null; }
    const content = [];
    if (job.ok && job.message === 'uptodate') {
      content.push(h('h1', {}, T('Everything is up to date')), h('p', {}, T('There are no new updates. Nothing on the server was changed.')));
    } else if (job.ok) {
      content.push(h('h1', {}, (titles[job.mode] || (() => T('Installation complete')))()));
      if (job.mode === 'Update') content.push(h('p.hint', {}, T('The server as it was before this update was backed up first. If the new version causes problems, you can go back under "Backups" in the server management.')));
      if (job.mode === 'Modules') content.push(h('p.hint', {}, T('The server as it was before was backed up first. If a module causes problems, remove it again under "Manage modules", or go back under "Backups".')));
      if (job.mode === 'Restore') content.push(h('p.hint', {}, T('The server, its databases and its settings are back at the state of the backup. "Check for updates" offers the newer version again whenever you want it.')));
      if (job.changes && job.changes.length) content.push(h('h3', {}, T('Updated')), h('ul', {}, job.changes.map(c => h('li.hint', {}, c))));
      notes(job, content);
      if (job.mode === 'Modules' && job.moduleInstalled) content.push(h('p', {}, T("Check each new module's README for anything you need to set up yourself, for example in the game.")));
      if (job.mode === 'Install' || job.mode === 'Update' || job.mode === 'Rebuild' || job.mode === 'Setup')
        content.push(h('p', {}, T('You can now start the server in the server management.') + ' ' + T('If the map data is still missing, create it there first ("Create map data") - the worldserver needs it.')));
    } else {
      content.push(h('h1', {}, T('Unfortunately, that did not work')),
        h('p.bad.pre', { style: { fontWeight: 700 } }, job.message || ''));
      notes(job, content);
      content.push(h('p', {}, T('Completed steps are kept. Trying again continues where it got stuck. If the error persists, the log file helps narrow it down, for example when you share it with the community.')));
      content.push(h('div.row', {},
        primary(T('Try again'), retry),
        secondary(T('Show the log'), async () => {
          logWrap = h('div.console.tall');
          const r = await get('/api/job/log');
          logWrap.textContent = r.lines.join('\n');
          sheet.appendChild(logWrap);
          logWrap.scrollTop = logWrap.scrollHeight;
        })));
    }
    content.push(h('footer', {},
      primary(T('Go to server management'), async () => {
        try { await post('/api/setup/finished', { root: job.serverRoot }); } catch { }
        await post('/api/job/dismiss').catch(() => { });
        state.job = null;
        show(state.status && state.status.root ? 'manager' : 'welcome');
      })));
    replace(sheet, ...content);
  }

  async function retry() {
    try {
      const job = await post('/api/setup/retry', {});
      state.job = job; lastJobId = null; render(job);
    } catch (e) {
      // The first installation keeps its password only in memory: ask for it.
      const pw = await prompt(e.message, '', { password: true, title: T('Database password') });
      if (!pw) return;
      try { const job = await post('/api/setup/retry', { password: pw }); state.job = job; lastJobId = null; render(job); }
      catch (e2) { alert(e2.message); }
    }
  }

  function notes(job, content) {
    if (!job.notes || !job.notes.length) return;
    content.push(h('h3', {}, T('Modules')), h('ul', {}, job.notes.map(n => h('li.hint', {}, n))));
  }

  offs.push(on('job', j => { if (j) state.job = j; render(j); }));
  offs.push(on('joblog', l => {
    if (!logShown || !logWrap || !state.job || l.id !== state.job.id) return;
    const atEnd = logWrap.scrollTop + logWrap.clientHeight >= logWrap.scrollHeight - 30;
    logWrap.appendChild(document.createTextNode(l.line + '\n'));
    if (logWrap.childNodes.length > 4000) logWrap.textContent = logWrap.textContent.slice(-300000);
    if (atEnd) logWrap.scrollTop = logWrap.scrollHeight;
  }));
  timer = setInterval(() => { if (state.job && state.job.running) { state.job.elapsed++; if (elapsed) elapsed.textContent = T('Elapsed:') + ' ' + clock(state.job.elapsed); } }, 1000);
  render(state.job);
  el.close = () => { offs.forEach(f => f()); if (timer) clearInterval(timer); };
  return el;
}
