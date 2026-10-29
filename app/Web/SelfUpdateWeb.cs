using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;

namespace CoAInstaller.Web
{
    /// <summary>
    /// Replaces this program with a newer release from GitHub: the running file is moved aside (Windows and Linux both
    /// allow that), the new one takes its place and the program starts again. The game servers keep running.
    /// </summary>
    static class SelfUpdateWeb
    {
        public static bool JustUpdated;

        public static string TakeJustUpdated()
        {
            if (!JustUpdated) return null;
            JustUpdated = false;
            return L.F("{0} was updated to version {1}.", Product.Name, Product.Version);
        }

        static string Exe { get { return Path.GetFullPath(Paths.ExePath); } }
        static string OldFile { get { return Exe + ".old"; } }
        static string NewFile { get { return Exe + ".new"; } }

        public static void CleanUp()
        {
            foreach (var f in new[] { OldFile, NewFile })
                try { if (File.Exists(f)) File.Delete(f); } catch { }
        }

        public static void Download(string url, string sha256, Action<string> status)
        {
            string target = NewFile;
            try
            {
                using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
                {
                    http.DefaultRequestHeaders.UserAgent.ParseAdd(Product.FileStem);
                    using (var response = http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).Result)
                    {
                        response.EnsureSuccessStatusCode();
                        using (var src = response.Content.ReadAsStreamAsync().Result)
                        using (var dst = File.Create(target))
                        {
                            var buffer = new byte[64 * 1024];
                            long done = 0, last = 0; int n;
                            while ((n = src.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                dst.Write(buffer, 0, n); done += n;
                                if (done > 400L * 1024 * 1024) throw new InvalidOperationException(L.T("The download is far larger than expected."));
                                if (done - last >= 1024 * 1024) { last = done; status(L.F("Downloading the update ... {0} KB", done / 1024)); }
                            }
                        }
                    }
                }
                Verify(target, sha256);
            }
            catch
            {
                try { File.Delete(target); } catch { }
                throw;
            }
        }

        static void Verify(string file, string sha256)
        {
            var info = new FileInfo(file);
            if (info.Length < 1024 * 1024) throw new InvalidOperationException(L.T("The download is incomplete."));
            if (!string.IsNullOrEmpty(sha256))
            {
                string actual;
                using (var s = File.OpenRead(file)) actual = Convert.ToHexString(SHA256.HashData(s));
                if (!actual.Equals(sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(L.T("The download does not match the checksum GitHub publishes for it. Nothing was changed."));
            }
            var head = new byte[4];
            using (var s = File.OpenRead(file)) s.Read(head, 0, 4);
            bool program = Os.Windows ? head[0] == 'M' && head[1] == 'Z' : head[0] == 0x7F && head[1] == 'E' && head[2] == 'L' && head[3] == 'F';
            if (!program) throw new InvalidOperationException(L.T("The downloaded file is not a valid program. Nothing was changed."));
        }

        public static void Replace(string managementCopy)
        {
            string exe = Exe, old = OldFile, fresh = NewFile;
            if (File.Exists(old)) File.Delete(old);
            File.Move(exe, old);
            try
            {
                File.Move(fresh, exe);
                if (!Os.Windows) File.SetUnixFileMode(exe, File.GetUnixFileMode(old));
            }
            catch { File.Move(old, exe); throw; }
            try
            {
                if (!string.IsNullOrEmpty(managementCopy) && File.Exists(managementCopy) &&
                    !string.Equals(Path.GetFullPath(managementCopy), exe, Os.Windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    File.Copy(exe, managementCopy, true);
            }
            catch { }
        }

        public static void Restart()
        {
            var args = "--updated" + (Program.Opts.Listen != null ? " --listen " + MySql.Arg(Program.Opts.Listen) + " --port " + WebHost.Port : "") + (Program.Opts.NoWindow ? " --no-window" : "");
            var psi = new ProcessStartInfo(Exe, args) { UseShellExecute = Os.Windows, WorkingDirectory = Path.GetDirectoryName(Exe) };
            if (Program.Opts.Listen != null && !string.IsNullOrEmpty(Program.Opts.Password)) { psi.UseShellExecute = false; psi.Environment["AFK_REALM_PASSWORD"] = Program.Opts.Password; }
            Process.Start(psi);
            AppWindow.Close();
            WebHost.Shutdown();
        }

        /// <summary>Starts this program again (after the language changed it needs no restart; kept for the update).</summary>
        public static void RestartSame()
        {
            Process.Start(new ProcessStartInfo(Exe, "--restart") { UseShellExecute = Os.Windows, WorkingDirectory = Path.GetDirectoryName(Exe) });
            AppWindow.Close();
            WebHost.Shutdown();
        }
    }
}
