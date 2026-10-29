using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CoAInstaller
{
    /// <summary>
    /// "Get help": everything that is needed to find a problem - the logs of the build and of the server, its crash
    /// reports and dumps, the settings, the logs and crash reports of the game client, what is installed - gathered in
    /// one ZIP in the Downloads folder, ready to be sent on Discord. Nobody has to explain where which log lies.
    /// Logs are cut to their last part, passwords and account names are taken out of the settings. Crash dumps are
    /// large, so they go into a second ZIP that is only needed when someone asks for it.
    /// </summary>
    static class SupportBundle
    {
        public sealed class Result
        {
            public string Zip, Dumps, Folder;
            public long ZipBytes, DumpBytes;
            public int Files, DumpFiles;
        }

        const long LogTail = 1500000;           // the last 1.5 MB of a log: the problem is nearly always at its end
        const long InstallLogTail = 4000000;    // a failed compile writes its first error well above the end
        const long MaxDumps = 600000000;        // dumps together, newest first
        static readonly Regex Secret = new Regex(@"(?im)^([ \t]*(?:SET[ \t]+)?[\w.]*(Password|Passwd|DatabaseInfo|Secret|Token|ApiKey|accountName|accountList)[\w.]*[ \t]*[=\s][ \t]*)(.*)$");

        static string Stamp { get { return DateTime.Now.ToString("yyyy-MM-dd_HH-mm"); } }

        /// <summary>The folder the ZIP goes to: Downloads, or the logs folder when there is none.</summary>
        static string Target(Install inst)
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (var d in new[] { Path.Combine(home, "Downloads"), home, inst != null ? inst.LogDir : null, Path.GetTempPath() })
            {
                if (string.IsNullOrEmpty(d)) continue;
                try { Directory.CreateDirectory(d); return d; } catch { }
            }
            return Path.GetTempPath();
        }

        static IEnumerable<FileInfo> Files(string dir, params string[] endings)
        {
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return Enumerable.Empty<FileInfo>();
                return new DirectoryInfo(dir).GetFiles().Where(f => endings.Length == 0 || endings.Any(e => f.Name.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                    .OrderByDescending(f => f.LastWriteTimeUtc).ToList();
            }
            catch { return Enumerable.Empty<FileInfo>(); }
        }

        /// <summary>Makes the ZIP (and the one with the dumps, when there are any). client: the game folder, or null.</summary>
        public static Result Make(Install inst, string client, Action<string> progress)
        {
            if (progress == null) progress = s => { };
            var result = new Result { Folder = Target(inst) };
            result.Zip = Path.Combine(result.Folder, "AFK-Realm-help-" + Stamp + ".zip");
            var summary = new StringBuilder();
            var dumps = new List<KeyValuePair<string, FileInfo>>();
            var included = new List<string>();

            using (var stream = new FileStream(result.Zip, FileMode.Create))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                Action<string, string, long> tail = (path, entry, max) =>
                {
                    try
                    {
                        using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        {
                            long start = Math.Max(0, f.Length - max);
                            f.Seek(start, SeekOrigin.Begin);
                            var e = zip.CreateEntry(entry, CompressionLevel.Optimal);
                            using (var w = e.Open())
                            {
                                if (start > 0)
                                {
                                    var note = Encoding.UTF8.GetBytes("[... the first " + (start / 1024) + " KB are left out; this is the end of the file ...]\n");
                                    w.Write(note, 0, note.Length);
                                }
                                f.CopyTo(w);
                            }
                        }
                        included.Add(entry);
                    }
                    catch { }
                };
                Action<string, string> redacted = (path, entry) =>
                {
                    try
                    {
                        string content;
                        using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (var r = new StreamReader(f)) content = r.ReadToEnd();
                        content = Secret.Replace(content, m => m.Groups[1].Value + "\"(removed)\"");
                        var e = zip.CreateEntry(entry, CompressionLevel.Optimal);
                        using (var w = new StreamWriter(e.Open(), new UTF8Encoding(false))) w.Write(content);
                        included.Add(entry);
                    }
                    catch { }
                };
                Action<string, string> text = (entry, content) =>
                {
                    var e = zip.CreateEntry(entry, CompressionLevel.Optimal);
                    using (var w = new StreamWriter(e.Open(), new UTF8Encoding(false))) w.Write(content);
                };

                summary.AppendLine(Product.Name + " " + Product.Version + " - " + L.T("help files") + " - " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                summary.AppendLine("System: " + Environment.OSVersion + (Environment.Is64BitOperatingSystem ? ", 64-bit" : ", 32-bit") + ", " + Environment.ProcessorCount + " cores");
                summary.AppendLine("Server folder: " + (inst != null ? inst.Root : "(none yet)"));
                summary.AppendLine("Game client: " + (string.IsNullOrEmpty(client) ? "(none chosen)" : client));
                foreach (var d in new[] { inst != null ? inst.Root : null, client }.Where(x => !string.IsNullOrEmpty(x)).Select(x => { try { return Path.GetPathRoot(Path.GetFullPath(x)); } catch { return x; } }).Distinct(StringComparer.OrdinalIgnoreCase))
                    try { var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(d))); summary.AppendLine("Free space on " + drive.Name + ": " + (drive.AvailableFreeSpace / 1073741824.0).ToString("0.0") + " GB"); } catch { }

                // ---- the server: build, logs, crashes, settings, database
                if (inst != null && Directory.Exists(inst.Root))
                {
                    progress(L.T("Collecting the logs of the server …"));
                    summary.AppendLine();
                    summary.AppendLine("Core: " + GitHead(inst.SourceDir));
                    string modules = Path.Combine(inst.SourceDir, "modules");
                    if (Directory.Exists(modules))
                        foreach (var m in Directory.GetDirectories(modules).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                            summary.AppendLine("Module: " + Path.GetFileName(m) + "  " + GitHead(m));

                    foreach (var f in Files(inst.LogDir, ".log", ".txt", ".out"))
                        tail(f.FullName, "install/" + f.Name, f.Name.Equals("install.log", StringComparison.OrdinalIgnoreCase) ? InstallLogTail : LogTail);
                    foreach (var f in Files(inst.Root, ".out", ".log").Take(10)) tail(f.FullName, "install/" + f.Name, LogTail);
                    foreach (var dir in new[] { inst.ServerDir, inst.BinDir }.Distinct())
                        foreach (var f in Files(dir, ".log").Take(20)) tail(f.FullName, "server/" + f.Name, LogTail);
                    foreach (var dir in new[] { inst.ServerDir, inst.BinDir }.Distinct().Select(d => Path.Combine(d, "Crashes")).Distinct())
                    {
                        foreach (var f in Files(dir, ".txt", ".log").Take(5)) tail(f.FullName, "server/crashes/" + f.Name, LogTail);
                        foreach (var f in Files(dir, ".dmp").Take(3)) dumps.Add(new KeyValuePair<string, FileInfo>("server/", f));
                    }
                    foreach (var f in Files(inst.BinDir).Where(x => x.Name.StartsWith("core", StringComparison.OrdinalIgnoreCase) && x.Length > 0).Take(2))
                        dumps.Add(new KeyValuePair<string, FileInfo>("server/", f));
                    progress(L.T("Collecting the settings …"));
                    foreach (var f in Files(inst.ConfigDir, ".conf")) redacted(f.FullName, "settings/" + f.Name);
                    foreach (var f in Files(Path.Combine(inst.ConfigDir, "modules"), ".conf")) redacted(f.FullName, "settings/modules/" + f.Name);
                    foreach (var dir in new[] { Path.Combine(inst.DbDir, "data"), inst.DbDir })
                        foreach (var f in Files(dir, ".err", ".log").Take(3)) tail(f.FullName, "database/" + f.Name, LogTail);
                }

                // ---- the game client: its logs, crash reports, settings and what it holds
                if (!string.IsNullOrEmpty(client) && Directory.Exists(client))
                {
                    progress(L.T("Collecting the logs of the game client …"));
                    foreach (var f in Files(Os.Resolve(client, "Logs"), ".log", ".txt").Take(15)) tail(f.FullName, "client/logs/" + f.Name, LogTail);
                    foreach (var f in Files(Os.Resolve(client, "Errors"), ".txt", ".log").Take(6)) tail(f.FullName, "client/crashes/" + f.Name, LogTail);
                    foreach (var f in Files(Os.Resolve(client, "Errors"), ".dmp").Take(3)) dumps.Add(new KeyValuePair<string, FileInfo>("client/", f));
                    foreach (var f in Files(client, ".log", ".txt", ".conf").Where(x => x.Length < 20000000).Take(15)) tail(f.FullName, "client/" + f.Name, LogTail);
                    foreach (var f in Files(client, ".dmp").Take(2)) dumps.Add(new KeyValuePair<string, FileInfo>("client/", f));
                    string wtf = Os.Resolve(client, "WTF/Config.wtf");
                    if (File.Exists(wtf)) redacted(wtf, "client/Config.wtf");
                    var list = new StringBuilder();
                    list.AppendLine("Files of the game folder (name, size, last change):");
                    foreach (var f in Files(client)) list.AppendLine("  " + f.Name + "  " + f.Length + "  " + f.LastWriteTime.ToString("yyyy-MM-dd HH:mm"));
                    foreach (var dir in new[] { Os.Resolve(client, "Data") }.Concat(SafeDirs(Os.Resolve(client, "Data"))))
                    {
                        list.AppendLine(dir.Substring(client.Length).TrimStart('\\', '/') + ":");
                        foreach (var f in Files(dir)) list.AppendLine("  " + f.Name + "  " + f.Length + "  " + f.LastWriteTime.ToString("yyyy-MM-dd HH:mm"));
                    }
                    list.AppendLine("Add-ons:");
                    foreach (var d in SafeDirs(Os.Resolve(client, "Interface/AddOns"))) list.AppendLine("  " + Path.GetFileName(d));
                    text("client/files.txt", list.ToString());
                }

                // ---- a failed start of AFK Realm itself (startup-error.txt in its settings folder)
                try
                {
                    string own = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Product.FileStem, "startup-error.txt");
                    if (File.Exists(own)) tail(own, "afk-realm/startup-error.txt", LogTail);
                }
                catch { }

                // ---- crash dumps Windows kept for the server programs and the game
                try
                {
                    string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    string wer = string.IsNullOrEmpty(local) ? null : Path.Combine(local, "CrashDumps");
                    var mine = new[] { "worldserver", "authserver", "ascension", "wow", Product.FileStem.ToLowerInvariant(), "mysqld" };
                    foreach (var f in Files(wer, ".dmp").Where(x => mine.Any(n => x.Name.ToLowerInvariant().StartsWith(n))).Take(4))
                        dumps.Add(new KeyValuePair<string, FileInfo>("windows/", f));
                }
                catch { }

                summary.AppendLine();
                summary.AppendLine("Included:");
                foreach (var i in included) summary.AppendLine("  " + i);
                if (dumps.Count > 0)
                {
                    summary.AppendLine();
                    summary.AppendLine("Crash dumps (in the second ZIP):");
                    foreach (var d in dumps) summary.AppendLine("  " + d.Key + d.Value.Name + "  " + d.Value.Length + "  " + d.Value.LastWriteTime.ToString("yyyy-MM-dd HH:mm"));
                }
                text("summary.txt", summary.ToString());
                result.Files = included.Count + 1;
            }
            result.ZipBytes = new FileInfo(result.Zip).Length;

            if (dumps.Count > 0)
            {
                progress(L.T("Packing the crash dumps …"));
                result.Dumps = Path.Combine(result.Folder, "AFK-Realm-help-" + Stamp + "-dumps.zip");
                long total = 0;
                using (var stream = new FileStream(result.Dumps, FileMode.Create))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
                    foreach (var d in dumps.OrderByDescending(x => x.Value.LastWriteTimeUtc))
                    {
                        if (total + d.Value.Length > MaxDumps) continue;
                        try
                        {
                            var e = zip.CreateEntry(d.Key + d.Value.Name, CompressionLevel.Optimal);
                            using (var w = e.Open())
                            using (var f = new FileStream(d.Value.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) f.CopyTo(w);
                            total += d.Value.Length;
                            result.DumpFiles++;
                        }
                        catch { }
                    }
                result.DumpBytes = new FileInfo(result.Dumps).Length;
                if (result.DumpFiles == 0) { try { File.Delete(result.Dumps); } catch { } result.Dumps = null; result.DumpBytes = 0; }
            }
            return result;
        }

        static IEnumerable<string> SafeDirs(string dir)
        {
            try { return Directory.Exists(dir) ? Directory.GetDirectories(dir).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList() : new List<string>(); }
            catch { return new List<string>(); }
        }

        /// <summary>The commit a source folder is at (from .git, without running git), or "-".</summary>
        static string GitHead(string dir)
        {
            try
            {
                string git = Path.Combine(dir, ".git");
                if (File.Exists(git))
                {
                    var m = Regex.Match(File.ReadAllText(git), @"gitdir:\s*(.+)");
                    if (m.Success) git = Path.GetFullPath(Path.Combine(dir, m.Groups[1].Value.Trim()));
                }
                string head = File.ReadAllText(Path.Combine(git, "HEAD")).Trim();
                if (!head.StartsWith("ref:")) return head.Substring(0, Math.Min(12, head.Length));
                string reference = head.Substring(4).Trim();
                string file = Path.Combine(git, reference.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(file)) return reference + " " + File.ReadAllText(file).Trim().Substring(0, 12);
                string packed = Path.Combine(git, "packed-refs");
                if (File.Exists(packed))
                    foreach (var line in File.ReadAllLines(packed))
                        if (line.EndsWith(" " + reference)) return reference + " " + line.Substring(0, 12);
                return reference;
            }
            catch { return "-"; }
        }
    }
}
