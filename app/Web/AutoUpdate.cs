using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace CoAInstaller.Web
{
    /// <summary>
    /// The update plan at work (see UpdatePlan): looks for new server code as often as the admin chose, and at the
    /// planned time installs it on its own - only when there is something new for the parts chosen. Players are told
    /// in the game beforehand. A server that ran before is started again afterwards; a failed build leaves the old
    /// server in place (the engine uses the new programs only after a successful build), so it is simply started again.
    /// When the server does not come up after the update, the backup the engine made right before it is restored
    /// (unless the admin chose to be told only). Everything is written to logs\scheduled-update.log.
    /// </summary>
    static class AutoUpdate
    {
        public static UpdatePlan Plan = new UpdatePlan();
        public static DateTime LastCheck = DateTime.MinValue;
        public static volatile bool Running;
        static string lastResult = "";
        static DateTime? slot;
        static bool? relevant;
        static volatile bool checking;
        static readonly HashSet<int> told = new HashSet<int>();

        public static void Load(Install inst)
        {
            Plan = UpdatePlan.Load(inst);
            slot = null; relevant = null; told.Clear(); lastResult = "";
        }

        /// <summary>Called by the manager every few seconds.</summary>
        public static void Tick(Install inst, ServerControl ctl, bool worldReady)
        {
            var plan = Plan;
            var now = DateTime.Now;
            if (plan.CheckHours > 0 && LastCheck != DateTime.MinValue && now - LastCheck >= TimeSpan.FromHours(plan.CheckHours) && !Jobs.Busy)
                Manager.CheckForUpdates();
            if (Running) return;
            var due = plan.Current(now);
            if (due == null) { slot = null; return; }
            if (slot != due)
            {
                slot = due; relevant = null; told.Clear();
                checking = true;
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        var r = UpdateCheck.Run(inst);
                        Manager.Updates = r;
                        relevant = (plan.Core && r.BaseNew) || (plan.Modules && r.ModulesNew);
                    }
                    catch { relevant = false; }
                    checking = false;
                    Manager.Poke();
                });
                return;
            }
            if (checking || relevant == null) return;
            if (relevant == false)
            {
                plan.LastRun = UpdatePlan.Key(due.Value);
                try { plan.Save(inst); } catch { }
                Log(inst, "Planned update at " + plan.TimeText + ": nothing new, nothing done.");
                lastResult = L.F("Planned update at {0}: nothing new.", plan.TimeText);
                slot = null;
                return;
            }
            var left = due.Value - now;
            if (worldReady)
            {
                int reached = 0;
                foreach (int step in Steps(plan))
                    if (left.TotalSeconds <= step * 60 + 2) reached = step;
                if (reached > 0 && !told.Contains(reached))
                {
                    foreach (int step in Steps(plan)) if (step >= reached) told.Add(step);
                    int minutes = Math.Max(1, (int)Math.Round(left.TotalMinutes));
                    Announce(inst, "The server goes down for an update in " + minutes + (minutes == 1 ? " minute" : " minutes") + ". It is back once the update is done.");
                }
            }
            if (left > TimeSpan.Zero || Jobs.Busy || Manager.AutoRestarting) return;
            Run(inst, ctl, plan, due.Value, worldReady);
        }

        static List<int> Steps(UpdatePlan plan)
        {
            var steps = new List<int>();
            foreach (int s in new[] { plan.WarnMinutes, 5, 1 })
                if (s > 0 && s <= plan.WarnMinutes && !steps.Contains(s)) steps.Add(s);
            steps.Sort((a, b) => b.CompareTo(a));
            return steps;
        }

        static void Run(Install inst, ServerControl ctl, UpdatePlan plan, DateTime due, bool wasRunning)
        {
            Running = true;
            plan.LastRun = UpdatePlan.Key(due);
            try { plan.Save(inst); } catch { }
            slot = null;
            Action<string> st = t => Manager.SetNotice(L.T("Planned update:") + " " + t, "warn");
            string what = plan.Core && plan.Modules ? "core, Playerbots and modules" : plan.Core ? "core and Playerbots" : "modules";
            Log(inst, "Planned update started (" + what + ").");
            new Thread(() =>
            {
                string result;
                bool bad = false;
                DateTime started = DateTime.Now;
                try
                {
                    st(L.T("the server is updated and built again ..."));
                    string password = DbLogin.FromConfig(inst).Password;
                    var job = Jobs.RunEngine(inst, "Update", password, ctl.DbPort, plan.EngineArgs, null, true);
                    while (job.Running) Thread.Sleep(5000);
                    Log(inst, "Engine: " + (job.Ok ? "done" : "failed") + " - " + Flat(job.Message));
                    result = job.Ok ? L.T("The update was installed.") : L.T("The update failed; the server is as it was before.") + " " + Flat(job.Message);
                    bad = !job.Ok;
                    if (wasRunning)
                    {
                        string problem = Start(inst, ctl, st);
                        if (problem != null && job.Ok && plan.Restore)
                        {
                            Log(inst, "The server did not start after the update: " + Flat(problem));
                            string backup = BackupBefore(inst, started);
                            if (backup != null)
                            {
                                st(L.F("the server did not start; restoring the backup {0} ...", backup));
                                Log(inst, "Restoring the backup " + backup + ".");
                                var restore = Jobs.RunEngine(inst, "Restore", DbLogin.FromConfig(inst).Password, ctl.DbPort, "-Snapshot " + MySql.Arg(backup), null, true);
                                while (restore.Running) Thread.Sleep(5000);
                                Log(inst, "Restore: " + (restore.Ok ? "done" : "failed") + " - " + Flat(restore.Message));
                                problem = restore.Ok ? Start(inst, ctl, st) : Flat(restore.Message);
                                result = problem == null
                                    ? L.F("The server did not start after the update, so the backup {0} was restored; it runs as before.", backup)
                                    : L.F("The server did not start after the update, and restoring the backup {0} did not help either:", backup) + " " + problem;
                            }
                            else result = L.T("The server did not start after the update, and no backup of it was found:") + " " + problem;
                            bad = true;
                        }
                        else if (problem != null)
                        {
                            result += " " + L.T("The server did not start:") + " " + problem;
                            bad = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    result = L.T("The planned update could not run:") + " " + Flat(ex.Message);
                    bad = true;
                }
                Log(inst, result);
                lastResult = L.F("Planned update {0}:", DateTime.Now.ToString("dd.MM. HH:mm")) + " " + result;
                Running = false;
                Manager.SetNotice(lastResult, bad ? "bad" : "ok");
                Manager.CheckForUpdates();
            }) { IsBackground = true, Name = "scheduled-update" }.Start();
        }

        /// <summary>Starts the server and waits until the world accepts players; the reason when it does not.</summary>
        static string Start(Install inst, ServerControl ctl, Action<string> st)
        {
            try
            {
                ctl.StartDatabase(st);
                try { RegistrationPortal.EnsureRealmDefaults(inst); } catch { }
                ctl.StartAuth(st);
                try { AdminLink.Prepare(inst); } catch { }
                Process world = ctl.StartWorld(st);
                st(L.T("the worldserver is loading."));
                string problem = null;
                var done = new ManualResetEvent(false);
                ctl.WatchWorld(world, (ready, why) => { if (!ready) problem = why ?? L.T("The worldserver stopped."); done.Set(); });
                done.WaitOne();
                return problem;
            }
            catch (Exception ex) { return ex.Message; }
        }

        /// <summary>The backup the engine made before this update: the newest "before update" one from after its start.</summary>
        static string BackupBefore(Install inst, DateTime started)
        {
            try
            {
                string folder = Path.Combine(inst.Root, "Backups");
                if (!Directory.Exists(folder)) return null;
                foreach (var dir in Directory.GetDirectories(folder).OrderByDescending(d => d, StringComparer.Ordinal))
                {
                    string manifest = Path.Combine(dir, "manifest.txt");
                    if (!File.Exists(manifest)) continue;
                    if (!File.ReadAllLines(manifest).Any(l => l.Trim() == "reason=before update")) continue;
                    if (Directory.GetCreationTime(dir) < started.AddMinutes(-1)) return null;
                    return Path.GetFileName(dir);
                }
            }
            catch { }
            return null;
        }

        /// <summary>The line on the page: when the next planned update is, or how the last one went.</summary>
        public static Tuple<string, bool> State()
        {
            var plan = Plan;
            if (Running) return Tuple.Create(L.T("The planned update is running ..."), true);
            string line = "";
            if (plan.Mode != UpdatePlan.Off)
            {
                var next = plan.Next(DateTime.Now);
                if (slot != null && relevant == true)
                    line = L.F("Update at {0}: there is something new; the server is updated then.", plan.TimeText);
                else if (next != null)
                    line = next.Value.Date == DateTime.Today ? L.F("Next planned update today at {0} (only if there is something new).", plan.TimeText)
                        : next.Value.Date == DateTime.Today.AddDays(1) ? L.F("Next planned update tomorrow at {0} (only if there is something new).", plan.TimeText)
                        : L.F("Next planned update on {0} at {1} (only if there is something new).", next.Value.ToString("dddd", L.Culture), plan.TimeText);
            }
            if (lastResult.Length > 0) line = (line.Length > 0 ? line + "\n" : "") + lastResult;
            return Tuple.Create(line, false);
        }

        public static string Apply(Install inst, int check, bool notify, int mode, int day, string time, bool core, bool modules, bool restore, string warnText)
        {
            int minutes, warn;
            if (Array.IndexOf(UpdatePlan.CheckChoices, check) < 0) return L.T("Choose how often to look for updates.");
            if (mode != UpdatePlan.Off)
            {
                if (!RestartPlan.ParseTime(time, out minutes)) return L.T("Enter the time of day on the 24-hour clock, for example 05:00 or 17:30.");
                if (!core && !modules) return L.T("Choose what is updated: the core and Playerbots, the modules, or both.");
                if (!int.TryParse((warnText ?? "").Trim(), out warn) || warn < 0 || warn > 60) return L.T("Enter how many minutes before the update players are told: 0 to 60.");
                if (day < 0 || day > 6) day = (int)DayOfWeek.Friday;
            }
            else { minutes = Plan.Minutes; warn = Plan.WarnMinutes; }
            var plan = Plan;
            plan.CheckHours = check; plan.Notify = notify; plan.Mode = mode; plan.Day = day; plan.Minutes = minutes;
            plan.Core = core; plan.Modules = modules; plan.Restore = restore; plan.WarnMinutes = warn;
            // a time chosen anew may be the one that was handled already today
            plan.LastRun = "";
            try { plan.Save(inst); } catch (Exception ex) { return L.T("The setting could not be saved:") + "\n" + ex.Message; }
            if (told.Count > 0) Announce(inst, "The announced update is off.");
            slot = null; relevant = null; told.Clear();
            Manager.Poke();
            return null;
        }

        static void Announce(Install inst, string text)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { AdminLink.Run(inst, "announce " + text); } catch { }
                try { AdminLink.Run(inst, "notify " + text); } catch { }
            });
        }

        static void Log(Install inst, string text)
        {
            try
            {
                Directory.CreateDirectory(inst.LogDir);
                File.AppendAllText(Path.Combine(inst.LogDir, "scheduled-update.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text + Environment.NewLine);
            }
            catch { }
        }

        static string Flat(string text) { return (text ?? "").Replace("\r", "").Replace("\n", " | ").Trim(); }
    }
}
