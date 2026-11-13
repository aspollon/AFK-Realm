using System;
using System.IO;
using System.Linq;
using System.Text;

namespace CoAInstaller
{
    /// <summary>The graphics effect AFK64.exe loads, chosen in Visuals.txt beside it: 0 none, 1 Lumenveil, 2 LuxVelum.</summary>
    static class ClientVisuals
    {
        public const int Stock = 0, Lumenveil = 1, LuxVelum = 2;
        public const string File = "Visuals.txt";
        public const string Default = "; 0 = default graphics, 1 = Lumenveil, 2 = LuxVelum\r\n1\r\n";
        public const string LumenveilIni = "CoAVolFog.ini";

        /// <summary>The choice in the game folder; Lumenveil when the file is missing or unreadable (the exe's own rule).</summary>
        public static int Read(string client)
        {
            try
            {
                string path = Path.Combine(client, File);
                if (!System.IO.File.Exists(path)) return Lumenveil;
                foreach (string raw in System.IO.File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                    string digits = new string(line.TakeWhile(char.IsDigit).ToArray());
                    if (int.TryParse(digits, out int n) && n >= Stock && n <= LuxVelum) return n;
                    break;
                }
            }
            catch { }
            return Lumenveil;
        }

        /// <summary>Writes the choice; true when it changed.</summary>
        public static bool Write(string client, int choice)
        {
            if (string.IsNullOrEmpty(client) || choice < Stock || choice > LuxVelum) return false;
            string path = Path.Combine(client, File);
            bool changed = true;
            if (System.IO.File.Exists(path) && Read(client) == choice) changed = false;
            else System.IO.File.WriteAllText(path, "; 0 = default graphics, 1 = Lumenveil, 2 = LuxVelum\r\n" + choice + "\r\n", new UTF8Encoding(false));
            if (choice == Lumenveil) EnableOverlay(client);
            return changed;
        }

        /// <summary>Lumenveil's settings window and its keys (Ctrl+F7, Ctrl+F12, ...) are off when CoAVolFog.ini
        /// says Overlay=0. The CoA Custom package ships that value; turning it back on keeps every other setting.</summary>
        public static bool EnableOverlay(string client)
        {
            if (string.IsNullOrEmpty(client)) return false;
            string path = Path.Combine(client, LumenveilIni);
            try
            {
                if (!System.IO.File.Exists(path)) return false;
                var lines = System.IO.File.ReadAllLines(path).ToList();
                bool section = false, found = false, changed = false;
                int sectionAt = -1;
                for (int i = 0; i < lines.Count; i++)
                {
                    string text = lines[i].Trim();
                    if (text.StartsWith("[") && text.EndsWith("]"))
                    {
                        section = text.Equals("[CoAVolFog]", StringComparison.OrdinalIgnoreCase);
                        if (section) sectionAt = i;
                        continue;
                    }
                    if (!section || !text.StartsWith("Overlay", StringComparison.OrdinalIgnoreCase)) continue;
                    int eq = text.IndexOf('=');
                    if (eq < 0 || !text.Substring(0, eq).Trim().Equals("Overlay", StringComparison.OrdinalIgnoreCase)) continue;
                    found = true;
                    if (text.Substring(eq + 1).Trim() != "1")
                    {
                        lines[i] = "Overlay=1";
                        changed = true;
                    }
                    break;
                }
                if (!found && sectionAt >= 0)
                {
                    lines.Insert(sectionAt + 1, "Overlay=1");
                    changed = true;
                }
                if (changed) System.IO.File.WriteAllLines(path, lines, new UTF8Encoding(false));
                return changed;
            }
            catch { return false; }
        }

        public static string Name(int choice)
        {
            return choice == Stock ? L.T("no graphics effect") : choice == LuxVelum ? "LuxVelum" : "Lumenveil";
        }
    }
}
