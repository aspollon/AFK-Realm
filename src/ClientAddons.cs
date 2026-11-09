using System;
using System.Collections.Generic;
using System.Diagnostics;
#if !AFK_WEB
using System.Drawing;
#endif
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
#if !AFK_WEB
using System.Windows.Forms;
#endif

namespace CoAInstaller
{
    enum AddonState { Current, Missing, Outdated }

    /// <summary>An add-on for the game client that a module brings (the "client" part of its afk-realm.json).</summary>
    class ClientAddon
    {
        public string Module = "", Name = "", Source = "", Why = "", WhyEnglish = "";
        public bool ClearCache;
        public AddonState State;
    }

    /// <summary>
    /// The game client half of the modules: a module can name add-ons in its afk-realm.json
    /// ("client": {"addons": ["client/AddOns/Name"], "clearCache": true}). AFK Realm copies them
    /// into Interface\AddOns of the game client, clears the client's cache when the module asks for
    /// it (so the client forgets old levels and values of items, creatures and quests), takes an
    /// add-on out again when its module is removed, and packs them into a ZIP for other players.
    /// What it installed is recorded in Dependencies\client-addons.txt.
    /// </summary>
    static class ClientAddons
    {
        static string RecordFile(Install inst) { return Path.Combine(inst.Root, "Dependencies", "client-addons.txt"); }
        /// <summary>The kind of the server opened (Install.Kind): each kind has a game client of its own - CoA, EsteriaWoW
        /// and a stock 3.3.5a client are different folders - so the folder remembered is kept per kind.</summary>
        public static string ServerKind = global::CoAInstaller.Install.KindCoa;
        static string FolderFile
        {
            get
            {
                string name = ServerKind == global::CoAInstaller.Install.KindCoa ? "client-folder.txt" : "client-folder-" + ServerKind + ".txt";
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Product.FileStem, name);
            }
        }

        /// <summary>The EsteriaWoW client: its own archives (Patch-ZzzzzzzEsteriaClient.MPQ and the like).</summary>
        public static bool IsEsteriaClient(string client)
        {
            try
            {
                string data = Os.Resolve(client ?? "", "Data");
                return Directory.Exists(data) && Directory.EnumerateFiles(data, "*.mpq", SearchOption.TopDirectoryOnly)
                    .Any(f => Path.GetFileName(f).IndexOf("EsteriaClient", StringComparison.OrdinalIgnoreCase) >= 0);
            }
            catch { return false; }
        }

        /// <summary>A Conquest of Azeroth client: CoA64.exe or Ascension.exe in the game folder.</summary>
        public static bool IsCoaClient(string client)
        {
            try { return File.Exists(Os.Resolve(client ?? "", "CoA64.exe")) || File.Exists(Os.Resolve(client ?? "", "Ascension.exe")); }
            catch { return false; }
        }

        /// <summary>The game client folder chosen last time, or null.</summary>
        public static string ClientFolder
        {
            get { try { var d = File.Exists(FolderFile) ? File.ReadAllText(FolderFile).Trim() : ""; return d.Length > 0 ? d : null; } catch { return null; } }
            set { try { Directory.CreateDirectory(Path.GetDirectoryName(FolderFile)); File.WriteAllText(FolderFile, value ?? ""); } catch { } }
        }

        /// <summary>A game folder has a "Data" folder with the game's .MPQ archives.</summary>
        public static bool IsClientFolder(string dir)
        {
            try { return !string.IsNullOrEmpty(dir) && HasArchives(dir.TrimEnd('\\', '/')); }
            catch { return false; }
        }

        /// <summary>The game folder (the one with "Data") behind what the user picked: the game folder itself, the
        /// "Data" folder or a folder inside it, or a folder of a repack with the game somewhere below it. Null when
        /// there is no game folder (see also RacePacker.ArchivesOf, which reads the archives the same way).</summary>
        public static string GameRoot(string dir)
        {
            try
            {
                dir = (dir ?? "").Trim().TrimEnd('\\', '/');
                if (dir.Length == 0 || !Directory.Exists(dir)) return null;
                if (HasArchives(dir)) return dir;                                     // the game folder itself
                string parent = Path.GetDirectoryName(dir);
                string up = Named(Path.GetFileName(dir), "Data") ? parent             // the "Data" folder itself
                    : Named(Path.GetFileName(parent ?? ""), "Data") ? Path.GetDirectoryName(parent) : null;  // a folder in "Data"
                if (up != null && HasArchives(up)) return up;
                foreach (var d in Below(dir, 3)) if (HasArchives(d)) return d;        // a repack unpacked into a folder of its own
                return null;
            }
            catch { return null; }
        }

        static bool HasArchives(string dir)
        {
            string data = Os.Resolve(dir ?? "", "Data");
            return Directory.Exists(data) && Directory.EnumerateFiles(data).Any(f => f.EndsWith(".mpq", StringComparison.OrdinalIgnoreCase));
        }

        static bool Named(string name, string want) { return string.Equals(name, want, StringComparison.OrdinalIgnoreCase); }

        /// <summary>The folders below a folder, up to the given depth.</summary>
        static IEnumerable<string> Below(string folder, int depth)
        {
            if (depth == 0) yield break;
            string[] dirs;
            try { dirs = Directory.GetDirectories(folder); } catch { yield break; }
            foreach (var d in dirs.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                yield return d;
                foreach (var e in Below(d, depth - 1)) yield return e;
            }
        }

