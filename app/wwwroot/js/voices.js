// Voices for CoA64: quest voice-over (Living Voices) and chat read aloud (Companion Voice).
import { h, T, F, meta, post, replace } from './core.js';
import { primary, secondary, dialog, alert, confirm, toggle, field, slider, runProgress } from './ui.js';

export async function voicesDialog(folder) {
  let v;
  try { v = await post('/api/voices/look', { folder }); } catch (e) { return alert(e.message); }
  const state = h('div.small', { class: v.ready ? 'small muted' : 'small warn' }, v.state);
  const quest = toggle(T('Quest voice-over (Living Voices): quest givers speak their quest texts. About 3.6 GB download.'), v.quest);
  const companion = toggle(T('Chat read aloud (Companion Voice): a voice per race and sex, made on this computer'), v.companion);
  const party = toggle(T('Party chat'), v.party), whisper = toggle(T('Whispers to me'), v.whisper);
  const say = toggle(T('Say (players and bots)'), v.say), yell = toggle(T('Yell (players and bots)'), v.yell);
  const npcSay = toggle(T('Say (NPCs)'), v.npcSay), npcYell = toggle(T('Yell (NPCs and bosses)'), v.npcYell);
  const companionsOnly = toggle(T('Only lines the bots\' language model wrote (needs OllamaChat.VoiceLabels on the server)'), v.companionsOnly);
  const quiet = toggle(T('Quiet while I\'m in combat'), v.quietInCombat);
  const volume = slider(0, 100, 5, v.volume, x => x + ' %');
  const speed = slider(0.7, 1.5, 0.05, v.speed, x => x.toFixed(2));
  const chat = [party, whisper, say, yell, npcSay, npcYell, companionsOnly, quiet, volume, speed];
  const sync = () => chat.forEach(c => { c.disabled = !companion.checked; });
  companion.input.addEventListener('change', sync);
  sync();

  const values = () => ({
    folder, quest: quest.checked, companion: companion.checked, party: party.checked, whisper: whisper.checked,
    say: say.checked, yell: yell.checked, npcSay: npcSay.checked, npcYell: npcYell.checked,
    companionsOnly: companionsOnly.checked, quietInCombat: quiet.checked, volume: volume.value, speed: speed.value
  });

  async function install() {
    if (quest.checked && !v.questInstalled && !await confirm(T('The quest voice-over is about 3.6 GB. Download it now?'))) return false;
    if (!quest.checked && v.questInstalled && !await confirm(T('The quest voice-over will be removed from your game. Continue?'))) return false;
    const j = await runProgress(() => post('/api/voices/install', values()), T('Installing the voices …'));
    if (j && j.ok) await alert(j.message);
    return j && j.ok ? true : false;
  }

  async function save() {
    const r = await post('/api/voices/save', values());
    await alert(r.written ? T('Saved. The game uses the new settings at the next start.') : T('Saved. They go into the game with "Install".'));
  }

  const grid = (...xs) => h('div', { style: { display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '6px 18px', margin: '6px 0 6px 28px' } }, ...xs);
  const body = h('div', {},
    h('p.muted', {}, T('Voices need a CoA64 client that loads plugins. The engine is installed as Plugins\\LivingVoices64.dll; the voices come from Crono141\'s Living Voices and Companion Voice releases. In game: /lv and /cvoice.')),
    meta.remote ? h('p.small.warn', {}, F('The game folder is a folder on the computer {0} runs on.', meta.product)) : null,
    quest, h('div', { style: { height: '10px' } }), companion,
    grid(party, whisper, say, yell, npcSay, npcYell),
    h('div', { style: { margin: '0 0 0 28px' } }, companionsOnly, quiet),
    grid(field(T('Volume'), volume), field(T('Speed'), speed)),
    state);
  const installBtn = primary(T('Install'), async () => { if (await install()) await refreshState(); });
  installBtn.disabled = !v.ready;
  async function refreshState() {
    try { v = await post('/api/voices/look', { folder }); replace(state, v.state); state.className = v.ready ? 'small muted' : 'small warn'; } catch { }
  }
  await dialog({
    title: meta.product + ' – ' + T('Voices'), icon: 'client', body,
    left: [installBtn, secondary(T('Save settings'), () => save())],
    buttons: [{ text: T('Close'), value: null }]
  });
}
