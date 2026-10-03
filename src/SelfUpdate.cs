using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Forms;

namespace CoAInstaller
{
    /// <summary>
    /// Replaces the running program with a newer release from GitHub. Windows lets a running exe be
    /// renamed, so the old file is moved aside, the new one takes its place and the program restarts.
    /// The servers are separate processes and keep running.
    /// </summary>
    static class SelfUpdate
    {
        public const string RestartArgument = "--after-update";
        /// <summary>This start follows an update; the first page says so once.</summary>
        public static bool JustUpdated;

        static string Exe { get { return Path.GetFullPath(Application.ExecutablePath); } }
        static string OldFile { get { return Exe + ".old"; } }
        static string NewFile { get { return Exe + ".new"; } }

        /// <summary>Removes what an earlier update left behind. Called at every start.</summary>
        public static void CleanUp()
        {
            foreach (var f in new[] { OldFile, NewFile })
                try { if (File.Exists(f)) File.Delete(f); } catch { /* still in use for a moment; the next start removes it */ }
        }

        /// <summary>Downloads the release next to the running exe and checks that it is this program, complete and unaltered.</summary>
        public static void Download(string url, string sha256, Action<string> status)
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }   // TLS 1.2 for GitHub
            string target = NewFile;
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.UserAgent = Product.FileStem;
                req.Timeout = 30000; req.ReadWriteTimeout = 30000;
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var src = resp.GetResponseStream())
                using (var dst = File.Create(target))
                {
                    var buffer = new byte[64 * 1024];
                    long done = 0, last = 0; int n;
                    while ((n = src.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        dst.Write(buffer, 0, n); done += n;
                        if (done > 50L * 1024 * 1024) throw new InvalidOperationException("The download is far larger than expected.");
                        if (done - last >= 256 * 1024) { last = done; status("Downloading the update ... " + (done / 1024) + " KB"); }
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
            if (info.Length < 100 * 1024) throw new InvalidOperationException("The download is incomplete.");
            if (!string.IsNullOrEmpty(sha256))
            {
                string actual;
                using (var sha = SHA256.Create())
                using (var s = File.OpenRead(file)) actual = BitConverter.ToString(sha.ComputeHash(s)).Replace("-", "");
                if (!actual.Equals(sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The download does not match the checksum GitHub publishes for it. Nothing was changed.");
            }
            string own = Assembly.GetExecutingAssembly().GetName().Name, other;
            try { other = AssemblyName.GetAssemblyName(file).Name; }
            catch { throw new InvalidOperationException("The downloaded file is not a valid program. Nothing was changed."); }
            if (!string.Equals(own, other, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The downloaded file is not " + Product.Name + ". Nothing was changed.");
        }

        /// <summary>Puts the downloaded file in place of the running exe; on failure the old one is put back.</summary>
        public static void Replace(string managementCopy)
        {
            string exe = Exe, old = OldFile, fresh = NewFile;
            if (File.Exists(old)) File.Delete(old);
            File.Move(exe, old);
            try { File.Move(fresh, exe); }
            catch { File.Move(old, exe); throw; }
            // The copy in the server folder (target of the desktop shortcut), when this is another file.
            try
            {
                if (!string.IsNullOrEmpty(managementCopy) && File.Exists(managementCopy) &&
                    !string.Equals(Path.GetFullPath(managementCopy), exe, StringComparison.OrdinalIgnoreCase))
                    File.Copy(exe, managementCopy, true);
            }
            catch { /* it updates itself the next time it is started */ }
        }

        /// <summary>Starts the new version and ends this one.</summary>
        public static void Restart()
        {
            Process.Start(new ProcessStartInfo(Exe, RestartArgument) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(Exe) });
            Environment.Exit(0);
        }
    }
}
