using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace CoAInstaller.Web
{
    /// <summary>
    /// The window of AFK Realm: the page in an app window of Edge or a Chromium browser (no address bar, no tabs), with
    /// a profile of its own - so the window is a program of its own, and its end is seen. Without such a browser the
    /// page opens in the default browser.
    /// </summary>
    static class AppWindow
    {
        /// <summary>The window started last, while it is open; null when the page went to a normal browser.</summary>
        public static Process Current;
        /// <summary>The browser the app window was started with (for the start log).</summary>
        public static string Browser = "";

        /// <summary>Closes the app window this program opened (before it starts itself again after an update or a
        /// restart: the new one opens its own window). Since AFK Realm has no console window any more (1.1.12), nothing
        /// else ends the old browser process, and two AFK Realm windows were open. The window runs in a profile folder
        /// of its own, so no other browser window is touched.</summary>
        public static void Close()
        {
            var p = Current;
            Current = null;
            if (p == null) return;
            try
            {
                if (p.HasExited) return;
                p.CloseMainWindow();
                if (!p.WaitForExit(3000)) p.Kill(true);
            }
            catch { }
        }

        static IEnumerable<string> Candidates()
        {
            if (Os.Windows)
            {
                foreach (var root in new[] { Environment.GetEnvironmentVariable("ProgramFiles(x86)"), Environment.GetEnvironmentVariable("ProgramFiles"), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) })
                {
                    if (string.IsNullOrEmpty(root)) continue;
                    yield return Path.Combine(root, "Microsoft", "Edge", "Application", "msedge.exe");
                    yield return Path.Combine(root, "Google", "Chrome", "Application", "chrome.exe");
                    yield return Path.Combine(root, "BraveSoftware", "Brave-Browser", "Application", "brave.exe");
                }
                yield break;
            }
            foreach (var name in new[] { "chromium", "chromium-browser", "google-chrome", "google-chrome-stable", "microsoft-edge", "microsoft-edge-stable", "brave-browser", "vivaldi" })
            {
                foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':'))
                {
                    if (dir.Length == 0) continue;
                    yield return Path.Combine(dir, name);
                }
            }
            foreach (var flatpak in new[] { "/var/lib/flatpak/exports/bin/org.chromium.Chromium", "/var/lib/flatpak/exports/bin/com.google.Chrome", "/var/lib/flatpak/exports/bin/com.microsoft.Edge" })
                yield return flatpak;
        }

        static IEnumerable<string> SafeDirs(string folder, string pattern)
        {
            try { return Directory.GetDirectories(folder, pattern); } catch { return new string[0]; }
        }

        /// <summary>Whether a window can be shown at all (a Linux server without a desktop cannot).</summary>
        public static bool CanShow
        {
            get
            {
                if (Os.Windows) return true;
                return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")) || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
            }
        }

        /// <summary>Opens the page. <paramref name="track"/>: this program watches the window and ends with it.</summary>
        public static bool Open(string url, bool track) { return Open(url, track, false); }

        /// <summary>fresh: in a new profile folder - the usual one is held by a browser process that took the window
        /// over and never showed it (Edge then ends at once with code 0, and no page comes).</summary>
        public static bool Open(string url, bool track, bool fresh)
        {
            string browser = Candidates().FirstOrDefault(File.Exists);
            if (browser != null)
            {
                try
                {
                    string profile = Path.Combine(Paths.DataDir, "window");
                    if (fresh)
                    {
                        foreach (var old in SafeDirs(Paths.DataDir, "window-*")) try { Directory.Delete(old, true); } catch { }
                        profile = Path.Combine(Paths.DataDir, "window-" + DateTime.Now.ToString("yyyyMMddHHmmss"));
                    }
                    Directory.CreateDirectory(profile);
                    var psi = new ProcessStartInfo(browser) { UseShellExecute = false };
                    psi.ArgumentList.Add("--app=" + url);
                    psi.ArgumentList.Add("--user-data-dir=" + profile);
                    psi.ArgumentList.Add("--window-size=1280,860");
                    psi.ArgumentList.Add("--no-first-run");
                    psi.ArgumentList.Add("--no-default-browser-check");
                    psi.ArgumentList.Add("--disable-features=Translate");
                    if (!Os.Windows)
                    {
                        psi.RedirectStandardError = true; psi.RedirectStandardOutput = true;    // the browser's chatter stays out of the terminal
                    }
                    Browser = browser;
                    var p = Process.Start(psi);
                    if (p != null && !Os.Windows) { p.ErrorDataReceived += (s, e) => { }; p.OutputDataReceived += (s, e) => { }; p.BeginErrorReadLine(); p.BeginOutputReadLine(); }
                    if (track && p != null) Current = p;
                    return true;
                }
                catch { }
            }
            try
            {
                if (Os.Windows) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                else Process.Start(new ProcessStartInfo("xdg-open", MySql.Arg(url)) { UseShellExecute = false });
                return true;
            }
            catch { return false; }
        }
    }
}
