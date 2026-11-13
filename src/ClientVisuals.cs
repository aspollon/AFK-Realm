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
            if (System.IO.File.Exists(path) && Read(client) == choice) return false;
            System.IO.File.WriteAllText(path, "; 0 = default graphics, 1 = Lumenveil, 2 = LuxVelum\r\n" + choice + "\r\n", new UTF8Encoding(false));
            return true;
        }

        public static string Name(int choice)
        {
            return choice == Stock ? L.T("no graphics effect") : choice == LuxVelum ? "LuxVelum" : "Lumenveil";
        }
    }
}
