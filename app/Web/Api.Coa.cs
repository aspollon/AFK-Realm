using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Builder;

namespace CoAInstaller.Web
{
    /// <summary>The CoA Custom package: the same file-copy setup the Windows window uses.</summary>
    static partial class Api
    {
        static string DefaultPackage()
        {
            string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            return Path.Combine(downloads, "COA CUSTOM");
        }

        static string CoaPackage(Args a) { return CoaPackage(a.Str("package")); }

        static string CoaPackage(string requested)
        {
            string p = (requested ?? "").Trim();
            if (p.Length == 0) p = (Settings.CoaPackage ?? "").Trim();
            if (p.Length == 0) p = DefaultPackage();
            return p;
        }

        static string CoaClient(Args a) { return CoaClient(a.Str("client")); }

        static string CoaClient(string requested)
        {
            string c = (requested ?? "").Trim();
            if (c.Length == 0) c = ClientAddons.ClientFolder ?? "";
            return c;
        }

        static string CoaServer(Args a) { return CoaServer(a.Str("server")); }

        static string CoaServer(string requested)
        {
            string s = (requested ?? "").Trim();
            if (s.Length == 0) s = (Settings.CoaServer ?? "").Trim();
            if (s.Length == 0) s = Inst.Root;
            return s;
        }

        static void RememberPaths(Args a, string package, string client, string server)
        {
            if (!string.IsNullOrEmpty(package)) Settings.CoaPackage = package;
            if (!string.IsNullOrEmpty(client)) ClientAddons.ClientFolder = client;
            if (!string.IsNullOrEmpty(server)) Settings.CoaServer = server;
        }

        static object CoaState(Install inst)
        {
            string package = CoaPackage((string)null);
            string root = null;
            try { root = CoaCustom.PackageRoot(package); } catch { }
            string server = CoaServer((string)null);
            return new
            {
                server,
                package,
                packageRoot = root,
                version = CoaCustom.Installed(root ?? package),
                client = ClientAddons.ClientFolder ?? "",
                canUninstall = CoaUninstall.HasManifest(ClientAddons.ClientFolder),
                tokenSet = !string.IsNullOrEmpty(Settings.CoaGitHubToken),
                // CoA Custom's races are only playable with mod-custom-races (and what it requires) on the server
                missingModules = inst == null || !System.IO.Directory.Exists(ModuleCatalog.ModulesDir(inst)) ? new string[0] :
                    System.IO.Directory.Exists(System.IO.Path.Combine(ModuleCatalog.ModulesDir(inst), "mod-custom-races")) ? new string[0] : new[] { "mod-custom-races" },
                repository = CoaCustom.Repo
            };
        }

