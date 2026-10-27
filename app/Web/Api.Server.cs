using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.AspNetCore.Builder;

namespace CoAInstaller.Web
{
    /// <summary>Starting and stopping, the scheduled restart, updates, repair, the realm, map data.</summary>
    static partial class Api
    {
        static void MapServer(WebApplication app)
        {
            Post(app, "/api/server/start", a => StartServer(a.Bool("force")).Snapshot());

            Post(app, "/api/server/stop", a =>
            {
                RefuseDuringRestart();
                var ctl = Ctl;
                return Jobs.Run(L.T("Stopping the server"), (job, st) => { ctl.StopAll(st, true); st(L.T("Server is stopped.")); Manager.SetNotice(L.T("Server is stopped.")); }).Snapshot();
            });

            Post(app, "/api/server/restart", a =>
            {
                RefuseDuringRestart();
                var ctl = Ctl; var inst = Inst;
                return Jobs.Run(L.T("Restarting the server"), (job, st) =>
                {
                    ctl.StopAll(st, false);
                    st(L.T("Server is stopped. Starting it again ..."));
                    StartWork(inst, ctl, st);
                }).Snapshot();
            });

            Post(app, "/api/plan", a =>
            {
                string error = Manager.ApplyPlan(a.Int("mode"), a.Str("value"), a.Str("warn"));
                if (error != null) throw new UserError(error);
                return Ok;
            });
            Post(app, "/api/plan/postpone", a => { Manager.Postpone(); return Ok; });

            // Update, Rebuild, Setup (repair), Backup, Restore, RestoreParts, Modules
            Post(app, "/api/engine", a =>
            {
                string mode = a.Str("mode") ?? "";
                var allowed = new[] { "Update", "Rebuild", "Setup", "Backup", "Restore", "RestoreParts", "Modules" };
                if (!allowed.Contains(mode)) throw new UserError("Unknown task: " + mode);
                RefuseDuringRestart();
                var inst = Inst;
                string pw;
                try { pw = DbLogin.FromConfig(inst).Password; } catch (Exception ex) { throw new UserError(ex.Message); }
                string args = EngineArgs(mode, a);
                LastArgs = args;
                bool stop = mode != "Backup";
                return Jobs.RunEngine(inst, mode, pw, Ctl.DbPort, args, null, stop).Snapshot();
            });

            Get(app, "/api/realm", c =>
            {
                var inst = Inst;
                if (Ctl.Db == null) return new { name = (string)null, address = (string)null, database = false };
                string name = null, address = null;
                try { address = Accounts.GetRealmAddress(inst); } catch { }
                try { name = Accounts.GetRealmName(inst); Settings.Remember(inst.Root, name); } catch { }
                return new { name, address, database = true };
            });

            Post(app, "/api/realm/name", a =>
            {
                string n = (a.Str("name") ?? "").Trim();
                string err = Accounts.CheckRealmName(n);
                if (err != null) throw new UserError(err);
                var inst = Inst;
                return Jobs.Run(L.T("Renaming the server ..."), (job, st) =>
                {
                    EnsureDatabase(st); st(L.T("Renaming the server ...")); Accounts.SetRealmName(inst, n);
                    Settings.Remember(inst.Root, n);
                    job.Message = L.F("The server is now called \"{0}\". Restart the auth- and worldserver for it to take effect.", n);
                    Manager.SetNotice(job.Message, "ok");
                }).Snapshot();
            });

            Post(app, "/api/realm/address", a =>
            {
                string addr = (a.Str("address") ?? "").Trim();
                System.Net.IPAddress ip;
                if (!System.Net.IPAddress.TryParse(addr, out ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                    throw new UserError(L.T("Please enter an IPv4 address, for example 26.66.211.210 or 192.168.1.20."));
                var inst = Inst;
                return Jobs.Run(L.T("Setting the realm address ..."), (job, st) =>
                {
                    EnsureDatabase(st); st(L.T("Setting the realm address ...")); Accounts.SetRealmAddress(inst, addr);
                    job.Message = L.F("Realm address set: {0}. Restart the auth- and worldserver for it to take effect.", addr);
                    if (!Os.Windows && addr != "127.0.0.1") job.Message += " " + L.F("On Linux, allow the ports {0} and {1} (TCP) through the firewall if one is on, for example: sudo ufw allow {0}/tcp && sudo ufw allow {1}/tcp", Ctl.AuthPort, Ctl.WorldPort);
                    Manager.SetNotice(job.Message, "ok");
                }).Snapshot();
            });

            Post(app, "/api/bots/reset", a =>
            {
                var ctl = Ctl;
                return Jobs.Run(L.T("Reset random bots …"), (job, st) =>
                {
                    ctl.ResetRandomBots(st);
                    job.Message = L.T("All random bots were deleted.");
                    Manager.SetNotice(job.Message, "ok");
                }).Snapshot();
            });

            Post(app, "/api/offers/shown", a => { Manager.OfferShown(a.Bool("client"), a.Bool("bots")); return Ok; });
            Post(app, "/api/updates/check", a => { Manager.CheckForUpdates(); Manager.CheckDownloadUpdates(); return Ok; });

            Post(app, "/api/tool/update", a =>
            {
                var u = Manager.Updates;
                if (u == null || u.ToolDownload == null) throw new UserError(L.T("There is no update to install."));
                return Jobs.Run(L.T("Installing the update ..."), (job, st) =>
                {
                    st(L.T("Downloading the update ..."));
                    SelfUpdateWeb.Download(u.ToolDownload, u.ToolSha256, st);
                    st(L.T("Installing the update ..."));
                    SelfUpdateWeb.Replace(Manager.Inst == null ? null : Path.Combine(Manager.Inst.Root, "Builder", Path.GetFileName(Paths.ExePath)));
                    job.Then = "restart";
                },
                (job, ex) => { if (ex == null) ThreadPool.QueueUserWorkItem(_ => { Thread.Sleep(1500); SelfUpdateWeb.Restart(); }); }).Snapshot();
            });

            Post(app, "/api/mapdata", a => MapData(a.Str("folder"), a.Bool("mmaps", true), a.Bool("dbcOnly")).Snapshot());
            Get(app, "/api/mapdata/client", c => new { folder = ClientAddons.ClientFolder });
        }

        static void RefuseDuringRestart()
        {
            if (Manager.AutoRestarting) throw new UserError(L.T("The scheduled restart is running right now. Wait until the server is up again."));
        }

        /// <summary>The engine's arguments for a mode, from what the page sent.</summary>
        static string EngineArgs(string mode, Args a)
        {
            if (mode == "Restore")
            {
                string name = a.Str("snapshot") ?? "";
                if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[\w\-]+$")) throw new UserError(L.T("No backup was chosen."));
                return "-Snapshot " + MySql.Arg(name);
            }
            if (mode == "RestoreParts")
            {
                string folder = (a.Str("snapshot") ?? "").TrimEnd('\\', '/');
                var parts = a.List("parts").Where(p => p == "accounts" || p == "characters" || p == "settings").ToList();
                if (folder.Length == 0 || parts.Count == 0) throw new UserError(L.T("Nothing was chosen to restore."));
                return "-Snapshot " + MySql.Arg(folder) + " -Parts " + MySql.Arg(string.Join(",", parts));
            }
            if (mode == "Modules")
            {
                var add = a.List("add"); var remove = a.List("remove");
                if (add.Count == 0 && remove.Count == 0) throw new UserError(L.T("No module was chosen."));
                if (add.Concat(remove).Any(x => x.Contains('"') || x.Contains(';'))) throw new UserError("Invalid module.");
                return ((add.Count > 0 ? "-AddModules " + MySql.Arg(string.Join(";", add)) : "") + (remove.Count > 0 ? " -RemoveModules " + MySql.Arg(string.Join(";", remove)) : "")).Trim();
            }
            return null;
        }

        public static Job StartServer(bool force)
        {
            RefuseDuringRestart();
            var inst = Inst; var ctl = Ctl;
            if (!inst.HasMapData && !force) throw new NeedsConfirmation(L.T("Map data is missing. The worldserver will not run properly without it.\n\nStart anyway?"));
            if (inst.HasMapData && !inst.HasClientDbc)
                throw new UserError(L.T("The CoA DBC tables are missing, so the worldserver would stop right away.\n\nOpen \"Create map data\", choose your CoA game folder and tick \"Only refresh the CoA DBC tables\"."));
            return Jobs.Run(L.T("Start server"), (job, st) => StartWork(inst, ctl, st));
        }

        /// <summary>Database, authserver, worldserver; then the loading of the world is watched in the background.</summary>
        static void StartWork(Install inst, ServerControl ctl, Action<string> st)
        {
            ctl.RestoreAfterBotReset(); ctl.StartDatabase(st); ctl.StartAuth(st);
            if (ctl.World == null) { try { AdminLink.Prepare(inst); } catch { } }
            var world = ctl.StartWorld(st);
            Manager.SetNotice(L.F("The worldserver is loading. You can keep using {0} meanwhile.", Product.Name));
            ctl.WatchWorld(world, (ready, problem) =>
            {
                if (ready) Manager.SetNotice(L.T("The server is running. You can log in now."), "ok");
                else { Manager.SetNotice(problem, "bad"); Events.Send("alert", new { title = L.T("Start server"), text = problem }); }
            });
            try { Settings.Remember(inst.Root, Accounts.GetRealmName(inst)); } catch { }
        }

        // ---- map data -------------------------------------------------------------------------

        static string FindOnPath(string name)
        {
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                if (dir.Length == 0) continue;
                string c = Path.Combine(dir.Trim('"'), name);
                if (File.Exists(c)) return c;
            }
            return null;
        }

        /// <summary>
        /// Makes the map data from the game client: the extractors run in the game folder (they read its Data folder),
        /// CoA's own DBC tables come from the fork's tool, and the result moves into Server/Data. The output shows in the
        /// task's log instead of a window of its own.
        /// </summary>
        static Job MapData(string folder, bool mmaps, bool dbcOnly)
        {
            var inst = Inst;
            string dir = ClientAddons.GameRoot(folder ?? "");
            if (dir == null) throw new UserError(L.T("This is not a game folder: it needs a \"Data\" folder with the game's .MPQ archives.") + " " + L.T("You can also choose the \"Data\" folder itself."));
            if (Manager.Ctl != null && Manager.Ctl.World != null) throw new UserError(L.T("Stop the server first."));
            string clientData = Path.Combine(dir, "Data");
            bool coaDbc = inst.NeedsClientDbc;
            bool onlyDbc = coaDbc && dbcOnly;
            string python = Os.Windows ? inst.PythonExe : FindOnPath("python3");
            if (coaDbc && (python == null || !File.Exists(python) || !File.Exists(inst.MpqCliExe)))
                throw new UserError(L.T("The tools for the CoA DBC extraction are missing. Run \"Repair\" once, then try again."));
            return Jobs.Run(L.T("Create map data"), (job, st) =>
            {
                string[] tools = onlyDbc ? new string[0] : new[] { Os.Exe("map_extractor"), Os.Exe("vmap4_extractor"), Os.Exe("vmap4_assembler"), Os.Exe("mmaps_generator") };
                var copied = new List<string>();
                try
                {
                    foreach (var t in tools.Concat(onlyDbc ? new string[0] : new[] { "mmaps-config.yaml" }))
                    {
                        string src = Path.Combine(t.EndsWith(".yaml") ? inst.ServerDir : inst.BinDir, t);
                        if (!File.Exists(src) && t.EndsWith(".yaml")) src = Path.Combine(inst.BinDir, t);
                        if (!File.Exists(src)) continue;
                        string dst = Path.Combine(dir, t);
                        File.Copy(src, dst, true);
                        if (!Os.Windows) File.SetUnixFileMode(dst, File.GetUnixFileMode(src));
                        copied.Add(dst);
                    }
                    if (!onlyDbc && Os.Windows)
                        foreach (var dll in Directory.GetFiles(inst.ServerDir, "*.dll")) { string dst = Path.Combine(dir, Path.GetFileName(dll)); File.Copy(dll, dst, true); copied.Add(dst); }

                    Func<string, string, string, int> run = (title, program, arguments) =>
                    {
                        st(title);
                        job.Log("== " + title);
                        var psi = new ProcessStartInfo(program, arguments)
                        { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
                        using (var p = Process.Start(psi))
                        {
                            p.OutputDataReceived += (s, e) => { if (e.Data != null) job.Log(e.Data); };
                            p.ErrorDataReceived += (s, e) => { if (e.Data != null) job.Log(e.Data); };
                            p.BeginOutputReadLine(); p.BeginErrorReadLine();
                            p.WaitForExit();
                            return p.ExitCode;
                        }
                    };
                    if (!onlyDbc)
                    {
                        run(L.T("Step 1: dbc, maps and cameras ..."), Path.Combine(dir, Os.Exe("map_extractor")), "");
                        if (!Directory.Exists(Path.Combine(dir, "maps"))) throw new InvalidOperationException(L.T("ERROR during extraction. Is this the right game folder, and is the game closed?"));
                    }
                    if (coaDbc)
                    {
                        string coa = Path.Combine(dir, "coa-dbc");
                        if (Directory.Exists(coa)) Directory.Delete(coa, true);
                        int code = run(L.T("CoA DBC tables from the client archives ..."), python,
                            MySql.Arg(inst.DbcScript) + " extract " + MySql.Arg(clientData) + " coa-dbc --original --mpqcli " + MySql.Arg(inst.MpqCliExe));
                        if (code != 0) throw new InvalidOperationException(L.T("The CoA DBC tables could not be extracted (see the messages above). Is this the CoA client folder, and are the game and its launcher closed?"));
                        string dbc = Path.Combine(dir, "dbc");
                        if (Directory.Exists(dbc)) Directory.Delete(dbc, true);
                        Directory.Move(coa, dbc);
                        string serverDbc = Path.Combine(inst.DataDir, "dbc");
                        if (Directory.Exists(serverDbc)) Directory.Delete(serverDbc, true);
                    }
                    if (!onlyDbc)
                    {
                        run(L.T("Step 2: vmaps (messages like \"Couldn't open RootWmo\" are normal) ..."), Path.Combine(dir, Os.Exe("vmap4_extractor")), "");
                        Directory.CreateDirectory(Path.Combine(dir, "vmaps"));
                        run(L.T("Step 2: vmaps (messages like \"Couldn't open RootWmo\" are normal) ..."), Path.Combine(dir, Os.Exe("vmap4_assembler")), "Buildings vmaps");
                        try { Directory.Delete(Path.Combine(dir, "Buildings"), true); } catch { }
                        if (mmaps)
                        {
                            Directory.CreateDirectory(Path.Combine(dir, "mmaps"));
                            run(L.T("Step 3: mmaps - this may take a long time ..."), Path.Combine(dir, Os.Exe("mmaps_generator")), "");
                        }
                    }
                    st(L.T("Moving the data into the server ..."));
                    Directory.CreateDirectory(inst.DataDir);
                    foreach (var f in new[] { "dbc", "maps", "vmaps", "mmaps", "Cameras" })
                    {
                        string src = Path.Combine(dir, f);
                        if (Directory.Exists(src)) MoveMerge(src, Path.Combine(inst.DataDir, f));
                    }
                    job.Message = L.T("DONE. The data is now in the server.");
                    Manager.SetNotice(L.T("Map data is present."), "ok");
                }
                finally
                {
                    foreach (var c in copied) { try { File.Delete(c); } catch { } }
                }
            });
        }

        /// <summary>Moves a folder into another one, files of the same name replaced (as robocopy /MOVE does).</summary>
        static void MoveMerge(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var file in Directory.GetFiles(from))
            {
                string dst = Path.Combine(to, Path.GetFileName(file));
                if (File.Exists(dst)) File.Delete(dst);
                File.Move(file, dst);
            }
            foreach (var sub in Directory.GetDirectories(from)) MoveMerge(sub, Path.Combine(to, Path.GetFileName(sub)));
            Directory.Delete(from, false);
        }
    }

    /// <summary>The action needs a yes first (the page asks and sends it again with force).</summary>
    class NeedsConfirmation : UserError
    {
        public NeedsConfirmation(string question) : base(question) { }
    }
}
