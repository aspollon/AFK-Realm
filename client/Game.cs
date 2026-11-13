using System.Diagnostics;

namespace AfkClient;

internal static class Game
{
    // AFK64.exe is the client AFK installs; the others keep older or plain clients working.
    static readonly string[] ExeNames = { "AFK64.exe", "CoACustomRace.exe", "CoA64.exe", "Ascension.exe", "Ascension-b.exe", "Wow.exe" };

    public static bool IsClientFolder(string folder)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(folder) &&
                Directory.Exists(folder) &&
                Directory.Exists(Path.Combine(folder, "Data"));
        }
        catch { return false; }
    }

    public static string FindExe(string folder)
    {
        foreach (string name in ExeNames)
        {
            string file = Path.Combine(folder, name);
            if (File.Exists(file)) return file;
        }
        return null;
    }

    public static bool IsRunning(string folder)
    {
        try
        {
            foreach (string name in ExeNames)
            {
                foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(name)))
                {
                    try
                    {
                        string file = process.MainModule?.FileName;
                        if (!string.IsNullOrEmpty(file) &&
                            Path.GetFullPath(file).StartsWith(Path.GetFullPath(folder).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                    catch { }
                }
            }
        }
        catch { }
        return false;
    }

    public static void Launch(string folder)
    {
        string exe = FindExe(folder);
        if (exe == null) throw new InvalidOperationException("The game folder has no AFK64.exe, CoACustomRace.exe, CoA64.exe, Ascension.exe or Wow.exe.");
        if (IsRunning(folder)) throw new InvalidOperationException("The game is already running.");
        Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = folder, UseShellExecute = true });
    }

    public static void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return;
        Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
    }
}
