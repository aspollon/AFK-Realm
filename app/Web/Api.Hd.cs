using System;
using Microsoft.AspNetCore.Builder;

namespace CoAInstaller.Web
{
    /// <summary>The HD pack page: fueryin's Ultra-HD pack and LuxVelum in a CoA Custom game (ClientHd).</summary>
    static partial class Api
    {
        static object HdJson(string folder, string pack)
        {
            string client = ClientAddons.GameRoot((folder ?? ClientAddons.ClientFolder ?? "").Trim());
            var o = ClientHd.Load();
            if (pack != null) o.Pack = pack;
            string found = ClientHd.FindPack(o.Pack);
            var s = ClientHd.Look(client);
            string state;
            if (client == null) state = L.T("Choose your game folder in the game client window first.");
            else if (!s.Installed) state = L.T("The HD pack is not in your game yet.");
            else state = L.F("HD archives: {0} on, {1} off · graphics effect: {2} · LuxVelum: {3}", s.Active, s.Disabled, ClientVisuals.Name(ClientVisuals.Read(client)),
                !s.LuxFiles ? L.T("not installed") : s.LuxHost ? L.T("ready") : L.T("your CoA64.exe cannot load it yet"));
            var warnings = new System.Collections.Generic.List<string>();
            if (client != null && !s.LuxHost) warnings.Add(L.T("Your CoA64.exe cannot host LuxVelum yet; \"Install / apply\" brings the newest one when it is released."));
            return new
            {
                client, ready = client != null, state, warnings, page = ClientHd.PackPage,
                pack = found ?? o.Pack ?? "", packFound = found != null, installed = s.Installed,
                o.World, o.Orcs, o.Shaders, o.Cursor, o.Blood, o.Effects, o.Sounds, o.Lux, o.Addons,
                visuals = client != null ? ClientVisuals.Read(client) : ClientVisuals.Lumenveil
            };
        }

        static ClientHd.Options HdFrom(Args a)
        {
            var o = ClientHd.Load();
            o.World = a.Bool("world", o.World); o.Orcs = a.Bool("orcs", o.Orcs); o.Shaders = a.Bool("shaders", o.Shaders);
            o.Cursor = a.Bool("cursor", o.Cursor); o.Blood = a.Bool("blood", o.Blood); o.Effects = a.Bool("effects", o.Effects);
            o.Sounds = a.Bool("sounds", o.Sounds); o.Lux = a.Bool("lux", o.Lux); o.Addons = a.Bool("addons", o.Addons);
            o.Pack = (a.Str("pack") ?? o.Pack ?? "").Trim();
            o.Visuals = a.Int("visuals", -1);
            if (o.Visuals >= ClientVisuals.Stock && o.Visuals <= ClientVisuals.LuxVelum) o.Lux = o.Visuals == ClientVisuals.LuxVelum;
            else o.Visuals = -1;
            return o;
        }

        static string HdClient(Args a)
        {
            string client = ClientAddons.GameRoot((a.Str("folder") ?? ClientAddons.ClientFolder ?? "").Trim());
            if (client == null) throw new UserError(L.T("Choose your game folder in the game client window first."));
            if (ClientAddons.ClientRunning(client))
                throw new UserError(L.T("The game or its launcher is running from this folder. Please close it first."));
            return client;
        }

        static void MapHd(WebApplication app)
        {
            Post(app, "/api/hd/look", a => HdJson(a.Str("folder"), a.Str("pack")));

            Post(app, "/api/hd/install", a =>
            {
                string client = HdClient(a);
                var o = HdFrom(a);
                ClientHd.Save(o);
                return Jobs.Run(L.T("Installing the HD pack …"), (job, st) =>
                {
                    string result = ClientHd.Install(client, o, (t, x, y) =>
                    {
                        job.Status = t; job.Sub = y > 0 ? (double)x / y : -1;
                        if (y > 0) job.Action = ClientDownload.FormatBytes(x) + " / " + ClientDownload.FormatBytes(y);
                        job.Changed(false);
                    });
                    job.Message = L.T("Done:") + "\n\n" + (result.Length > 0 ? result : L.T("Everything was already up to date."));
                }).Snapshot();
            });

            Post(app, "/api/hd/remove", a =>
            {
                string client = HdClient(a);
                string result = ClientHd.Remove(client);
                return new { ok = true, message = result.Length > 0 ? result : L.T("Nothing to remove.") };
            });
        }
    }
}
