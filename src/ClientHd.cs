using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace CoAInstaller
{
    /// <summary>
    /// fueryin's "Conquest of Azeroth – Ultra-HD Mod Pack" in a CoA Custom game. The player downloads the pack (Google
    /// Drive) and unpacks it; this copies its parts under names that do not clash with CoA Custom's archives, leaves the
    /// pack's own CoA64.exe, d3d9.dll and Config.wtf out, and switches each part on or off by renaming. LuxVelum (fog,
    /// light, water) needs a CoA64.exe that hosts LuxVelum64.dll. See docs/2026-11-12-hd-pack.md.
    /// </summary>
    static class ClientHd
    {
        public const string PackPage = "https://github.com/fueryin/Conquest-of-Azeroth-Ultra-HD-Graphics-Modpack/tree/CoA-HD-PATCH";
        const string Off = ".off";

        public sealed class Options
        {
            public bool World = true, Orcs = false, Shaders = true, Cursor = false, Blood = false, Effects = true, Sounds = true;
            public bool Lux = true, Addons = true;
            public string Pack = "";
            // Visuals.txt: -1 = follow the LuxVelum switch, else 0 none, 1 Lumenveil, 2 LuxVelum.
            public int Visuals = -1;
        }

        /// <summary>
        /// The pack's archive, its name in the game and the switch it belongs to. "-HD" names stay out with -hd 0 (CoA64's
        /// HD rule); ZX sorts above CoA's own HD archives (patch-Z, WC*, CHA) and below CoA Custom's (patch-ZZ*), whose
        /// files these never touch. AAP/AAS stay low: their DBCs must not replace CoA Custom's.
        /// </summary>
        static readonly string[][] Archives =
        {
            new[] { "patch-ZZB.mpq", "patch-ZXB-HD.MPQ", "world" },
            new[] { "patch-ZZD.mpq", "patch-ZXD-HD.MPQ", "world" },
            new[] { "patch-ZZE.mpq", "patch-ZXE-HD.MPQ", "world" },
            new[] { "patch-ZZU.mpq", "patch-ZXU-HD.MPQ", "orcs" },
            new[] { "patch-ZZA.mpq", "patch-ZXA.MPQ", "shaders" },
            new[] { "patch-ZZY.mpq", "patch-ZXY.MPQ", "cursor" },
            new[] { "patch-ZZZ.mpq", "patch-ZXZ.MPQ", "blood" },
            new[] { "patch-AAP.mpq", "patch-AAP.MPQ", "effects" },
            new[] { "patch-AAS.mpq", "patch-AAS.MPQ", "sounds" },
        };

        static readonly string[] LuxFiles = { "LuxVelum64.dll", "fogdata.bin", "waterdata.bin" };
        static readonly string[] PackAddons = { "BloodyScreen", "CoATooltip", "DanceMusic", "EnhancedBattlegroundScoreboard", "Leatrix_Plus" };

        static string OptionsFile { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Product.FileStem, "hdpack.json"); } }

        public static Options Load()
        {
            try { if (File.Exists(OptionsFile)) return new JavaScriptSerializer().Deserialize<Options>(File.ReadAllText(OptionsFile)) ?? new Options(); }
            catch { }
            return new Options();
        }

        public static void Save(Options o)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(OptionsFile));
            File.WriteAllText(OptionsFile, new JavaScriptSerializer().Serialize(o), new UTF8Encoding(false));
        }

        static bool On(Options o, string part)
        {
            switch (part)
            {
                case "world": return o.World;
                case "orcs": return o.Orcs;
                case "shaders": return o.Shaders;
                case "cursor": return o.Cursor;
                case "blood": return o.Blood;
                case "effects": return o.Effects;
                case "sounds": return o.Sounds;
                default: return false;
            }
        }

        /// <summary>A file or folder in dir whose name matches case-insensitively (the pack uses .mpq, Linux cares).</summary>
        static string Find(string dir, string name, bool folder = false)
        {
            if (dir == null || !Directory.Exists(dir)) return null;
            string exact = Path.Combine(dir, name);
            if (folder ? Directory.Exists(exact) : File.Exists(exact)) return exact;
            var all = folder ? Directory.GetDirectories(dir) : Directory.GetFiles(dir);
            return all.FirstOrDefault(p => string.Equals(Path.GetFileName(p), name, StringComparison.OrdinalIgnoreCase));
        }

        static bool IsPack(string dir)
        {
            return Find(Find(dir, "Data", true), "patch-ZZD.mpq") != null;
        }

        /// <summary>The unpacked pack: the saved folder, else a folder in Downloads that holds Data\patch-ZZD.mpq.</summary>
        public static string FindPack(string saved)
        {
            if (!string.IsNullOrWhiteSpace(saved) && IsPack(saved.Trim())) return saved.Trim();
            try
            {
                string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (!Directory.Exists(downloads)) return null;
                foreach (string d in Directory.GetDirectories(downloads))
                {
                    if (IsPack(d)) return d;
                    try { foreach (string e in Directory.GetDirectories(d)) if (IsPack(e)) return e; } catch { }
                }
            }
            catch { }
            return null;
        }

        static string Data(string client) { return Path.Combine(client, "Data"); }
        static string AddOn(string client, string name) { return Path.Combine(client, "Interface", "AddOns", name); }

        /// <summary>The game's exe can host LuxVelum64.dll (CoA64 with the LuxVelum host).</summary>
        public static bool LuxHost(string client)
        {
            byte[] needle = Encoding.ASCII.GetBytes("LuxVelum_Attach");
            foreach (string exe in Directory.GetFiles(client, "*.exe"))
            {
                try
                {
                    if (new FileInfo(exe).Length < 1024 * 1024) continue;
                    byte[] hay = File.ReadAllBytes(exe);
                    for (int i = 0; i + needle.Length <= hay.Length; i++)
                    {
                        if (hay[i] != needle[0]) continue;
                        int k = 1;
                        while (k < needle.Length && hay[i + k] == needle[k]) k++;
                        if (k == needle.Length) return true;
                    }
                }
                catch { }
            }
            return false;
        }

        public sealed class State
        {
            public bool Installed, Dxvk, LuxHost, LuxFiles;
            public int Active, Disabled;
        }

        public static State Look(string client)
        {
            var s = new State();
            if (client == null) return s;
            foreach (var a in Archives)
            {
                if (Find(Data(client), a[1]) != null) s.Active++;
                else if (Find(Data(client), a[1] + Off) != null) s.Disabled++;
            }
            s.Installed = s.Active + s.Disabled > 0;
            s.Dxvk = File.Exists(Path.Combine(client, "x64", "d3d9.dll"));
            s.LuxHost = LuxHost(client);
            s.LuxFiles = File.Exists(Path.Combine(client, "LuxVelum64.dll"));
            return s;
        }

        // ------------------------------------------------------------------------------------ install and switches

        public static string Install(string client, Options o, Action<string, long, long> report)
        {
            var done = new List<string>();
            string pack = FindPack(o.Pack);
            string packData = pack == null ? null : Find(pack, "Data", true);

            foreach (var a in Archives)
            {
                string active = Path.Combine(Data(client), a[1]);
                string disabled = active + Off;
                string have = Find(Data(client), a[1]), parked = Find(Data(client), a[1] + Off);
                if (On(o, a[2]))
                {
                    if (have != null) continue;
                    if (parked != null) { File.Move(parked, active); done.Add(L.F("{0} switched on", a[1])); continue; }
                    string source = Find(packData, a[0]);
                    if (source == null) throw new InvalidOperationException(L.F("{0} is not in the HD pack folder. Download and unpack the pack first, then choose its folder.", a[0]));
                    Copy(source, active, report);
                    done.Add(L.F("{0} installed (from {1})", a[1], a[0]));
                }
                else if (have != null)
                {
                    if (parked != null) File.Delete(parked);
                    File.Move(have, disabled);
                    done.Add(L.F("{0} switched off", a[1]));
                }
            }

            // Every HD install also brings AFK64.exe, Lumenveil64.dll and Visuals.txt (the standard client effect).
            // LuxVelum, when switched on, needs the same exe as its host; a failed download only leaves a note.
            try { string exe = ClientVoices.EnsureNewestExe(client, report, CoaClientExe.Afk); if (exe != null) done.Add(exe); }
            catch (Exception e) { if (o.Lux) done.Add(L.F("The newest AFK64.exe could not be fetched ({0}); LuxVelum waits for it.", e.Message)); }
            done.AddRange(InstallLux(client, pack, o, report));
            int visuals = o.Visuals >= ClientVisuals.Stock ? o.Visuals
                : o.Lux ? ClientVisuals.LuxVelum
                : ClientVisuals.Read(client) == ClientVisuals.LuxVelum ? ClientVisuals.Lumenveil : -1;
            if (ClientVisuals.Write(client, visuals)) done.Add(L.F("Graphics effect: {0}", ClientVisuals.Name(visuals)));
            done.AddRange(InstallAddons(client, pack, o));
            return string.Join("\n", done);
        }

        static IEnumerable<string> InstallLux(string client, string pack, Options o, Action<string, long, long> report)
        {
            var done = new List<string>();
            string ini = Path.Combine(client, "LuxVelum.ini");
            if (o.Lux)
            {
                foreach (string name in LuxFiles)
                {
                    string target = Path.Combine(client, name);
                    if (File.Exists(target)) continue;
                    string source = Find(pack, name);
                    if (source == null) throw new InvalidOperationException(L.F("{0} is not in the HD pack folder. Download and unpack the pack first, then choose its folder.", name));
                    Copy(source, target, report);
                    done.Add(L.F("{0} installed", name));
                }
                if (!File.Exists(ini))
                {
                    string source = Find(pack, "LuxVelum.ini");
                    if (source != null) { File.Copy(source, ini); done.Add(L.F("{0} installed", "LuxVelum.ini")); }
                }
            }
            if (File.Exists(ini) && SetEnable(ini, o.Lux)) done.Add(o.Lux ? L.T("LuxVelum switched on") : L.T("LuxVelum switched off"));
            return done;
        }

        /// <summary>Sets Enable= in the [LuxVelum] section; true when the file changed.</summary>
        static bool SetEnable(string ini, bool on)
        {
            string text = File.ReadAllText(ini);
            string value = on ? "1" : "0";
            var rx = new Regex(@"^(\s*Enable\s*=\s*)(\d)", RegexOptions.Multiline);
            var m = rx.Match(text);
            string next = m.Success ? rx.Replace(text, "${1}" + value, 1) : text.Replace("[LuxVelum]", "[LuxVelum]\r\nEnable=" + value);
            if (next == text) return false;
            File.WriteAllText(ini, next, new UTF8Encoding(false));
            return true;
        }

        static IEnumerable<string> InstallAddons(string client, string pack, Options o)
        {
            var done = new List<string>();
            string packAddons = Find(Find(pack, "Interface", true), "AddOns", true);
            foreach (string name in PackAddons)
            {
                string target = AddOn(client, name), parked = target + Off;
                if (o.Addons)
                {
                    if (!Directory.Exists(target) && Directory.Exists(parked)) Directory.Move(parked, target);
                    if (!Directory.Exists(target))
                    {
                        string from = Find(packAddons, name, true);
                        if (from == null) continue;
                        CopyDir(from, target);
                        done.Add(L.F("Add-on {0} installed", name));
                    }
                    if (name == "DanceMusic") WriteDanceMusic(target);
                }
                else if (Directory.Exists(target))
                {
                    if (Directory.Exists(parked)) Directory.Delete(parked, true);
                    Directory.Move(target, parked);
                    done.Add(L.F("Add-on {0} switched off", name));
                }
            }
            return done;
        }

        /// <summary>The DanceMusic Lua that picks the song when you dance and gives CoA Custom races their donor's song.</summary>
        static void WriteDanceMusic(string addon)
        {
            using (var s = typeof(ClientHd).Assembly.GetManifestResourceStream("CoAInstaller.hdpack.DanceMusic.lua"))
            {
                if (s == null) return;
                using (var r = new StreamReader(s)) File.WriteAllText(Path.Combine(addon, "DanceMusic.lua"), r.ReadToEnd().Replace("\r\n", "\n").Replace("\n", "\r\n"), new UTF8Encoding(false));
            }
        }

        /// <summary>Removes everything this page put into the game, the switched-off parts included.</summary>
        public static string Remove(string client)
        {
            var done = new List<string>();
            foreach (var a in Archives)
                foreach (string n in new[] { a[1], a[1] + Off })
                {
                    string f = Find(Data(client), n);
                    if (f != null) { File.Delete(f); done.Add(L.F("{0} removed", n)); }
                }
            foreach (string n in LuxFiles.Concat(new[] { "LuxVelum.ini" }))
            {
                string f = Path.Combine(client, n);
                if (File.Exists(f)) { File.Delete(f); done.Add(L.F("{0} removed", n)); }
            }
            foreach (string name in PackAddons)
                foreach (string d in new[] { AddOn(client, name), AddOn(client, name) + Off })
                    if (Directory.Exists(d)) { Directory.Delete(d, true); done.Add(L.F("Add-on {0} removed", Path.GetFileName(d))); }
            return string.Join("\n", done);
        }

        /// <summary>
        /// The pack as installed in the owner's game, for the players' client pack and launcher (switched-off parts
        /// included under their active names). The launcher downloads the archives and LuxVelum only for players who
        /// switch them on. Paths relative to the game folder.
        /// </summary>
        public static List<KeyValuePair<string, string>> PlayerFiles(string client)
        {
            var list = new List<KeyValuePair<string, string>>();
            if (client == null || !Directory.Exists(client)) return list;
            foreach (var a in Archives)
            {
                string f = Find(Data(client), a[1]) ?? Find(Data(client), a[1] + Off);
                if (f != null) list.Add(new KeyValuePair<string, string>("Data/" + a[1], f));
            }
            foreach (string n in LuxFiles.Concat(new[] { "LuxVelum.ini" }))
            {
                string f = Path.Combine(client, n);
                if (File.Exists(f)) list.Add(new KeyValuePair<string, string>(n, f));
            }
            foreach (string name in PackAddons)
            {
                string dir = AddOn(client, name);
                if (!Directory.Exists(dir)) continue;
                string prefix = Path.GetFullPath(dir).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                    list.Add(new KeyValuePair<string, string>("Interface/AddOns/" + name + "/" + Path.GetFullPath(f).Substring(prefix.Length).Replace('\\', '/'), f));
            }
            return list;
        }

        static void Copy(string source, string target, Action<string, long, long> report)
        {
            string tmp = target + ".part";
            long total = new FileInfo(source).Length, copied = 0;
            string title = L.F("Copying {0} ...", Path.GetFileName(target));
            var buffer = new byte[4 * 1024 * 1024];
            using (var from = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var to = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                int n;
                while ((n = from.Read(buffer, 0, buffer.Length)) > 0)
                {
                    to.Write(buffer, 0, n);
                    copied += n;
                    report(title, copied, total);
                }
            }
            if (File.Exists(target)) File.Delete(target);
            File.Move(tmp, target);
        }

        static void CopyDir(string from, string to)
        {
            foreach (string f in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                string rel = f.Substring(from.TrimEnd('\\', '/').Length + 1);
                if (rel.Split('\\', '/').Any(p => p == ".vscode" || p == ".git")) continue;
                string dest = Path.Combine(to, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.Copy(f, dest, true);
            }
        }
    }
}

