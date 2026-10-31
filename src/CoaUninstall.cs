using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CoAInstaller
{
    /// <summary>
    /// Remembers which client files the CoA setup touched, so exactly those files can be
    /// restored or removed again. Files the user already had are left alone when no backup
    /// exists; files created by the setup are removed. WTF, Cache and personal add-ons are
    /// never part of the record.
    /// </summary>
    static class CoaUninstall
    {
        const string ManifestName = ".afk-coa-install.tsv";
        const string GeneratedBackup = ".afk-coa-backup";
        static string clientRoot;
        static readonly List<Entry> entries = new List<Entry>();
        static readonly HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        sealed class Entry
        {
            public char Kind;                 // F file, P generated file with its own backup, D directory
            public string Path;
            public bool Existed;
        }

        public static bool HasManifest(string client)
        {
            try { return !string.IsNullOrEmpty(client) && File.Exists(Path.Combine(client, ManifestName)); }
            catch { return false; }
        }

        /// <summary>Starts a setup run. An existing record is kept: a later CoA update must not
        /// forget the original files from the first installation.</summary>
        public static void Begin(string client)
        {
            clientRoot = Path.GetFullPath(client).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            entries.Clear(); seen.Clear();
            string file = Path.Combine(client, ManifestName);
            try
            {
                if (File.Exists(file))
                    foreach (string line in File.ReadAllLines(file))
                    {
                        var p = line.Split('\t');
                        if (p.Length < 3 || p[0].Length != 1) continue;
                        var e = new Entry { Kind = p[0][0], Path = p[1], Existed = p[2] == "1" };
                        entries.Add(e);
                        seen.Add(e.Path);
                    }
            }
            catch { }
        }

        /// <summary>Records a file before the setup copies it. Only files below the chosen
        /// client are recorded; server DBCs and configs are a separate concern.</summary>
        public static void Capture(string target)
        {
            if (string.IsNullOrEmpty(clientRoot) || string.IsNullOrEmpty(target)) return;
            string full;
            try { full = Path.GetFullPath(target); } catch { return; }
            if (!full.StartsWith(clientRoot, StringComparison.OrdinalIgnoreCase)) return;
            if (!seen.Add(full)) return;
            entries.Add(new Entry { Kind = 'F', Path = full, Existed = File.Exists(full) || Directory.Exists(full) });
        }

        /// <summary>Records a generated file (the AFK client patch) and keeps its old version
        /// next to it if it already existed.</summary>
        public static void CaptureGenerated(string target)
        {
            if (string.IsNullOrEmpty(clientRoot) || string.IsNullOrEmpty(target)) return;
            string full;
            try { full = Path.GetFullPath(target); } catch { return; }
            if (!full.StartsWith(clientRoot, StringComparison.OrdinalIgnoreCase)) return;
            if (seen.Add(full))
                entries.Add(new Entry { Kind = 'P', Path = full, Existed = File.Exists(full) });
            if (!File.Exists(full)) return;
            string backup = full + GeneratedBackup;
            try { if (!File.Exists(backup)) File.Copy(full, backup, false); } catch { }
        }

        /// <summary>Records an add-on directory and keeps the user's previous copy beside it.</summary>
        public static void CaptureDirectory(string target)
        {
            if (string.IsNullOrEmpty(clientRoot) || string.IsNullOrEmpty(target)) return;
            string full;
            try { full = Path.GetFullPath(target); } catch { return; }
            if (!full.StartsWith(clientRoot, StringComparison.OrdinalIgnoreCase)) return;
            if (!seen.Add(full)) return;
            bool exists = Directory.Exists(full);
            entries.Add(new Entry { Kind = 'D', Path = full, Existed = exists });
            if (!exists) return;
            string backup = full + GeneratedBackup;
            try { if (!Directory.Exists(backup)) CopyFolder(full, backup); } catch { }
        }

        /// <summary>Writes the record after the setup has copied the files.</summary>
        public static void Flush()
        {
            if (string.IsNullOrEmpty(clientRoot)) return;
            try
            {
                var text = new StringBuilder();
                foreach (var e in entries)
                    text.Append(e.Kind).Append('\t').Append(e.Path).Append('\t').Append(e.Existed ? "1" : "0").Append('\n');
                File.WriteAllText(Path.Combine(clientRoot, ManifestName), text.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        /// <summary>Restores files the setup overwrote and removes files it created. Only the
        /// paths in the record are touched.</summary>
        public static string Run(string client, Action<string, int, int> progress)
        {
            if (!HasManifest(client)) throw new InvalidOperationException(L.T("No CoA installation record was found for this client. The setup can only undo installations made with a version that writes this record."));
            if (ClientAddons.ClientRunning(client))
                throw new InvalidOperationException(L.T("The game or its launcher is running. Close it before removing the CoA files."));
            if (progress == null) progress = (t, a, b) => { };

            var list = new List<Entry>();
            foreach (string line in File.ReadAllLines(Path.Combine(client, ManifestName)))
            {
                var p = line.Split('\t');
                if (p.Length >= 3 && p[0].Length == 1)
                    list.Add(new Entry { Kind = p[0][0], Path = p[1], Existed = p[2] == "1" });
            }
            int removed = 0, restored = 0, skipped = 0;
            int i = 0;
            foreach (var e in list.OrderBy(x => x.Kind == 'D' ? 1 : 0))
            {
                i++;
                progress(L.F("Removing CoA client files ({0}/{1}) ...", i, list.Count), i, list.Count);
                try
                {
                    if (e.Kind == 'D')
                    {
                        string backup = e.Path + GeneratedBackup;
                        if (e.Existed && Directory.Exists(backup))
                        {
                            if (Directory.Exists(e.Path)) Directory.Delete(e.Path, true);
                            CopyFolder(backup, e.Path);
                            TryDeleteDirectory(backup);
                            restored++;
                        }
                        else if (!e.Existed && Directory.Exists(e.Path))
                        {
                            Directory.Delete(e.Path, true);
                            removed++;
                        }
                        else skipped++;
                        continue;
                    }

                    string fileBackup = e.Kind == 'P' ? e.Path + GeneratedBackup : e.Path + ".coa-patcher-backup";
                    if (e.Existed && File.Exists(fileBackup))
                    {
                        File.Copy(fileBackup, e.Path, true);
                        if (e.Kind == 'P') try { File.Delete(fileBackup); } catch { }
                        restored++;
                    }
                    else if (!e.Existed && File.Exists(e.Path))
                    {
                        File.Delete(e.Path);
                        removed++;
                    }
                    else skipped++;
                }
                catch (Exception ex)
                {
                    progress(L.F("Could not remove {0}: {1}", e.Path, ex.Message), i, list.Count);
                    skipped++;
                }
            }

            try { File.Delete(Path.Combine(client, ManifestName)); } catch { }
            RemoveEmptyKnownFolders(client);
            return L.F("CoA client files removed: {0} deleted, {1} restored, {2} left unchanged.", removed, restored, skipped);
        }

        static void RemoveEmptyKnownFolders(string client)
        {
            foreach (string relative in new[] { "Data", "Interface\\AddOns" })
            {
                string root;
                try { root = Path.Combine(client, relative); } catch { continue; }
                if (!Directory.Exists(root)) continue;
                foreach (string dir in Directory.GetDirectories(root, "*", SearchOption.AllDirectories)
                    .OrderByDescending(x => x.Length))
                    try { if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); } catch { }
            }
        }

        static void TryDeleteDirectory(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
        }

        static void CopyFolder(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string file in Directory.GetFiles(from))
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
            foreach (string dir in Directory.GetDirectories(from))
                CopyFolder(dir, Path.Combine(to, Path.GetFileName(dir)));
        }
    }
}
