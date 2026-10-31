// The base race of each CoA custom race: start area, reputation, faction and racials.
// The stock races of the base client are intentionally not shown here.
import { h, T, F, post } from './core.js';
import { dialog, alert, primary, secondary, select } from './ui.js';

export async function baseRacesDialog() {
  let data;
  try { data = await post('/api/races/open'); } catch (e) { return alert(e.message); }
  const view = data.view;
  const bases = (view.bases || []).map(b => [b.key, b.name]);
  const names = (view.baseRaces || []).map(r => r.name);
  const clean = n => (n || '').replace(/\s*\((Alliance|Horde|Allianz)\)\s*$/i, '').trim();
  const counts = {};
  for (const n of names) counts[clean(n)] = (counts[clean(n)] || 0) + 1;
  const sideName = side => side === 'alliance' ? T('Alliance') : side === 'horde' ? T('Horde') : T('Both sides');

  function row(r) {
    let name = clean(r.name);
    if ((counts[name] || 0) > 1) name += ' (' + sideName(r.side) + ')';
    const pick = select(bases, r.base, async value => {
      try { await post('/api/races/act', { action: 'base', key: r.key, value }); }
      catch (e) { await alert(e.message); }
    });
    return h('div.row', { style: { alignItems: 'center', margin: '4px 0' } },
      h('span', { style: { minWidth: '270px' } }, name),
      h('span.small.muted', { style: { minWidth: '90px' } }, sideName(r.side)),
      pick);
  }

  const content = h('div', {},
    h('p', {}, T('A custom race inherits the start zone, outfit, faction, reputations and racial abilities of its base race. Visibility and class choice are handled by the CoA UI; only the base race is configured here.')),
    h('p.small.muted', {}, T('Stock races of the base client are not listed.') ),
    h('div.list', { style: { maxHeight: '60vh', padding: '6px 10px' } }, ...(view.baseRaces || []).map(row)));

  const result = await dialog({
    title: T('Base races'), icon: 'races', wide: true, body: content,
    buttons: [
      { text: T('Close'), value: null },
      { text: T('Save'), kind: 'primary', onclick: async () => {
        try { await post('/api/races/save'); await alert(T('Saved.')); return true; }
        catch (e) { await alert(e.message); return false; }
      } }
    ]
  });
  return result;
}
