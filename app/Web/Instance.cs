using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace CoAInstaller.Web
{
    /// <summary>
    /// The running AFK Realm, written to the settings folder (web-instance.json: process, port, key), so a second start
    /// can show the first one's window instead of starting another. The key lets the window in; nobody else can read the
    /// file (its folder belongs to the user).
    /// </summary>
    class Instance
    {
        public int Pid { get; set; }
        public int Port { get; set; }
        public string Token { get; set; }
        public string Host { get; set; }

        public string Url { get { return "http://" + (Host ?? "127.0.0.1") + ":" + Port + "/"; } }
        public string LoginUrl { get { return Url + "login?t=" + Token; } }

        static string FilePath { get { return Path.Combine(Paths.SettingsDir, "web-instance.json"); } }

        public static Instance Running()
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                var i = JsonSerializer.Deserialize<Instance>(File.ReadAllText(FilePath));
                if (i == null || i.Pid == Environment.ProcessId) return null;
                try { using (var p = Process.GetProcessById(i.Pid)) if (p.HasExited) return null; }
                catch { return null; }
                // The process may be another program by now (a number used again): ask it.
                using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) })
                {
                    var answer = http.GetStringAsync("http://127.0.0.1:" + i.Port + "/ping").Result;
                    return answer.Trim() == "AFK Realm" ? i : null;
                }
            }
            catch { return null; }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Paths.SettingsDir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this), new UTF8Encoding(false));
                Os.Private(FilePath);
            }
            catch { }
        }

        public static void Remove()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                var i = JsonSerializer.Deserialize<Instance>(File.ReadAllText(FilePath));
                if (i != null && i.Pid == Environment.ProcessId) File.Delete(FilePath);
            }
            catch { }
        }
    }

    /// <summary>Folders of AFK Realm itself (not of a server).</summary>
    static class Paths
    {
        /// <summary>%APPDATA%\AFK-Realm on Windows, ~/.config/AFK-Realm on Linux - the same place the classic version uses.</summary>
        public static string SettingsDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Product.FileStem); } }

        /// <summary>Larger things of AFK Realm (PowerShell for Linux, the window's browser profile).</summary>
        public static string DataDir
        {
            get
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(local)) local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
                return Path.Combine(local, Product.FileStem);
            }
        }

        /// <summary>The folder AFK Realm's program file is in.</summary>
        public static string ExeDir { get { return AppContext.BaseDirectory; } }

        /// <summary>The program file of AFK Realm (for copies and restarts).</summary>
        public static string ExePath { get { return Environment.ProcessPath; } }
    }
}
