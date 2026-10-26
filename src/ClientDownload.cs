using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace CoAInstaller
{
    /// <summary>
    /// Downloads and updates the game client from the public CoA development launcher.
    ///
    /// The launcher publishes a manifest of every client file with its size and SHA-256. Files are fetched one by one
    /// from a content-addressed object store, verified, and only then moved into place. A .part file makes interrupted
    /// downloads resumable. A small state file in the client remembers what was downloaded, so a later update only
    /// reads/hashes files that changed. WTF, Cache, add-ons, logs and the realmlist are never written.
    /// </summary>
    static class ClientDownload
    {
        public const string ManifestUrl = "https://launcher-api.coa-development.org/downloads/client/latest.json";
        public const string ObjectsUrl = "https://launcher-api.coa-development.org/downloads/client/objects";
        const string StateFile = ".afk-client-state";
        const string PartsDir = ".afk-client-parts";
        const long MaxManifestBytes = 16L * 1024 * 1024;
        const long MaxFileBytes = 16L * 1024 * 1024 * 1024;

        public sealed class FileEntry
        {
            public string Path;
            public long Size;
            public string Sha256;
        }

        public sealed class Manifest
        {
            public string Version;
            public string PublishedAt;
            public List<FileEntry> Files = new List<FileEntry>();
            public long TotalBytes { get { return Files.Sum(f => f.Size); } }
        }

        public enum Kind { Have, Missing, Changed, Modified, Kept }

        public sealed class Item
        {
            public FileEntry File;
            public Kind Kind;
        }

        public sealed class Plan
        {
            public string Version;
            public List<Item> Items = new List<Item>();
            public long TotalBytes, NeedBytes;
            public int Have, Missing, Changed, Modified, Kept;
            public int NeedCount { get { return Missing + Changed + Modified; } }
        }

        public sealed class Result
        {
            public string Version;
            public int Downloaded, Kept, AlreadyPresent;
            public long Bytes;
        }

        public delegate void Progress(string phase, long done, long total, string file);

        sealed class StateEntry
        {
            public long Size;
            public long Mtime;
            public string Sha256;
        }

        // ------------------------------------------------------------------------------------ manifest

        static string GetText(string url, long max, int timeout = 30000)
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = Product.FileStem;
            req.Timeout = timeout;
            req.ReadWriteTimeout = timeout;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var stream = resp.GetResponseStream())
            using (var memory = new MemoryStream())
            {
                var buffer = new byte[64 * 1024];
                long total = 0;
                int n;
                while ((n = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    total += n;
                    if (total > max) throw new InvalidOperationException(L.T("The client list is too large."));
                    memory.Write(buffer, 0, n);
                }
                return Encoding.UTF8.GetString(memory.ToArray());
            }
        }

        static Manifest cachedManifest;
        static DateTime cachedManifestAt;

        /// <summary>The current client manifest, cached for an hour so the page buttons do not ask GitHub/CoA on
        /// every redraw.</summary>
        public static Manifest Latest(bool refresh = false)
        {
            lock (typeof(ClientDownload))
            {
                if (!refresh && cachedManifest != null && DateTime.UtcNow - cachedManifestAt < TimeSpan.FromHours(1))
                    return cachedManifest;
            }
            var manifest = FetchManifest();
            lock (typeof(ClientDownload)) { cachedManifest = manifest; cachedManifestAt = DateTime.UtcNow; }
            return manifest;
        }

        /// <summary>The manifest version a managed folder was brought to, or null.</summary>
        public static string InstalledVersion(string client)
        {
            try
            {
                string file = StatePath(client);
                if (!File.Exists(file)) return null;
                foreach (var line in File.ReadAllLines(file))
                    if (line.StartsWith("# version\t", StringComparison.OrdinalIgnoreCase))
                    {
                        string version = line.Substring("# version\t".Length).Trim();
                        return version.Length > 0 ? version : null;
                    }
            }
            catch { }
            return null;
        }

        public static Manifest FetchManifest()
        {
            string text = GetText(ManifestUrl, MaxManifestBytes);
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var root = serializer.DeserializeObject(text) as Dictionary<string, object>;
            if (root == null) throw new InvalidOperationException(L.T("The client list could not be read."));
            object v;
            int schema;
            if (!root.TryGetValue("schema", out v) || !int.TryParse(Convert.ToString(v), out schema) || schema != 1)
                throw new InvalidOperationException(L.T("The client list uses an unknown format. Update AFK Realm and try again."));
            var manifest = new Manifest
            {
                Version = root.TryGetValue("version", out v) ? Convert.ToString(v) : "",
                PublishedAt = root.TryGetValue("publishedAt", out v) ? Convert.ToString(v) : ""
            };
            if (string.IsNullOrEmpty(manifest.Version) || manifest.Version.Length > 64)
                throw new InvalidOperationException(L.T("The client list has no version."));
            object files;
            if (!root.TryGetValue("files", out files) || !(files is object[]))
                throw new InvalidOperationException(L.T("The client list has no files."));
            foreach (var raw in (object[])files)
            {
                var d = raw as Dictionary<string, object>;
                if (d == null) continue;
                string path = d.TryGetValue("path", out v) ? Convert.ToString(v) : "";
                path = NormalizeRelative(path);
                if (path.Length == 0 || Excluded(path)) continue;
                long size;
                if (!d.TryGetValue("size", out v) || !long.TryParse(Convert.ToString(v), out size) || size < 0 || size > MaxFileBytes)
                    throw new InvalidOperationException(L.F("The client list has a bad size for {0}.", path));
                string sha = d.TryGetValue("sha256", out v) ? Convert.ToString(v) : "";
                if (!IsSha256(sha)) throw new InvalidOperationException(L.F("The client list has a bad checksum for {0}.", path));
                manifest.Files.Add(new FileEntry { Path = path, Size = size, Sha256 = sha.ToLowerInvariant() });
            }
            if (manifest.Files.Count == 0) throw new InvalidOperationException(L.T("The client list is empty."));
            return manifest;
        }

        // ------------------------------------------------------------------------------------ paths and state

        static bool IsSha256(string s)
        {
            return s != null && s.Length == 64 && s.All(c => Uri.IsHexDigit(c));
        }

        static string NormalizeRelative(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            path = path.Replace('\\', '/').Trim();
            if (Path.IsPathRooted(path) || path.IndexOf(':') >= 0) return "";
            var parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "";
            foreach (var part in parts)
                if (part == "." || part == ".." || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return "";
            return string.Join("/", parts);
        }

        static string FullPath(string client, string relative)
        {
            string root = Path.GetFullPath(client).TrimEnd('\\') + "\\";
            string full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', '\\')));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(L.F("The client list contains a path outside the game folder: {0}", relative));
            return full;
        }

        /// <summary>Never touched by an update: the player's settings, cache, add-ons and logs.</summary>
        static bool Excluded(string path)
        {
            string p = path.Replace('\\', '/').ToLowerInvariant();
            return p.EndsWith(".log") || p == "realmlist.wtf" || p.StartsWith("wtf/") || p.StartsWith("cache/") ||
                p.StartsWith("errors/") || p.StartsWith("logs/") || p.StartsWith("interface/addons/") ||
                p.StartsWith(PartsDir + "/") || p == StateFile;
        }

        /// <summary>Graphics wrappers the player may have chosen themselves. Created when missing, never replaced.</summary>
        static bool PlayerOwned(string path)
        {
            string name = path.Replace('\\', '/').Split('/').Last().ToLowerInvariant();
            return name == "d3d8.dll" || name == "d3d9.dll" || name == "d3d10core.dll" || name == "d3d11.dll" ||
                name == "dxgi.dll" || name == "dxvk.conf" || name.EndsWith(".ini") || name.EndsWith(".conf") || name.EndsWith(".wtf");
        }

        static string StatePath(string client) { return Path.Combine(client, StateFile); }
        static string PartsPath(string client) { return Path.Combine(client, PartsDir); }

        static Dictionary<string, StateEntry> ReadState(string client)
        {
            var result = new Dictionary<string, StateEntry>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string file = StatePath(client);
                if (!File.Exists(file)) return result;
                foreach (var line in File.ReadAllLines(file))
                {
                    if (line.StartsWith("#") || line.Length == 0) continue;
                    string[] f = line.Split('\t');
                    long size, mtime;
                    if (f.Length != 4 || !long.TryParse(f[1], out size) || !long.TryParse(f[2], out mtime) || !IsSha256(f[3])) continue;
                    result[f[0].Replace('\\', '/')] = new StateEntry { Size = size, Mtime = mtime, Sha256 = f[3].ToLowerInvariant() };
                }
            }
            catch { }
            return result;
        }

        static void SaveState(string client, Dictionary<string, StateEntry> state, string version)
        {
            try
            {
                Directory.CreateDirectory(client);
                string file = StatePath(client), temp = file + ".tmp";
                var lines = new List<string> { "# " + Product.Name + " client state", "# version\t" + (version ?? "") };
                foreach (var pair in state.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
                    lines.Add(pair.Key + "\t" + pair.Value.Size + "\t" + pair.Value.Mtime + "\t" + pair.Value.Sha256);
                File.WriteAllLines(temp, lines, new UTF8Encoding(false));
                if (File.Exists(file)) File.Delete(file);
                File.Move(temp, file);
            }
            catch { }
        }

        static long Mtime(string path)
        {
            try
            {
                var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                return (File.GetLastWriteTimeUtc(path).Ticks - epoch.Ticks) / TimeSpan.TicksPerSecond;
            }
            catch { return 0; }
        }

        static string Hash(string path, Func<bool> cancelled)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                var buffer = new byte[1024 * 1024];
                int n;
                while ((n = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (cancelled != null && cancelled()) throw new OperationCanceledException();
                    sha.TransformBlock(buffer, 0, n, null, 0);
                }
                sha.TransformFinalBlock(new byte[0], 0, 0);
                return BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
            }
        }

        // ------------------------------------------------------------------------------------ plan

        /// <summary>Compares the folder with the manifest. Returns what is present, missing or changed.</summary>
        public static Plan Inspect(string client, Manifest manifest, Progress progress, Func<bool> cancelled)
        {
            var state = ReadState(client);
            var plan = new Plan { Version = manifest.Version, TotalBytes = manifest.TotalBytes };
            long done = 0;
            foreach (var file in manifest.Files)
            {
                if (cancelled != null && cancelled()) throw new OperationCanceledException();
                string path = FullPath(client, file.Path);
                Item item;
                FileInfo info = new FileInfo(path);
                if (!info.Exists)
                {
                    item = new Item { File = file, Kind = Kind.Missing };
                    plan.Missing++;
                }
                else if (PlayerOwned(file.Path))
                {
                    item = new Item { File = file, Kind = Kind.Kept };
                    plan.Kept++;
                }
                else if (info.Length != file.Size)
                {
                    item = new Item { File = file, Kind = Kind.Changed };
                    plan.Changed++;
                }
                else
                {
                    StateEntry entry;
                    bool trusted = state.TryGetValue(file.Path, out entry) && entry.Size == info.Length && entry.Mtime == Mtime(path) &&
                        entry.Sha256.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase);
                    string actual = trusted ? file.Sha256 : Hash(path, cancelled);
                    if (actual.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        item = new Item { File = file, Kind = Kind.Have };
                        plan.Have++;
                        state[file.Path] = new StateEntry { Size = info.Length, Mtime = Mtime(path), Sha256 = file.Sha256 };
                    }
                    else
                    {
                        item = new Item { File = file, Kind = Kind.Modified };
                        plan.Modified++;
                    }
                }
                if (item.Kind == Kind.Missing || item.Kind == Kind.Changed || item.Kind == Kind.Modified)
                    plan.NeedBytes += file.Size;
                plan.Items.Add(item);
                done += file.Size;
                if (progress != null) progress(L.T("Checking your game ..."), done, plan.TotalBytes, file.Path);
            }
            SaveState(client, state, null);
            return plan;
        }

        // ------------------------------------------------------------------------------------ download

        static void DownloadOne(string client, FileEntry file, long totalDone, long total, Progress progress, Func<bool> cancelled)
        {
            string target = FullPath(client, file.Path);
            string parts = PartsPath(client);
            Directory.CreateDirectory(parts);
            string part = Path.Combine(parts, file.Sha256 + ".part");
            Directory.CreateDirectory(Path.GetDirectoryName(target));

            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (cancelled != null && cancelled()) throw new OperationCanceledException();
                long have = File.Exists(part) ? new FileInfo(part).Length : 0;
                if (have > file.Size)
                {
                    File.Delete(part);
                    have = 0;
                }
                using (var sha = SHA256.Create())
                {
                    if (have > 0)
                    {
                        using (var read = File.OpenRead(part))
                        {
                            var buffer = new byte[1024 * 1024];
                            int n;
                            while ((n = read.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                if (cancelled != null && cancelled()) throw new OperationCanceledException();
                                sha.TransformBlock(buffer, 0, n, null, 0);
                            }
                        }
                    }
                    if (have == file.Size)
                    {
                        sha.TransformFinalBlock(new byte[0], 0, 0);
                        string actual = BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
                        if (actual.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                        {
                            Place(part, target);
                            return;
                        }
                        File.Delete(part);
                        have = 0;
                    }

                    bool restart = false;
                    try
                    {
                        try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }
                        var req = (HttpWebRequest)WebRequest.Create(ObjectsUrl + "/" + file.Sha256);
                        req.UserAgent = Product.FileStem;
                        req.Timeout = 30000;
                        req.ReadWriteTimeout = 60000;
                        if (have > 0) req.AddRange(have);
                        using (var resp = (HttpWebResponse)req.GetResponse())
                        {
                            if (have > 0 && resp.StatusCode == HttpStatusCode.OK)
                                restart = true;
                            else if (resp.StatusCode != HttpStatusCode.OK && resp.StatusCode != HttpStatusCode.PartialContent)
                                throw new InvalidOperationException(L.F("The client download server answered {0}.", (int)resp.StatusCode));
                            if (!restart)
                            {
                                if (have > 0)
                                {
                                    string range = resp.Headers["Content-Range"];
                                    if (string.IsNullOrEmpty(range) || !range.StartsWith("bytes " + have + "-", StringComparison.OrdinalIgnoreCase))
                                        restart = true;
                                }
                                if (!restart)
                                {
                                    using (var stream = resp.GetResponseStream())
                                    using (var fileStream = new FileStream(part, FileMode.Append, FileAccess.Write, FileShare.Read))
                                    {
                                        var buffer = new byte[256 * 1024];
                                        long last = 0, now = have;
                                        int n;
                                        while ((n = stream.Read(buffer, 0, buffer.Length)) > 0)
                                        {
                                            if (cancelled != null && cancelled()) throw new OperationCanceledException();
                                            if (now + n > file.Size) throw new InvalidOperationException(L.F("{0} is larger than the client list says.", file.Path));
                                            fileStream.Write(buffer, 0, n);
                                            sha.TransformBlock(buffer, 0, n, null, 0);
                                            now += n;
                                            if (progress != null && now - last >= 1024 * 1024)
                                            {
                                                last = now;
                                                progress(L.F("Downloading {0} ...", file.Path), totalDone + now, total, file.Path);
                                            }
                                        }
                                        if (now < file.Size)
                                            throw new IOException(L.F("The connection ended after {0} of {1} bytes.", now, file.Size));
                                        if (progress != null) progress(L.F("Downloading {0} ...", file.Path), totalDone + now, total, file.Path);
                                        fileStream.Flush();
                                    }
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception)
                    {
                        if (attempt == 2) throw;
                        Thread.Sleep(1000 * (attempt + 1));
                        if (progress != null) progress(L.F("Retrying {0} ...", file.Path), totalDone + have, total, file.Path);
                        continue;
                    }
                    if (restart)
                    {
                        try { File.Delete(part); } catch { }
                        continue;
                    }
                    sha.TransformFinalBlock(new byte[0], 0, 0);
                    string finished = BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
                    if (!finished.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        try { File.Delete(part); } catch { }
                        if (attempt == 2) throw new InvalidOperationException(L.F("The checksum of {0} does not match. Nothing was changed.", file.Path));
                        continue;
                    }
                    Place(part, target);
                    return;
                }
            }
        }

        static void Place(string part, string target)
        {
            try
            {
                if (File.Exists(target))
                {
                    string backup = target + ".afk-backup";
                    try { File.Copy(target, backup, true); } catch { }
                    File.Delete(target);
                }
                File.Move(part, target);
            }
            catch (Exception ex)
            {
                throw new IOException(L.F("Could not put {0} into the game folder: {1}", target, ex.Message), ex);
            }
        }

        /// <summary>Runs the plan and downloads every missing/changed file. Throws OperationCanceledException when cancelled.</summary>
        public static Result Update(string client, Manifest manifest, Progress progress, Func<bool> cancelled)
        {
            var plan = Inspect(client, manifest, progress, cancelled);
            var state = ReadState(client);
            var result = new Result { Version = manifest.Version, Kept = plan.Kept, AlreadyPresent = plan.Have };
            long total = Math.Max(1, plan.NeedBytes);
            long done = 0;
            try
            {
                foreach (var item in plan.Items.Where(i => i.Kind == Kind.Missing || i.Kind == Kind.Changed || i.Kind == Kind.Modified))
                {
                    if (cancelled != null && cancelled()) throw new OperationCanceledException();
                    DownloadOne(client, item.File, done, total, progress, cancelled);
                    done += item.File.Size;
                    result.Downloaded++;
                    result.Bytes += item.File.Size;
                    string target = FullPath(client, item.File.Path);
                    state[item.File.Path] = new StateEntry { Size = item.File.Size, Mtime = Mtime(target), Sha256 = item.File.Sha256 };
                    SaveState(client, state, null);
                }
                SaveState(client, state, manifest.Version);
                return result;
            }
            catch (OperationCanceledException)
            {
                SaveState(client, state, null);
                throw;
            }
        }

        public static long FreeSpace(string client)
        {
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(client));
                if (string.IsNullOrEmpty(root)) return 0;
                return new DriveInfo(root).AvailableFreeSpace;
            }
            catch { return 0; }
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024) return (bytes / (1024.0 * 1024 * 1024)).ToString("0.0", CultureInfo.InvariantCulture) + " GB";
            if (bytes >= 1024L * 1024) return (bytes / (1024.0 * 1024)).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
            if (bytes >= 1024) return (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB";
            return bytes + " B";
        }
    }
}
