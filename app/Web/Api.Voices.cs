using System;
using Microsoft.AspNetCore.Builder;

namespace CoAInstaller.Web
{
    /// <summary>The Voices page: quest voice-over and chat read aloud for CoA64 (ClientVoices).</summary>
    static partial class Api
    {
        static object VoicesJson(string folder)
        {
            string client = ClientAddons.GameRoot((folder ?? ClientAddons.ClientFolder ?? "").Trim());
            var o = ClientVoices.Load();
            var s = ClientVoices.Look(client);
            string state;
            if (client == null) state = L.T("Choose your game folder in the game client window first.");
            else if (s.Host == null) state = L.T("This game folder has no CoA64 client that loads plugins (Plugins\\*.dll). Voices need that CoA64 build.");
            else state = L.F("Plugin: {0} · Companion Voice: {1} · Living Voices: {2}",
                s.Plugin ?? L.T("not installed"), s.Companion ?? L.T("not installed"), s.Quest ?? L.T("not installed"));
            return new
            {
                client, host = s.Host == null ? null : System.IO.Path.GetFileName(s.Host), state, ready = client != null && s.Host != null,
                questInstalled = s.Quest != null,
                o.Quest, o.Companion, o.Party, o.Whisper, o.Say, o.Yell, o.NpcSay, o.NpcYell, o.CompanionsOnly, o.QuietInCombat, o.Volume, o.Speed
            };
        }

        static ClientVoices.Options VoicesFrom(Args a)
        {
            var o = ClientVoices.Load();
            o.Quest = a.Bool("quest", o.Quest); o.Companion = a.Bool("companion", o.Companion);
            o.Party = a.Bool("party", o.Party); o.Whisper = a.Bool("whisper", o.Whisper);
            o.Say = a.Bool("say", o.Say); o.Yell = a.Bool("yell", o.Yell);
            o.NpcSay = a.Bool("npcSay", o.NpcSay); o.NpcYell = a.Bool("npcYell", o.NpcYell);
            o.CompanionsOnly = a.Bool("companionsOnly", o.CompanionsOnly); o.QuietInCombat = a.Bool("quietInCombat", o.QuietInCombat);
            o.Volume = a.Int("volume", o.Volume); o.Speed = a.Num("speed", o.Speed);
            return o;
        }

        static void MapVoices(WebApplication app)
        {
            Post(app, "/api/voices/look", a => VoicesJson(a.Str("folder")));

            // Saved settings only: written into the add-on at once when it is installed (nothing to download).
            Post(app, "/api/voices/save", a =>
            {
                var o = VoicesFrom(a);
                ClientVoices.Save(o);
                string client = ClientAddons.GameRoot((a.Str("folder") ?? ClientAddons.ClientFolder ?? "").Trim());
                bool written = client != null && !ClientAddons.ClientRunning(client) && ClientVoices.WriteDefaultsOnly(client, o);
                return new { ok = true, written };
            });

            Post(app, "/api/voices/install", a =>
            {
                string client = ClientAddons.GameRoot((a.Str("folder") ?? ClientAddons.ClientFolder ?? "").Trim());
                if (client == null) throw new UserError(L.T("Choose your game folder in the game client window first."));
                if (ClientAddons.ClientRunning(client))
                    throw new UserError(L.T("The game or its launcher is running from this folder. Please close it first: add-ons are only loaded at the start, and a running game writes its old cache back when it closes."));
                var o = VoicesFrom(a);
                ClientVoices.Save(o);
                return Jobs.Run(L.T("Installing the voices …"), (job, st) =>
                {
                    string result = ClientVoices.Install(client, o, (t, x, y) =>
                    {
                        job.Status = t; job.Sub = y > 0 ? (double)x / y : -1;
                        if (y > 0) job.Action = ClientDownload.FormatBytes(x) + " / " + ClientDownload.FormatBytes(y);
                        job.Changed(false);
                    });
                    job.Message = L.T("Done:") + "\n\n" + (result.Length > 0 ? result : L.T("Everything was already up to date."));
                }).Snapshot();
            });
        }
    }
}
