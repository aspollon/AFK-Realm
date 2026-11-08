using System;
using System.Globalization;
using System.IO;

namespace CoAInstaller
{
    /// <summary>
    /// How often AFK Realm looks for new server code, whether it shows it in the bar, and when it installs it on its
    /// own: every day or on one day of the week at a set time, the CoA core and Playerbots and/or the modules. A new
    /// AFK Realm release is always shown at once; this plan is about the server code only. Like the scheduled restart
    /// it works while AFK Realm runs (in the tray as well).
    /// </summary>
    class UpdatePlan
    {
        public const int Off = 0, Daily = 1, Weekly = 2;
        /// <summary>The choices for how often to look: 0 = only when AFK Realm starts or when asked.</summary>
        public static readonly int[] CheckChoices = { 0, 1, 3, 6, 12, 24 };

        public int CheckHours = 6;
        public bool Notify = true;              // show server-code updates in the bar
        public int Mode = Off;
        public int Day = (int)DayOfWeek.Friday; // Weekly
        public int Minutes = 3 * 60;            // minutes after midnight
        public bool Core = true, Modules = true;
        public bool Restore = true;             // a server that does not start after the update gets its backup back
        public int WarnMinutes = 10;
        public string LastRun = "";             // the slot already handled ("yyyy-MM-dd HH:mm")

        static string FileOf(Install inst) { return Path.Combine(inst.Root, "Dependencies", "update-plan.txt"); }

        public static UpdatePlan Load(Install inst)
        {
            var plan = new UpdatePlan();
            try
            {
                if (File.Exists(FileOf(inst)))
                    foreach (var line in File.ReadAllLines(FileOf(inst)))
                    {
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        string key = line.Substring(0, eq).Trim(), value = line.Substring(eq + 1).Trim();
                        int n;
                        bool isNumber = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n);
                        if (key == "last") plan.LastRun = value;
                        else if (!isNumber) continue;
                        else if (key == "check") plan.CheckHours = n;
                        else if (key == "notify") plan.Notify = n != 0;
                        else if (key == "mode") plan.Mode = n;
                        else if (key == "day") plan.Day = n;
                        else if (key == "time") plan.Minutes = n;
                        else if (key == "core") plan.Core = n != 0;
                        else if (key == "modules") plan.Modules = n != 0;
                        else if (key == "restore") plan.Restore = n != 0;
                        else if (key == "warn") plan.WarnMinutes = n;
                    }
            }
            catch { }
            plan.Clamp();
            return plan;
        }

        public void Save(Install inst)
        {
            Clamp();
            Directory.CreateDirectory(Path.GetDirectoryName(FileOf(inst)));
            Func<bool, string> b = x => x ? "1" : "0";
            File.WriteAllLines(FileOf(inst), new[]
            {
                "check=" + CheckHours.ToString(CultureInfo.InvariantCulture),
                "notify=" + b(Notify),
                "mode=" + Mode.ToString(CultureInfo.InvariantCulture),
                "day=" + Day.ToString(CultureInfo.InvariantCulture),
                "time=" + Minutes.ToString(CultureInfo.InvariantCulture),
                "core=" + b(Core),
                "modules=" + b(Modules),
                "restore=" + b(Restore),
                "warn=" + WarnMinutes.ToString(CultureInfo.InvariantCulture),
                "last=" + LastRun
            });
        }

        void Clamp()
        {
            if (Array.IndexOf(CheckChoices, CheckHours) < 0) CheckHours = 6;
            if (Mode < Off || Mode > Weekly) Mode = Off;
            Day = Math.Max(0, Math.Min(6, Day));
            Minutes = Math.Max(0, Math.Min(24 * 60 - 1, Minutes));
            WarnMinutes = Math.Max(0, Math.Min(60, WarnMinutes));
            if (!Core && !Modules) Mode = Off;
        }

        public string TimeText { get { return (Minutes / 60).ToString("00") + ":" + (Minutes % 60).ToString("00"); } }

        public static string Key(DateTime slot) { return slot.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture); }

        /// <summary>The slot that is due now: from its warning time until an hour after it (AFK Realm opened a little
        /// late still counts), and not handled yet. Null when none is.</summary>
        public DateTime? Current(DateTime now)
        {
            if (Mode == Off) return null;
            foreach (var slot in new[] { Slot(now.Date), Slot(now.Date.AddDays(1)) })
            {
                if (slot == null) continue;
                var s = slot.Value;
                if (now >= s.AddMinutes(-WarnMinutes - 1) && now <= s.AddHours(1) && Key(s) != LastRun) return s;
            }
            return null;
        }

        /// <summary>The next slot from now on (for the line on the page).</summary>
        public DateTime? Next(DateTime now)
        {
            if (Mode == Off) return null;
            for (int d = 0; d <= 7; d++)
            {
                var slot = Slot(now.Date.AddDays(d));
                if (slot != null && slot.Value.AddHours(1) >= now && Key(slot.Value) != LastRun) return slot;
            }
            return null;
        }

        DateTime? Slot(DateTime date)
        {
            if (Mode == Weekly && (int)date.DayOfWeek != Day) return null;
            return date.AddMinutes(Minutes);
        }

        /// <summary>The engine's arguments for what is to be updated.</summary>
        public string EngineArgs { get { return Core && Modules ? null : Core ? "-CoreOnly" : "-ModulesOnly"; } }
    }
}
