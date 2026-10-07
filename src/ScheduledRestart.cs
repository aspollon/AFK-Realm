using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace CoAInstaller
{
    /// <summary>
    /// When the server is restarted on its own: after it has run for a while, every day at a set time, or
    /// when the worldserver has grown too big. A world that has run for long tends to use more and more
    /// memory; a restart gives it back. Players are told in the game a few minutes ahead.
    ///
    /// AFK Realm does the restart itself (clean stop, start again), so it only happens while AFK Realm is
    /// open. Nothing is handed to the server that would shut it down with nobody there to start it again.
    /// </summary>
    class RestartPlan
    {
        public const int Off = 0, AfterUptime = 1, Daily = 2, Memory = 3;

        public int Mode = Off;
        public double Hours = 6;                // AfterUptime
        public int DailyMinutes = 5 * 60;       // Daily: minutes after midnight
        public double MemoryGb = 8;             // Memory
        public int WarnMinutes = 10;            // players are told this long before

        static string FileOf(Install inst) { return Path.Combine(inst.Root, "Dependencies", "scheduled-restart.txt"); }

        public static RestartPlan Load(Install inst)
        {
            var plan = new RestartPlan();
            try
            {
                foreach (var line in File.ReadAllLines(FileOf(inst)))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim(), value = line.Substring(eq + 1).Trim();
                    double number;
                    if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) continue;
                    if (key == "mode") plan.Mode = (int)number;
                    else if (key == "hours") plan.Hours = number;
                    else if (key == "daily") plan.DailyMinutes = (int)number;
                    else if (key == "memory") plan.MemoryGb = number;
                    else if (key == "warn") plan.WarnMinutes = (int)number;
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
            File.WriteAllLines(FileOf(inst), new[]
            {
                "mode=" + Mode.ToString(CultureInfo.InvariantCulture),
                "hours=" + Hours.ToString("0.##", CultureInfo.InvariantCulture),
                "daily=" + DailyMinutes.ToString(CultureInfo.InvariantCulture),
                "memory=" + MemoryGb.ToString("0.##", CultureInfo.InvariantCulture),
                "warn=" + WarnMinutes.ToString(CultureInfo.InvariantCulture)
            });
        }

        void Clamp()
        {
            if (Mode < Off || Mode > Memory) Mode = Off;
            Hours = Math.Max(0.5, Math.Min(24 * 30, Hours));
            DailyMinutes = Math.Max(0, Math.Min(24 * 60 - 1, DailyMinutes));
            MemoryGb = Math.Max(1, Math.Min(512, MemoryGb));
            WarnMinutes = Math.Max(0, Math.Min(60, WarnMinutes));
        }

        public string DailyText { get { return (DailyMinutes / 60).ToString("00") + ":" + (DailyMinutes % 60).ToString("00"); } }

        /// <summary>"5", "5.5" or "5,5" hours; "18000" seconds is understood as well, because that is how server settings count.</summary>
        public static bool ParseHours(string text, out double hours)
        {
            hours = 0;
            text = (text ?? "").Trim().Replace(',', '.');
            double number;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number) || number <= 0) return false;
            hours = number >= 600 ? number / 3600.0 : number;
            return hours >= 0.5 && hours <= 24 * 30;
        }

        /// <summary>"05:00", "5:00", "5" or "0500".</summary>
        public static bool ParseTime(string text, out int minutes)
        {
            minutes = 0;
            text = (text ?? "").Trim().Replace('.', ':');
            int h, m = 0;
            if (text.Contains(":"))
            {
                var parts = text.Split(':');
                if (parts.Length != 2 || !int.TryParse(parts[0], out h) || !int.TryParse(parts[1], out m)) return false;
            }
            else if (text.Length == 4 && int.TryParse(text.Substring(0, 2), out h) && int.TryParse(text.Substring(2), out m)) { }
            else if (!int.TryParse(text, out h)) return false;
            if (h < 0 || h > 23 || m < 0 || m > 59) return false;
            minutes = h * 60 + m;
            return true;
        }

        public static bool ParseGb(string text, out double gb)
        {
            gb = 0;
            text = (text ?? "").Trim().Replace(',', '.');
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out gb) && gb >= 1 && gb <= 512;
        }

        /// <summary>
        /// When the restart is due, seen from now, or null when none is: the moment the server goes down.
        /// It is returned once the announcement has to go out (the warning time before it), and never
        /// earlier than the warning time from now, so players are always told in time.
        /// </summary>
        /// <param name="worldStart">when the running worldserver was started</param>
        /// <param name="memoryOver">Memory mode: the worldserver has been over the limit for a while</param>
        public DateTime? Due(DateTime now, DateTime worldStart, bool memoryOver)
        {
            var warn = TimeSpan.FromMinutes(WarnMinutes);
            DateTime due;
            if (Mode == AfterUptime) due = worldStart.AddHours(Hours);
            else if (Mode == Daily)
            {
                due = now.Date.AddMinutes(DailyMinutes);
                // Today's time is over (by more than an hour, so opening AFK Realm a little late still counts): tomorrow's.
                if (now > due.AddHours(1)) due = due.AddDays(1);
                // A server that was started after the announcement would have gone out has had its restart.
                if (worldStart > due - warn - TimeSpan.FromMinutes(1)) return null;
            }
            else if (Mode == Memory)
            {
                // Right after a start the world is as small as it gets: a limit below that would restart it forever.
                if (!memoryOver || now - worldStart < TimeSpan.FromMinutes(20)) return null;
                due = now + warn;
            }
            else return null;

            if (now < due - warn) return null;
            return due < now + warn ? now + warn : due;
        }

        /// <summary>The steps at which players are told, in minutes before the restart, largest first.</summary>
        public List<int> Steps()
        {
            var steps = new List<int>();
            foreach (int s in new[] { WarnMinutes, 5, 1 })
                if (s > 0 && s <= WarnMinutes && !steps.Contains(s)) steps.Add(s);
            steps.Sort((a, b) => b.CompareTo(a));
            return steps;
        }

        public static string Span(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            if (t.TotalHours >= 1) return (int)t.TotalHours + " h " + t.Minutes + " min";
            if (t.TotalMinutes >= 1) return (int)Math.Ceiling(t.TotalMinutes) + " min";
            return "less than a minute";
        }
    }
}
