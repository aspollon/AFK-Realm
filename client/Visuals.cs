using System.Text;

namespace AfkClient;

/// <summary>Visuals.txt beside AFK64.exe: 0 no effect, 1 Lumenveil (the exe's default), 2 LuxVelum.</summary>
internal static class Visuals
{
    public const int Stock = 0, Lumenveil = 1, LuxVelum = 2;

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
    }
}
