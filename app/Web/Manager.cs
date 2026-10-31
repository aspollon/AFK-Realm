using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace CoAInstaller.Web
{
    /// <summary>
    /// The server AFK Realm looks after: its state every two seconds (database, auth- and worldserver, memory, the tick),
    /// the scheduled restart, and what is new (updates of the server, of AFK Realm, of the game client and of CoA Custom).
    /// It runs on its own, whether a page is open or not, like the server management of the classic window while it was
    /// shown. Every change goes to the pages as a "status" event.
    /// </summary>
    static class Manager
    {
        public static Install Inst;
        public static ServerControl Ctl;
        static readonly object gate = new object();
        static Thread loop;
        static readonly AutoResetEvent wake = new AutoResetEvent(false);

        /// <summary>The line under the server buttons: what was last done or is going on.</summary>
        public static string Notice = "";
        public static string NoticeKind = "info";                // info | ok | warn | bad

        // what is new
        public static UpdateCheck.Result Updates;
        public static string ClientNews, CoaNews, ClientVersion, CoaVersion, InstalledClient, InstalledCoa;
        public static string ClientPending = "";
        public static bool ClientChecking;
        public static List<KeyValuePair<string, string>> BotOffer = new List<KeyValuePair<string, string>>();
        public static bool OfferClient;

        // scheduled restart (see RestartPlan)
        public static RestartPlan Plan;
        static DateTime? restartAt;
        static readonly HashSet<int> restartTold = new HashSet<int>();
        static DateTime restartSnooze = DateTime.MinValue;
        static int memoryOver;
        public static volatile bool AutoRestarting;
        static string lastAutoRestart = "";

        // the tick
        static string tickText;
        static DateTime tickAsked = DateTime.MinValue;
        static bool tickBusy;

        static string lastSent;
        public static object Status;

        public static void Open(Install inst)
        {
            lock (gate)
            {
                Inst = inst;
                Ctl = new ServerControl(inst);
                Plan = RestartPlan.Load(inst);
                restartAt = null; restartTold.Clear(); restartSnooze = DateTime.MinValue; memoryOver = 0; lastAutoRestart = "";
                tickText = null; tickAsked = DateTime.MinValue;
                Updates = null; ClientNews = CoaNews = null; ClientPending = ""; OfferClient = false; BotOffer = new List<KeyValuePair<string, string>>();
                Notice = ""; lastSent = null;
            }
            Settings.LastInstall = inst.Root;
            L.AddModules(ModuleCatalog.ModulesDir(inst));
            try { Ctl.RestoreAfterBotReset(); } catch { }
            if (loop == null)
            {
                loop = new Thread(Loop) { IsBackground = true, Name = "manager" };
                loop.Start();
            }
            Poke();
            CheckForUpdates();
            CheckDownloadUpdates();
            CheckClient(true);
        }

        public static void Close()
        {
            lock (gate) { Inst = null; Ctl = null; Status = null; lastSent = null; }
            Events.Send("status", null);
        }

        /// <summary>Checks the state right away (after a task changed something).</summary>
        public static void Poke() { wake.Set(); }

        public static void SetNotice(string text, string kind = "info")
        {
            Notice = text ?? ""; NoticeKind = kind;
            Poke();
        }

        static void Loop()
        {
            while (true)
            {
                try { Refresh(); } catch { }
                wake.WaitOne(2000);
            }
        }

        static void Refresh()
        {
            var inst = Inst; var ctl = Ctl;
            if (inst == null || ctl == null) return;
            bool db = false, auth = false, world = false, worldReady = false, maps = false, dbc = false;
            DateTime worldStart = DateTime.MinValue; long worldMemory = 0;
            try
            {
                db = ctl.Db != null; auth = ctl.Auth != null;
                var worldProcess = ctl.World;
                world = worldProcess != null;
                if (world) { try { worldStart = worldProcess.StartTime; worldMemory = Math.Max(worldProcess.PrivateMemorySize64, worldProcess.WorkingSet64); } catch { } }
                worldReady = world && Net.PortOpen(ctl.WorldPort);
                maps = inst.HasMapData; dbc = inst.HasClientDbc;
            }
            catch { }
            if (!worldReady) { tickText = null; tickAsked = DateTime.MinValue; }
            else RefreshTick(inst, ctl);

            string memory = worldMemory > 0 ? "  ·  " + L.F("{0} GB of memory", (worldMemory / 1073741824.0).ToString("0.0", CultureInfo.InvariantCulture)) : "";
            string tick = tickText != null ? "  ·  " + tickText : "";
            string uptime = worldReady && worldStart != DateTime.MinValue ? L.F("Running for {0}", Clock(DateTime.Now - worldStart)) + memory + tick : "";

            var planState = Schedule(inst, ctl, worldReady, worldStart, worldMemory);

            string mapText = !maps ? L.T("Map data is still missing. The worldserver cannot run without it.")
                : !dbc ? L.T("The CoA DBC tables are missing. Use \"Create map data\" and tick \"Only refresh the CoA DBC tables\".")
                : L.T("Map data is present.");

            var u = Updates;
            object serverBanner = null, toolBanner = null;
            if (u != null && u.Server.Count > 0)
                serverBanner = new
                {
                    text = L.F("Server update available: {0}  –  click to install", NewsSummary(u.Server)) +
                        (u.PatchWarnings.Count > 0 ? "  ·  ⚠ " + (u.PatchWarnings.Count == 1 ? L.T("1 module patch would not fit") : L.F("{0} module patches would not fit", u.PatchWarnings.Count)) : ""),
                    news = u.Server, warnings = u.PatchWarnings,
                    limited = u.LimitedUntil == null ? null : L.F("(GitHub counts the changes again from {0})", u.LimitedUntil.Value.ToString("HH:mm"))
                };
            if (u != null && u.ToolVersion != null)
                toolBanner = new
                {
                    text = u.ToolDownload != null ? L.F("New: {0} {1} is available  –  click to update", Product.Name, u.ToolVersion) : L.F("New: {0} {1} is available  –  click to download", Product.Name, u.ToolVersion),
                    version = u.ToolVersion, url = u.ToolUrl, download = u.ToolDownload != null
                };

            string dir = ModuleCatalog.ModulesDir(inst);
            Func<string, bool> has = m => Directory.Exists(Path.Combine(dir, m));
            var status = new
            {
                root = inst.Root,
                db, auth, world, worldReady,
                uptime,
                notice = Notice, noticeKind = NoticeKind,
                canStart = !(auth && world), canStop = db || auth || world, canRestart = auth || world,
                maps, dbc, mapText, needsClientDbc = inst.NeedsClientDbc,
                plan = new { mode = Plan.Mode, hours = Plan.Hours, daily = Plan.DailyText, memory = Plan.MemoryGb, warn = Plan.WarnMinutes },
                planState = planState.Item1, planWarn = planState.Item2, postpone = planState.Item3,
                autoRestarting = AutoRestarting,
                serverBanner, toolBanner,
                clientBanner = ClientNews, coaBanner = CoaNews,
                clientButton = InstalledClient != null && InstalledClient != ClientVersion ? L.F("Update game client to {0} …", ClientVersion) : L.T("Game client …"),
                racesButton = InstalledCoa != null && InstalledCoa != CoaVersion ? L.F("Update custom races to {0} …", CoaVersion) : L.T("Set up the races …"),
                clientState = ClientChecking ? L.T("Checking the game client …") : ClientPending.Length == 0 ? "" : L.T("Not everything the installed modules bring for the game client is in your game yet."),
                clientPending = ClientPending.Length > 0,
                offerClient = OfferClient,
                botOffer = BotOffer.Select(b => new { key = b.Key, why = b.Value }).ToArray(),
                badges = new { modules = ClientPending.Length > 0 ? "!" : null, maintenance = serverBanner != null ? "new" : null },
                modules = new
                {
                    journey = has("mod-world-journey"), auction = has("mod-playerbots-auctions"), classic = has("mod-classic-classes"),
                    races = has("mod-custom-races"), flying = has("mod-azeroth-flying"), immersion = has("mod-playerbots-immersion")
                },
                busy = Jobs.Busy,
            };
            string json = JsonSerializer.Serialize(status, Events.Json);
            Status = status;
            if (json != lastSent)
            {
                lastSent = json;
                Events.Send("status", status);
            }
        }

        static string Clock(TimeSpan span)
        {
            if (span < TimeSpan.Zero) span = TimeSpan.Zero;
            long seconds = (long)span.TotalSeconds;
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}", seconds / 3600, (seconds / 60) % 60, seconds % 60);
        }

        static string NewsSummary(List<string> news)
        {
            var parts = news.ConvertAll(n => n.Split(':', '：')[0]);
            if (parts.Count <= 3) return string.Join(", ", parts);
            return L.F("{0}, {1} and {2} more", parts[0], parts[1], parts.Count - 2);
        }

        static void RefreshTick(Install inst, ServerControl ctl)
        {
            if (tickBusy) return;
            if (tickAsked != DateTime.MinValue && DateTime.Now - tickAsked < TimeSpan.FromSeconds(10)) return;
            try { if (AdminLink.Problem(inst, ctl) != null) return; } catch { return; }
            tickBusy = true;
            DateTime asked = DateTime.Now;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string text = null;
                try
                {
                    string answer = AdminLink.Run(inst, "server info");
                    var last = Regex.Match(answer ?? "", @"Update time diff:\s*(\d+)\s*ms", RegexOptions.IgnoreCase);
                    if (last.Success)
                    {
                        var mean = Regex.Match(answer, @"Mean:\s*(\d+)\s*ms", RegexOptions.IgnoreCase);
                        text = mean.Success ? L.F("{0} ms tick (mean {1} ms)", last.Groups[1].Value, mean.Groups[1].Value) : L.F("{0} ms tick", last.Groups[1].Value);
                    }
                }
                catch { }
                tickText = text; tickAsked = asked; tickBusy = false;
            });
        }

        // ---- scheduled restart ------------------------------------------------------------------

        /// <summary>The line of the scheduled restart, whether it is a warning, and whether "Postpone" is offered.</summary>
        static Tuple<string, bool, bool> Schedule(Install inst, ServerControl ctl, bool worldReady, DateTime worldStart, long worldMemory)
        {
            if (AutoRestarting) return Tuple.Create(L.T("The scheduled restart is running ..."), true, false);
            var plan = Plan;
            var now = DateTime.Now;
            if (!worldReady || worldStart == DateTime.MinValue)
            {
                restartAt = null; restartTold.Clear(); memoryOver = 0;
                return Tuple.Create(lastAutoRestart, lastAutoRestart.Length > 0, false);
            }
            double gb = worldMemory / 1073741824.0;
            bool over = plan.Mode == RestartPlan.Memory && gb > plan.MemoryGb;
            memoryOver = over ? memoryOver + 1 : 0;
            if (restartAt == null && plan.Mode != RestartPlan.Off && now >= restartSnooze)
            {
                restartAt = plan.Due(now, worldStart, memoryOver >= 15);
                if (restartAt != null)
                {
                    restartTold.Clear();
                    RestartLog(inst, "Restart announced for " + restartAt.Value.ToString("HH:mm:ss") + " (" +
                        (plan.Mode == RestartPlan.AfterUptime ? "the server has run for " + RestartPlan.Span(now - worldStart)
                        : plan.Mode == RestartPlan.Daily ? "daily at " + plan.DailyText
                        : "the worldserver uses " + gb.ToString("0.0", CultureInfo.InvariantCulture) + " GB") + ").");
                }
            }
            string running = (worldMemory > 0 ? L.F("Worldserver: running for {0}, {1} GB of memory.", RestartPlan.Span(now - worldStart), gb.ToString("0.0", CultureInfo.InvariantCulture))
                : L.F("Worldserver: running for {0}.", RestartPlan.Span(now - worldStart))) + " ";
            if (restartAt != null)
            {
                var left = restartAt.Value - now;
                if (left <= TimeSpan.Zero)
                {
                    // Not in the middle of something else that was started.
                    if (!Jobs.Busy) DoScheduledRestart(inst, ctl);
                    return Tuple.Create(L.T("The scheduled restart is running ..."), true, false);
                }
                int reached = 0;
                foreach (int step in plan.Steps())
                    if (left.TotalSeconds <= step * 60 + 2) reached = step;
                if (reached > 0 && !restartTold.Contains(reached))
                {
                    foreach (int step in plan.Steps()) if (step >= reached) restartTold.Add(step);
                    int minutes = Math.Max(1, (int)Math.Round(left.TotalMinutes));
                    Announce(inst, "The server restarts in " + minutes + (minutes == 1 ? " minute" : " minutes") + ". It is back a few minutes later.",
                        reached <= 1 || reached == plan.WarnMinutes);
                }
                return Tuple.Create(running + (restartTold.Count > 0 ? L.F("Restart in {0} - players have been told.", RestartPlan.Span(left)) : L.F("Restart in {0}.", RestartPlan.Span(left))), true, true);
            }
            string next = "";
            bool warn = false;
            if (now < restartSnooze) next = L.F("The restart is postponed until {0}.", restartSnooze.ToString("HH:mm"));
            else if (plan.Mode == RestartPlan.AfterUptime)
                next = L.F("Next restart at {0}, in {1}.", worldStart.AddHours(plan.Hours).ToString("HH:mm"), RestartPlan.Span(worldStart.AddHours(plan.Hours) - now));
            else if (plan.Mode == RestartPlan.Daily)
            {
                var due = now.Date.AddMinutes(plan.DailyMinutes);
                if (now > due.AddHours(1) || worldStart > due.AddMinutes(-plan.WarnMinutes - 1)) due = due.AddDays(1);
                next = due.Date == now.Date ? L.F("Next restart today at {0}.", plan.DailyText) : L.F("Next restart tomorrow at {0}.", plan.DailyText);
            }
            else if (plan.Mode == RestartPlan.Memory && worldMemory <= 0)
            {
                next = L.T("Windows does not tell how much memory the worldserver uses, so this rule cannot work here. Choose one of the others.");
                warn = true;
            }
            else if (plan.Mode == RestartPlan.Memory)
            {
                bool early = over && now - worldStart < TimeSpan.FromMinutes(20);
                next = early
                    ? L.F("That is over the limit of {0} GB already, shortly after the start: the limit is too low for this server and is not applied before the world has run for 20 minutes.", plan.MemoryGb.ToString("0.##", CultureInfo.InvariantCulture))
                    : L.F("Restart at {0} GB.", plan.MemoryGb.ToString("0.##", CultureInfo.InvariantCulture));
                warn = early;
            }
            return Tuple.Create(running + next, warn, false);
        }

        public static string ApplyPlan(int mode, string value, string warnText)
        {
            var plan = Plan;
            double hours = plan.Hours, gb = plan.MemoryGb; int daily = plan.DailyMinutes, warn = plan.WarnMinutes;
            if (mode == RestartPlan.AfterUptime && !RestartPlan.ParseHours(value, out hours))
                return L.T("Enter after how many hours the server is restarted, for example 6 or 5.5 (at least half an hour).");
            if (mode == RestartPlan.Daily && !RestartPlan.ParseTime(value, out daily))
                return L.T("Enter the time of day on the 24-hour clock, for example 05:00 or 17:30.");
            if (mode == RestartPlan.Memory && !RestartPlan.ParseGb(value, out gb))
                return L.T("Enter the memory limit in GB, for example 8 (at least 1).");
            if (mode != RestartPlan.Off && (!int.TryParse((warnText ?? "").Trim(), out warn) || warn < 0 || warn > 60))
                return L.T("Enter how many minutes before the restart players are told: 0 to 60.");
            bool announced = restartAt != null && restartTold.Count > 0;
            plan.Mode = mode; plan.Hours = hours; plan.DailyMinutes = daily; plan.MemoryGb = gb; plan.WarnMinutes = warn;
            try { plan.Save(Inst); } catch (Exception ex) { return L.T("The setting could not be saved:") + "\n" + ex.Message; }
            restartAt = null; restartTold.Clear(); restartSnooze = DateTime.MinValue; memoryOver = 0;
            if (announced) Announce(Inst, "The announced restart is off.", false);
            Poke();
            return null;
        }

        public static void Postpone()
        {
            bool announced = restartTold.Count > 0;
            restartAt = null; restartTold.Clear(); memoryOver = 0;
            restartSnooze = DateTime.Now.AddHours(1);
            if (announced) Announce(Inst, "The restart has been postponed.", false);
            Poke();
        }

        static void Announce(Install inst, string text, bool alsoOnScreen)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { AdminLink.Run(inst, "announce " + text); } catch { }
                if (alsoOnScreen) { try { AdminLink.Run(inst, "notify " + text); } catch { } }
            });
        }

        static void RestartLog(Install inst, string text)
        {
            try
            {
                Directory.CreateDirectory(inst.LogDir);
                File.AppendAllText(Path.Combine(inst.LogDir, "scheduled-restart.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text + Environment.NewLine);
            }
            catch { }
        }

        static void DoScheduledRestart(Install inst, ServerControl ctl)
        {
            AutoRestarting = true;
            restartAt = null; restartTold.Clear(); memoryOver = 0;
            Action<string> st = t => SetNotice(L.T("Scheduled restart:") + " " + t, "warn");
            RestartLog(inst, "Restarting the server.");
            new Thread(() =>
            {
                string problem = null;
                try
                {
                    ctl.StopAll(st, false);
                    ctl.StartDatabase(st); ctl.StartAuth(st);
                    try { AdminLink.Prepare(inst); } catch { }
                    Process world = ctl.StartWorld(st);
                    st(L.T("the worldserver is loading."));
                    var done = new ManualResetEvent(false);
                    ctl.WatchWorld(world, (ready, why) => { if (!ready) problem = why; done.Set(); });
                    done.WaitOne();
                }
                catch (Exception ex) { problem = ex.Message; }
                RestartLog(inst, problem == null ? "The server is running again." : "The restart failed: " + problem.Replace("\r", "").Replace("\n", " | "));
                string when = DateTime.Now.ToString("HH:mm");
                lastAutoRestart = problem == null ? "" : L.F("The scheduled restart at {0} failed. Details are in logs\\scheduled-restart.log.", when);
                AutoRestarting = false;
                if (problem == null) SetNotice(L.F("The server was restarted as scheduled at {0} and is running again.", when), "ok");
                else SetNotice(L.F("The scheduled restart at {0} failed:", when) + " " + problem.Split('\n')[0], "bad");
            }) { IsBackground = true, Name = "scheduled-restart" }.Start();
        }

        // ---- what is new --------------------------------------------------------------------------

        public static void CheckForUpdates()
        {
            var inst = Inst;
            if (inst == null) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { var r = UpdateCheck.Run(inst); if (Inst == inst) { Updates = r; Poke(); } } catch { }
            });
        }

        static string RaceFolderForUpdate(Install inst)
        {
            string configured = ConfigOption.Unquote(ClientPatch.ConfValue(inst, "mod_custom_races.conf", "CustomRaces.ImportFolder") ?? "");
            return configured.Length > 0 ? configured : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "AFK-Rassen");
        }

        public static void CheckDownloadUpdates()
        {
            var inst = Inst;
            if (inst == null) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string clientNews = null, coaNews = null, clientVersion = null, installedClient = null, coaVersion = null, installedCoa = null;
                try
                {
                    string client = ClientAddons.ClientFolder;
                    if (client != null && ClientAddons.IsClientFolder(client))
                    {
                        var manifest = ClientDownload.Latest();
                        clientVersion = manifest.Version;
                        installedClient = ClientDownload.InstalledVersion(client);
                        if (installedClient != null && installedClient != clientVersion)
                            clientNews = L.F("Game client update available: {0} (installed {1}) — click to update", clientVersion, installedClient);
                    }
                }
                catch { }
                try
                {
                    var release = CoaCustom.Latest();
                    coaVersion = release.Version;
                    installedCoa = CoaCustom.Installed(RaceFolderForUpdate(inst));
                    if (installedCoa != null && installedCoa != coaVersion)
                        coaNews = L.F("CoA Custom update available: {0} (installed {1}) — click to update", coaVersion, installedCoa);
                }
                catch { }
                if (Inst != inst) return;
                ClientNews = clientNews; CoaNews = coaNews; ClientVersion = clientVersion; InstalledClient = installedClient; CoaVersion = coaVersion; InstalledCoa = installedCoa;
                Poke();
            });
        }

        /// <summary>
        /// Whether the game has everything the modules bring. <paramref name="offer"/>: the first time a state is seen,
        /// the page is asked to open the game client window by itself (once per state, also across starts), and a module
        /// that changes what the bots are made of asks once for new bots.
        /// </summary>
        public static void CheckClient(bool offer)
        {
            var inst = Inst;
            if (inst == null) return;
            ClientChecking = true; Poke();
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string pending = "";
                try { pending = ClientAddons.Pending(inst); } catch { }
                if (Inst != inst) return;
                ClientPending = pending; ClientChecking = false;
                if (pending.Length == 0)
                {
                    // A setup can finish while the page still holds the old offer flag. Clear it
                    // on every check, not only when the offer parameter is set.
                    OfferClient = false;
                    try
                    {
                        string offered = Path.Combine(inst.Root, "Dependencies", "client-offered.txt");
                        if (File.Exists(offered)) File.Delete(offered);
                    }
                    catch { }
                }
                if (offer)
                {
                    string offered = Path.Combine(inst.Root, "Dependencies", "client-offered.txt");
                    try
                    {
                        bool offerClient = pending.Length > 0 && !(File.Exists(offered) && File.ReadAllText(offered).Trim() == pending);
                        if (offerClient) File.WriteAllText(offered, pending);
                        else if (pending.Length == 0 && File.Exists(offered)) File.Delete(offered);
                        OfferClient = offerClient;
                    }
                    catch { OfferClient = false; }
                    try { BotOffer = File.Exists(inst.WorldExe) ? ModuleBots.NotOffered(inst, File.GetLastWriteTimeUtc(inst.WorldExe)) : new List<KeyValuePair<string, string>>(); }
                    catch { BotOffer = new List<KeyValuePair<string, string>>(); }
                }
                Poke();
            });
        }

        /// <summary>The page has shown the offer: it is not offered again.</summary>
        public static void OfferShown(bool client, bool bots)
        {
            if (client) OfferClient = false;
            if (bots && BotOffer.Count > 0 && Inst != null) { try { ModuleBots.MarkOffered(Inst, BotOffer.Select(b => b.Key)); } catch { } BotOffer = new List<KeyValuePair<string, string>>(); }
            Poke();
        }

        /// <summary>After the engine: the module cards, the game client and the updates are looked at again.</summary>
        public static void AfterEngine(string mode, bool ok)
        {
            var inst = Inst;
            if (inst == null) return;
            if (ok && (mode == "Update" || mode == "Modules" || mode == "Install" || mode == "Rebuild" || mode == "Restore"))
            {
                L.AddModules(ModuleCatalog.ModulesDir(inst));
                CheckForUpdates();
                CheckClient(true);
            }
            Poke();
        }
    }
}
