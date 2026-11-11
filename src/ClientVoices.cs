using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

namespace CoAInstaller
{
    /// <summary>
    /// Voices for CoA64: quest voice-over (Living Voices) and chat read aloud (Companion Voice), both by Crono141.
    /// CoA64 loads Plugins\*.dll; aspollon/coa-living-voices builds their engine as Plugins\LivingVoices64.dll and
    /// carries the Companion Voice add-on with say/yell and CoA Custom races. The voice data comes from Crono141's
    /// releases. The settings of the Voices page go into the add-on as AFKDefaults.lua (applied once per change).
    /// </summary>
    static class ClientVoices
    {
        public const string PluginRepo = "aspollon/coa-living-voices";
        public const string CompanionRepo = "Crono141/CompanionVoice";
        public const string QuestRepo = "Crono141/LivingVoices";
        const string Marker = ".afk-voices-version";

        public sealed class Options
        {
            public bool Quest = true, Companion = true;
            public bool Party = true, Whisper = true, Say = true, Yell = true, NpcSay = true, NpcYell = true;
            public bool CompanionsOnly = false, QuietInCombat = false;
            public int Volume = 100;
            public double Speed = 1.0;
            public int Stamp = 0;
        }

        static string OptionsFile { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Product.FileStem, "voices.json"); } }

        public static Options Load()
        {
            try { if (File.Exists(OptionsFile)) return new JavaScriptSerializer().Deserialize<Options>(File.ReadAllText(OptionsFile)) ?? new Options(); }
            catch { }
            return new Options();
        }

        public static void Save(Options o)
        {
            o.Volume = Math.Max(0, Math.Min(100, o.Volume));
            o.Speed = Math.Max(0.7, Math.Min(1.5, o.Speed));
            o.Stamp++;
            Directory.CreateDirectory(Path.GetDirectoryName(OptionsFile));
            File.WriteAllText(OptionsFile, new JavaScriptSerializer().Serialize(o), new UTF8Encoding(false));
        }

        static string AddOn(string client, string name) { return Path.Combine(client, "Interface", "AddOns", name); }
        static string PluginFile(string client) { return Path.Combine(client, "Plugins", "LivingVoices64.dll"); }

        /// <summary>The client's exe that loads Plugins\*.dll (CoA64 with the plugin loader), or null.</summary>
        public static string PluginHost(string client)
        {
            if (client == null || !Directory.Exists(client)) return null;
            byte[] needle = Encoding.ASCII.GetBytes("CoaPlugin_OpenLua");
            foreach (string exe in Directory.GetFiles(client, "*.exe"))
            {
                try
                {
                    if (new FileInfo(exe).Length < 1024 * 1024) continue;
                    if (IndexOf(File.ReadAllBytes(exe), needle) >= 0) return exe;
                }
                catch { }
            }
            return null;
        }

        static int IndexOf(byte[] hay, byte[] needle)
        {
            for (int i = 0; i + needle.Length <= hay.Length; i++)
            {
                if (hay[i] != needle[0]) continue;
                int k = 1;
                while (k < needle.Length && hay[i + k] == needle[k]) k++;
                if (k == needle.Length) return i;
            }
            return -1;
        }

        public sealed class State
        {
            public string Host, Plugin, Companion, Quest;
        }

        public static State Look(string client)
        {
            var s = new State();
            if (client == null) return s;
            s.Host = PluginHost(client);
            s.Plugin = ReadMarker(Path.GetDirectoryName(PluginFile(client)), File.Exists(PluginFile(client)));
            s.Companion = ReadMarker(AddOn(client, "CompanionVoice"), Directory.Exists(Path.Combine(AddOn(client, "CompanionVoice"), "engine")));
            s.Quest = ReadMarker(AddOn(client, "LivingVoices"), File.Exists(Path.Combine(AddOn(client, "LivingVoices"), "LivingVoices.MPQ")));
            return s;
        }

        static string ReadMarker(string dir, bool present)
        {
            if (!present) return null;
            try { string f = Path.Combine(dir, Marker); if (File.Exists(f)) return File.ReadAllText(f).Trim(); } catch { }
            return "?";
        }

        static void WriteMarker(string dir, string version)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, Marker), version ?? "", new UTF8Encoding(false));
        }

        // ------------------------------------------------------------------------------------ install

        public static string Install(string client, Options o, Action<string, long, long> report)
        {
            var done = new List<string>();
            string downloads = Path.Combine(client, ".afk-voices-downloads");
            Directory.CreateDirectory(downloads);
            try
            {
                if (!o.Quest && !o.Companion)
                {
                    RemoveDir(AddOn(client, "LivingVoices"), done);
                    RemoveDir(AddOn(client, "CompanionVoice"), done);
                    if (File.Exists(PluginFile(client))) { File.Delete(PluginFile(client)); done.Add("Plugins\\LivingVoices64.dll " + L.T("removed")); }
                    return string.Join("\n", done);
                }

                // the engine plugin and the add-on fork: always, small
                var plugin = LatestRelease(PluginRepo, n => n.StartsWith("CoA64-Voices-", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
                string pluginZip = Fetch(plugin, plugin.Assets[0], downloads, report);
                // the game's CoA64.exe cannot load plugins: the one built for that comes with the release
                if (PluginHost(client) == null) done.Add(InstallExe(client, pluginZip));

                if (o.Companion)
                {
                    var cv = LatestRelease(CompanionRepo, n => n.StartsWith("CompanionVoice", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                                                           && n.IndexOf("src", StringComparison.OrdinalIgnoreCase) < 0);
                    string target = AddOn(client, "CompanionVoice");
                    if (Look(client).Companion != cv.Tag)
                    {
                        string cvZip = Fetch(cv, cv.Assets[0], downloads, report);
                        report(L.T("Unpacking the Companion Voice engine and voices ..."), 0, 0);
                        ExtractAddOn(cvZip, "CompanionVoice", target, null);
                        WriteMarker(target, cv.Tag);
                        TryDelete(cvZip);
                        done.Add(L.F("Companion Voice {0}: speech engine and voices", cv.Tag));
                    }
                    // the fork's Lua over upstream's; upstream's voice list stays, it belongs to its voices folder
                    ExtractAddOn(pluginZip, "CompanionVoice", target, rel => rel != "Voices.lua" || !File.Exists(Path.Combine(target, "Voices.lua")));
                    WriteDefaults(target, o);
                    done.Add(L.T("Companion Voice add-on (party, whisper, say, yell) with the settings of this page"));
                }
                else RemoveDir(AddOn(client, "CompanionVoice"), done);

                if (o.Quest)
                {
                    var lv = LatestRelease(QuestRepo, n => n.EndsWith("-part1.zip", StringComparison.OrdinalIgnoreCase) || n.EndsWith("-part2.zip", StringComparison.OrdinalIgnoreCase));
                    string target = AddOn(client, "LivingVoices");
                    if (Look(client).Quest != lv.Tag)
                    {
                        foreach (var asset in lv.Assets.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase))
                        {
                            string zip = Fetch(lv, asset, downloads, report);
                            report(L.F("Unpacking {0} ...", asset.Name), 0, 0);
                            ExtractAddOn(zip, "LivingVoices", target, null);
                            TryDelete(zip);
                        }
                        WriteMarker(target, lv.Tag);
                        done.Add(L.F("Living Voices {0}: quest voice-over", lv.Tag));
                    }
                }
                else RemoveDir(AddOn(client, "LivingVoices"), done);

                report(L.T("Installing the voice plugin ..."), 0, 0);
                using (var zip = ZipFile.OpenRead(pluginZip))
                {
                    var entry = zip.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/').EndsWith("Plugins/LivingVoices64.dll", StringComparison.OrdinalIgnoreCase));
                    if (entry == null) throw new InvalidOperationException(L.T("The voice plugin is missing from its release."));
                    Directory.CreateDirectory(Path.GetDirectoryName(PluginFile(client)));
                    string tmp = PluginFile(client) + ".new";
                    entry.ExtractToFile(tmp, true);
                    if (File.Exists(PluginFile(client))) File.Delete(PluginFile(client));
                    File.Move(tmp, PluginFile(client));
                }
                WriteMarker(Path.GetDirectoryName(PluginFile(client)), plugin.Tag);
                TryDelete(pluginZip);
                done.Add(L.F("Voice plugin {0} (Plugins\\LivingVoices64.dll)", plugin.Tag));
            }
            finally
            {
                try { if (!Directory.EnumerateFileSystemEntries(downloads).Any()) Directory.Delete(downloads); } catch { }
            }
            return string.Join("\n", done);
        }

        /// <summary>CoA64.exe (and XvidDecoder.dll) from the release's CoA64 folder; the game's own exe stays as .afk-backup.</summary>
        static string InstallExe(string client, string pluginZip)
        {
            using (var zip = ZipFile.OpenRead(pluginZip))
            {
                var exe = zip.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/').Equals("CoA64/CoA64.exe", StringComparison.OrdinalIgnoreCase));
                if (exe == null)
                    throw new InvalidOperationException(L.T("This game folder has no CoA64 client that loads plugins (Plugins\\*.dll). Voices need that CoA64 build."));
                string built = null;
                var ver = zip.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/').Equals("CoA64/COA-CUSTOM-VERSION.txt", StringComparison.OrdinalIgnoreCase));
                if (ver != null) using (var r = new StreamReader(ver.Open())) built = r.ReadToEnd().Trim();
                foreach (var e in new[] { exe, zip.Entries.FirstOrDefault(x => x.FullName.Replace('\\', '/').Equals("CoA64/XvidDecoder.dll", StringComparison.OrdinalIgnoreCase)) })
                {
                    if (e == null) continue;
                    string target = Path.Combine(client, e.Name);
                    string backup = target + ".afk-backup";
                    try { if (File.Exists(target) && !File.Exists(backup)) File.Copy(target, backup); } catch { }
                    try { CoaUninstall.Capture(target); } catch { }
                    string tmp = target + ".new";
                    e.ExtractToFile(tmp, true);
                    if (File.Exists(target)) File.Delete(target);
                    File.Move(tmp, target);
                }
                string line = L.T("CoA64.exe that loads plugins (yours is kept as CoA64.exe.afk-backup)");
                string have = null;
                try { string pkg = Settings.CoaPackage; if (!string.IsNullOrEmpty(pkg)) have = CoaSetup.ReadVersion(Path.Combine(CoaSetup.PackageForPlayers() ?? pkg, "manifest.json")); } catch { }
                if (!string.IsNullOrEmpty(built) && !string.IsNullOrEmpty(have) && built != have)
                    line += "\n" + L.F("Note: this CoA64.exe was built for CoA Custom {0}, your package is {1}. If the game misbehaves, put CoA64.exe.afk-backup back and wait for a matching voices release.", built, have);
                return line;
            }
        }

        /// <summary>Only the settings file, when nothing has to be downloaded.</summary>
        public static bool WriteDefaultsOnly(string client, Options o)
        {
            string target = AddOn(client, "CompanionVoice");
            if (!File.Exists(Path.Combine(target, "CompanionVoice.toc"))) return false;
            WriteDefaults(target, o);
            return true;
        }

        static void WriteDefaults(string addon, Options o)
        {
            Func<bool, string> b = x => x ? "true" : "false";
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("-- Written by AFK Realm (Voices page). A new stamp applies these settings once; changes made in game stay until the page changes again.");
            sb.AppendLine("CompanionVoiceAFK = {");
            sb.AppendLine("  stamp = " + o.Stamp.ToString(ci) + ",");
            sb.AppendLine("  settings = {");
            sb.AppendLine("    party = " + b(o.Party) + ", whisper = " + b(o.Whisper) + ", say = " + b(o.Say) + ", yell = " + b(o.Yell) + ",");
            sb.AppendLine("    npcSay = " + b(o.NpcSay) + ", npcYell = " + b(o.NpcYell) + ", companionsOnly = " + b(o.CompanionsOnly) + ", quietInCombat = " + b(o.QuietInCombat) + ",");
            sb.AppendLine("    volume = " + (o.Volume / 100.0).ToString("0.##", ci) + ", speed = " + o.Speed.ToString("0.##", ci) + ",");
            sb.AppendLine("  },");
            sb.AppendLine("}");
            File.WriteAllText(Path.Combine(addon, "AFKDefaults.lua"), sb.ToString(), new UTF8Encoding(false));
        }

        /// <summary>Every file below ".../Interface/AddOns/name/" in the ZIP, into target. keep(rel) false skips a file.</summary>
        static void ExtractAddOn(string zipFile, string name, string target, Func<string, bool> keep)
        {
            string key = "Interface/AddOns/" + name + "/";
            int count = 0;
            using (var zip = ZipFile.OpenRead(zipFile))
                foreach (var e in zip.Entries)
                {
                    string full = e.FullName.Replace('\\', '/');
                    int at = full.IndexOf(key, StringComparison.OrdinalIgnoreCase);
                    if (at < 0 || full.EndsWith("/")) continue;
                    string rel = full.Substring(at + key.Length);
                    if (rel.Length == 0 || rel.Split('/').Any(p => p == "..") || (keep != null && !keep(rel))) continue;
                    string dest = Path.Combine(target, rel.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    e.ExtractToFile(dest, true);
                    count++;
                }
            if (count == 0) throw new InvalidOperationException(L.F("{0} has no Interface\\AddOns\\{1} folder.", Path.GetFileName(zipFile), name));
        }

        static void RemoveDir(string dir, List<string> done)
        {
            if (!Directory.Exists(dir)) return;
            Directory.Delete(dir, true);
            done.Add("Interface\\AddOns\\" + Path.GetFileName(dir) + " " + L.T("removed"));
        }

        static void TryDelete(string f) { try { File.Delete(f); } catch { } }

        // ------------------------------------------------------------------------------------ for the players

        /// <summary>The files players get from the owner's game: the CoA64.exe that loads plugins, the voice plugin and
        /// both voice add-ons as installed (with the owner's AFKDefaults.lua). Paths relative to the game folder.</summary>
        public static List<KeyValuePair<string, string>> PlayerFiles(string client)
        {
            var list = new List<KeyValuePair<string, string>>();
            string host = PluginHost(client);
            if (host == null) return list;
            list.Add(new KeyValuePair<string, string>(Path.GetFileName(host), host));
            string plugin = PluginFile(client);
            if (File.Exists(plugin)) list.Add(new KeyValuePair<string, string>("Plugins/LivingVoices64.dll", plugin));
            foreach (string name in new[] { "CompanionVoice", "LivingVoices" })
            {
                string dir = AddOn(client, name);
                if (!Directory.Exists(dir)) continue;
                string prefix = Path.GetFullPath(dir).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    if (Path.GetFileName(f).Equals(Marker, StringComparison.OrdinalIgnoreCase)) continue;
                    list.Add(new KeyValuePair<string, string>("Interface/AddOns/" + name + "/" + Path.GetFullPath(f).Substring(prefix.Length).Replace('\\', '/'), f));
                }
            }
            return list;
        }

        /// <summary>Adds PlayerFiles to a ZIP being written. Returns the exe's name (or null when the game has none).</summary>
        public static string AddToZip(ZipArchive zip, string client, Action<string, int, int> progress, ISet<string> taken = null)
        {
            var files = PlayerFiles(client);
            if (files.Count == 0) return null;
            int done = 0;
            foreach (var f in files)
            {
                if (progress != null) progress(L.F("Packing the voices ({0}/{1}) ...", done + 1, files.Count), done, files.Count);
                done++;
                if (taken != null && !taken.Add(f.Key)) continue;
                bool packed = f.Key.EndsWith(".MPQ", StringComparison.OrdinalIgnoreCase) || f.Key.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase);
                zip.CreateEntryFromFile(f.Value, f.Key, packed ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
            }
            return files[0].Key;
        }

        /// <summary>
        /// Rewrites a finished client pack into a complete one: its own entries first (module add-ons and the AFK client
        /// patch win), then the voices with the owner's CoA64.exe, then the CoA Custom package's client part when a
        /// package folder is given. Streams, so a ZIP of several GB never sits in memory as ZipArchiveMode.Update would.
        /// Returns false (and leaves the ZIP alone) when there is nothing to add.
        /// </summary>
        public static bool AppendToZipFile(string zipFile, string client, Action<string, int, int> progress, string package = null)
        {
            bool voices = PlayerFiles(client).Count > 0;
            if (!voices && package == null) return false;
            string temp = zipFile + ".full";
            if (File.Exists(temp)) File.Delete(temp);
            try
            {
                using (var source = ZipFile.OpenRead(zipFile))
                using (var target = ZipFile.Open(temp, ZipArchiveMode.Create))
                {
                    var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var e in source.Entries)
                    {
                        string name = e.FullName.Replace('\\', '/');
                        if (name.EndsWith("/") || !taken.Add(name)) continue;
                        bool stored = name.EndsWith(".MPQ", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
                        var copy = target.CreateEntry(name, stored ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
                        copy.LastWriteTime = e.LastWriteTime;
                        using (var from = e.Open())
                        using (var to = copy.Open())
                            from.CopyTo(to);
                    }
                    if (voices) AddToZip(target, client, progress, taken);
                    if (package != null) CoaSetup.AddPackageClientToZip(target, package, client, taken, progress);
                }
                File.Delete(zipFile);
                File.Move(temp, zipFile);
            }
            catch
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                throw;
            }
            return true;
        }

        // ------------------------------------------------------------------------------------ GitHub releases

        sealed class Asset { public string Name, Url; public long Size; }
        sealed class Release { public string Tag; public List<Asset> Assets = new List<Asset>(); }

        static HttpWebRequest Request(string url, int timeout)
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = Product.FileStem;
            string token = Settings.GitHubToken;
            if (!string.IsNullOrEmpty(token) && url.StartsWith("https://api.github.com/", StringComparison.Ordinal)) req.Headers["Authorization"] = "token " + token;
            req.Timeout = timeout;
            req.ReadWriteTimeout = Math.Max(timeout, 60000);
            return req;
        }

        static Release LatestRelease(string repo, Func<string, bool> wanted)
        {
            string json;
            using (var resp = (HttpWebResponse)Request("https://api.github.com/repos/" + repo + "/releases/latest", 30000).GetResponse())
            using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                json = reader.ReadToEnd();
            var rel = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(json) as Dictionary<string, object>;
            object v;
            var found = new Release { Tag = rel != null && rel.TryGetValue("tag_name", out v) ? Convert.ToString(v) : "" };
            if (rel != null && rel.TryGetValue("assets", out v) && v is object[])
                foreach (var raw in (object[])v)
                {
                    var a = raw as Dictionary<string, object>;
                    if (a == null) continue;
                    string name = a.TryGetValue("name", out v) ? Convert.ToString(v) : "";
                    if (!wanted(name)) continue;
                    long size; long.TryParse(a.TryGetValue("size", out v) ? Convert.ToString(v) : "0", out size);
                    found.Assets.Add(new Asset { Name = name, Size = size, Url = a.TryGetValue("browser_download_url", out v) ? Convert.ToString(v) : "" });
                }
            if (found.Assets.Count == 0) throw new InvalidOperationException(L.F("The newest release of {0} has no file to install.", repo));
            return found;
        }

        /// <summary>A release file into downloads, resumed where an earlier attempt stopped.</summary>
        static string Fetch(Release rel, Asset asset, string downloads, Action<string, long, long> report)
        {
            string target = Path.Combine(downloads, asset.Name);
            if (File.Exists(target) && new FileInfo(target).Length == asset.Size) return target;
            string part = target + ".part";
            for (int attempt = 0; ; attempt++)
            {
                long have = File.Exists(part) ? new FileInfo(part).Length : 0;
                if (asset.Size > 0 && have > asset.Size) { File.Delete(part); have = 0; }
                try
                {
                    if (asset.Size == 0 || have < asset.Size)
                    {
                        var req = Request(asset.Url, 30000);
                        if (have > 0) req.AddRange(have);
                        using (var resp = (HttpWebResponse)req.GetResponse())
                        {
                            if (have > 0 && resp.StatusCode == HttpStatusCode.OK) have = 0;
                            using (var src = resp.GetResponseStream())
                            using (var dst = new FileStream(part, have > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write))
                            {
                                var buffer = new byte[256 * 1024];
                                long done = have;
                                int n;
                                while ((n = src.Read(buffer, 0, buffer.Length)) > 0)
                                {
                                    dst.Write(buffer, 0, n);
                                    done += n;
                                    report(L.F("Downloading {0} ({1}) ...", asset.Name, rel.Tag), done, asset.Size);
                                }
                            }
                        }
                    }
                    if (asset.Size > 0 && new FileInfo(part).Length != asset.Size) throw new IOException(L.F("{0} arrived incomplete.", asset.Name));
                    if (File.Exists(target)) File.Delete(target);
                    File.Move(part, target);
                    return target;
                }
                catch (Exception) when (attempt < 2) { }
            }
        }
    }
}
