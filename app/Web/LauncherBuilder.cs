using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace CoAInstaller.Web
{
    /// <summary>
    /// Builds the player-side AFK Client package: a manifest of the files the working client already
    /// has (add-ons, the generated client patch, root files and race archives) plus the launcher config.
    /// The player client reads the manifest and downloads only files whose SHA-256 differs.
    /// </summary>
    static class LauncherBuilder
    {
        public const string ClientAssetName = "AFK-Client.exe";
        const string ClientReleaseUrl = "https://github.com/aspollon/AFK-Realm/releases/latest/download/" + ClientAssetName;
        static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        sealed class FileEntry
        {
            public string Path { get; set; } = "";
            public string Entry { get; set; } = "";
            public long Size { get; set; }
            public string Sha256 { get; set; } = "";
        }

        sealed class ManifestFile
        {
            public string Product { get; set; } = "AFK Client";
            public string Realm { get; set; } = "";
            public string RealmAddress { get; set; } = "";
            public string Version { get; set; } = "";
            public string BuiltAt { get; set; } = "";
            public List<FileEntry> Files { get; set; } = new List<FileEntry>();
        }

        public sealed class LauncherConfig
        {
            public string Product { get; set; } = "AFK Client";
            public string Version { get; set; } = "";
            public string Realm { get; set; } = "";
            public string RealmAddress { get; set; } = "";
            public string PortalUrl { get; set; } = "";
            public string ManifestUrl { get; set; } = "";
            public string ClientDownloadUrl { get; set; } = RegistrationPortal.DefaultClientUrl;
            public string BuiltAt { get; set; } = "";
        }

        static string Folder(Install inst) { return Path.Combine(inst.Root, "Dependencies", "launcher"); }
        static string ManifestPath(Install inst) { return Path.Combine(Folder(inst), "manifest.json"); }
        static string ConfigPath(Install inst) { return Path.Combine(Folder(inst), "launcher.json"); }
        static string PackagePath(Install inst) { return Path.Combine(Folder(inst), "AFK-Client.zip"); }
        public static string ClientExePath(Install inst) { return Path.Combine(Folder(inst), ClientAssetName); }
        public static bool PackageAvailable(Install inst) { return File.Exists(PackagePath(inst)); }

        public static object Status(Install inst)
        {
            var m = Read(inst);
            string pack = RegistrationPortal.ClientPackFile(inst);
            bool packExists = File.Exists(pack);
            bool package = File.Exists(PackagePath(inst));
            bool client = File.Exists(ClientExePath(inst));
            string baseUrl = "";
            try { baseUrl = RegistrationPortal.PublicBaseUrl(inst); } catch { }
            long bytes = m == null ? 0 : m.Files.Sum(f => f.Size);
            return new
            {
                built = m != null,
                stale = m != null && packExists && File.GetLastWriteTimeUtc(pack) > File.GetLastWriteTimeUtc(ManifestPath(inst)),
                version = m?.Version ?? "",
                builtAt = m?.BuiltAt ?? "",
                files = m?.Files.Count ?? 0,
                bytes,
                realm = m?.Realm ?? "",
                realmAddress = m?.RealmAddress ?? "",
                portalUrl = baseUrl,
                manifestUrl = baseUrl.Length > 0 ? baseUrl + "/api/public/manifest" : "",
                package,
                packageSize = package ? new FileInfo(PackagePath(inst)).Length : 0,
                packageAt = package ? File.GetLastWriteTimeUtc(PackagePath(inst)).ToString("yyyy-MM-dd HH:mm 'UTC'") : "",
                clientExe = client,
                clientExeSize = client ? new FileInfo(ClientExePath(inst)).Length : 0,
                clientExeAt = client ? File.GetLastWriteTimeUtc(ClientExePath(inst)).ToString("yyyy-MM-dd HH:mm 'UTC'") : "",
                clientExeUrl = ClientReleaseUrl
            };
        }

        static ManifestFile Read(Install inst)
        {
            try
            {
                string file = ManifestPath(inst);
                if (!File.Exists(file)) return null;
                return JsonSerializer.Deserialize<ManifestFile>(File.ReadAllText(file, Encoding.UTF8), Json);
            }
            catch { return null; }
        }

        /// <summary>Builds the player pack (if needed), hashes every file and writes the manifest and launcher config.</summary>
        public static void Build(Install inst, Action<string, int, int> progress)
        {
            if (progress == null) progress = (t, a, b) => { };
            string client = ClientAddons.ClientFolder;
            if (string.IsNullOrWhiteSpace(client) || !ClientAddons.IsClientFolder(client))
                throw new InvalidOperationException(L.T("Choose your game folder under Client build first. The client pack is built from your own client."));
            if (ClientAddons.ClientRunning(client))
                throw new InvalidOperationException(L.T("The game or its launcher is running from this folder. Close it before building the player client pack."));

            try { RegistrationPortal.EnsureDefaults(inst); } catch { }
            Directory.CreateDirectory(Folder(inst));
            progress(L.T("Building the player client files ..."), 0, 0);
            RegistrationPortal.BuildClientPack(inst, progress);
            string pack = RegistrationPortal.ClientPackFile(inst);
            if (!File.Exists(pack)) throw new InvalidOperationException(L.T("The player client pack could not be built."));

            string realm = "";
            try { realm = Accounts.GetRealmName(inst); } catch { }
            string address = RegistrationPortal.PublicRealmAddress(inst);
            var manifest = new ManifestFile
            {
                Realm = realm, RealmAddress = address,
                BuiltAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'")
            };
            var hashes = new StringBuilder();
            int count;
            using (var zip = ZipFile.OpenRead(pack))
            {
                var entries = zip.Entries.Where(e => !Skip(e.FullName)).ToList();
                count = entries.Count;
                int done = 0;
                foreach (var entry in entries)
                {
                    progress(L.F("Checking {0} ...", entry.FullName), done++, count);
                    string target = entry.FullName.Replace('\\', '/').TrimStart('/');
                    if (!SafePath(target) || entry.Length == 0) continue;
                    string hash;
                    using (var stream = entry.Open())
                    using (var sha = SHA256.Create())
                        hash = Convert.ToHexString(sha.ComputeHash(stream));
                    manifest.Files.Add(new FileEntry { Path = target, Entry = entry.FullName, Size = entry.Length, Sha256 = hash });
                    hashes.Append(target).Append('|').Append(hash).Append('\n');
                }
            }
            if (manifest.Files.Count == 0)
                throw new InvalidOperationException(L.T("None of the installed modules brings an add-on or a client patch for the game client."));
            manifest.Version = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" +
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashes.ToString()))).Substring(0, 8).ToLowerInvariant();
            File.WriteAllText(ManifestPath(inst), JsonSerializer.Serialize(manifest, Json), new UTF8Encoding(false));

            string baseUrl = RegistrationPortal.PublicBaseUrl(inst);
            var config = new LauncherConfig
            {
                Version = manifest.Version, Realm = realm, RealmAddress = address,
                PortalUrl = baseUrl,
                ManifestUrl = baseUrl.Length > 0 ? baseUrl + "/api/public/manifest" : "",
                ClientDownloadUrl = ClientUrl(inst),
                BuiltAt = manifest.BuiltAt
            };
            File.WriteAllText(ConfigPath(inst), JsonSerializer.Serialize(config, Json), new UTF8Encoding(false));

            progress(L.T("Building the AFK Client package ..."), 0, 0);
            EnsureClientExe(inst, progress);
            BuildPackage(inst, baseUrl, manifest);
        }

        static string ClientUrl(Install inst)
        {
            try
            {
                string url = (RegistrationPortal.Load(inst).ClientUrl ?? "").Trim();
                if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    return url;
            }
            catch { }
            return RegistrationPortal.DefaultClientUrl;
        }

        static bool Skip(string path)
        {
            string name = (path ?? "").Replace('\\', '/').TrimStart('/');
            if (name.EndsWith("/")) return true;
            return name.Equals("READ ME FIRST.txt", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Data/enUS/realmlist.wtf", StringComparison.OrdinalIgnoreCase);
        }

        static bool SafePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (path.StartsWith("/") || path.StartsWith("\\") || path.Contains(":") || path.Contains("..")) return false;
            return path.Split('/', '\\').All(p => p.Length > 0 && p != "." && p != "..");
        }

        static void EnsureClientExe(Install inst, Action<string, int, int> progress)
        {
            if (File.Exists(ClientExePath(inst))) return;
            try
            {
                progress(L.T("Downloading the AFK Client ..."), 0, 0);
                using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) })
                using (var response = http.GetAsync(ClientReleaseUrl, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult())
                {
                    if (!response.IsSuccessStatusCode) return;
                    Directory.CreateDirectory(Folder(inst));
                    string temp = ClientExePath(inst) + ".tmp";
                    using (var input = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                    using (var output = File.Create(temp))
                        input.CopyTo(output);
                    File.Move(temp, ClientExePath(inst), true);
                }
            }
            catch { }
        }

        static void BuildPackage(Install inst, string baseUrl, ManifestFile manifest)
        {
            string target = PackagePath(inst), temp = target + ".tmp";
            try
            {
                if (File.Exists(temp)) File.Delete(temp);
                using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
                {
                    zip.CreateEntryFromFile(ConfigPath(inst), "launcher.json", CompressionLevel.Optimal);
                    if (File.Exists(ClientExePath(inst)))
                        zip.CreateEntryFromFile(ClientExePath(inst), ClientAssetName, CompressionLevel.Optimal);
                    var readme = zip.CreateEntry("READ ME FIRST.txt");
                    using (var writer = new StreamWriter(readme.Open(), new UTF8Encoding(false)))
                    {
                        writer.WriteLine("AFK Client");
                        writer.WriteLine();
                        if (File.Exists(ClientExePath(inst)))
                        {
                            writer.WriteLine("1. Unpack this ZIP into a folder of its own.");
                            writer.WriteLine("2. Start AFK-Client.exe.");
                            writer.WriteLine("3. Choose your Conquest of Azeroth game folder (the one with the Data folder).");
                            writer.WriteLine("4. Click Check for updates, then Play.");
                        }
                        else
                        {
                            writer.WriteLine("This package contains the server configuration, but not AFK-Client.exe.");
                            writer.WriteLine("Download " + ClientAssetName + " from:");
                            writer.WriteLine("  " + ClientReleaseUrl);
                            writer.WriteLine("Put it next to launcher.json, then start it.");
                            writer.WriteLine();
                            writer.WriteLine("1. Start AFK-Client.exe.");
                            writer.WriteLine("2. Choose your Conquest of Azeroth game folder.");
                            writer.WriteLine("3. Click Check for updates, then Play.");
                        }
                        writer.WriteLine();
                        writer.WriteLine("Server: " + (string.IsNullOrWhiteSpace(manifest.Realm) ? "AFK Realm" : manifest.Realm));
                        writer.WriteLine("Portal: " + (baseUrl.Length > 0 ? baseUrl : "not configured yet"));
                        writer.WriteLine("Manifest: " + (baseUrl.Length > 0 ? baseUrl + "/api/public/manifest" : "not configured yet"));
                        writer.WriteLine();
                        writer.WriteLine("The full game client is not included. Use your existing CoA client or the official launcher.");
                    }
                }
                File.Move(temp, target, true);
            }
            catch
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                throw;
            }
        }

        public static async Task Manifest(HttpContext ctx, Install inst)
        {
            var m = Read(inst);
            if (m == null) { ctx.Response.StatusCode = 404; return; }
            var publicManifest = new
            {
                product = "AFK Client",
                realm = m.Realm,
                realmAddress = m.RealmAddress,
                version = m.Version,
                builtAt = m.BuiltAt,
                files = m.Files.Select(f => new
                {
                    path = f.Path,
                    size = f.Size,
                    sha256 = f.Sha256,
                    url = "/download/launcher-file?path=" + Uri.EscapeDataString(f.Path)
                }).ToArray()
            };
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.Headers["Cache-Control"] = "no-store";
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(publicManifest, Json));
        }

        public static async Task Config(HttpContext ctx, Install inst)
        {
            string file = ConfigPath(inst);
            if (!File.Exists(file)) { ctx.Response.StatusCode = 404; return; }
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.Headers["Content-Disposition"] = "attachment; filename=\"launcher.json\"";
            await ctx.Response.SendFileAsync(file);
        }

        public static async Task ServeFile(HttpContext ctx, Install inst)
        {
            string path = (ctx.Request.Query["path"].ToString() ?? "").Replace('\\', '/').TrimStart('/');
            var m = Read(inst);
            var entry = m?.Files.FirstOrDefault(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (entry == null) { ctx.Response.StatusCode = 404; return; }
            string pack = RegistrationPortal.ClientPackFile(inst);
            if (!File.Exists(pack)) { ctx.Response.StatusCode = 404; return; }
            using (var zip = ZipFile.OpenRead(pack))
            {
                var z = zip.GetEntry(entry.Entry);
                if (z == null) { ctx.Response.StatusCode = 404; return; }
                ctx.Response.ContentType = "application/octet-stream";
                ctx.Response.ContentLength = z.Length;
                ctx.Response.Headers["Content-Disposition"] = "attachment; filename=\"" + Path.GetFileName(entry.Path).Replace("\"", "") + "\"";
                using (var stream = z.Open())
                    await stream.CopyToAsync(ctx.Response.Body);
            }
        }

        public static async Task Package(HttpContext ctx, Install inst)
        {
            string file = PackagePath(inst);
            if (!File.Exists(file)) { ctx.Response.StatusCode = 404; return; }
            ctx.Response.ContentType = "application/zip";
            ctx.Response.Headers["Content-Disposition"] = "attachment; filename=\"AFK-Client.zip\"";
            await ctx.Response.SendFileAsync(file);
        }
    }
}