        static string AddonsDir(string client) { return Os.Resolve(client, "Interface/AddOns"); }

        /// <summary>The add-ons the installed modules bring, with their state in the given client folder (null: all count as missing).</summary>
        public static List<ClientAddon> Find(Install inst, string client)
        {
            var result = new List<ClientAddon>();
            string modules = ModuleCatalog.ModulesDir(inst);
            if (!Directory.Exists(modules)) return result;
            var json = new JavaScriptSerializer();
            foreach (var dir in Directory.GetDirectories(modules).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                string file = Path.Combine(dir, ModuleCatalog.ManifestName);
                if (!File.Exists(file)) { AddFoundAddons(dir, client, result); continue; }
                Dictionary<string, object> clientPart = null;
                object v;
                try
                {
                    var data = json.DeserializeObject(File.ReadAllText(file, Encoding.UTF8)) as Dictionary<string, object>;
                    if (data != null && data.TryGetValue("client", out v)) clientPart = v as Dictionary<string, object>;
                }
                catch { continue; }
                if (clientPart == null) continue;
                bool clear = clientPart.TryGetValue("clearCache", out v) && v is bool && (bool)v;
                string whyEnglish = clientPart.TryGetValue("why", out v) ? Convert.ToString(v) : "", why = L.Outside(whyEnglish);
                var list = clientPart.TryGetValue("addons", out v) ? v as object[] : null;
                if (list == null) continue;
                string root = Path.GetFullPath(dir).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                foreach (var entry in list.OfType<string>())
                {
                    string source;
                    try { source = Path.GetFullPath(Path.Combine(dir, entry.Replace('/', Path.DirectorySeparatorChar))); } catch { continue; }
                    string name = Path.GetFileName(source.TrimEnd('\\', '/'));
                    // Only a folder inside the module, with a plain name and a .toc of that name, counts as an add-on.
                    if (!source.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !Regex.IsMatch(name, @"^[\w.\-]+$") ||
                        !File.Exists(Path.Combine(source, name + ".toc")))
                        continue;
                    var addon = new ClientAddon { Module = Path.GetFileName(dir), Name = name, Source = source, ClearCache = clear, Why = why, WhyEnglish = whyEnglish };
                    addon.State = client == null ? AddonState.Missing : StateOf(addon, client);
                    result.Add(addon);
                }
            }
            AddEmbeddedDungeonClear(inst, client, result);
            return result;
        }

