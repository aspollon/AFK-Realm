using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace CoAInstaller
{
    /// <summary>
    /// The CoA Custom client/server package from ilusixn. The release is a set of ZIP files that must all be
    /// extracted into the same folder. This installs all ZIPs of the newest coa-custom release (client and server),
    /// verifies each GitHub asset, and writes a small version marker for the race importer and its update notice.
    /// </summary>
    static class CoaCustom
    {
        public const string Repo = "ilusixn/CustomRacesCoa";
        const string VersionFile = ".afk-coa-custom-version";
        static readonly object cacheLock = new object();
        static Release cached;
        static DateTime cachedAt;

        public sealed class Asset
        {
            public string Name, Url, Sha256;
            public long Size;
        }

        public sealed class Release
        {
            public string Tag;
            public List<Asset> Assets = new List<Asset>();
            public string Version { get { return Tag.StartsWith("coa-custom-", StringComparison.OrdinalIgnoreCase) ? Tag.Substring("coa-custom-".Length) : Tag; } }
        }

        static string Get(string url, int timeout = 30000)
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = Product.FileStem;
            string token = Settings.CoaGitHubToken;
            if (!string.IsNullOrEmpty(token)) req.Headers["Authorization"] = "token " + token;
            req.Timeout = timeout;
            req.ReadWriteTimeout = timeout;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var stream = resp.GetResponseStream())
            using (var memory = new MemoryStream())
            {
                var buffer = new byte[64 * 1024];
                int n;
                while ((n = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (memory.Length + n > 4 * 1024 * 1024) throw new InvalidOperationException(L.T("The release list is too large."));
                    memory.Write(buffer, 0, n);
                }
                return Encoding.UTF8.GetString(memory.ToArray());
            }
        }

        /// <summary>The newest coa-custom release, or the known 1.9.1 package when GitHub cannot be reached.</summary>
        public static Release Latest(bool refresh = false)
        {
            lock (cacheLock)
            {
                if (!refresh && cached != null && DateTime.UtcNow - cachedAt < TimeSpan.FromHours(1)) return cached;
            }
            if (string.IsNullOrEmpty(Settings.CoaGitHubToken))
                throw new InvalidOperationException(L.T("A GitHub key for CoA Custom is required before downloading."));
            Release found = ParseRelease(Get("https://api.github.com/repos/" + Repo + "/releases?per_page=20"));
            if (found == null)
                throw new InvalidOperationException(L.T("No downloadable CoA Custom release was found in the private repository."));
            lock (cacheLock) { cached = found; cachedAt = DateTime.UtcNow; }
            return found;
        }

        static Release ParseRelease(string json)
        {
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var releases = serializer.DeserializeObject(json) as object[];
            if (releases == null) return null;
            foreach (var raw in releases)
            {
                var rel = raw as Dictionary<string, object>;
                if (rel == null) continue;
                object v;
                string tag = rel.TryGetValue("tag_name", out v) ? Convert.ToString(v) : "";
                if (!tag.StartsWith("coa-custom-", StringComparison.OrdinalIgnoreCase)) continue;
                bool draft = rel.TryGetValue("draft", out v) && v is bool && (bool)v;
                if (draft) continue;
                var release = new Release { Tag = tag };
                object assets;
                if (rel.TryGetValue("assets", out assets) && assets is object[])
                    foreach (var a in (object[])assets)
                    {
                        var asset = a as Dictionary<string, object>;
                        if (asset == null) continue;
                        string name = asset.TryGetValue("name", out v) ? Convert.ToString(v) : "";
                        if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                        long size;
                        if (!asset.TryGetValue("size", out v) || !long.TryParse(Convert.ToString(v), out size)) continue;
                        // Use the API asset URL: the browser download URL of a private
                        // release returns 404 even with a token.
                        string url = asset.TryGetValue("url", out v) ? Convert.ToString(v) : "";
                        if (string.IsNullOrEmpty(url)) url = asset.TryGetValue("browser_download_url", out v) ? Convert.ToString(v) : "";
                        string digest = asset.TryGetValue("digest", out v) ? Convert.ToString(v) : "";
                        if (digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) digest = digest.Substring(7);
                        release.Assets.Add(new Asset { Name = name, Url = url, Size = size, Sha256 = digest });
                    }
                if (release.Assets.Count > 0) return release;
            }
            return null;
        }

        static string InstalledRecord(Install inst) { return Path.Combine(inst.Root, "Dependencies", "coa-custom-installed.txt"); }

        /// <summary>Written after "CoA Custom > Install / update" copied a package into this server's client.</summary>
        public static void RecordInstalled(Install inst, string version)
        {
            if (inst == null || string.IsNullOrEmpty(version)) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(InstalledRecord(inst)));
                File.WriteAllText(InstalledRecord(inst), version.Trim(), new UTF8Encoding(false));
            }
            catch { }
        }

        /// <summary>
        /// The CoA Custom version this server has, for the update notice: the newest of the version the setup last
        /// installed, the version AFK Realm downloaded into the race folder, and the package chosen on the setup page.
        /// Only the download was counted before, so a package downloaded by hand (or into another folder) kept the
        /// notice up after it was installed ("1.9.6 available, installed 1.9.2").
        /// </summary>
        public static string Installed(Install inst, string raceFolder)
        {
            var found = new List<string>();
            try { if (inst != null && File.Exists(InstalledRecord(inst))) found.Add(File.ReadAllText(InstalledRecord(inst)).Trim()); } catch { }
            found.Add(Installed(raceFolder));
            try
            {
                string package = Settings.CoaPackage;
                if (!string.IsNullOrEmpty(package)) found.Add(CoaSetup.ReadVersion(Path.Combine(package, "manifest.json")));
            }
            catch { }
            string best = null;
            foreach (string v in found)
                if (!string.IsNullOrEmpty(v) && (best == null || UpdateCheck.IsNewer(v, best))) best = v;
            return best;
        }

        /// <summary>A newer release than the installed version: "1.9.6-x64" is the same as "1.9.6".</summary>
        public static bool IsUpdate(string latest, string installed)
        {
            return !string.IsNullOrEmpty(latest) && !string.IsNullOrEmpty(installed) && UpdateCheck.IsNewer(latest, installed);
        }

        /// <summary>The version previously downloaded into a race folder, or null.</summary>
        public static string Installed(string raceFolder)
        {
            try
            {
                string file = Path.Combine(raceFolder ?? "", VersionFile);
                if (File.Exists(file)) return File.ReadAllText(file).Trim();
            }
            catch { }
            return null;
        }

        /// <summary>Where the CoA-Custom\files tree is below the chosen race folder.</summary>
        public static string PackageRoot(string raceFolder)
        {
            try
            {
                foreach (var dir in Directory.GetDirectories(raceFolder, "CoA-Custom", SearchOption.TopDirectoryOnly))
                    if (Directory.Exists(Path.Combine(dir, "files"))) return dir;
            }
            catch { }
            return null;
        }

        // ------------------------------------------------------------------------------------ download and extract

        static void DownloadAsset(Asset asset, string target, Action<string, int, int> report, int index, int count)
        {
            string part = target + ".part";
            long have = File.Exists(part) ? new FileInfo(part).Length : 0;
            if (asset.Size > 0 && have > asset.Size) { File.Delete(part); have = 0; }
            if (asset.Size > 0 && have == asset.Size)
            {
                bool good = true;
                if (!string.IsNullOrEmpty(asset.Sha256))
                {
                    string actual;
                    using (var sha = SHA256.Create())
                    using (var stream = File.OpenRead(part))
                        actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                    good = actual.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase);
                }
                if (good)
                {
                    if (File.Exists(target)) File.Delete(target);
                    File.Move(part, target);
                    return;
                }
                File.Delete(part);
                have = 0;
            }
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }
                    var req = (HttpWebRequest)WebRequest.Create(asset.Url);
                    req.UserAgent = Product.FileStem;
                    string downloadToken = Settings.CoaGitHubToken;
                    if (!string.IsNullOrEmpty(downloadToken)) req.Headers["Authorization"] = "token " + downloadToken;
                    req.Accept = "application/octet-stream";
                    req.Timeout = 30000;
                    req.ReadWriteTimeout = 60000;
                    if (have > 0) req.AddRange(have);
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    {
                        if (have > 0 && resp.StatusCode == HttpStatusCode.OK) { File.Delete(part); have = 0; }
                        if (resp.StatusCode != HttpStatusCode.OK && resp.StatusCode != HttpStatusCode.PartialContent)
                            throw new InvalidOperationException(L.F("The download server answered {0}.", (int)resp.StatusCode));
                        using (var src = resp.GetResponseStream())
                        using (var dst = new FileStream(part, FileMode.Append, FileAccess.Write, FileShare.Read))
                        {
                            var buffer = new byte[256 * 1024];
                            long done = have;
                            int n;
                            while ((n = src.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                dst.Write(buffer, 0, n);
                                done += n;
                                if (report != null) report(L.F("Downloading CoA Custom {0} ({1}/{2}) ...", asset.Name, index + 1, count), (int)Math.Min(int.MaxValue, done), asset.Size > int.MaxValue ? 0 : (int)asset.Size);
                            }
                        }
                    }
                    if (!string.IsNullOrEmpty(asset.Sha256))
                    {
                        string actual;
                        using (var sha = SHA256.Create())
                        using (var stream = File.OpenRead(part))
                            actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                        if (!actual.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
                        {
                            File.Delete(part);
                            if (attempt == 2) throw new InvalidOperationException(L.F("The checksum of {0} does not match.", asset.Name));
                            have = 0;
                            continue;
                        }
                    }
                    if (File.Exists(target)) File.Delete(target);
                    File.Move(part, target);
                    return;
                }
                catch (Exception)
                {
                    if (attempt == 2) throw;
                    have = File.Exists(part) ? new FileInfo(part).Length : 0;
                }
            }
        }

        /// <summary>Downloads one release ZIP whole, with resume and checksum (the fallback of CoaDelta).</summary>
        internal static void DownloadAssetTo(Asset asset, string target, Action<string, int, int> report, int index, int count)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            DownloadAsset(asset, target, report, index, count);
        }

        /// <summary>Brings raceFolder up to the newest release: only the files that differ from the folder are downloaded
        /// (CoaDelta), whole ZIPs only where most of one is needed. Returns the version.</summary>
        public static string DownloadAndExtract(string raceFolder, Action<string, int, int> report)
        {
            var release = Latest(true);
            if (release.Assets.Count == 0) throw new InvalidOperationException(L.T("No CoA Custom ZIP files were found."));
            Directory.CreateDirectory(raceFolder);
            string downloads = Path.Combine(raceFolder, ".coa-custom-downloads");
            Directory.CreateDirectory(downloads);
            string marker = Path.Combine(raceFolder, VersionFile);
            // the folder is only "this version" again once every file is in place
            try { if (File.Exists(marker)) File.Delete(marker); } catch { }
            int written = CoaDelta.Update(release, raceFolder, downloads, report);
            File.WriteAllText(marker, release.Version, new UTF8Encoding(false));
            if (report != null) report(L.F("CoA Custom {0}: {1} files updated, the rest was already up to date.", release.Version, written), 1, 1);
            try { if (!Directory.EnumerateFileSystemEntries(downloads).Any()) Directory.Delete(downloads); } catch { }
            return release.Version;
        }
    }
}