        static void MapCoa(WebApplication app)
        {
            CoaSetup.AfterRaceSettings = RefreshRaces;
            Post(app, "/api/coa/state", a => CoaState(Inst));

            // races the game client offers that the race settings leave off (Manager's racesBanner)
            Post(app, "/api/coa/races/add", a =>
            {
                var inst = Inst;
                if (inst == null) return (object)new { ok = false };
                var added = CoaSetup.AddMissingServerRaces(inst);
                Manager.CheckDownloadUpdates();
                Manager.CheckClient(false);
                return (object)new { ok = true, added = added.Select(r => r.Value + " (" + r.Key + ")").ToArray(), running = Ctl.World != null };
            });

            Post(app, "/api/coa/races/leave", a =>
            {
                var inst = Inst;
                if (inst == null) return (object)new { ok = false };
                CoaSetup.LeaveRacesOff(inst, Manager.RacesNewsIds);
                Manager.CheckDownloadUpdates();
                return new { ok = true };
            });

            Post(app, "/api/coa/paths", a =>
            {
                RememberPaths(a, CoaPackage(a), CoaClient(a), CoaServer(a));
                return new { ok = true, package = CoaPackage((string)null), client = CoaClient((string)null), server = CoaServer((string)null) };
            });

            Post(app, "/api/coa/token", a =>
            {
                Settings.CoaGitHubToken = (a.Str("token") ?? "").Trim();
                return new { ok = true, tokenSet = !string.IsNullOrEmpty(Settings.CoaGitHubToken) };
            });

            Post(app, "/api/coa/check", a =>
            {
                var inst = Inst;
                string package = CoaPackage(a), client = CoaClient(a), server = CoaServer(a);
                RememberPaths(a, package, client, server);
                int code = CoaSetup.Run(new[] { "--package", package, "--server", server, "--client", client, "--check" });
                if (code != 0) throw new UserError(L.T("The package could not be checked."));
                return new { ok = true, message = L.T("The package can be copied.") };
            });

            Post(app, "/api/coa/install", a =>
            {
                string package = CoaPackage(a), client = CoaClient(a), server = CoaServer(a);
                if (!Directory.Exists(package)) throw new UserError(L.F("Package folder not found: {0}", package));
                if (!Directory.Exists(client)) throw new UserError(L.F("Game client folder not found: {0}", client));
                if (!Directory.Exists(server)) throw new UserError(L.F("Server folder not found: {0}", server));
                var inst = new Install(server);
                if (!inst.IsInstalled) throw new UserError(L.F("No complete AFK Realm server in: {0}", server));
                bool closeGame = ClientAddons.ClientRunning(client);
                if (closeGame && !a.Bool("force"))
                    throw new NeedsConfirmation(L.T("The game or its launcher is running from this folder. Close it and go on with the install?"));
                RememberPaths(a, package, client, server);
                return Jobs.Run(L.T("Installing CoA Custom ..."), (job, st) =>
                {
                    // Everything the install needs, done here instead of asked of the user: the game closed (its files
                    // are replaced), the worldserver stopped (it reads the race settings and DBCs only at its start), and
                    // the database running - the race settings ask it which race numbers have characters; without it
                    // they could not put the numbers right (the races "only worked with the server on").
                    if (closeGame) { st(L.T("Closing the game ...")); ClientAddons.CloseClient(client); }
                    var ctl = Manager.Inst != null && string.Equals(Manager.Inst.Root, inst.Root, StringComparison.OrdinalIgnoreCase) && Manager.Ctl != null ? Manager.Ctl : new ServerControl(inst);
                    bool wasRunning = ctl.World != null;
                    if (wasRunning || ctl.Auth != null) ctl.StopAll(st, false);
                    ctl.StartDatabase(st);
                    // Bring the race settings up to date BEFORE the client patch is built.
                    // Saving them afterwards changes the patch inputs and makes AFK Realm
                    // report the freshly built patch as outdated again.
                    try { RefreshRaces(inst); } catch { }
                    st(L.T("Copying the CoA package and building the client patch ..."));
                    int code = CoaSetup.Run(new[] { "--package", package, "--server", server, "--client", client });
                    if (code != 0)
                        throw new UserError(L.F("CoA Custom setup failed: {0}", string.IsNullOrEmpty(CoaSetup.LastError) ? L.T("See the AFK Realm log.") : CoaSetup.LastError));
                    job.Message = L.T("CoA Custom is installed.");
                    // the server starts with the new races (again, if it ran before)
                    if (inst.HasMapData && inst.HasClientDbc)
                    {
                        st(wasRunning ? L.T("Starting the server again ...") : L.T("Starting the server ..."));
                        try { StartWork(inst, ctl, st); job.Message += " " + L.T("The server is starting with the new races."); }
                        catch (Exception ex) { job.Message += " " + L.T("The server could not be started:") + " " + ex.Message; }
                    }
                }, (job, error) =>
                {
                    Manager.CheckClient(false);
                }).Snapshot();
            });

            Post(app, "/api/coa/uninstall", a =>
            {
                string client = CoaClient(a);
                if (!Directory.Exists(client)) throw new UserError(L.F("Game client folder not found: {0}", client));
                if (ClientAddons.ClientRunning(client))
                    throw new UserError(L.T("The game or its launcher is running. Close it before removing the CoA files."));
                if (!CoaUninstall.HasManifest(client))
                    throw new UserError(L.T("No CoA installation record was found for this client."));
                RememberPaths(a, CoaPackage(a), client, CoaServer(a));
                return Jobs.Run(L.T("Removing CoA client files ..."), (job, st) =>
                {
                    job.Message = CoaUninstall.Run(client, (t, x, y) =>
                    {
                        job.Status = t;
                        if (y > 0) job.Sub = (double)x / y;
                        job.Changed(false);
                    });
                }, (job, error) => Manager.CheckClient(false)).Snapshot();
            });

            Post(app, "/api/coa/export", a =>
            {
                string package = CoaPackage(a), client = CoaClient(a), server = CoaServer(a);
                if (!Directory.Exists(package)) throw new UserError(L.F("Package folder not found: {0}", package));
                if (!Directory.Exists(client)) throw new UserError(L.F("Game client folder not found: {0}", client));
                RememberPaths(a, package, client, server);
                var inst = new Install(server);
                if (!inst.IsInstalled) throw new UserError(L.F("No complete AFK Realm server in: {0}", server));
                string file = Path.Combine(Files.Temp, Guid.NewGuid().ToString("N") + ".zip");
                const string name = "CoA-Custom-client.zip";
                return Jobs.Run(L.T("Packing the complete CoA Custom client ..."), (job, st) =>
                {
                    CoaSetup.ExportClientZip(file, package, inst, client, (t, x, y) =>
                    {
                        job.Status = t;
                        if (y > 0) job.Sub = (double)x / y;
                        job.Changed(false);
                    });
                    job.Payload = new { url = Files.Offer(file, name, true), name };
                    job.Message = L.T("The complete CoA Custom client ZIP is ready.");
                }).Snapshot();
            });

            Post(app, "/api/coa/download", a =>
            {
                if (string.IsNullOrEmpty(Settings.CoaGitHubToken))
                    throw new UserError(L.T("A GitHub key for CoA Custom is required before downloading."));
                string target = (a.Str("target") ?? "").Trim();
                if (target.Length == 0) target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "CoA-Custom");
                return Jobs.Run(L.T("Downloading CoA Custom ..."), (job, st) =>
                {
                    string version = CoaCustom.DownloadAndExtract(target, (t, x, y) =>
                    {
                        job.Status = t;
                        if (y > 0) job.Sub = (double)x / y;
                        job.Changed(false);
                    });
                    string root = CoaCustom.PackageRoot(target);
                    Settings.CoaPackage = root ?? target;
                    job.Payload = new { version, package = root ?? target, target };
                    job.Message = L.F("CoA Custom {0} downloaded.", version);
                }).Snapshot();
            });
        }
    }
}