        /// <summary>
        /// A module without an afk-realm.json - most modules of the AzerothCore catalog, those EsteriaWoW brings, a module
        /// added by hand to a CoA server -
        /// can still carry an add-on for the game: a folder with a .toc file of its own name (Battlemon/Battlemon.toc).
        /// Such folders count as the module's add-ons; folders inside an add-on (its libraries) do not.
        /// </summary>
        static void AddFoundAddons(string module, string client, List<ClientAddon> result)
        {
            const string why = "An add-on in the module's folder.";
            var found = new List<string>();
            Action<string, int> look = null;
            look = (folder, depth) =>
            {
                string name = Path.GetFileName(folder);
                if (name.StartsWith(".")) return;
                if (Regex.IsMatch(name, @"^[\w.\-]+$") && File.Exists(Path.Combine(folder, name + ".toc"))) { found.Add(folder); return; }
                if (depth == 0) return;
                string[] subs;
                try { subs = Directory.GetDirectories(folder); } catch { return; }
                foreach (var sub in subs.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) look(sub, depth - 1);
            };
            string[] top;
            try { top = Directory.GetDirectories(module); } catch { return; }
            foreach (var sub in top.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) look(sub, 4);
            foreach (var source in found)
            {
                string name = Path.GetFileName(source);
                // Blizzard's own interface parts are part of every client; two modules with the same add-on give it once.
                if (name.StartsWith("Blizzard_", StringComparison.OrdinalIgnoreCase) || result.Any(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
                var addon = new ClientAddon { Module = Path.GetFileName(module), Name = name, Source = source, Why = L.T(why), WhyEnglish = why };
                addon.State = client == null ? AddonState.Missing : StateOf(addon, client);
                result.Add(addon);
            }
        }

        // ---------------------------------------------------------------- add-ons of the owner's own game

        static string SharedFile(Install inst) { return Path.Combine(inst.Root, "Dependencies", "shared-addons.txt"); }

        /// <summary>The add-ons of the owner's game he hands out to his players with the client pack and the AFK Client.</summary>
        public static List<string> SharedNames(Install inst)
        {
            try { return File.Exists(SharedFile(inst)) ? File.ReadAllLines(SharedFile(inst)).Select(l => l.Trim()).Where(l => Regex.IsMatch(l, @"^[\w.\- ]+$")).Distinct(StringComparer.OrdinalIgnoreCase).ToList() : new List<string>(); }
            catch { return new List<string>(); }
        }

        public static void SetSharedNames(Install inst, IEnumerable<string> names)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SharedFile(inst)));
            File.WriteAllLines(SharedFile(inst), names.Where(n => Regex.IsMatch(n ?? "", @"^[\w.\- ]+$")).Distinct(StringComparer.OrdinalIgnoreCase), new UTF8Encoding(false));
        }

        /// <summary>The add-ons in the game's Interface\AddOns that are not Blizzard's own (they come with every client).</summary>
        public static List<string> OwnAddonFolders(string client)
        {
            string dir = client == null ? null : AddonsDir(client);
            if (dir == null || !Directory.Exists(dir)) return new List<string>();
            return Directory.GetDirectories(dir).Select(Path.GetFileName)
                .Where(n => !n.StartsWith("Blizzard_", StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(n, @"^[\w.\- ]+$"))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>The chosen add-ons as entries of the pack, read from the owner's game; one a module brings already is left to the module.</summary>
        public static List<ClientAddon> Shared(Install inst, string client, List<ClientAddon> fromModules)
        {
            const string why = "An add-on from the server owner's game.";
            var result = new List<ClientAddon>();
            string dir = client == null ? null : AddonsDir(client);
            if (dir == null) return result;
            foreach (var name in SharedNames(inst))
            {
                string source = Path.Combine(dir, name);
                if (!Directory.Exists(source) || fromModules.Any(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
                result.Add(new ClientAddon { Module = "-", Name = name, Source = source, Why = L.T(why), WhyEnglish = why, State = AddonState.Current });
            }
            return result;
        }

        /// <summary>mod-dungeon-clear keeps its companion addon in a separate repository. AFK Realm carries the
        /// addon as an embedded ZIP, so installing the module also installs and removes the DungeonClear addon.</summary>
        static void AddEmbeddedDungeonClear(Install inst, string client, List<ClientAddon> result)
        {
            if (result.Any(a => a.Name.Equals("DungeonClear", StringComparison.OrdinalIgnoreCase))) return;
            string module = Path.Combine(ModuleCatalog.ModulesDir(inst), "mod-dungeon-clear");
            if (!Directory.Exists(module)) return;
            string source = EnsureDungeonClearAddon(inst);
            if (source == null) return;
            const string why = "The DungeonClear panel starts, pauses and steers the tank bot through a dungeon.";
            var addon = new ClientAddon { Module = "mod-dungeon-clear", Name = "DungeonClear", Source = source, Why = L.T(why), WhyEnglish = why };
            addon.State = client == null ? AddonState.Missing : StateOf(addon, client);
            result.Add(addon);
        }

        static string EnsureDungeonClearAddon(Install inst)
        {
            const string resource = "CoAInstaller.dungeonclear.zip";
            string target = Path.Combine(inst.Root, "Dependencies", "ClientAddons", "DungeonClear");
            string marker = Path.Combine(target, ".afk-version");
            try
            {
                if (File.Exists(Path.Combine(target, "DungeonClear.toc")) && File.Exists(marker) && File.ReadAllText(marker).Trim() == Product.Version)
                    return target;
                if (Directory.Exists(target)) Directory.Delete(target, true);
                Directory.CreateDirectory(target);
                using (var stream = typeof(ClientAddons).Assembly.GetManifestResourceStream(resource))
                {
                    if (stream == null) return null;
                    using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
                    {
                        string root = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                        foreach (var entry in zip.Entries)
                        {
                            if (string.IsNullOrEmpty(entry.Name)) continue;
                            string dest = Path.GetFullPath(Path.Combine(target, entry.FullName));
                            if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                            Directory.CreateDirectory(Path.GetDirectoryName(dest));
                            entry.ExtractToFile(dest, true);
                        }
                    }
                }
                File.WriteAllText(marker, Product.Version);
                return target;
            }
            catch { return null; }
        }

        static AddonState StateOf(ClientAddon addon, string client)
        {
            string target = Path.Combine(AddonsDir(client), addon.Name);
            if (!Directory.Exists(target)) return AddonState.Missing;
            return Fingerprint(addon.Source) == Fingerprint(target) ? AddonState.Current : AddonState.Outdated;
        }

        /// <summary>One hash over the names and contents of every file in a folder.</summary>
        static string Fingerprint(string dir)
        {
            using (var sha = SHA256.Create())
            {
                foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    // The version marker belongs to the installer, not to the add-on. Excluding it means a
                    // new AFK Realm version does not make an unchanged embedded add-on look outdated.
                    if (Path.GetFileName(file).Equals(".afk-version", StringComparison.OrdinalIgnoreCase)) continue;
                    var name = Encoding.UTF8.GetBytes(file.Substring(dir.Length).Replace('\\', '/').ToLowerInvariant() + "\n");
                    sha.TransformBlock(name, 0, name.Length, null, 0);
                    var data = File.ReadAllBytes(file);
                    sha.TransformBlock(data, 0, data.Length, null, 0);
                }
                sha.TransformFinalBlock(new byte[0], 0, 0);
                return BitConverter.ToString(sha.Hash);
            }
        }

        /// <summary>What AFK Realm put into the client: add-on name -> module.</summary>
        static Dictionary<string, string> Record(Install inst)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(RecordFile(inst)))
                    foreach (var line in File.ReadAllLines(RecordFile(inst)))
                    {
                        var f = line.Split('|');
                        if (f.Length >= 2 && Regex.IsMatch(f[1], @"^[\w.\-]+$")) d[f[1]] = f[0];
                    }
            }
            catch { }
            return d;
        }

        static void SaveRecord(Install inst, Dictionary<string, string> record)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RecordFile(inst)));
            File.WriteAllLines(RecordFile(inst), record.OrderBy(r => r.Key, StringComparer.OrdinalIgnoreCase).Select(r => r.Value + "|" + r.Key), new UTF8Encoding(false));
        }

        /// <summary>Add-ons AFK Realm installed whose module is gone: they are taken out of the client.</summary>
        public static List<string> Leftovers(Install inst, List<ClientAddon> current, string client)
        {
            return Record(inst).Keys.Where(name => !current.Any(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) &&
                (client == null || Directory.Exists(Path.Combine(AddonsDir(client), name)))).ToList();
        }

        /// <summary>True while a program from the client folder runs (the game or its launcher).</summary>
        public static bool ClientRunning(string client) { return GameProcesses(client, true).Count > 0; }

        /// <summary>
        /// The game and its launcher: programs started from the game folder itself. Programs in its subfolders do not
        /// count - many put the server into the game folder (G:\Games\AscensionWoW\Server\...), and its worldserver,
        /// authserver and database counted as "the game is running": Play refused to start, the CoA Custom install
        /// refused while the server ran (and would have closed the server). AFK Realm itself never counts.
        /// </summary>
        static List<Process> GameProcesses(string client, bool dispose)
        {
            var found = new List<Process>();
            string root = Path.GetFullPath(client).TrimEnd('\\', '/');
            int self = Process.GetCurrentProcess().Id;
            foreach (var p in Process.GetProcesses())
            {
                bool keep = false;
                try
                {
                    // AFK Realm itself may be started from the game folder. It is the manager,
                    // not the game or launcher, and must not block its own install.
                    if (p.Id != self && !p.ProcessName.StartsWith(Product.FileStem, StringComparison.OrdinalIgnoreCase))
                    {
                        var mm = p.MainModule;
                        string dir = mm != null && mm.FileName != null ? Path.GetDirectoryName(mm.FileName) : null;
                        keep = dir != null && string.Equals(dir.TrimEnd('\\', '/'), root, StringComparison.OrdinalIgnoreCase);
                    }
                }
                catch { }
                if (keep) found.Add(p);
                if (!keep || dispose) p.Dispose();
            }
            return found;
        }

        /// <summary>Closes the game and its launcher started from this folder (asked to close, ended after 10 seconds).</summary>
        public static void CloseClient(string client)
        {
            var found = GameProcesses(client, false);
            foreach (var p in found) try { p.CloseMainWindow(); } catch { }
            foreach (var p in found)
            {
                try { if (!p.WaitForExit(10000)) { p.Kill(); p.WaitForExit(5000); } } catch { }
                p.Dispose();
            }
        }

        static void CopyFolder(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
            foreach (var dir in Directory.GetDirectories(from)) CopyFolder(dir, Path.Combine(to, Path.GetFileName(dir)));
        }

        /// <summary>Copies the add-ons into the client, takes out the leftovers and clears the cache when asked. Returns what was done.</summary>
        public static string Install(Install inst, string client, List<ClientAddon> addons, List<string> leftovers, bool clearCache)
        {
            var done = new List<string>();
            var record = Record(inst);
            string target = AddonsDir(client);
            Directory.CreateDirectory(target);
            foreach (var addon in addons)
            {
                string to = Path.Combine(target, addon.Name);
                if (Directory.Exists(to)) Directory.Delete(to, true);
                CopyFolder(addon.Source, to);
                record[addon.Name] = addon.Module;
                done.Add(addon.State == AddonState.Outdated ? L.F("{0} updated", addon.Name) : addon.State == AddonState.Current ? L.F("{0} copied again", addon.Name) : L.F("{0} installed", addon.Name));
            }
            // The DungeonClear add-on only loads from a folder with exactly that name. Remove the names a
            // repository download or an old manual installation leaves behind.
            if (addons.Any(a => a.Name.Equals("DungeonClear", StringComparison.OrdinalIgnoreCase)))
            {
                foreach (string wrong in new[] { "mod-dungeon-clear-addon", "mod-dungeon-clear-addon-main", "DungeonClear-main", "DungeonClear-master" })
                {
                    string folder = Path.Combine(target, wrong);
                    if (!Directory.Exists(folder) || !File.Exists(Path.Combine(folder, "DungeonClear.toc"))) continue;
                    Directory.Delete(folder, true);
                    record.Remove(wrong);
                    done.Add(L.F("{0} removed (the add-on folder must be named DungeonClear)", wrong));
                }
            }
            foreach (var name in leftovers)
            {
                string folder = Path.Combine(target, name);
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
                record.Remove(name);
                done.Add(L.F("{0} removed (its module is no longer installed)", name));
            }
            SaveRecord(inst, record);
            if (clearCache)
            {
                string cache = Os.Resolve(client, "Cache");
                if (Directory.Exists(cache)) { Directory.Delete(cache, true); done.Add(L.T("the client's cache cleared")); }
                else done.Add(L.T("the client had no cache to clear"));
            }
            return string.Join("\n", done);
        }

        /// <summary>A ZIP for other players: the add-ons in Interface\AddOns, to be unpacked into the game folder, and a note on what to do.</summary>
        public static void Export(string file, List<ClientAddon> addons, Install inst, string client, ClientPatch.Plan patch) { Export(file, addons, inst, client, patch, null); }

        public static void Export(string file, List<ClientAddon> addons, Install inst, string client, ClientPatch.Plan patch, Action<string, int, int> progress)
        {
            if (File.Exists(file)) File.Delete(file);
            bool withPatch = patch != null && !patch.Empty && client != null;
            using (var zip = ZipFile.Open(file, ZipArchiveMode.Create))
            {
                if (withPatch) ClientPatch.AddToZip(zip, inst, client, patch, progress);
                if (progress != null) progress(L.T("Adding the add-ons …"), 0, 0);
                foreach (var addon in addons)
                    foreach (var path in Directory.GetFiles(addon.Source, "*", SearchOption.AllDirectories))
                        zip.CreateEntryFromFile(path, "Interface/AddOns/" + addon.Name + "/" + path.Substring(addon.Source.Length).TrimStart('\\', '/').Replace('\\', '/'), CompressionLevel.Optimal);
                string realm = GameLaunch.AddToZip(zip, inst);
                // In the language of the server owner, and in English below it for players who speak another one.
                var note = new StringBuilder(ReadMe(addons, patch, withPatch, false));
                if (realm != null) note.AppendLine().AppendLine(realm);
                if (L.Code != "en") note.AppendLine().AppendLine("----------------------------------------------------------------").AppendLine().Append(ReadMe(addons, patch, withPatch, true));
                var entry = zip.CreateEntry("READ ME FIRST.txt");
                using (var w = new StreamWriter(entry.Open(), new UTF8Encoding(false))) w.Write(note.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n"));
            }
        }

        /// <summary>The note in the ZIP: in the language shown, or in English.</summary>
        static string ReadMe(List<ClientAddon> addons, ClientPatch.Plan patch, bool withPatch, bool english)
        {
            Func<string, string> t = s => english ? s : L.T(s);
            Func<string, object[], string> f = (s, a) => english ? string.Format(s, a) : L.F(s, a);
            var note = new StringBuilder();
            note.AppendLine(f(L.N("Files for the game client, from the modules of this server ({0})."), new object[] { Product.Name }));
            note.AppendLine();
            note.AppendLine(t(L.N("1. Close the game and its launcher.")));
            note.AppendLine(t(L.N("2. Unpack this ZIP into your game folder (the folder with the \"Data\" folder).")));
            note.AppendLine("   " + t(L.N("Replace files that are already there.")) + (addons.Count > 0 ? " " + t(L.N("The add-ons land in Interface\\AddOns.")) : ""));
            if (withPatch) note.AppendLine("   " + f(L.N("Data\\{0} is the client patch of the modules."), new object[] { ClientPatch.ArchiveName }));
            var root = withPatch ? patch.RootFiles.Where(r => r.Zip).Select(r => r.To).ToList() : new List<string>();
            if (root.Count > 0)
            {
                note.AppendLine("   " + f(L.N("Next to Ascension.exe: {0}."), new object[] { string.Join(", ", root) }));
                if (root.Any(r => r.Equals("Ascension-b.exe", StringComparison.OrdinalIgnoreCase)))
                    note.AppendLine("   " + t(L.N("Start the game with Ascension-b.exe (the exe of CoA Custom), not with Ascension.exe: only it shows the races of CoA Custom and all their looks.")));
                if (root.Any(r => r.Equals("Ascension.exe", StringComparison.OrdinalIgnoreCase)))
                    note.AppendLine("   " + t(L.N("The ZIP brings its own Ascension.exe (the one of CoA Custom). Keep a copy of yours first, as Ascension.exe.old for example, if you want to go back.")));
                if (root.Any(r => r.Equals("EsteriaAppearance.dll", StringComparison.OrdinalIgnoreCase)))
                    note.AppendLine("   " + t(L.N("The game folder must be writable: EsteriaAppearance.dll keeps the looks of the races in Interface\\AddOns\\EsteriaAppearanceCache. A game under C:\\Program Files is better moved to another folder.")));
                if (root.Any(r => r.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
                    note.AppendLine("   " + t(L.N("If your antivirus program removes a .dll of the ZIP, allow it there.")));
            }
            if (addons.Any(a => a.ClearCache) || (withPatch && patch.ClearCache))
                note.AppendLine(t(L.N("3. Delete the folder \"Cache\" in your game folder, so the game forgets old values of items, creatures and quests.")));
            note.AppendLine();
            if (withPatch)
            {
                note.AppendLine(t(L.N("The client patch was built from the game client of the server owner. It fits the same version of the " +
                    "Conquest of Azeroth client; after the server owner updates it, get a new ZIP. It contains changed files " +
                    "of the game client: it is meant for the players of this server, not for passing on.")));
                foreach (var why in english ? patch.WhyEnglish : patch.Why) note.AppendLine("  " + why);
                note.AppendLine();
            }
            if (addons.Count > 0) note.AppendLine(t(L.N("Add-ons:")));
            foreach (var addon in addons)
            {
                string why = english ? addon.WhyEnglish : addon.Why;
                note.AppendLine("  " + addon.Name + "  " + f(L.N("(module {0})"), new object[] { addon.Module }) + (why.Length > 0 ? " - " + why : ""));
            }
            note.AppendLine();
            note.AppendLine(t(L.N("Do the same again after the server owner updates these modules.")));
            return note.ToString();
        }

        /// <summary>What is still to be done in the client, as one key (empty: nothing).</summary>
        public static string Pending(Install inst)
        {
            try
            {
                string client = ClientFolder;
                if (client != null && !IsClientFolder(client)) client = null;
                var addons = Find(inst, client);
                var left = Leftovers(inst, addons, client);
                var open = addons.Where(a => a.State != AddonState.Current).Select(a => a.Name + ":" + a.State).Concat(left.Select(l => l + ":left")).ToList();
                string problem;
                var patch = ClientPatch.StateOf(inst, client, ClientPatch.Collect(inst), out problem);
                if (patch != PatchState.None && patch != PatchState.Current) open.Add("client-patch:" + patch);
                return string.Join(",", open);
            }
            catch { return ""; }
        }
    }

#if !AFK_WEB
    /// <summary>The add-ons of the modules and the game client they go into.</summary>
    class ClientDialog : AfkForm
    {
        readonly Install inst;
        readonly TextBox folder = Ui.Input(440);
        readonly ListView list = new ListView { View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, Dock = DockStyle.Fill, Font = Ui.Base, BorderStyle = BorderStyle.FixedSingle };
        readonly Toggle clearCache = new Toggle(L.T("Clear the game's cache (recommended: the game then forgets the old levels and values of items, creatures and quests)"));
        readonly Label state = new Label { AutoSize = true, Font = Ui.Small, ForeColor = Ui.Muted, MaximumSize = new Size(780, 0), Margin = new Padding(0, 4, 0, 0) };
        readonly Button install = Ui.Primary(L.T("Install add-ons"));
        readonly Button export = Ui.Secondary(L.T("Export as ZIP …"));
        readonly Button download = Ui.Secondary(L.T("Download / update client …"));
        List<ClientAddon> addons = new List<ClientAddon>();
        List<string> leftovers = new List<string>();
        ClientPatch.Plan patch = new ClientPatch.Plan();
        PatchState patchState = PatchState.None;
        string patchProblem;

        public ClientDialog(Install i, string reason = null)
        {
            inst = i;
            Text = Product.Name + " – " + L.T("Game client"); Font = Ui.Base; BackColor = Ui.Surface; ShowIcon = false;
            StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(840, 520); MinimumSize = new Size(720, 440);

            var top = Ui.Column(); top.Dock = DockStyle.Top; top.AutoSize = true; top.Padding = new Padding(16, 12, 16, 4);
            if (reason != null) top.Controls.Add(new Label { Text = reason, AutoSize = true, Font = Ui.Bold, ForeColor = Ui.AccentText, MaximumSize = new Size(800, 0), Margin = new Padding(0, 0, 0, 6) });
            top.Controls.Add(Ui.Para(L.F("Some modules bring an add-on for the game client, or change files of the client. {0} copies the add-ons into Interface\\AddOns " +
                "of your game and builds the client patch (Data\\{1}) from your own game client, keeps both up to date and takes them out again " +
                "when their module is removed. Other players can get everything as a ZIP.", Product.Name, ClientPatch.ArchiveName), 800));
            var row = Ui.Row();
            row.Controls.Add(Ui.FieldLabel(L.T("Game folder"), 110));
            row.Controls.Add(folder);
            var browse = Ui.Secondary(L.T("Browse …"));
            browse.Click += (s, e) =>
            {
                using (var d = new FolderBrowserDialog { Description = L.T("Choose your game folder (it contains the \"Data\" folder)") })
                {
                    string root = ClientAddons.GameRoot(folder.Text);
                    if (root != null) d.SelectedPath = root;
                    if (d.ShowDialog(this) == DialogResult.OK) { folder.Text = ClientAddons.GameRoot(d.SelectedPath) ?? d.SelectedPath; Refresh_(); }
                }
            };
            row.Controls.Add(browse);
            download.Margin = new Padding(6, 0, 0, 0);
            row.Controls.Add(download);
            top.Controls.Add(row);

            list.Columns.Add(L.T("Add-on or patch"), 190);
            list.Columns.Add(L.T("Module"), 170);
            list.Columns.Add(L.T("In your game"), 150);
            list.Columns.Add(L.T("What it does"), 300);
            var listPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 6, 16, 4) };
            listPanel.Controls.Add(list);

            var under = Ui.Column(); under.Dock = DockStyle.Bottom; under.AutoSize = true; under.Padding = new Padding(16, 0, 16, 6);
            clearCache.Wrap(790);
            under.Controls.Add(clearCache); under.Controls.Add(state);

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58, Padding = new Padding(14, 10, 14, 10), FlowDirection = FlowDirection.RightToLeft, BackColor = Ui.Panel, WrapContents = false };
            var close = Ui.Secondary(L.T("Close")); close.Click += (s, e) => Close();
            bottom.Controls.Add(close); bottom.Controls.Add(export); bottom.Controls.Add(install);

            Controls.Add(listPanel); Controls.Add(under); Controls.Add(bottom); Controls.Add(top);
            folder.Text = ClientAddons.ClientFolder ?? "";
            folder.Leave += (s, e) => { if (folder.Text.Trim() != checkedFolder && Visible) Refresh_(); };
            install.Click += (s, e) => Install_();
            export.Click += (s, e) => Export_();
            download.Click += (s, e) => Download_();
            Shown += (s, e) => { Refresh_(); CheckDownloadUpdate(); };
        }

        /// <summary>The button says when a new client manifest version is available. The client itself is not
        /// changed here; Download_ plans and copies only the files that differ.</summary>
        void CheckDownloadUpdate()
        {
            string client = Client;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var manifest = ClientDownload.Latest();
                    string installed = client == null ? null : ClientDownload.InstalledVersion(client);
                    BeginInvoke((Action)(() =>
                    {
                        if (IsDisposed) return;
                        download.Text = installed != null && installed != manifest.Version
                            ? L.F("Update client to {0} …", manifest.Version)
                            : client != null ? L.T("Update / repair client …") : L.F("Download client {0} …", manifest.Version);
                    }));
                }
                catch { }
            });
        }

        string checkedFolder;

        string Client { get { return ClientAddons.GameRoot(folder.Text); } }

        void Refresh_()
        {
            string client = Client;
            checkedFolder = folder.Text.Trim();
            string readError = null;
            // reading the client and building the patch to compare takes a while with races of the race folder
            Busy.Run(this, L.T("Checking what your game needs …"), report =>
            {
                try { addons = ClientAddons.Find(inst, client); leftovers = ClientAddons.Leftovers(inst, addons, client); }
                catch (Exception ex) { addons = new List<ClientAddon>(); leftovers = new List<string>(); readError = L.T("The modules could not be read:") + " " + ex.Message; }
                patchProblem = null;
                try { patch = ClientPatch.Collect(inst); patchState = ClientPatch.StateOf(inst, client, patch, out patchProblem); }
                catch (Exception ex) { patch = new ClientPatch.Plan(); patchState = PatchState.None; patchProblem = ex.Message; }
            });
            if (readError != null) state.Text = readError;
            list.BeginUpdate(); list.Items.Clear();
            foreach (var a in addons)
            {
                var it = new ListViewItem(a.Name) { Tag = a };
                it.SubItems.Add(a.Module);
                it.SubItems.Add(client == null ? "-" : a.State == AddonState.Current ? L.T("up to date") : a.State == AddonState.Outdated ? L.T("older version") : L.T("not installed"));
                it.SubItems.Add(a.Why);
                it.ForeColor = client == null || a.State == AddonState.Current ? Ui.Text : Ui.Warn;
                list.Items.Add(it);
            }
            if (patchState != PatchState.None)
            {
                var it = new ListViewItem(L.F("Client patch ({0})", ClientPatch.ArchiveName));
                it.SubItems.Add(patch.Empty ? L.T("(removed)") : string.Join(", ", patch.Modules));
                it.SubItems.Add(client == null ? "-" : patchState == PatchState.Current ? L.T("up to date") : patchState == PatchState.Outdated ? L.T("to be built again") :
                    patchState == PatchState.Leftover ? L.T("will be taken out") : L.T("not installed"));
                it.SubItems.Add(patchProblem ?? (patch.Empty ? L.T("No module needs it any more.") : string.Join(" ", patch.Why)));
                it.ForeColor = client == null || patchState == PatchState.Current ? Ui.Text : Ui.Warn;
                list.Items.Add(it);
            }
            foreach (var name in leftovers)
            {
                var it = new ListViewItem(name) { ForeColor = Ui.Warn };
                it.SubItems.Add(L.T("(removed)")); it.SubItems.Add(L.T("will be taken out")); it.SubItems.Add(L.T("Its module is no longer installed."));
                list.Items.Add(it);
            }
            list.EndUpdate();
            bool patchDue = patchState == PatchState.Missing || patchState == PatchState.Outdated || patchState == PatchState.Leftover;
            bool due = addons.Any(a => a.State != AddonState.Current) || leftovers.Count > 0 || patchDue;
            bool anything = addons.Count > 0 || leftovers.Count > 0 || patchState != PatchState.None;
            clearCache.Visible = addons.Any(a => a.ClearCache) || patch.ClearCache;
            clearCache.Checked = clearCache.Visible && (addons.Any(a => a.ClearCache && a.State != AddonState.Current) || (patch.ClearCache && patchDue));
            export.Enabled = addons.Count > 0 || !patch.Empty;
            install.Enabled = client != null && anything;
            install.Text = due || !anything ? L.T("Install") : L.T("Install again");
            if (!anything) state.Text = L.T("None of the installed modules brings an add-on or a client patch for the game client.");
            else if (folder.Text.Trim().Length == 0) state.Text = L.T("Choose your game folder, the one with the \"Data\" folder.");
            else if (client == null) state.Text = L.T("This is not a game folder: it needs a \"Data\" folder with the game's .MPQ archives.") + " " + L.T("You can also choose the \"Data\" folder itself.");
            else if (patchProblem != null) state.Text = L.T("The client patch cannot be built:") + " " + patchProblem;
            else state.Text = due ? L.T("Not everything is in your game yet. Close the game and its launcher, then choose \"Install\".") : L.T("Your game has everything the installed modules bring.");
            // Haranir and Esteria Vulpera load many files from paths below the client; Windows' 260-character
            // limit starts to bite when the game folder itself is deep. A short path is the reliable fix.
            string longPath = client != null && client.Length > 100
                ? L.F("This game folder path is long ({0} characters). Ascension.exe is not long-path aware, so custom textures may be missing; move the client to a short path such as C:\\CoA.", client.Length)
                : null;
            if (longPath != null) state.Text = longPath + " " + state.Text;
            state.ForeColor = client != null && (due || longPath != null) ? Ui.Warn : Ui.Muted;
        }

        void Download_()
        {
            string target = Client;
            if (target == null && folder.Text.Trim().Length > 0 && Directory.Exists(folder.Text.Trim()))
                target = folder.Text.Trim();
            if (target == null)
            {
                string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "CoA Client");
                try { Directory.CreateDirectory(fallback); } catch { fallback = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); }
                using (var d = new FolderBrowserDialog { Description = L.T("Choose the folder for the game client. It can be empty; about 43 GB will be downloaded into it."), SelectedPath = fallback })
                {
                    if (d.ShowDialog(this) != DialogResult.OK) return;
                    target = ClientAddons.GameRoot(d.SelectedPath) ?? d.SelectedPath;
                }
                bool hasFiles = Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any();
                if (hasFiles && !ClientAddons.IsClientFolder(target) &&
                    !Ui.Confirm(this, L.F("The folder {0} already contains files and is not a game folder. Download the client into it anyway?", target))) return;
            }

            if (ClientAddons.ClientRunning(target))
            {
                Ui.Error(this, L.T("The game or its launcher is running from this folder. Close it before downloading or updating the client."));
                return;
            }

            ClientDownload.Manifest manifest;
            try { manifest = Busy.Run(this, L.T("Reading the client list ..."), report => ClientDownload.FetchManifest()); }
            catch (Exception ex) { Ui.Error(this, L.T("The client list could not be read:") + "\n" + ex.Message); return; }

            long free = ClientDownload.FreeSpace(target);
            if (free > 0 && free < manifest.TotalBytes + 1024L * 1024 * 1024 && !ClientAddons.IsClientFolder(target))
            {
                Ui.Error(this, L.F("The game client needs {0}. The drive only has {1} free.", ClientDownload.FormatBytes(manifest.TotalBytes), ClientDownload.FormatBytes(free)));
                return;
            }

            string text = L.F("Download or update the game client?\n\nVersion: {0}\nFiles: {1}\nFull size: {2}\n\n" +
                "The files come from the public CoA development launcher. The client data belongs to its owners; only use it as allowed. " +
                "Your WTF, Cache, add-ons and realmlist are kept, and game files you changed are backed up as .afk-backup.",
                manifest.Version, manifest.Files.Count, ClientDownload.FormatBytes(manifest.TotalBytes));
            if (!Ui.Confirm(this, text)) return;

            using (var d = new ClientDownloadDialog(inst, target, manifest))
            {
                var answer = d.ShowDialog(this);
                if (answer == DialogResult.OK && d.Result != null)
                {
                    ClientAddons.ClientFolder = target;
                    folder.Text = target;
                    Refresh_();
                    MessageBox.Show(this, L.F("The game client {0} is ready.\n\n{1} files downloaded, {2} already present, {3} kept as yours.\n\nInstall the add-ons and the client patch next.",
                        d.Result.Version, d.Result.Downloaded, d.Result.AlreadyPresent, d.Result.Kept),
                        Product.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else if (answer == DialogResult.Cancel)
                {
                    Ui.Error(this, L.T("The download was stopped. Everything already downloaded is kept; run it again to continue."));
                }
                else if (d.Failure != null) Ui.Error(this, L.T("The client download failed:") + "\n" + d.Failure);
            }
        }

        void Install_()
        {
            string client = Client;
            if (client == null) { Refresh_(); return; }
            if (ClientAddons.ClientRunning(client))
            {
                Ui.Error(this, L.T("The game or its launcher is running from this folder. Please close it first: add-ons are only loaded at the start, " +
                    "and a running game writes its old cache back when it closes."));
                return;
            }
            try
            {
                bool clear = clearCache.Visible && clearCache.Checked;
                string done = Busy.Run(this, L.T("Installing into your game …"), report =>
                {
                    report(L.T("Copying the add-ons …"));
                    string result = ClientAddons.Install(inst, client, addons, leftovers, false);
                    try
                    {
                        string patched = ClientPatch.Install(inst, client, patch, (t, a, b) => report(t, a, b));
                        if (patched.Length > 0) result = (result + "\n" + patched).Trim();
                    }
                    finally
                    {
                        string cache = Os.Resolve(client, "Cache");
                        if (clear && Directory.Exists(cache)) { report(L.T("Clearing the game's cache …")); Directory.Delete(cache, true); result += "\n" + L.T("the client's cache cleared"); }
                    }
                    return result;
                });
                ClientAddons.ClientFolder = client;
                Refresh_();
                MessageBox.Show(this, L.T("Done:") + "\n\n" + done, Product.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { Ui.Error(this, L.T("Not everything could be installed:") + "\n" + ex.Message); Refresh_(); }
        }

        void Export_()
        {
            if (addons.Count == 0 && patch.Empty) return;
            if (!patch.Empty && Client == null)
            {
                Ui.Error(this, L.T("Choose your game folder first: the client patch is built from your own game client."));
                return;
            }
            using (var d = new SaveFileDialog { Filter = L.T("ZIP archive") + " (*.zip)|*.zip", FileName = "Game client files for " + Product.Name + " server.zip", Title = L.T("Save the files for the game client as ZIP") })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    string file = d.FileName, client = Client;
                    Busy.Run(this, L.T("Making the ZIP for the other players …"), report => ClientAddons.Export(file, addons, inst, client, patch, (t, a, b) => report(t, a, b)));
                    MessageBox.Show(this, L.T("Saved:") + " " + d.FileName + "\n\n" + L.T("Give it to the other players: they unpack it into their game folder (it says how inside)."),
                        Product.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) { Ui.Error(this, L.T("The ZIP could not be saved:") + "\n" + ex.Message); }
            }
        }
    }
#endif
}
