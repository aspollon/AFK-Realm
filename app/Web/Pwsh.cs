using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace CoAInstaller.Web
{
    /// <summary>
    /// The engine is a PowerShell script. Windows has PowerShell; on Linux the engine needs PowerShell 7 (pwsh): the one
    /// of the system when there is one, otherwise AFK Realm fetches the official build once into its own folder - no
    /// root, nothing installed system-wide.
    /// </summary>
    static class Pwsh
    {
        const string Version = "7.4.6";
        static readonly string X64Sha = "6f6015203c47806c5cc444c19d8ed019695e610fbd948154264bf9ca8e157561";
        static readonly string Arm64Sha = "c0159b03e85f44ae1e7697818a011558da6c813d0aae848bf5ac13bf435d8624";

        static string Folder { get { return Path.Combine(Paths.DataDir, "pwsh-" + Version); } }

        public static string Find()
        {
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':'))
            {
                if (dir.Length == 0) continue;
                string candidate = Path.Combine(dir, "pwsh");
                if (File.Exists(candidate) && Works(candidate)) return candidate;
            }
            string own = Path.Combine(Folder, "pwsh");
            return File.Exists(own) ? own : null;
        }

        static bool Works(string pwsh)
        {
            try
            {
                var psi = new ProcessStartInfo(pwsh, "-NoLogo -NoProfile -Command \"$PSVersionTable.PSVersion.Major\"") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                using (var p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(20000);
                    int major;
                    return int.TryParse(output.Trim(), out major) && major >= 7;
                }
            }
            catch { return false; }
        }

        /// <summary>The pwsh to use, fetched first when there is none.</summary>
        public static string Ensure(Action<string> status)
        {
            string found = Find();
            if (found != null) return found;
            bool arm = RuntimeInformation.OSArchitecture == Architecture.Arm64;
            string url = "https://github.com/PowerShell/PowerShell/releases/download/v" + Version + "/powershell-" + Version + "-linux-" + (arm ? "arm64" : "x64") + ".tar.gz";
            Directory.CreateDirectory(Paths.DataDir);
            string archive = Path.Combine(Paths.DataDir, "pwsh.tar.gz");
            status(L.T("Fetching PowerShell 7 for the engine (once) ..."));
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(20) })
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd(Product.FileStem);
                using (var response = http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).Result)
                {
                    response.EnsureSuccessStatusCode();
                    long total = response.Content.Headers.ContentLength ?? -1;
                    using (var src = response.Content.ReadAsStreamAsync().Result)
                    using (var dst = File.Create(archive))
                    {
                        var buffer = new byte[1 << 16]; long done = 0; int n, last = -1;
                        while ((n = src.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            dst.Write(buffer, 0, n); done += n;
                            if (total > 0) { int pct = (int)(done * 100 / total); if (pct / 5 != last) { last = pct / 5; status(L.F("Download: {0} %", pct)); } }
                        }
                    }
                }
            }
            string sha;
            using (var s = File.OpenRead(archive)) sha = Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();
            if (sha != (arm ? Arm64Sha : X64Sha)) { File.Delete(archive); throw new InvalidOperationException(L.T("The download does not match the expected file. Nothing was changed.")); }
            string temp = Folder + ".part";
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
            Directory.CreateDirectory(temp);
            var psi = new ProcessStartInfo("tar", "-xzf " + MySql.Arg(archive) + " -C " + MySql.Arg(temp)) { UseShellExecute = false, RedirectStandardError = true };
            using (var p = Process.Start(psi))
            {
                string err = p.StandardError.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode != 0) throw new InvalidOperationException("tar: " + err.Trim());
            }
            if (Directory.Exists(Folder)) Directory.Delete(Folder, true);
            Directory.Move(temp, Folder);
            File.Delete(archive);
            string pwsh = Path.Combine(Folder, "pwsh");
            File.SetUnixFileMode(pwsh, File.GetUnixFileMode(pwsh) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            if (!Works(pwsh)) throw new InvalidOperationException(L.T("PowerShell 7 could not be started on this computer."));
            return pwsh;
        }
    }
}
