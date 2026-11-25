using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;

namespace CoAInstaller
{
    /// <summary>
    /// Azeroth Remastered (Visuals 3) runs AFK64.exe on Mado's Vulkan engine, which needs his data beside the exe: the
    /// translated shaders (Vulkan\shaders\translated), Cata water, Lumenveil and wind data, the For Ever and AR light
    /// tables, the FSR and DLSS runtimes, the WaystoneHD settings add-on and patch-w.MPQ. They come from his public
    /// player package (Shotboom12/Mados-Azeroth-Remastered), fetched once per version. docs/2026-11-25-azeroth-remastered.md
    /// </summary>
    static class ClientAzerothRemastered
    {
        // Bump both with a new package: the marker makes the next start fetch it.
        const string Version = "2026-11-24-BRUME1-v1.0.4";
        const string Url = "https://github.com/Shotboom12/Mados-Azeroth-Remastered/releases/download/v1.0.4/Azeroth.Remastered-2026-11-24-BRUME1-English-v1.0.4.zip";
        const string Marker = ".afk-azeroth-remastered";
        const string Licenses = "Azeroth Remastered LICENSES";

        // The package's own exe and launcher are not ours: AFK64.exe carries the engine, and it sets the mip bias itself.
        static readonly string[] Skip = { "WowClient64.exe", "Launch-Azeroth-Remastered.ps1", "XvidDecoder.dll" };

        static string MarkerFile(string client) { return Path.Combine(client, "Vulkan", Marker); }

        /// <summary>The package of this version is in place (its shader list included).</summary>
        public static bool Installed(string client)
        {
            try
            {
                return File.Exists(Path.Combine(client, "Vulkan", "shaders", "translated", "manifest.txt"))
                    && File.Exists(MarkerFile(client)) && File.ReadAllText(MarkerFile(client)).Trim() == Version;
            }
            catch { return false; }
        }

        /// <summary>
        /// Fetches and unpacks Mado's package when Azeroth Remastered is chosen and this version is not in place. Null when
        /// there was nothing to do; otherwise one line for the log. Never throws: the other graphics packs still start.
        /// </summary>
        public static string Ensure(string client, bool wanted, Action<string, long, long> report)
        {
            if (!wanted || Installed(client)) return null;
            string zip = Path.Combine(client, ".afk-azeroth-remastered.zip");
            try
            {
                bool first = !File.Exists(MarkerFile(client));
                Download(zip, report);
                int n = Unpack(zip, client, report);
                if (first) ApplySettings(zip, client);
                Directory.CreateDirectory(Path.GetDirectoryName(MarkerFile(client)));
                File.WriteAllText(MarkerFile(client), Version);
                return L.F("Azeroth Remastered: Mado's data {0} installed ({1} files)", Version, n);
            }
            catch (Exception e) { return L.F("Azeroth Remastered: Mado's data could not be installed ({0})", e.Message); }
            finally { try { File.Delete(zip); } catch { } }
        }

        static void Download(string zip, Action<string, long, long> report)
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }
            var req = (HttpWebRequest)WebRequest.Create(Url);
            req.UserAgent = Product.FileStem;
            req.Timeout = 30000;
            req.ReadWriteTimeout = 60000;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var src = resp.GetResponseStream())
            using (var dst = new FileStream(zip, FileMode.Create, FileAccess.Write))
            {
                var buffer = new byte[256 * 1024];
                long done = 0, total = resp.ContentLength;
                int n;
                while ((n = src.Read(buffer, 0, buffer.Length)) > 0)
                {
                    dst.Write(buffer, 0, n);
                    done += n;
                    report(L.T("Downloading Azeroth Remastered (Mado's data, about 450 MB) ..."), done, total);
                }
            }
        }

        // The package's client\ goes into the game folder, LICENSES\ beside it; the shader list last, so a broken copy is redone.
        static int Unpack(string zip, string client, Action<string, long, long> report)
        {
            string root = Path.GetFullPath(client).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            int n = 0;
            using (var archive = ZipFile.OpenRead(zip))
            {
                var files = new List<KeyValuePair<ZipArchiveEntry, string>>();
                foreach (var e in archive.Entries)
                {
                    if (e.Length == 0 && e.FullName.EndsWith("/")) continue;
                    string name = e.FullName.Replace('\\', '/');
                    int cut = name.IndexOf('/');
                    if (cut < 0) continue;
                    string rel = name.Substring(cut + 1);
                    if (rel.StartsWith("client/", StringComparison.OrdinalIgnoreCase)) rel = rel.Substring(7);
                    else if (rel.StartsWith("LICENSES/", StringComparison.OrdinalIgnoreCase)) rel = Licenses + "/" + rel.Substring(9);
                    else continue;
                    if (rel.Length == 0 || Skip.Any(s => rel.Equals(s, StringComparison.OrdinalIgnoreCase))) continue;
                    files.Add(new KeyValuePair<ZipArchiveEntry, string>(e, rel));
                }
                files = files.OrderBy(kv => kv.Value.EndsWith("shaders/translated/manifest.txt", StringComparison.OrdinalIgnoreCase) ? 1 : 0).ToList();
                long total = files.Sum(kv => kv.Key.Length), done = 0;
                foreach (var kv in files)
                {
                    string target = Path.GetFullPath(Path.Combine(root, kv.Value.Replace('/', Path.DirectorySeparatorChar)));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    string tmp = target + ".new";
                    kv.Key.ExtractToFile(tmp, true);
                    if (File.Exists(target)) File.Delete(target);
                    File.Move(tmp, target);
                    done += kv.Key.Length;
                    n++;
                    if (n % 200 == 0) report(L.T("Unpacking Azeroth Remastered ..."), done, total);
                }
            }
            return n;
        }

        // Mado's recommended HD settings, once, as his installer writes them; gxApi stays with Visuals.txt.
        static void ApplySettings(string zip, string client)
        {
            var settings = new List<KeyValuePair<string, string>>();
            using (var archive = ZipFile.OpenRead(zip))
            {
                var entry = archive.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/').EndsWith("/HD-Settings.txt", StringComparison.OrdinalIgnoreCase));
                if (entry == null) return;
                using (var reader = new StreamReader(entry.Open()))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(line, "^SET\\s+(\\S+)\\s+\"(.*)\"");
                        if (m.Success && !m.Groups[1].Value.Equals("gxApi", StringComparison.OrdinalIgnoreCase))
                            settings.Add(new KeyValuePair<string, string>(m.Groups[1].Value, m.Groups[2].Value));
                    }
                }
            }
            string wtf = Path.Combine(client, "WTF");
            Directory.CreateDirectory(wtf);
            string config = Path.Combine(wtf, "Config.wtf");
            var lines = File.Exists(config) ? File.ReadAllLines(config).ToList() : new List<string>();
            foreach (var kv in settings)
            {
                string text = "SET " + kv.Key + " \"" + kv.Value + "\"";
                int at = lines.FindIndex(l => System.Text.RegularExpressions.Regex.IsMatch(l, "^SET\\s+" + System.Text.RegularExpressions.Regex.Escape(kv.Key) + "\\s", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
                if (at >= 0) lines[at] = text;
                else lines.Add(text);
            }
            File.WriteAllLines(config, lines);
        }
    }
}
