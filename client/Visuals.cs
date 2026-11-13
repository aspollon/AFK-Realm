using System.Text;

namespace AfkClient;

/// <summary>Visuals.txt beside AFK64.exe: 0 no effect, 1 Lumenveil (the exe's default), 2 LuxVelum.</summary>
internal static class Visuals
{
    public const int Stock = 0, Lumenveil = 1, LuxVelum = 2;
    const string LumenveilIni = "CoAVolFog.ini";

    public static int Read(string gameFolder)
    {
        try
        {
            string path = Path.Combine(gameFolder ?? "", "Visuals.txt");
            if (!File.Exists(path)) return Lumenveil;
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
                string digits = new string(line.TakeWhile(char.IsDigit).ToArray());
                return int.TryParse(digits, out int n) && n >= Stock && n <= LuxVelum ? n : Lumenveil;
            }
        }
        catch { }
        return Lumenveil;
    }

    public static void Write(string gameFolder, int choice)
    {
        if (choice < Stock || choice > LuxVelum || !Directory.Exists(gameFolder)) return;
        File.WriteAllText(Path.Combine(gameFolder, "Visuals.txt"),
            "; 0 = default graphics, 1 = Lumenveil, 2 = LuxVelum\r\n" + choice + "\r\n", new UTF8Encoding(false));
        if (choice == Lumenveil) EnableOverlay(gameFolder);
    }

    /// <summary>Lumenveil's settings window and its keys (Ctrl+F7, Ctrl+F12, ...) stay off while CoAVolFog.ini
    /// says Overlay=0. The CoA Custom package ships that value; turn it back on without touching the other settings.</summary>
    public static bool EnableOverlay(string gameFolder)
    {
        try
        {
            string path = Path.Combine(gameFolder ?? "", LumenveilIni);
            if (!File.Exists(path)) return false;
            var lines = File.ReadAllLines(path).ToList();
            bool section = false, found = false, changed = false;
            int sectionAt = -1;
            for (int i = 0; i < lines.Count; i++)
            {
                string text = lines[i].Trim();
                if (text.StartsWith('[') && text.EndsWith(']'))
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
            if (changed) File.WriteAllLines(path, lines, new UTF8Encoding(false));
            return changed;
        }
        catch { return false; }
    }
}
