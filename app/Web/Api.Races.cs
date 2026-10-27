using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Builder;

namespace CoAInstaller.Web
{
    /// <summary>The "Custom races" page: the model lives here while the page is open; the page sends each step.</summary>
    static partial class Api
    {
        static RacesModel racesModel;
        static readonly object racesLock = new object();
        static RacePacker.Archives packSource, packYours;
        static List<RacePacker.Candidate> packRaces;
        static string packFolder, packTarget;

        static RacesModel Races
        {
            get
            {
                var m = racesModel;
                if (m == null || Inst == null || m.inst.Root != Inst.Root) throw new UserError(L.T("Open the page again."));
                return m;
            }
        }

        static void ClosePack()
        {
            try { if (packSource != null) packSource.Dispose(); } catch { }
            try { if (packYours != null) packYours.Dispose(); } catch { }
            packSource = packYours = null; packRaces = null;
        }

        static void MapRaces(WebApplication app)
        {
            Post(app, "/api/races/open", a =>
            {
                lock (racesLock)
                {
                    racesModel = new RacesModel(Inst);
                    return new { view = racesModel.View(), path = racesModel.conf.Path, exists = File.Exists(racesModel.conf.Path) || File.Exists(racesModel.conf.Path + ".dist") };
                }
            });

            Post(app, "/api/races/act", a =>
            {
                lock (racesLock)
                {
                    var m = Races;
                    string action = a.Str("action") ?? "", key = a.Str("key"), message = null;
                    try
                    {
                        switch (action)
                        {
                            case "toggle": m.Toggle(key, a.Bool("on"), a.Str("answer")); break;
                            case "base": m.SetBase(key, a.Str("value") ?? ""); break;
                            case "free": m.Free(key); break;
                            case "alt": m.SetAlt(key, a.Bool("on")); break;
                            case "move": message = m.Move(key, a.Int("step")); break;
                            case "delete": message = m.DeletePack(key); break;
                            case "deleteAlt": message = m.DeleteAlt(key); break;
                            case "flag":
                                string k = a.Str("option");
                                if (k != "CustomRaces.Enable" && k != "CustomRaces.MoreRaces" && k != "CustomRaces.HighRaces") throw new UserError("Unknown option.");
                                m.Set(k, a.Bool("on") ? "1" : "0");
                                break;
                            case "reset": m.ResetDefaults(); break;
                            default: throw new UserError("Unknown action.");
                        }
                    }
                    catch (RacesModel.Ask ask) { return new { ask = ask.Question }; }
                    return new { view = m.View(), message };
                }
            });

            Post(app, "/api/races/save", a => { lock (racesLock) { var m = Races; string msg = m.Save(); Manager.CheckClient(false); return new { message = msg, view = m.View() }; } });

            Post(app, "/api/races/ids", a => { lock (racesLock) return new { text = Races.RaceIds() }; });

            Post(app, "/api/races/coa/check", a =>
            {
                var m = Races;
                try
                {
                    var release = CoaCustom.Latest();
                    string installed = CoaCustom.Installed(m.RaceFolder());
                    m.coaNote = installed == null ? L.F("CoA Custom {0} can be downloaded here", release.Version)
                        : installed == release.Version ? L.F("CoA Custom {0} is installed", installed) : L.F("CoA Custom update {0} available (installed {1})", release.Version, installed);
                    m.coaButton = installed == null ? L.F("Download CoA Custom {0} …", release.Version)
                        : installed != release.Version ? L.F("Update CoA Custom to {0} …", release.Version) : L.F("Reinstall CoA Custom {0} …", installed);
                }
                catch { }
                lock (racesLock) return new { view = m.View() };
            });

            Post(app, "/api/races/coa/question", a =>
            {
                var m = Races;
                CoaCustom.Release release;
                try { release = CoaCustom.Latest(true); }
                catch (Exception ex) { throw new UserError(L.T("The CoA Custom package could not be checked:") + "\n" + ex.Message); }
                string target = m.RaceFolder();
                string installed = CoaCustom.Installed(target);
                string action = installed != null && installed != release.Version ? L.T("Update") : L.T("Download");
                long free = 0;
                try { Directory.CreateDirectory(target); free = new DriveInfo(Os.Windows ? Path.GetPathRoot(Path.GetFullPath(target)) : target).AvailableFreeSpace; } catch { }
                string text = L.F("{0} CoA Custom {1}?\n\nIt downloads all {2} ZIP files of the release (client and server), about {3}, and extracts them into:\n{4}\n\nExisting files are overwritten. After that the race selection opens.",
                    action, release.Version, release.Assets.Count, ClientDownload.FormatBytes(release.Assets.Sum(x => x.Size)), target);
                if (free > 0 && free < release.Assets.Sum(x => x.Size) + 512L * 1024 * 1024) text += "\n\n" + L.F("Only {0} is free on that drive.", ClientDownload.FormatBytes(free));
                return new { text, title = L.F("{0} CoA Custom {1} ...", action, release.Version) };
            });

            Post(app, "/api/races/coa/download", a =>
            {
                var m = Races;
                string target = m.RaceFolder();
                return Jobs.Run(L.T("Checking the CoA Custom package ..."), (job, st) =>
                {
                    string version;
                    try { version = CoaCustom.DownloadAndExtract(target, (s, x, y) => { job.Status = s; job.Sub = y > 0 ? (double)x / y : -1; job.Changed(false); }); }
                    catch (Exception ex) { throw new UserError(L.T("The CoA Custom package could not be installed:") + "\n" + ex.Message); }
                    m.coaNote = L.F("CoA Custom {0} installed", version);
                    job.Payload = new { root = CoaCustom.PackageRoot(target) ?? Path.Combine(target, "CoA-Custom") };
                    job.Message = m.coaNote;
                }).Snapshot();
            });

            // The race selection: the import button gives the folder; the races of that client are read here.
            Post(app, "/api/races/pack/open", a =>
            {
                var m = Races;
                string picked = a.Str("folder") ?? "";
                if (!Directory.Exists(picked)) throw new UserError(L.T("This folder could not be read:") + " " + picked);
                string source;
                lock (racesLock) source = m.PrepareImport(picked);
                string yourClient = ClientAddons.ClientFolder;
                if (yourClient != null && Path.GetFullPath(source).TrimEnd('\\', '/').Equals(Path.GetFullPath(yourClient).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                    throw new UserError(L.T("That is your own game client. Choose the folder of another client."));
                lock (racesLock)
                {
                    ClosePack();
                    packFolder = source;
                    packTarget = m.folder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "AFK-Rassen");
                    try
                    {
                        packSource = RacePacker.Open(source, yourClient);
                        packYours = new RacePacker.Archives(yourClient == null ? new List<string>() : ClientPatch.ClientArchives(yourClient));
                        packRaces = RacePacker.Races(packSource, yourClient, packTarget);
                    }
                    catch (Exception ex) { ClosePack(); throw new UserError(L.T("The races of this client could not be read:") + " " + ex.Message); }
                    return new
                    {
                        source, target = packTarget,
                        intro = L.F("Choose the game folder of another client or repack (or a folder with its patch archives, like the client folder of CoA Custom). Each race you tick gets a folder of its own in your race folder ({0}): its rows and the files your client does not have. Afterwards the other client is no longer needed.", packTarget) +
                            (string.IsNullOrEmpty(m.inst.DataDir) ? "" : L.T(" The racial spells of your server data folder come along with the races, so your game client can show them.")),
                        status = packRaces.Count == 0 ? L.T("This client has no races beyond the stock ones.") : yourClient != null ? L.F("{0} races. Tick the ones you want; a race your client has too can get the other client's looks.", packRaces.Count) : L.F("{0} races. Tick the ones you want.", packRaces.Count),
                        races = packRaces.Select((r, i) => new
                        {
                            index = i, name = r.Name, inFolder = r.InRaceFolder,
                            notes = string.Join(", ", new[] { r.MaleOnly ? L.T("male only") : null, r.InYourClient ? L.T("your client has it too") : null, r.InRaceFolder ? L.T("in your race folder already") : null }.Where(x => x != null))
                        }).ToArray(),
                        view = m.View()
                    };
                }
            });

            Post(app, "/api/races/pack/create", a =>
            {
                var m = Races;
                var chosen = a.List("indices").Select(s => { int i; return int.TryParse(s, out i) ? i : -1; }).Where(i => packRaces != null && i >= 0 && i < packRaces.Count).Select(i => packRaces[i]).ToList();
                if (chosen.Count == 0) throw new UserError(L.T("Tick the races you want."));
                string source = packFolder, target = packTarget;
                var src = packSource; var yours = packYours;
                string dataDir = m.inst.DataDir;
                return Jobs.Run(L.T("Create race packs"), (job, st) =>
                {
                    var lines = new List<string>(); int made = 0;
                    try { var kept = RacePacker.KeepRacePatch(source, target); if (kept != null) { lines.Add(kept); made++; } }
                    catch (Exception ex) { lines.Add(L.T("The 64-race patch could not be kept:") + " " + ex.Message); }
                    string label = RacePacker.SourceLabel(source);
                    foreach (var race in chosen)
                    {
                        try
                        {
                            Directory.CreateDirectory(target);
                            uint[] spells = ServerRacials.ClientSpells(dataDir, race.Name, race.Id);
                            lines.Add(RacePacker.Pack(src, yours, race, target, label, spells, text => st(text)));
                            race.InRaceFolder = true; made++;
                        }
                        catch (Exception ex) { lines.Add(L.F("{0}: not made - {1}", race.Name, ex.Message)); }
                    }
                    if (made > 0) lock (racesLock) m.AfterImport(target);
                    job.Payload = new { made, lines };
                    job.Message = string.Join("\n", lines);
                }).Snapshot();
            });

            Post(app, "/api/races/pack/close", a => { lock (racesLock) ClosePack(); return Ok; });
        }
    }
}
