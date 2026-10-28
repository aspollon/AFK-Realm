using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace CoAInstaller
{
    enum PatchState { None, Current, Missing, Outdated, Leftover }

    /// <summary>
    /// The client patch of the modules: an archive (Data\patch-ZZZZZ.MPQ) that changes files of the game client - the
    /// "client": {"patch": ...} part of a module's afk-realm.json. A module brings files of its own ("files") and asks
    /// for changes to files of the client ("edits"); AFK Realm takes those files from the player's own game client,
    /// so it never hands out files of the game, and builds the archive anew whenever the client, a module or a setting
    /// it reads has changed. The archive goes into the game folder, and into the ZIP for the other players.
    /// The late letter matters: CoA Custom carries its loading screens in patch-ZZZD.MPQ, so a patch-Y archive
    /// would be overridden by it.
    /// </summary>
    static class ClientPatch
    {
        public const string ArchiveName = "patch-ZZZZZ.MPQ";
        const string LegacyArchiveName = "patch-Y.MPQ";
        const string StampName = "AFK-Realm\\client-patch.txt";

        static string RecordFile(Install inst) { return Path.Combine(inst.Root, "Dependencies", "client-patch.txt"); }

        /// <summary>A file next to the game's exe (a DLL the game loads), taken from a file of the user's own (a module
        /// never ships one).</summary>
        internal class RootFile { public string Module, From, To; public bool Zip = true, GameExe; }

        internal class FileSpec { public string Module, From, To; public List<Dictionary<string, object>> Fills; }
        internal class EditSpec { public string Module, File, InsertAfter, Line, Each; public Dictionary<string, object> DropRows, AddRows, CloneRows, FillRows, SetRows, CopyRows, ImportRace, SectionRules, AddFile; public long? IdShift; }

        public class Plan
        {
            public List<string> Modules = new List<string>();
            public List<string> Why = new List<string>();
            public List<string> WhyEnglish = new List<string>();
            public bool ClearCache;
            internal List<FileSpec> Files = new List<FileSpec>();
            internal List<EditSpec> Edits = new List<EditSpec>();
            internal List<RaceImport.Wanted> Imports = new List<RaceImport.Wanted>();
            internal List<RootFile> RootFiles = new List<RootFile>();
            /// <summary>A folder of files for next to the exe that was chosen but is not there: module TAB folder.</summary>
            internal List<string> MissingFolders = new List<string>();
            /// <summary>Race numbers whose sections get the strict rules (RaceRepair, after the race check found them wrong).</summary>
            internal HashSet<int> StrictSections = new HashSet<int>();
            /// <summary>Textures the race check did not find, by race number: the rows of the race stop pointing at them
            /// (RaceRepair, when packing the race again did not bring them).</summary>
            internal Dictionary<int, HashSet<string>> MissingTextures = new Dictionary<int, HashSet<string>>();
            public bool Empty { get { return Files.Count == 0 && Edits.Count == 0; } }
        }

        /// <summary>What the installed modules want changed in the client.</summary>
        public static Plan Collect(Install inst)
        {
            var plan = new Plan();
            string modules = ModuleCatalog.ModulesDir(inst);
            if (!Directory.Exists(modules)) return plan;
            var json = new JavaScriptSerializer();
            foreach (var dir in Directory.GetDirectories(modules).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                string file = Path.Combine(dir, ModuleCatalog.ManifestName);
                if (!File.Exists(file)) continue;
                Dictionary<string, object> clientPart, patch;
                object v;
                try
                {
                    var data = json.DeserializeObject(File.ReadAllText(file, Encoding.UTF8)) as Dictionary<string, object>;
                    if (data == null || !data.TryGetValue("client", out v) || (clientPart = v as Dictionary<string, object>) == null) continue;
                    if (!clientPart.TryGetValue("patch", out v) || (patch = v as Dictionary<string, object>) == null) continue;
                }
                catch { continue; }
                string module = Path.GetFileName(dir);
                string root = Path.GetFullPath(dir).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                int before = plan.Files.Count + plan.Edits.Count;
                foreach (var f in List(patch, "files"))
                {
                    if (!When(inst, f)) continue;
                    string from = Text(f, "from"), to = Text(f, "to");
                    string source;
                    try { source = Path.GetFullPath(Path.Combine(dir, from.Replace('/', Path.DirectorySeparatorChar))); } catch { continue; }
                    if (!source.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(source) || !SafeClientPath(to)) continue;
                    plan.Files.Add(new FileSpec { Module = module, From = source, To = to.Replace('/', '\\'), Fills = Dicts(f.TryGetValue("fill", out v) ? v : null) });
                }
                foreach (var f in List(patch, "rootFiles"))
                {
                    if (!When(inst, f)) continue;
                    string from = Convert.ToString(f.TryGetValue("from", out v) ? Settings(inst, v) : "");
                    string to = Text(f, "to");
                    // A module may name a file of its own instead of a fixed name (the patched game exe of a
                    // client pack is Ascension.exe in some versions and Ascension-b.exe in others).
                    if (to.Length == 0) to = Path.GetFileName(from);
                    bool gameExe = f.TryGetValue("gameExe", out v) && v is bool && (bool)v;
                    if (!Regex.IsMatch(to, @"^[A-Za-z0-9_.-]+\.(dll|exe)$") || !SafeRootPath(to, gameExe)) continue;
                    plan.RootFiles.Add(new RootFile { Module = module, To = to, From = from, GameExe = gameExe });
                }
                // a folder of the user's own whose files all go next to the exe (CoA Custom's files\client_root)
                foreach (var f in List(patch, "rootFolders"))
                {
                    if (!When(inst, f)) continue;
                    string folder = Convert.ToString(f.TryGetValue("from", out v) ? Settings(inst, v) : "").Trim();
                    if (folder.Length == 0) continue;                                       // none chosen: nothing to copy
                    bool zip = f.TryGetValue("zip", out v) && v is bool && (bool)v;
                    if (!Directory.Exists(folder)) { plan.MissingFolders.Add(module + "\t" + folder); continue; }
                    string top = Path.GetFullPath(folder).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                    foreach (var one in Directory.GetFiles(top, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    {
                        string to = one.Substring(top.Length).Replace('/', '\\');
                        // The folder is the user's own choice: it may bring the game's exe too (CoA Custom 1.8 ships its Ascension.exe).
                        // When the setup page chose the exe separately, only that one goes next to the game.
                        if (plan.RootFiles.Any(r => r.GameExe) &&
                            (Path.GetFileName(to).Equals("Ascension.exe", StringComparison.OrdinalIgnoreCase) ||
                             Path.GetFileName(to).Equals("Ascension-b.exe", StringComparison.OrdinalIgnoreCase)))
                            continue;
                        if ((!IsClientDataPath(to) && !SafeRootPath(to, true)) || plan.RootFiles.Any(r => r.To.Equals(to, StringComparison.OrdinalIgnoreCase))) continue;
                        plan.RootFiles.Add(new RootFile { Module = module, From = one, To = to, Zip = zip });
                    }
                }
                foreach (var e in Expand(inst, List(patch, "edits")))
                {
                    string target = Text(e, "file");
                    if (!SafeClientPath(target) || !When(inst, e)) continue;
                    foreach (var k in e.Keys.Where(k => k != "when").ToList()) e[k] = Settings(inst, e[k]);
                    var edit = new EditSpec { Module = module, File = target.Replace('/', '\\'), InsertAfter = Text(e, "insertAfter"), Line = Text(e, "line"),
                        Each = Text(e, "__each"),
                        DropRows = e.TryGetValue("dropRows", out v) ? v as Dictionary<string, object> : null,
                        AddRows = e.TryGetValue("addRows", out v) ? v as Dictionary<string, object> : null,
                        CloneRows = e.TryGetValue("cloneRows", out v) ? v as Dictionary<string, object> : null,
                        FillRows = e.TryGetValue("fillRows", out v) ? v as Dictionary<string, object> : null,
                        SetRows = e.TryGetValue("setRows", out v) ? v as Dictionary<string, object> : null,
                        CopyRows = e.TryGetValue("copyRows", out v) ? v as Dictionary<string, object> : null,
                        ImportRace = e.TryGetValue("importRace", out v) ? v as Dictionary<string, object> : null,
                        SectionRules = e.TryGetValue("sectionRules", out v) ? v as Dictionary<string, object> : null,
                        AddFile = e.TryGetValue("addFile", out v) ? v as Dictionary<string, object> : null };
                    if (edit.ImportRace != null)
                    {
                        // a race from another client: its rows go into this patch, its files into an archive of its own
                        var pack = RaceImport.Find(Text(edit.ImportRace, "folder"), Text(edit.ImportRace, "key"));
                        int race;
                        if (!int.TryParse(Text(edit.ImportRace, "id"), out race) || race <= 0) continue;
                        if (pack != null) plan.Imports.Add(new RaceImport.Wanted { Race = race, Pack = pack });
                        // without its folder the build says so (Build), instead of leaving the race out unseen
                    }
                    if ((edit.Line.Length > 0 && edit.InsertAfter.Length > 0) || edit.DropRows != null || edit.AddRows != null || edit.CloneRows != null ||
                        edit.FillRows != null || edit.SetRows != null || edit.CopyRows != null || edit.ImportRace != null || edit.SectionRules != null || edit.AddFile != null)
                        plan.Edits.Add(edit);
                }
                if (plan.Files.Count + plan.Edits.Count + plan.RootFiles.Count(r => r.Module == module) > before)
                {
                    plan.Modules.Add(module);
                    string why = clientPart.TryGetValue("why", out v) ? Convert.ToString(v) : "";
                    if (why.Length > 0) { plan.Why.Add(module + ": " + L.Outside(why)); plan.WhyEnglish.Add(module + ": " + why); }
                    plan.ClearCache |= clientPart.TryGetValue("clearCache", out v) && v is bool && (bool)v;
                }
            }
            // The client pack may still carry stock CoA binaries. Patch them to temporary copies so
            // both the installed files and the ZIP get the customize/start fixes without asking
            // the player to run a Python script. Unknown versions are left untouched.
            ClientBinaryPatch.Prepare(plan);
            return plan;
        }

        static IEnumerable<Dictionary<string, object>> List(Dictionary<string, object> d, string key)
        {
            object v;
            var list = d.TryGetValue(key, out v) ? v as object[] : null;
            return list == null ? Enumerable.Empty<Dictionary<string, object>>() : list.OfType<Dictionary<string, object>>();
        }

        static string Text(Dictionary<string, object> d, string key) { object v; return d.TryGetValue(key, out v) && v != null ? Convert.ToString(v) : ""; }

        /// <summary>
        /// A number of an edit taken from a server setting: {"setting": key, "conf": file, "default": number} becomes the
        /// number the setting holds (its default when it holds none). Example: the number a realm gave a race.
        /// </summary>
        static object Settings(Install inst, object v)
        {
            var d = v as Dictionary<string, object>;
            if (d != null)
            {
                if (d.ContainsKey("setting"))
                {
                    string text = ConfValue(inst, Text(d, "conf"), Text(d, "setting"));
                    object isText;
                    if (d.TryGetValue("text", out isText) && isText is bool && (bool)isText) return text ?? Text(d, "default");
                    long n;
                    if (text != null && long.TryParse(text.Trim(), out n)) return n;
                    object def;
                    return d.TryGetValue("default", out def) ? def : 0;
                }
                foreach (var k in d.Keys.ToList()) d[k] = Settings(inst, d[k]);
                return d;
            }
            var list = v as object[];
            if (list != null) for (int i = 0; i < list.Length; i++) list[i] = Settings(inst, list[i]);
            return v;
        }

        /// <summary>
        /// Edits written once for many: an edit with "forEach" ({conf, key}) stands for one edit per word of that setting,
        /// with "{each}" replaced by the word everywhere in it (texts and names). Example: the same changes for every race
        /// a realm chose, each with its own settings ("CustomRaces.{each}.Id").
        /// </summary>
        static IEnumerable<Dictionary<string, object>> Expand(Install inst, IEnumerable<Dictionary<string, object>> items)
        {
            foreach (var item in items)
            {
                object v;
                var each = item.TryGetValue("forEach", out v) ? v as Dictionary<string, object> : null;
                if (each == null) { yield return item; continue; }
                foreach (var word in Words(ConfValue(inst, Text(each, "conf"), Text(each, "key")) ?? Text(each, "default")))
                {
                    var copy = (Dictionary<string, object>)Subst(item, "{each}", word);
                    copy.Remove("forEach");
                    copy["__each"] = word;
                    yield return copy;
                }
            }
        }

        static object Subst(object v, string token, string value)
        {
            var text = v as string;
            if (text != null) return text.Replace(token, value);
            var d = v as Dictionary<string, object>;
            if (d != null)
            {
                var copy = new Dictionary<string, object>();
                foreach (var kv in d) copy[kv.Key.Replace(token, value)] = Subst(kv.Value, token, value);
                return copy;
            }
            var list = v as object[];
            if (list != null) return list.Select(x => Subst(x, token, value)).ToArray();
            return v;
        }

        /// <summary>One object or a list of them.</summary>
        static List<Dictionary<string, object>> Dicts(object v)
        {
            if (v is Dictionary<string, object>) return new List<Dictionary<string, object>> { (Dictionary<string, object>)v };
            return ((v as object[]) ?? new object[0]).OfType<Dictionary<string, object>>().ToList();
        }

        /// <summary>The words of a setting: "Goblin Worgen" or "Goblin, Worgen".</summary>
        static string[] Words(string value)
        {
            return Regex.Split(value ?? "", @"[\s,;]+").Where(w => w.Length > 0).ToArray();
        }

        /// <summary>
        /// Whether a file or edit of a module's client patch is wanted with the server's settings: every condition of its
        /// "when" ({conf, key, default} and one of: "has" - the setting lists this word; "in" - the setting is one of these
        /// words; "notIn" - it is none of them; "is" - the setting is this word; case does not count) must hold. Without
        /// "when", always.
        /// </summary>
        static bool When(Install inst, Dictionary<string, object> item)
        {
            object v;
            if (!item.TryGetValue("when", out v)) return true;
            foreach (var c in Dicts(v))
            {
                string value = ConfValue(inst, Text(c, "conf"), Text(c, "key")) ?? Text(c, "default");
                var words = Words(value);
                object w;
                if (c.TryGetValue("has", out w) && !words.Any(x => x.Equals(Convert.ToString(w), StringComparison.OrdinalIgnoreCase))) return false;
                if (c.TryGetValue("is", out w) && !value.Trim().Equals(Convert.ToString(w), StringComparison.OrdinalIgnoreCase)) return false;
                if (c.TryGetValue("in", out w))
                {
                    var allowed = ((w as object[]) ?? new object[0]).Select(Convert.ToString);
                    if (!allowed.Any(a => a.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase))) return false;
                }
                if (c.TryGetValue("notIn", out w))
                {
                    var refused = ((w as object[]) ?? new object[0]).Select(Convert.ToString);
                    if (refused.Any(a => a.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase))) return false;
                }
            }
            return true;
        }

        /// <summary>Only files of the interface and the data tables, by a plain relative name.</summary>
        static bool SafeClientPath(string path)
        {
            return Regex.IsMatch(path ?? "", @"^(Interface|DBFilesClient)[\\/][\w .\-\\/]+$", RegexOptions.IgnoreCase) && !path.Contains("..");
        }

        /// <summary>The archives of the client, the one that wins last: the game reads the patches after its base archives.</summary>
        internal static List<string> ClientArchives(string client) { return ClientArchives(client, false); }

        /// <summary>The archives of a game client in the order the game reads them. other: a client AFK Realm did not
        /// patch (the old patch-Y and the current patch are its own).</summary>
        internal static List<string> ClientArchives(string client, bool other)
        {
            string data = Os.Resolve(client, "Data");
            Func<string, int> rank = path =>
            {
                string name = Path.GetFileName(path).ToLowerInvariant();
                bool locale = !Path.GetDirectoryName(path).Equals(data, StringComparison.OrdinalIgnoreCase);
                if (!name.StartsWith("patch")) return locale ? 1 : 0;
                if (locale) return 2;
                // patch.MPQ, then patch-2 to patch-9, then the lettered ones
                return name == "patch.mpq" ? 3 : Regex.IsMatch(name, @"^patch-\d\.mpq$") ? 4 : 5;
            };
            var all = new List<string>();
            if (!Directory.Exists(data)) return all;
            Func<string, IEnumerable<string>> archives = dir => Directory.GetFiles(dir).Where(f => f.EndsWith(".mpq", StringComparison.OrdinalIgnoreCase));
            all.AddRange(archives(data));
            foreach (var sub in Directory.GetDirectories(data))
                if (Regex.IsMatch(Path.GetFileName(sub), @"^[a-z]{2}[A-Z]{2}$")) all.AddRange(archives(sub));
            return all.Where(p => other || (
                    !Path.GetFileName(p).Equals(ArchiveName, StringComparison.OrdinalIgnoreCase) &&
                    !Path.GetFileName(p).Equals(LegacyArchiveName, StringComparison.OrdinalIgnoreCase) &&
                    !RaceImport.IsOurArchive(Path.GetFileName(p))))
                .OrderBy(rank).ThenBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>The newest copy of a file of the client: from the archive the game reads last.</summary>
        internal static byte[] FromClient(string client, string name, out string source)
        {
            foreach (var path in Enumerable.Reverse(ClientArchives(client)))
            {
                MpqArchive mpq;
                try { mpq = new MpqArchive(path); } catch { continue; }
                using (mpq)
                    if (mpq.Contains(name)) { source = Path.GetFileName(path); return mpq.Read(name); }
            }
            source = null;
            return null;
        }

        /// <summary>The value of an option of a server config file (the .conf, or its .dist), or null.</summary>
        internal static string ConfValue(Install inst, string conf, string key)
        {
            foreach (var path in new[] { Path.Combine(inst.ConfigDir, "modules", conf), Path.Combine(inst.ConfigDir, conf),
                                         Path.Combine(inst.ConfigDir, "modules", conf + ".dist"), Path.Combine(inst.ConfigDir, conf + ".dist") })
            {
                if (!File.Exists(path)) continue;
                var m = Regex.Match(File.ReadAllText(path), @"(?m)^[ \t]*" + Regex.Escape(key) + @"[ \t]*=[ \t]*(.*?)[ \t]*\r?$");
                if (m.Success) return m.Groups[1].Value.Trim().Trim('"');
            }
            return null;
        }

        /// <summary>The contents of the archive as it should be now: name -> bytes. Throws when the client lacks a file a module edits.</summary>
        public static SortedDictionary<string, byte[]> Build(Install inst, string client, Plan plan, List<string> notes)
        {
            // ZIP export and state checks use Build without Install; remove the old AFK patch-Y here as well.
            RemoveLegacyPatch(client);
            var files = new SortedDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in plan.Files)
            {
                var bytes = File.ReadAllBytes(f.From);
                foreach (var fill in f.Fills) bytes = Fill(inst, bytes, fill);
                files[f.To] = bytes;
            }
            IdShifts(client, plan, files);
            Func<string, byte[]> load = name =>
            {
                byte[] b; string src;
                if (files.TryGetValue(name, out b)) return b;
                b = FromClient(client, name, out src);
                if (b != null && notes != null) notes.Add(name + " (from " + src + ")");
                return b;
            };
            Action<string, byte[]> save = (name, b) => files[name] = b;
            // client races moved to other numbers (by the Custom races page) first: every edit of the modules - the rows
            // made playable or copied for a race by its number (CustomRaces.X.Source) - means the number the race has
            // after the move, as the page shows it. Moved after those edits, a copied race found no row to copy and a
            // race edited in place had the row of another race edited.
            string moves = ConfValue(inst, "mod_custom_races.conf", "CustomRaces.MoveClientRaces");
            if (!string.IsNullOrWhiteSpace(moves))
                RaceImport.MoveClientRaces(load, save, moves, notes == null ? null : new Action<string>(notes.Add));
            foreach (var group in plan.Edits.Where(e => e.ImportRace == null && e.SectionRules == null && e.AddFile == null).GroupBy(e => e.File, StringComparer.OrdinalIgnoreCase))
            {
                byte[] bytes;
                string source = L.T("this server's modules");
                if (!files.TryGetValue(group.Key, out bytes))
                {
                    bytes = FromClient(client, group.Key, out source);
                    if (bytes == null) throw new InvalidOperationException(L.F("The game client has no {0}, which module {1} changes. Is this the game folder of Conquest of Azeroth?", group.Key, group.First().Module));
                }
                foreach (var edit in group)
                    bytes = edit.DropRows != null ? DropRows(bytes, edit, group.Key) : edit.AddRows != null ? AddRows(bytes, edit, group.Key) :
                        edit.CloneRows != null ? CloneRows(bytes, edit, group.Key) :
                        edit.FillRows != null ? FillRows(bytes, edit, group.Key) :
                        edit.SetRows != null ? SetRows(bytes, edit, group.Key, notes) :
                        edit.CopyRows != null ? CopyRows(bytes, edit, group.Key) : InsertAfter(bytes, edit, group.Key);
                files[group.Key] = bytes;
                if (notes != null) notes.Add(L.F("{0} (from {1})", group.Key, source));
            }
            // imported races, then the rules of the character creation for every added race
            // files of the user's race folder (the icon of a race): taken as they are, when they exist
            foreach (var e in plan.Edits.Where(e => e.AddFile != null))
            {
                string from = Text(e.AddFile, "from");
                if (from.Length > 0 && File.Exists(from) && new FileInfo(from).Length < 16 << 20) files[e.File] = File.ReadAllBytes(from);
            }
            foreach (var e in plan.Edits.Where(e => e.ImportRace != null))
            {
                var pack = RaceImport.Find(Text(e.ImportRace, "folder"), Text(e.ImportRace, "key"));
                int race, baseRace;
                if (pack == null)
                    throw new InvalidOperationException(Text(e.ImportRace, "folder").Length == 0
                        ? L.F("The race {0} comes from a race folder, but no race folder is set (module {1}). Open its setup page and save it.", Text(e.ImportRace, "key"), e.Module)
                        : L.F("The race folder {0} has no race \"{1}\" (module {2}).", Text(e.ImportRace, "folder"), Text(e.ImportRace, "key"), e.Module));
                if (!int.TryParse(Text(e.ImportRace, "id"), out race) || race <= 0) continue;
                if (!int.TryParse(Text(e.ImportRace, "base"), out baseRace)) baseRace = BaseRaceId(Text(e.ImportRace, "base"));
                string name = Text(e.ImportRace, "name");
                RaceImport.AddRace(load, save, pack, race, baseRace, name.Length > 0 ? name : pack.Name, notes == null ? null : new Action<string>(notes.Add),
                    Text(e.ImportRace, "helmetPrefix"));
            }
            foreach (var e in plan.Edits.Where(e => e.SectionRules != null))
            {
                int race;
                if (!int.TryParse(Text(e.SectionRules, "race"), out race) || race <= 0) continue;
                HashSet<string> missing;
                if (plan.MissingTextures.TryGetValue(race, out missing)) RaceImport.DropMissingTextures(load, save, race, missing);
                if (plan.StrictSections.Contains(race)) RaceImport.StrictSections(load, save, race);
                RaceImport.SectionRules(load, save, race, Text(e.SectionRules, "maleOnly") == "1", notes == null ? null : new Action<string>(notes.Add),
                    Text(e.SectionRules, "femaleOnly") == "1");
            }
            // the reputations of their base races, in the client's own faction table as mod-custom-races does on the server
            var lend = new List<KeyValuePair<int, int>>();
            foreach (var e in plan.Edits.Where(e => e.SectionRules != null))
            {
                int race;
                if (!int.TryParse(Text(e.SectionRules, "race"), out race) || race <= 0 || string.IsNullOrEmpty(e.Each)) continue;
                lend.Add(new KeyValuePair<int, int>(race, BaseRaceId(ConfValue(inst, "mod_custom_races.conf", "CustomRaces." + e.Each + ".Base"))));
            }
            if (lend.Count > 0) RaceImport.LendFactions(load, save, lend, notes == null ? null : new Action<string>(notes.Add));
            SortSections(files);
            FillHeights(files, client, plan, load);
            if (files.Count > 0) files[StampName] = Encoding.UTF8.GetBytes(Stamp(files) + string.Concat(plan.Imports.Select(w => "race " + w.Race + " " + w.Pack.Key + "\r\n")));
            return files;
        }

        /// <summary>
        /// The skin, face, hair and underwear rows grouped the way the client expects them: by race, sex, section,
        /// variation and colour. The client counts the rows of a race, sex and section where they lie together; rows of
        /// one group spread over the table (rows added at its end, copies of a race of the client, HD rows taken from
        /// the nearest style) make it write past what it counted, and the game crashes at its exit or when it changes
        /// a character (EsteriaWoW's race guide, the repair of October 3, 2026). Sorting keeps every row and every id, so
        /// the HD table still pairs with the plain one by id.
        /// </summary>
        static void SortSections(SortedDictionary<string, byte[]> files)
        {
            foreach (string name in new[] { "DBFilesClient\\CharSections.dbc", "DBFilesClient\\HDCharSections.dbc" })
            {
                byte[] bytes;
                if (!files.TryGetValue(name, out bytes) || bytes == null) continue;
                var t = DbcTable.Parse(bytes, name.Substring(name.LastIndexOf('\\') + 1));
                if (t.Fields < 10) continue;
                var sorted = t.Records.OrderBy(r => t.Field(r, 1)).ThenBy(r => t.Field(r, 2)).ThenBy(r => t.Field(r, 3))
                    .ThenBy(r => t.Field(r, 8)).ThenBy(r => t.Field(r, 9)).ThenBy(r => t.Field(r, 0)).ToList();
                if (sorted.SequenceEqual(t.Records)) continue;
                t.Records.Clear();
                t.Records.AddRange(sorted);
                files[name] = t.ToBytes();
            }
        }

        // ---------------------------------------------------------------------------- the size of the races

        static readonly Regex HeightToken = new Regex(@"\{height:(\d+):([01])\}");
        // heights already measured: model file (and where it came from) -> height in its own units
        static readonly Dictionary<string, double> measured = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        /// <summary>"{height:RACE:SEX}" in a script of the patch (SEX 0 male, 1 female) becomes how tall the model of that
        /// race stands, in yards: its vertices in the standing pose times the scales of its display and model - the box a
        /// model file carries can hold all its animations. A script of the character creation sizes its camera by it.
        /// "0" when the client does not know the race or its model.</summary>
        static void FillHeights(SortedDictionary<string, byte[]> files, string client, Plan plan, Func<string, byte[]> load)
        {
            var scripts = files.Keys.Where(k => k.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) && Encoding.UTF8.GetString(files[k]).Contains("{height:")).ToList();
            if (scripts.Count == 0) return;
            byte[] crBytes = load("DBFilesClient\\ChrRaces.dbc"), diBytes = load("DBFilesClient\\CreatureDisplayInfo.dbc"), mdBytes = load("DBFilesClient\\CreatureModelData.dbc");
            if (crBytes == null || diBytes == null || mdBytes == null) return;
            var cr = DbcTable.Parse(crBytes, "ChrRaces.dbc");
            var di = DbcTable.Parse(diBytes, "CreatureDisplayInfo.dbc");
            var md = DbcTable.Parse(mdBytes, "CreatureModelData.dbc");
            var races = new Dictionary<uint, byte[]>(); foreach (var r in cr.Records) races[cr.Field(r, 0)] = r;
            var displays = new Dictionary<uint, byte[]>(); foreach (var r in di.Records) displays[di.Field(r, 0)] = r;
            var models = new Dictionary<uint, byte[]>(); foreach (var r in md.Records) models[md.Field(r, 0)] = r;
            Func<DbcTable, byte[], int, double> real = (t, r, f) => { double v = BitConverter.ToSingle(BitConverter.GetBytes(t.Field(r, f)), 0); return v > 0.01 && v < 100 ? v : 1.0; };
            var heights = new Dictionary<string, string>();
            foreach (var name in scripts)
            {
                string text = HeightToken.Replace(Encoding.UTF8.GetString(files[name]), m =>
                {
                    string value;
                    if (heights.TryGetValue(m.Value, out value)) return value;
                    double height = 0;
                    byte[] race, display, model;
                    if (races.TryGetValue(uint.Parse(m.Groups[1].Value), out race) && displays.TryGetValue(cr.Field(race, 4 + int.Parse(m.Groups[2].Value)), out display)
                        && models.TryGetValue(di.Field(display, 1), out model))
                    {
                        string path = DbcString(md, model, 2);
                        if (path.EndsWith(".mdx", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase)) path = path.Substring(0, path.Length - 4) + ".m2";
                        double own = ModelHeight(path, client, plan);
                        if (own > 0) height = own * real(di, display, 4) * real(md, model, 4);
                    }
                    value = height.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
                    heights[m.Value] = value;
                    return value;
                });
                files[name] = Encoding.UTF8.GetBytes(text);
            }
        }

        static string DbcString(DbcTable t, byte[] row, int field)
        {
            uint at = t.Field(row, field);
            if (at == 0 || at >= t.Strings.Length) return "";
            int end = (int)at;
            while (end < t.Strings.Length && t.Strings[end] != 0) end++;
            return Encoding.UTF8.GetString(t.Strings, (int)at, end - (int)at);
        }

        /// <summary>The height of a model file in its standing pose (its vertices), 0 when it is not found or not read.
        /// An imported race's model is in its race folder; the others are in the client's archives.</summary>
        static double ModelHeight(string path, string client, Plan plan)
        {
            if (path.Length == 0) return 0;
            string key = path + "|" + ClientRaces.ArchivesKey(client) + "|" + string.Join(",", plan.Imports.Select(w => w.Pack.Dir));
            double height;
            lock (measured) if (measured.TryGetValue(key, out height)) return height;
            byte[] bytes = null;
            foreach (var w in plan.Imports)
            {
                string file = Os.Resolve(Path.Combine(w.Pack.Dir, "files"), path);
                if (File.Exists(file)) { bytes = File.ReadAllBytes(file); break; }
            }
            if (bytes == null) { string source; try { bytes = FromClient(client, path, out source); } catch { bytes = null; } }
            height = bytes == null ? 0 : VertexHeight(bytes);
            lock (measured) measured[key] = height;
            return height;
        }

        /// <summary>The lowest to the highest vertex of a model file of the client (MD20, its vertices 48 bytes each).</summary>
        internal static double VertexHeight(byte[] m2)
        {
            if (m2.Length < 68 || m2[0] != 'M' || m2[1] != 'D' || m2[2] != '2' || m2[3] != '0') return 0;
            uint count = BitConverter.ToUInt32(m2, 60), offset = BitConverter.ToUInt32(m2, 64);
            if (count == 0 || offset + (long)count * 48 > m2.Length) return 0;
            float low = float.MaxValue, high = float.MinValue;
            for (long i = 0, at = offset + 8; i < count; i++, at += 48)
            {
                float z = BitConverter.ToSingle(m2, (int)at);
                if (z < low) low = z;
                if (z > high) high = z;
            }
            return high > low ? high - low : 0;
        }

        static int BaseRaceId(string name)
        {
            switch ((name ?? "").Replace(" ", "").ToLowerInvariant())
            {
                case "orc": return 2; case "dwarf": return 3; case "nightelf": return 4; case "undead": case "scourge": return 5;
                case "tauren": return 6; case "gnome": return 7; case "troll": return 8; case "bloodelf": return 10; case "draenei": return 11;
                default: return 1;
            }
        }

        static byte[] Fill(Install inst, byte[] bytes, Dictionary<string, object> fill)
        {
            string marker = Text(fill, "marker"), template = Text(fill, "line");
            if (marker.Length == 0 || template.Length == 0) return bytes;
            object v;
            var each = fill.TryGetValue("forEach", out v) ? v as Dictionary<string, object> : null;
            if (each != null)
            {
                // One line per word of a setting: "{each}" is the word, "{conf:KEY}" the value of setting KEY (with "{each}"
                // in it), for inside a Lua string.
                Func<string, string> luaText = t => (t ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", " ");
                string conf = Text(each, "conf");
                var made = new List<string>();
                foreach (var word in Words(ConfValue(inst, conf, Text(each, "key")) ?? Text(each, "default")))
                {
                    string one = template.Replace("{each}", word);
                    one = Regex.Replace(one, @"\{conf:([^}]+)\}", m => luaText(ConfValue(inst, conf, m.Groups[1].Value) ?? ""));
                    made.Add(one);
                }
                string all = Encoding.UTF8.GetString(bytes);
                var parts = all.Split('\n');
                for (int i = 0; i < parts.Length; i++)
                    if (parts[i].Contains(marker)) parts[i] = string.Join("\n", made) + (made.Count == 0 ? "-- " + marker : "") + (parts[i].EndsWith("\r") ? "\r" : "");
                return Encoding.UTF8.GetBytes(string.Join("\n", parts));
            }
            string value = ConfValue(inst, Text(fill, "conf"), Text(fill, "key")) ?? Text(fill, "default");
            var numbers = Regex.Matches(value, @"\d+").Cast<Match>().Select(m => int.Parse(m.Value)).Distinct().OrderBy(n => n);
            string set = "{ " + string.Join(", ", numbers.Select(n => "[" + n + "] = true")) + " }";
            Func<string, string> lua = t => t.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", " ");
            // {set}: the numbers of the setting as a Lua set; {words}: its words as a Lua list of strings; {text}: the
            // setting itself, for inside a Lua string.
            string line = template.Replace("{set}", set)
                .Replace("{words}", "{ " + string.Join(", ", Words(value).Select(w => "\"" + lua(w) + "\"")) + " }")
                .Replace("{text}", lua(value.Trim()));
            string text = Encoding.UTF8.GetString(bytes);
            var lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
                if (lines[i].Contains(marker)) lines[i] = line + (lines[i].EndsWith("\r") ? "\r" : "");
            return Encoding.UTF8.GetBytes(string.Join("\n", lines));
        }

        static byte[] InsertAfter(byte[] bytes, EditSpec edit, string file)
        {
            string text = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
            string nl = text.Contains("\r\n") ? "\r\n" : "\n";
            var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
            if (lines.Any(l => l.Trim().Equals(edit.Line.Trim(), StringComparison.OrdinalIgnoreCase))) return bytes;
            int at = lines.FindIndex(l => l.Trim().Equals(edit.InsertAfter.Trim(), StringComparison.OrdinalIgnoreCase));
            if (at < 0) throw new InvalidOperationException(L.F("{0} of the game client has no line \"{1}\", after which module {2} adds its own. The client may have changed; update the module.", file, edit.InsertAfter, edit.Module));
            lines.Insert(at + 1, edit.Line);
            bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            var result = Encoding.UTF8.GetBytes(string.Join(nl, lines));
            return bom ? new byte[] { 0xEF, 0xBB, 0xBF }.Concat(result).ToArray() : result;
        }

        static byte[] DropRows(byte[] bytes, EditSpec edit, string file)
        {
            var table = DbcTable.Parse(bytes, file);
            object v;
            long idBelow = edit.DropRows.TryGetValue("idBelow", out v) ? Convert.ToInt64(v) : long.MaxValue;
            int field = edit.DropRows.TryGetValue("field", out v) ? Convert.ToInt32(v) : 0;
            var listed = (edit.DropRows.TryGetValue("values", out v) ? v as object[] : null) ?? new object[0];
            var values = new HashSet<uint>(listed.Select(x => Convert.ToUInt32(x)));
            if (field < 0 || field >= table.Fields) throw new InvalidOperationException(L.F("{0} has no field {1} (module {2}).", file, field, edit.Module));
            table.Records.RemoveAll(r => table.Field(r, 0) < idBelow && values.Contains(table.Field(r, field)));
            return table.ToBytes();
        }

        /// <summary>
        /// Rows added to a DBC table: every combination of the value lists in "cross" (one list per field, in field order),
        /// unless the table has that row already (a row the table holds twice keeps only its first). The fields keep the
        /// width the table has (CharBaseInfo.dbc of the CoA client stores a byte per field), and the rows go at the end,
        /// so the rows of the client keep their order.
        /// </summary>
        static byte[] AddRows(byte[] bytes, EditSpec edit, string file)
        {
            var table = DbcTable.Parse(bytes, file);
            object v;
            var lists = ((edit.AddRows.TryGetValue("cross", out v) ? v as object[] : null) ?? new object[0])
                .Select(l => ((l as object[]) ?? new object[0]).Select(x => Convert.ToUInt32(x)).ToArray()).ToList();
            if (lists.Count != table.Fields || lists.Any(l => l.Length == 0))
                throw new InvalidOperationException(L.F("{0} has {1} fields; module {2} gives values for {3}.", file, table.Fields, edit.Module, lists.Count));
            int width = table.Fields > 0 ? table.RecordSize / table.Fields : 0;
            if ((width != 1 && width != 2 && width != 4) || width * table.Fields != table.RecordSize)
                throw new InvalidOperationException(L.F("{0} has a row layout module {1} cannot add rows to.", file, edit.Module));
            if (width < 4 && lists.Any(l => l.Any(x => x >= (1u << (8 * width)))))
                throw new InvalidOperationException(L.F("{0} stores {1} byte(s) per field; a value of module {2} does not fit.", file, width, edit.Module));
            var have = new HashSet<string>();
            // A row the table holds twice takes a place in the client's list for nothing: the second one goes.
            table.Records.RemoveAll(r => !have.Add(Convert.ToBase64String(r)));
            var combo = new uint[lists.Count];
            // The first-field values this edit really appends a row for. A table may already hold more rows for a stock
            // value than the module is allowed to add (the CoA client has 31 class rows for Troll 8); those rows are the
            // client's own and are not the module's to judge.
            var addedFirst = new HashSet<uint>();
            Action<int> walk = null;
            walk = i =>
            {
                if (i == lists.Count)
                {
                    var rec = new byte[table.RecordSize];
                    for (int f = 0; f < combo.Length; f++)
                        for (int b = 0; b < width; b++) rec[f * width + b] = (byte)(combo[f] >> (8 * b));
                    if (have.Add(Convert.ToBase64String(rec)))
                    {
                        table.Records.Add(rec);
                        addedFirst.Add(combo[0]);
                    }
                    return;
                }
                foreach (var value in lists[i]) { combo[i] = value; walk(i + 1); }
            };
            walk(0);
            // Some tables the client reads into lists of fixed size; more rows than that crash the game, so the module
            // names its limit: of the whole table ("maxRows"), or of the rows that share their first field ("maxPerRace":
            // the character creation of the CoA client takes 30 classes per race in CharBaseInfo.dbc).
            int most = edit.AddRows.TryGetValue("maxRows", out v) ? Convert.ToInt32(v) : int.MaxValue;
            if (table.Records.Count > most)
                throw new InvalidOperationException(L.F("{0} would have {1} rows, more than the {2} the game takes (module {3}). Another module may have added rows to it too.",
                    file, table.Records.Count, most, edit.Module));
            int perFirst = edit.AddRows.TryGetValue("maxPerRace", out v) ? Convert.ToInt32(v) : int.MaxValue;
            var crowded = table.Records.Where(r => addedFirst.Contains(table.Field(r, 0)))
                .GroupBy(r => table.Field(r, 0)).FirstOrDefault(g => g.Count() > perFirst);
            if (crowded != null)
                throw new InvalidOperationException(L.F("{0} would have {1} rows for {2}, more than the {3} the game takes (module {4}). Another module may have added rows to it too.",
                    file, crowded.Count(), crowded.Key, perFirst, edit.Module));
            return table.ToBytes();
        }

        /// <summary>
        /// Rows copied within a DBC table: every row whose bytes at the offsets in "match" hold those values is copied with
        /// the bytes in "set" changed and a new id (the first field, after the highest id), unless a row with the copy's
        /// bytes at the "key" offsets exists already. For tables with byte fields, such as CharStartOutfit.dbc (the start
        /// outfit of a race, class and sex).
        /// </summary>
        static byte[] CloneRows(byte[] bytes, EditSpec edit, string file)
        {
            var table = DbcTable.Parse(bytes, file);
            object v;
            Func<string, Dictionary<int, byte>> offsets = name =>
            {
                var d = edit.CloneRows.TryGetValue(name, out v) ? v as Dictionary<string, object> : null;
                var result = new Dictionary<int, byte>();
                if (d != null) foreach (var kv in d) result[int.Parse(kv.Key)] = Convert.ToByte(kv.Value);
                return result;
            };
            var match = offsets("match"); var set = offsets("set");
            var key = ((edit.CloneRows.TryGetValue("key", out v) ? v as object[] : null) ?? new object[0]).Select(x => Convert.ToInt32(x)).ToArray();
            if (match.Count == 0 || set.Count == 0 || table.RecordSize < 4 || match.Keys.Concat(set.Keys).Concat(key).Any(o => o < 4 || o >= table.RecordSize))
                throw new InvalidOperationException(L.F("The rows module {0} copies in {1} are not described in a way it can use.", edit.Module, file));
            Func<byte[], string> keyOf = r => string.Join(",", key.Select(o => r[o]));
            bool replace = edit.CloneRows.TryGetValue("replace", out v) && v is bool && (bool)v;
            var have = new Dictionary<string, byte[]>();
            foreach (var r in table.Records) if (!have.ContainsKey(keyOf(r))) have[keyOf(r)] = r;
            uint next = table.Records.Count == 0 ? 1 : table.Records.Max(r => BitConverter.ToUInt32(r, 0)) + 1;
            foreach (var row in table.Records.Where(r => match.All(m => r[m.Key] == m.Value)).ToList())
            {
                var copy = (byte[])row.Clone();
                foreach (var s in set) copy[s.Key] = s.Value;
                byte[] existing;
                if (have.TryGetValue(keyOf(copy), out existing))
                {
                    // "replace": the row the copy would duplicate takes the copy's bytes and keeps its id.
                    if (replace && existing != row) Buffer.BlockCopy(copy, 4, existing, 4, copy.Length - 4);
                    continue;
                }
                BitConverter.GetBytes(next++).CopyTo(copy, 0);
                have[keyOf(copy)] = copy;
                table.Records.Add(copy);
            }
            return table.ToBytes();
        }

        /// <summary>
        /// Fields of rows changed in a DBC table: every row whose fields in "match" (field number: value) hold those values
        /// gets the values in "set". The fields keep the width the table stores them in. Example: making a race of
        /// ChrRaces.dbc playable for a faction.
        /// </summary>
        static byte[] SetRows(byte[] bytes, EditSpec edit, string file, List<string> notes = null)
        {
            var table = DbcTable.Parse(bytes, file);
            object v;
            // A value in quotes is a text: it goes into the table's strings, and the field takes its offset.
            Func<string, Dictionary<int, uint>> fields = name =>
            {
                var d = edit.SetRows.TryGetValue(name, out v) ? v as Dictionary<string, object> : null;
                var result = new Dictionary<int, uint>();
                if (d != null) foreach (var kv in d) result[int.Parse(kv.Key)] = kv.Value is string ? table.AddString((string)kv.Value) : Convert.ToUInt32(kv.Value);
                return result;
            };
            var match = fields("match"); var set = fields("set");
            if (match.Count == 0 || set.Count == 0 || match.Keys.Concat(set.Keys).Any(f => f < 0 || f >= table.Fields))
                throw new InvalidOperationException(L.F("The fields module {0} changes in {1} are not described in a way it can use.", edit.Module, file));
            int changed = 0;
            foreach (var row in table.Records.Where(r => match.All(m => table.Field(r, m.Key) == m.Value)))
            {
                foreach (var s in set) table.SetField(row, s.Key, s.Value);
                changed++;
            }
            if (changed == 0)
            {
                // A race selected for an older client can survive in CustomRaces.Races after
                // the client (or the race folder) changed. Skip that one stale race instead of
                // stopping the whole client patch; the race page removes it on the next save.
                if (edit.Module.Equals("mod-custom-races", StringComparison.OrdinalIgnoreCase) &&
                    file.EndsWith("ChrRaces.dbc", StringComparison.OrdinalIgnoreCase))
                {
                    if (notes != null)
                        notes.Add(L.F("The race {0} is no longer in this game client; its row was skipped. Open Custom races and save to remove it.", edit.Each ?? "?"));
                    return bytes;
                }
                throw new InvalidOperationException(L.F("{0} of the game client has no row module {1} changes. The client may have changed; update the module.", file, edit.Module));
            }
            return table.ToBytes();
        }

        /// <summary>
        /// Rows copied within a DBC table, by field: every row whose fields in "match" (field number: value) hold those
        /// values is copied with the fields in "set" changed. With "newId" the copy gets a new id (field 0, after the
        /// highest). A copy is left out when a row agrees with it in the fields of "key" already (default: all fields but
        /// the id). The fields keep the width the table stores them in. Example: the skins and hair of a race of the
        /// client, copied to the number a module gives that race.
        /// </summary>
        /// <summary>
        /// Ids of copied rows that tables share: the HD tables of the Ascension client pair with their plain ones by id
        /// (HDCharSections row 8500 is the HD version of CharSections row 8500). copyRows with "idGroup" (a name) and
        /// "idKey" (what the copies are for, such as the new race) gives the copies the same new ids in every table of
        /// the group: each key gets a block of ids above the highest id of all tables in the group, its rows keep their
        /// distance to each other, and the HD copy of a row has the id of its plain copy.
        /// </summary>
        static void IdShifts(string client, Plan plan, SortedDictionary<string, byte[]> files)
        {
            var groups = new Dictionary<string, List<EditSpec>>(StringComparer.OrdinalIgnoreCase);
            object v;
            foreach (var e in plan.Edits)
                if (e.CopyRows != null && e.CopyRows.TryGetValue("idGroup", out v) && Convert.ToString(v).Length > 0)
                {
                    List<EditSpec> list;
                    if (!groups.TryGetValue(Convert.ToString(v), out list)) groups[Convert.ToString(v)] = list = new List<EditSpec>();
                    list.Add(e);
                }
            var tables = new Dictionary<string, DbcTable>(StringComparer.OrdinalIgnoreCase);
            Func<string, DbcTable> table = name =>
            {
                DbcTable t;
                if (tables.TryGetValue(name, out t)) return t;
                byte[] bytes; string source;
                if (!files.TryGetValue(name, out bytes)) bytes = FromClient(client, name, out source);
                if (bytes == null) throw new InvalidOperationException(L.F("The game client has no {0}.", name));
                return tables[name] = DbcTable.Parse(bytes, name);
            };
            foreach (var g in groups.Values)
            {
                long next = g.Select(e => e.File).Distinct(StringComparer.OrdinalIgnoreCase).Select(table)
                    .Max(t => t.Records.Count == 0 ? 0L : t.Records.Max(r => (long)t.Field(r, 0))) + 1;
                // the ids the copies of each key are made from, over all tables of the group
                var spans = new List<KeyValuePair<string, long[]>>();
                foreach (var e in g)
                {
                    var t = table(e.File);
                    var match = new Dictionary<int, uint>();
                    var d = e.CopyRows.TryGetValue("match", out v) ? v as Dictionary<string, object> : null;
                    if (d != null) foreach (var kv in d) match[int.Parse(kv.Key)] = Convert.ToUInt32(kv.Value);
                    string key = e.CopyRows.TryGetValue("idKey", out v) ? Convert.ToString(v) : "";
                    var ids = t.Records.Where(r => match.All(m => t.Field(r, m.Key) == m.Value)).Select(r => (long)t.Field(r, 0)).ToList();
                    if (ids.Count == 0) continue;
                    int at = spans.FindIndex(s => s.Key == key);
                    if (at < 0) spans.Add(new KeyValuePair<string, long[]>(key, new[] { ids.Min(), ids.Max() }));
                    else spans[at] = new KeyValuePair<string, long[]>(key, new[] { Math.Min(spans[at].Value[0], ids.Min()), Math.Max(spans[at].Value[1], ids.Max()) });
                }
                var shift = new Dictionary<string, long>();
                foreach (var s in spans)
                {
                    shift[s.Key] = next - s.Value[0];
                    next += s.Value[1] - s.Value[0] + 1;
                }
                if (next > uint.MaxValue) throw new InvalidOperationException(L.F("Too many rows to copy into {0}.", g[0].File));
                foreach (var e in g)
                {
                    long n;
                    if (shift.TryGetValue(e.CopyRows.TryGetValue("idKey", out v) ? Convert.ToString(v) : "", out n)) e.IdShift = n;
                }
            }
        }

        static byte[] CopyRows(byte[] bytes, EditSpec edit, string file)
        {
            var table = DbcTable.Parse(bytes, file);
            object v;
            // A value in quotes is a text: it goes into the table's strings, and the field takes its offset.
            Func<string, Dictionary<int, uint>> fields = name =>
            {
                var d = edit.CopyRows.TryGetValue(name, out v) ? v as Dictionary<string, object> : null;
                var result = new Dictionary<int, uint>();
                if (d != null) foreach (var kv in d) result[int.Parse(kv.Key)] = kv.Value is string ? table.AddString((string)kv.Value) : Convert.ToUInt32(kv.Value);
                return result;
            };
            var match = fields("match"); var set = fields("set");
            bool newId = edit.IdShift.HasValue || (edit.CopyRows.TryGetValue("newId", out v) && v is bool && (bool)v);
            var key = ((edit.CopyRows.TryGetValue("key", out v) ? v as object[] : null) ?? new object[0]).Select(x => Convert.ToInt32(x)).ToArray();
            if (key.Length == 0) key = Enumerable.Range(newId ? 1 : 0, Math.Max(0, table.Fields - (newId ? 1 : 0))).ToArray();
            if (match.Count == 0 || set.Count == 0 || match.Keys.Concat(set.Keys).Concat(key).Any(f => f < 0 || f >= table.Fields))
                throw new InvalidOperationException(L.F("The rows module {0} copies in {1} are not described in a way it can use.", edit.Module, file));
            Func<byte[], string> keyOf = r => string.Join(",", key.Select(f => table.Field(r, f)));
            var have = new HashSet<string>(table.Records.Select(keyOf));
            uint next = table.Records.Count == 0 ? 1 : table.Records.Max(r => table.Field(r, 0)) + 1;
            foreach (var row in table.Records.Where(r => match.All(m => table.Field(r, m.Key) == m.Value)).ToList())
            {
                var copy = (byte[])row.Clone();
                foreach (var f in set) table.SetField(copy, f.Key, f.Value);
                if (!have.Add(keyOf(copy))) continue;
                if (edit.IdShift.HasValue) table.SetField(copy, 0, (uint)(table.Field(row, 0) + edit.IdShift.Value));
                else if (newId) table.SetField(copy, 0, next++);
                table.Records.Add(copy);
            }
            return table.ToBytes();
        }

        /// <summary>
        /// Rows of a DBC table filled from a sibling: a row whose bytes at a "match" offset hold one of its values and whose
        /// 32-bit numbers in "empty" are all 0 or below gets the bytes in "copy" from a row that agrees with it at the "same"
        /// offsets and is not empty - first from a row whose byte at "prefer.offset" is in the same list of "prefer.groups".
        /// CharStartOutfit.dbc: an outfit of the CoA client that only shows armour, without items, takes the items of the
        /// same class and sex of another race of the same faction.
        /// </summary>
        static byte[] FillRows(byte[] bytes, EditSpec edit, string file)
        {
            var table = DbcTable.Parse(bytes, file);
            var spec = edit.FillRows;
            object v;
            Func<string, int[]> ints = name => ((spec.TryGetValue(name, out v) ? v as object[] : null) ?? new object[0]).Select(x => Convert.ToInt32(x)).ToArray();
            var match = new Dictionary<int, HashSet<byte>>();
            if (spec.TryGetValue("match", out v) && v is Dictionary<string, object>)
                foreach (var kv in (Dictionary<string, object>)v)
                    match[int.Parse(kv.Key)] = new HashSet<byte>(((kv.Value as object[]) ?? new[] { kv.Value }).Select(x => Convert.ToByte(x)));
            int[] empty = ints("empty"), copy = ints("copy"), same = ints("same");
            int preferAt = -1; var groups = new List<HashSet<byte>>();
            if (spec.TryGetValue("prefer", out v) && v is Dictionary<string, object>)
            {
                var p = (Dictionary<string, object>)v;
                preferAt = p.TryGetValue("offset", out v) ? Convert.ToInt32(v) : -1;
                if (p.TryGetValue("groups", out v) && v is object[])
                    foreach (var g in (object[])v) groups.Add(new HashSet<byte>(((g as object[]) ?? new object[0]).Select(x => Convert.ToByte(x))));
            }
            int size = table.RecordSize;
            if (empty.Length != 2 || copy.Length != 2 || empty[0] < 4 || empty[1] > size || (empty[1] - empty[0]) % 4 != 0 || copy[0] < 4 || copy[1] > size ||
                copy[0] >= copy[1] || same.Any(o => o < 4 || o >= size) || match.Keys.Any(o => o < 4 || o >= size) || preferAt >= size)
                throw new InvalidOperationException(L.F("The rows module {0} fills in {1} are not described in a way it can use.", edit.Module, file));
            Func<byte[], bool> isEmpty = r => { for (int o = empty[0]; o < empty[1]; o += 4) if (BitConverter.ToInt32(r, o) > 0) return false; return true; };
            var donors = table.Records.Where(r => !isEmpty(r)).ToList();
            foreach (var row in table.Records)
            {
                if (!match.All(m => m.Value.Contains(row[m.Key])) || !isEmpty(row)) continue;
                var fitting = donors.Where(d => same.All(o => d[o] == row[o])).ToList();
                if (preferAt >= 4)
                {
                    var group = groups.FirstOrDefault(g => g.Contains(row[preferAt]));
                    var near = group == null ? null : fitting.Where(d => group.Contains(d[preferAt])).OrderBy(d => d[preferAt]).FirstOrDefault();
                    if (near != null) fitting.Insert(0, near);
                }
                var donor = fitting.FirstOrDefault();
                if (donor != null) Buffer.BlockCopy(donor, copy[0], row, copy[0], copy[1] - copy[0]);
            }
            return table.ToBytes();
        }

        static string Stamp(SortedDictionary<string, byte[]> files)
        {
            using (var sha = SHA256.Create())
            {
                foreach (var f in files)
                {
                    if (f.Key.Equals(StampName, StringComparison.OrdinalIgnoreCase)) continue;
                    var name = Encoding.UTF8.GetBytes(f.Key.ToLowerInvariant() + "\n");
                    sha.TransformBlock(name, 0, name.Length, null, 0);
                    sha.TransformBlock(f.Value, 0, f.Value.Length, null, 0);
                }
                sha.TransformFinalBlock(new byte[0], 0, 0);
                return "Made by " + Product.Name + " for the game client it was built from.\r\nContents " + BitConverter.ToString(sha.Hash).Replace("-", "").Substring(0, 16) + "\r\n";
            }
        }

        /// <summary>The stamp of the patch in the game folder: null without one, "" when the archive there is not ours.</summary>
        static string InstalledStamp(string client)
        {
            string path = Os.Resolve(client, "Data/" + ArchiveName);
            if (!File.Exists(path)) return null;
            try
            {
                using (var mpq = new MpqArchive(path))
                {
                    var b = mpq.Read(StampName);
                    string stamp = b == null ? "" : Encoding.UTF8.GetString(b);
                    return stamp.StartsWith("Made by " + Product.Name) ? stamp : "";
                }
            }
            catch { return ""; }
        }

        /// <summary>Removes the old patch-Y of versions before the loading-screen archive moved after CoA's
        /// patch-ZZZD. Only a file that carries AFK Realm's own stamp is touched.</summary>
        static void RemoveLegacyPatch(string client)
        {
            if (string.IsNullOrEmpty(client)) return;
            string data = Os.Resolve(client, "Data");
            if (!Directory.Exists(data)) return;
            foreach (string old in Directory.GetFiles(data, "*", SearchOption.AllDirectories).Where(f => Path.GetFileName(f).Equals(LegacyArchiveName, StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    bool ours = false;
                    using (var mpq = new MpqArchive(old))
                    {
                        var b = mpq.Read(StampName);
                        ours = b != null && Encoding.UTF8.GetString(b).StartsWith("Made by " + Product.Name);
                    }
                    if (ours) File.Delete(old);
                }
                catch { }
            }
        }

        static string Foreign { get { return L.F("Data\\{0} in the game folder was not made by {1}. Rename or remove it, then install again.", ArchiveName, Product.Name); } }

        // ---------------------------------------------------------------------------- files next to the exe

        static string RootRecord(Install inst) { return Path.Combine(inst.Root, "Dependencies", "client-root-files.txt"); }

        // The hashes taken so far, by path, size and time: a folder of files for next to the exe can be hundreds of MB.
        static readonly Dictionary<string, string> hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        static string Hash(string path)
        {
            var fi = new FileInfo(path);
            string key = fi.FullName + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks, h;
            lock (hashes) if (hashes.TryGetValue(key, out h)) return h;
            using (var sha = System.Security.Cryptography.SHA1.Create())
            using (var f = File.OpenRead(path))
                h = BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "");
            lock (hashes) hashes[key] = h;
            return h;
        }

        /// <summary>CoA Custom's equipment-fitting tables live in client_root\Data\Retail\Assets\EquipmentFitting.
        /// They are data files, not an overwrite of the game's Data folder, so this one subtree may be copied.</summary>
        static bool IsClientDataPath(string to)
        {
            const string prefix = @"Data\Retail\Assets\EquipmentFitting\";
            if (!to.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            if (to.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return false;
            return to.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)
                && to.Split('\\').All(p => p.Length > 0 && p != "." && p != "..");
        }

        /// <summary>A file next to the exe, or in a folder of its own there - never in the game's own folders, never the realm list,
        /// and the game itself only when gameExe (a folder the user chose).</summary>
        static bool SafeRootPath(string to, bool gameExe = false)
        {
            if (to.Length == 0 || Path.IsPathRooted(to) || to.IndexOf(':') >= 0 || to.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return false;
            var parts = to.Split('\\');
            if (parts.Any(p => p.Length == 0 || p == "." || p == "..")) return false;
            if (new[] { "Data", "WTF", "Cache", "Interface", "Logs", "Errors", "AFK-Realm" }.Any(d => d.Equals(parts[0], StringComparison.OrdinalIgnoreCase)) && parts.Length > 1) return false;
            if (to.Equals("realmlist.wtf", StringComparison.OrdinalIgnoreCase)) return false;
            // the game itself only from a folder the user chose (its old exe is kept as .afk-backup and comes back)
            return gameExe || !new[] { "Ascension.exe", "Wow.exe", "Launcher.exe" }.Any(n => n.Equals(to, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The patched game exe of the same CoA Custom pack (Ascension.exe in 1.8.3, Ascension-b.exe in
        /// older ones), from the client_root, its files folder or the pack; null when there is none.</summary>
        public static string GameExeOf(string clientRoot)
        {
            if (string.IsNullOrEmpty(clientRoot)) return null;
            var dirs = new List<string> { clientRoot.TrimEnd('\\', '/') };
            string files = Path.GetDirectoryName(clientRoot.TrimEnd('\\', '/'));
            if (!string.IsNullOrEmpty(files))
            {
                dirs.Add(files);
                dirs.Add(Os.Resolve(files, "client"));
                string pack = Path.GetDirectoryName(files);
                if (!string.IsNullOrEmpty(pack)) dirs.Add(pack);
            }
            foreach (string name in new[] { "Ascension.exe", "Ascension-b.exe" })
                foreach (string dir in dirs)
                {
                    string file = Os.Resolve(dir, name);
                    if (File.Exists(file)) return file;
                }
            return null;
        }

        /// <summary>CoA Custom's files for next to the exe (files\client_root), from the folder of the pack, its files folder,
        /// or that folder itself; null when there is none.</summary>
        public static string ClientRootOf(string picked)
        {
            if (string.IsNullOrEmpty(picked)) return null;
            picked = picked.TrimEnd('\\', '/');
            foreach (var c in new[] { Os.Resolve(picked, "files/client_root"), Os.Resolve(picked, "client_root"), picked })
                if (Path.GetFileName(c).Equals("client_root", StringComparison.OrdinalIgnoreCase) && Directory.Exists(c) && Directory.GetFiles(c).Length > 0) return c;
            return null;
        }

        /// <summary>The 64-race patch of the same CoA Custom (normally files\client\dinput8.dll), or null.
        /// Some repacks keep it in client_root or the pack root instead, so the exact path is checked first
        /// and then the pack folder.</summary>
        public static string CoAPatchOf(string clientRoot)
        {
            if (string.IsNullOrEmpty(clientRoot)) return null;
            string root = clientRoot.TrimEnd('\\', '/');
            string files = Path.GetDirectoryName(root);
            string exact = files == null ? null : Os.Resolve(files, "client/dinput8.dll");
            if (exact != null && File.Exists(exact) && RacePacker.IsRacePatch(exact)) return exact;
            foreach (string start in new[] { files, root, files == null ? null : Path.GetDirectoryName(files) })
            {
                if (string.IsNullOrEmpty(start) || !Directory.Exists(start)) continue;
                string found = RacePacker.FindRacePatch(start);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>Finds a CoA Custom client_root from the pack folder, its files folder, client or client_root.
        /// The import dialog may be given any of them; the parent folders are checked as well.</summary>
        public static string ClientRootNear(string picked)
        {
            if (string.IsNullOrEmpty(picked)) return null;
            string dir = picked.TrimEnd('\\', '/');
            for (int depth = 0; depth < 3 && !string.IsNullOrEmpty(dir); ++depth)
            {
                string root = ClientRootOf(dir);
                if (root != null) return root;
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }

        /// <summary>After the client patch was installed once, the 64-race DLL is next to
        /// Ascension.exe. If the path into the CoA pack has gone away since then, use that
        /// installed copy instead of telling the player that the DLL is missing.</summary>
        static void UseInstalledRootFiles(Plan plan, string client)
        {
            if (string.IsNullOrEmpty(client)) return;
            foreach (var r in plan.RootFiles)
            {
                if (!r.To.Equals("dinput8.dll", StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrEmpty(r.From) && File.Exists(r.From)) continue;
                string dest = Os.Resolve(client, r.To);
                if (File.Exists(dest) && RacePacker.IsRacePatch(dest)) r.From = dest;
            }
        }

        static bool InstalledRootFilesIntact(Install inst, string client)
        {
            if (inst == null || string.IsNullOrEmpty(client)) return false;
            var placed = RootPlaced(inst);
            if (placed.Count == 0) return false;
            foreach (var p in placed)
            {
                string dest = Os.Resolve(client, p.Key);
                if (!File.Exists(dest) || Hash(dest) != p.Value) return false;
            }
            return true;
        }

        /// <summary>When a source root folder has gone away, its files next to the game are the
        /// only copies left. A ZIP for other players still has to contain them.</summary>
        static void AddInstalledRootFiles(Plan plan, Install inst, string client)
        {
            if (inst == null || string.IsNullOrEmpty(client) || plan.MissingFolders.Count == 0) return;
            foreach (var p in RootPlaced(inst))
            {
                if (plan.RootFiles.Any(r => r.To.Equals(p.Key, StringComparison.OrdinalIgnoreCase))) continue;
                string dest = Os.Resolve(client, p.Key);
                if (File.Exists(dest) && Hash(dest) == p.Value)
                    plan.RootFiles.Add(new RootFile { Module = L.T("the installed client"), To = p.Key, From = dest, Zip = true });
            }
        }

        static void CheckRootFiles(Plan plan, string client = null, Install inst = null)
        {
            UseInstalledRootFiles(plan, client);
            // The CoA pack may have been deleted after its files were installed. The files next
            // to the game are what matters then; do not send the player back to a folder that no
            // longer exists.
            bool missingCoa = plan.MissingFolders.Any(m => m.StartsWith("mod-custom-races\t", StringComparison.OrdinalIgnoreCase));
            bool coaInstalled = missingCoa && !string.IsNullOrEmpty(client) &&
                (File.Exists(Os.Resolve(client, "Ascension.exe")) || File.Exists(Os.Resolve(client, "Ascension-b.exe"))) &&
                File.Exists(Path.Combine(client, "EsteriaAppearance.dll"));
            bool installed = InstalledRootFilesIntact(inst, client);
            foreach (var m in plan.MissingFolders)
            {
                if (installed || (coaInstalled && m.StartsWith("mod-custom-races\t", StringComparison.OrdinalIgnoreCase))) continue;
                var p = m.Split('\t');
                throw new InvalidOperationException(L.F("{0} wants the files of {1} next to the game, but that folder is not there. Open its setup page and choose the folder again, or choose to copy nothing.", p[0], p[1]));
            }
            foreach (var r in plan.RootFiles)
                if (string.IsNullOrEmpty(r.From) || !File.Exists(r.From))
                {
                    if (r.To.Equals("dinput8.dll", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException(L.F("The 64-race patch (dinput8.dll) for {0} was not found. Import the CoA Custom client again, or put dinput8.dll next to Ascension.exe.", r.Module));
                    throw new InvalidOperationException(L.F("{0} wants {1} next to the game, from {2}, which is not there. Open its setup page and choose the file again, or clear that option.",
                        r.Module, r.To, string.IsNullOrEmpty(r.From) ? L.T("a file not chosen yet") : r.From));
                }
        }

        /// <summary>The files next to the exe put there before: name -> hash. Found: those that were there already, the same,
        /// before (put there by hand): they are left where they are when they are no longer wanted.</summary>
        static Dictionary<string, string> RootPlaced(Install inst) { HashSet<string> found; return RootPlaced(inst, out found); }

        static Dictionary<string, string> RootPlaced(Install inst, out HashSet<string> found)
        {
            var placed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(RootRecord(inst)))
                foreach (var line in File.ReadAllLines(RootRecord(inst)))
                {
                    var parts = line.Split('\t');
                    if (parts.Length < 2) continue;
                    placed[parts[0]] = parts[1];
                    if (parts.Length > 2 && parts[2] == "found") found.Add(parts[0]);
                }
            return placed;
        }

        static bool RootFilesCurrent(Install inst, string client, Plan plan)
        {
            UseInstalledRootFiles(plan, client);
            HashSet<string> found;
            var placed = RootPlaced(inst, out found);
            foreach (var r in plan.RootFiles)
            {
                string dest = Os.Resolve(client, r.To);
                if (!File.Exists(dest) || Hash(dest) != Hash(r.From)) return false;
            }
            // A source folder that has gone away is not a request to remove its installed files;
            // the files next to the game are still the ones the patch made.
            if (plan.MissingFolders.Count > 0) return true;
            // one placed before and no longer wanted is still there
            foreach (var p in placed)
                if (!found.Contains(p.Key) && !plan.RootFiles.Any(r => r.To.Equals(p.Key, StringComparison.OrdinalIgnoreCase)) && File.Exists(Os.Resolve(client, p.Key)) && Hash(Path.Combine(client, p.Key)) == p.Value)
                    return false;
            return true;
        }

        /// <summary>Puts the wanted files next to the exe and takes back those no longer wanted. A file of the same name
        /// that was there before is kept as &lt;name&gt;.afk-backup and comes back when ours goes.</summary>
        static List<string> SyncRootFiles(Install inst, string client, Plan plan)
        {
            var notes = new List<string>();
            HashSet<string> found;
            var placed = RootPlaced(inst, out found);
            var now = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var put = new List<string>();
            var gone = new List<string>();
            try
            {
                foreach (var r in plan.RootFiles)
                {
                    string dest = Os.Resolve(client, r.To), want = Hash(r.From), mine;
                    if (File.Exists(dest) && Hash(dest) == want)
                    {
                        // the same file already there: ours from before, or put there by hand (then it stays when no longer wanted)
                        now[r.To] = want;
                        if (found.Contains(r.To) || !(placed.TryGetValue(r.To, out mine) && mine == want)) keep.Add(r.To);
                        continue;
                    }
                    if (File.Exists(dest) && !(placed.TryGetValue(r.To, out mine) && Hash(dest) == mine) && !File.Exists(dest + ".afk-backup"))
                        File.Move(dest, dest + ".afk-backup");
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    File.Copy(r.From, dest, true);
                    now[r.To] = want;
                    put.Add(r.To);
                }
                if (plan.MissingFolders.Count == 0)
                    foreach (var p in placed)
                    {
                        if (now.ContainsKey(p.Key) || found.Contains(p.Key)) continue;
                        string dest = Os.Resolve(client, p.Key);
                        if (File.Exists(dest) && Hash(dest) == p.Value)
                        {
                            File.Delete(dest);
                            if (File.Exists(dest + ".afk-backup")) File.Move(dest + ".afk-backup", dest);
                            gone.Add(p.Key);
                        }
                    }
                else
                    // The source folder is gone, not the files next to the game: keep its
                    // installed files and keep the record for them.
                    foreach (var p in placed)
                        if (!now.ContainsKey(p.Key) && File.Exists(Os.Resolve(client, p.Key)))
                        {
                            now[p.Key] = p.Value;
                            if (found.Contains(p.Key)) keep.Add(p.Key);
                        }
            }
            catch (IOException ex) { throw new InvalidOperationException(L.F("A file next to the game could not be changed ({0}). Close the game and its launcher first.", ex.Message)); }
            catch (UnauthorizedAccessException ex) { throw new InvalidOperationException(L.F("A file next to the game could not be changed ({0}).", ex.Message)); }
            // a few by name, many by their number
            if (put.Count > 3) notes.Add(L.F("{0} files placed next to the game", put.Count)); else notes.AddRange(put.Select(n => L.F("{0} placed next to the game", n)));
            if (gone.Count > 3) notes.Add(L.F("{0} files taken away", gone.Count)); else notes.AddRange(gone.Select(n => L.F("{0} taken away", n)));
            Directory.CreateDirectory(Path.GetDirectoryName(RootRecord(inst)));
            if (now.Count == 0) { if (File.Exists(RootRecord(inst))) File.Delete(RootRecord(inst)); }
            else File.WriteAllLines(RootRecord(inst), now.Select(p => p.Key + "\t" + p.Value + (keep.Contains(p.Key) ? "\tfound" : "")), new UTF8Encoding(false));
            return notes;
        }

        // The patch a set of inputs gives, so that the client patch is not built just to see whether it changed.
        static readonly Dictionary<string, string> wantedStamps = new Dictionary<string, string>();

        static void Stat(StringBuilder sb, string path)
        {
            var fi = new FileInfo(path);
            sb.Append('|').Append(path).Append(':');
            if (fi.Exists) sb.Append(fi.Length).Append(':').Append(fi.LastWriteTimeUtc.Ticks);
        }

        /// <summary>Everything the build of the patch reads, by size and time: the client's archives, the settings of the
        /// modules, their files and manifests, and the race folder.</summary>
        static string InputsKey(Install inst, string client, Plan plan)
        {
            var sb = new StringBuilder(ClientRaces.ArchivesKey(client));
            foreach (var dir in new[] { Path.Combine(inst.ConfigDir, "modules"), inst.ConfigDir })
                if (Directory.Exists(dir))
                    foreach (var f in Directory.GetFiles(dir, "*.conf*").OrderBy(f => f, StringComparer.OrdinalIgnoreCase)) Stat(sb, f);
            string modules = ModuleCatalog.ModulesDir(inst);
            if (Directory.Exists(modules))
                foreach (var d in Directory.GetDirectories(modules).OrderBy(d => d, StringComparer.OrdinalIgnoreCase)) Stat(sb, Path.Combine(d, ModuleCatalog.ManifestName));
            foreach (var f in plan.Files) Stat(sb, f.From);
            foreach (var e in plan.Edits)
            {
                sb.Append('|').Append(e.Module).Append(':').Append(e.File);
                if (e.ImportRace != null) sb.Append(":import");
                object from;
                if (e.AddFile != null && e.AddFile.TryGetValue("from", out from) && from is string && ((string)from).Length > 0) Stat(sb, (string)from);
            }
            foreach (var w in plan.Imports) { sb.Append('|').Append(w.Race); Stat(sb, Path.Combine(w.Pack.Dir, "race.json")); }
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())));
        }

        /// <summary>Whether the client has the patch the modules want. Building it reads the client, which takes a moment.</summary>
        public static PatchState StateOf(Install inst, string client, Plan plan, out string problem)
        {
            problem = null;
            ClientBinaryPatch.Prepare(plan);
            UseInstalledRootFiles(plan, client);
            string installed = client == null ? null : InstalledStamp(client);
            if (plan.Empty) return (!string.IsNullOrEmpty(installed) && WasOurs(inst)) || (client != null && !RootFilesCurrent(inst, client, plan)) ? PatchState.Leftover : PatchState.None;
            if (client == null) return PatchState.Missing;
            if (installed == "") { problem = Foreign; return PatchState.Outdated; }
            try
            {
                string key = InputsKey(inst, client, plan), wanted;
                lock (wantedStamps)
                    if (!wantedStamps.TryGetValue(key, out wanted)) wanted = null;
                if (wanted == null)
                {
                    var files = Build(inst, client, plan, null);
                    wanted = Encoding.UTF8.GetString(files[StampName]);
                    lock (wantedStamps) { wantedStamps.Clear(); wantedStamps[key] = wanted; }
                }
                if (installed == null) return PatchState.Missing;
                CheckRootFiles(plan, client, inst);
                return installed == wanted && RaceImport.Current(client, plan.Imports) && RootFilesCurrent(inst, client, plan) ? PatchState.Current : PatchState.Outdated;
            }
            catch (Exception ex) { problem = ex.Message; return PatchState.Outdated; }
        }

        static bool WasOurs(Install inst) { return File.Exists(RecordFile(inst)); }

        /// <summary>The server gets the same appearance tables the imported races bring to the client. Its own
        /// tables are kept as .afk-base and restored when the last imported race goes.</summary>
        static readonly string[] ServerAppearanceTables =
        {
            "CharSections.dbc", "HDCharSections.dbc", "CharHairGeosets.dbc", "HDCharHairGeosets.dbc",
            "CharacterFacialHairStyles.dbc", "HDCharacterFacialHairStyles.dbc", "CharHairTextures.dbc"
        };

        static void SyncServerAppearance(Install inst, Plan plan, SortedDictionary<string, byte[]> files)
        {
            string dir = Path.Combine(inst.DataDir, "dbc");
            if (!Directory.Exists(dir)) return;

            bool imported = plan.Imports.Count > 0;
            foreach (string name in ServerAppearanceTables)
            {
                string target = Path.Combine(dir, name);
                string original = target + ".afk-base";
                try
                {
                    if (!File.Exists(original) && File.Exists(target)) File.Copy(target, original, false);
                    byte[] bytes;
                    if (imported && files != null && files.TryGetValue("DBFilesClient\\" + name, out bytes))
                        File.WriteAllBytes(target, bytes);
                    else if (!imported && File.Exists(original))
                        File.Copy(original, target, true);
                }
                catch (IOException)
                {
                    // A running worldserver may hold the table; the next install/restart repeats this.
                }
            }
        }

        /// <summary>Builds the patch and puts it into the game folder, or takes it out when no module needs it any more.</summary>
        public static string Install(Install inst, string client, Plan plan) { return Install(inst, client, plan, null); }

        /// <summary>The same, reporting what it does (text, step, steps; 0 steps: no measure).</summary>
        public static string Install(Install inst, string client, Plan plan, Action<string, int, int> progress)
        {
            if (progress == null) progress = (t, a, b) => { };
            ClientBinaryPatch.Prepare(plan);
            UseInstalledRootFiles(plan, client);
            RemoveLegacyPatch(client);
            string path = Os.Resolve(client, "Data/" + ArchiveName);
            string installed = InstalledStamp(client);
            // Race archives are independent of the client patch: even when no module changes a client file,
            // the races must be written (and old per-race archives removed).
            // race packs made by an older packer: packed again from their client first (RaceRepair.RefreshOldPacks)
            var repacked = RaceRepair.RefreshOldPacks(inst, client, plan.Imports, t => progress(t, 0, 0));
            if (repacked.Count > 0)
            {
                plan = Collect(inst);
                ClientBinaryPatch.Prepare(plan);
                UseInstalledRootFiles(plan, client);
            }
            var raceNotes = RaceImport.Sync(client, plan.Imports, progress);
            raceNotes.InsertRange(0, repacked);
            if (plan.Empty)
            {
                SyncServerAppearance(inst, plan, null);
                var gone = SyncRootFiles(inst, client, plan);
                gone.AddRange(raceNotes);
                if (!string.IsNullOrEmpty(installed) && WasOurs(inst))
                {
                    try { File.Delete(path); }
                    catch (IOException) { throw new InvalidOperationException(L.F("Data\\{0} is in use. Close the game and its launcher first.", ArchiveName)); }
                    File.Delete(RecordFile(inst));
                    return L.T("the client patch removed (no module needs it any more)") + (gone.Count > 0 ? ", " + string.Join(", ", gone) : "");
                }
                return string.Join(", ", gone);
            }
            if (installed == "") throw new InvalidOperationException(Foreign);
            CheckRootFiles(plan, client, inst);
            var notes = new List<string>();
            string temp = path + ".new";
            string what = L.F("Writing Data\\{0}", ArchiveName);
            SortedDictionary<string, byte[]> files = null;
            // With custom races: the patch is checked before it goes into the game (RaceCheck), what the check finds is
            // repaired (RaceRepair) and the patch built again - until nothing is left; a race that cannot be repaired is
            // switched off, so no player gets it in pieces.
            bool races = plan.Modules.Any(m => m.IndexOf("custom-races", StringComparison.OrdinalIgnoreCase) >= 0);
            var repaired = new List<string>();
            var tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var strict = new HashSet<int>();
            var missingTextures = new Dictionary<int, HashSet<string>>();
            var off = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int offDone = 0;
            for (int round = 0; ; round++)
            {
                plan.StrictSections = new HashSet<int>(strict);
                plan.MissingTextures = missingTextures.ToDictionary(kv => kv.Key, kv => new HashSet<string>(kv.Value, StringComparer.OrdinalIgnoreCase));
                var roundNotes = new List<string>();
                progress(L.T("Building the client patch from your game client …"), 0, 0);
                files = Build(inst, client, plan, roundNotes);
                progress(what + " …", 0, files.Count + 2);
                MpqArchive.Write(temp, files, (done, all) => progress(what + ": " + done + " of " + all + " files", done, all));
                if (!races || round >= 5) { notes.AddRange(roundNotes); break; }
                progress(L.T("Checking the custom races …"), 0, 0);
                var found = RaceCheck.Inspect(inst, client, ArchivesWith(client, path, temp)).Where(f => f.Kind != RaceCheck.Stopped).ToList();
                if (found.Count == 0) { notes.AddRange(roundNotes); break; }
                bool changed = round < 4 && RaceRepair.Fix(inst, client, found, tried, strict, missingTextures, off, repaired, t => progress(t, 0, 0));
                if (!changed)
                    foreach (var f in found.Where(f => !string.IsNullOrEmpty(f.Key) && !off.ContainsKey(f.Key))) off[f.Key] = f.Text;
                if (off.Count > offDone)
                {
                    RaceRepair.SwitchOff(inst, off.Keys);
                    foreach (var o in off.Skip(offDone)) repaired.Add(L.F("switched off - {0}", o.Value));
                    offDone = off.Count;
                    changed = true;
                }
                if (!changed) { notes.AddRange(roundNotes); break; }
                // the settings changed: what the modules want of the client, and the race archives, are read again
                plan = Collect(inst);
                ClientBinaryPatch.Prepare(plan);
                UseInstalledRootFiles(plan, client);
                raceNotes = RaceImport.Sync(client, plan.Imports, progress);
                if (plan.Empty) break;
            }
            try
            {
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            catch (IOException)
            {
                try { File.Delete(temp); } catch { }
                throw new InvalidOperationException(L.F("Data\\{0} is in use. Close the game and its launcher first.", ArchiveName));
            }
            lock (wantedStamps) { wantedStamps.Clear(); wantedStamps[InputsKey(inst, client, plan)] = Encoding.UTF8.GetString(files[StampName]); }
            SyncServerAppearance(inst, plan, files);
            notes.AddRange(raceNotes);
            progress(L.T("Placing the files next to the game …"), 0, 0);
            notes.AddRange(SyncRootFiles(inst, client, plan));
            // every custom race, both sexes, against what the game reads now: written to logs\race-check.txt with the repairs
            if (races)
            {
                var problems = RaceCheck.Run(inst, client, repaired);
                if (repaired.Count > 0) notes.Add(L.F("race check: {0} repaired, see logs\\race-check.txt", repaired.Count));
                notes.AddRange(repaired.Take(12));
                notes.Add(problems.Count == 0 ? L.T("race check: no problems") : L.F("race check: {0} problems, see logs\\race-check.txt", problems.Count));
                notes.AddRange(problems.Take(12));
            }
            Directory.CreateDirectory(Path.GetDirectoryName(RecordFile(inst)));
            File.WriteAllLines(RecordFile(inst), new[] { ArchiveName }.Concat(plan.Modules), new UTF8Encoding(false));
            return L.F("the client patch Data\\{0} built for {1}", ArchiveName, string.Join(", ", plan.Modules)) + (notes.Count > 0 ? " " + L.F("(changes {0})", string.Join(", ", notes)) : "");
        }

        /// <summary>The archives of the game in the order it reads them, with the patch about to be installed (temp) in the
        /// place of the installed one.</summary>
        static List<string> ArchivesWith(string client, string path, string temp)
        {
            // every archive the game reads - the race archives of AFK Realm too, the imported races' models are in them
            var list = ClientArchives(client, true);
            int at = list.FindIndex(p => string.Equals(Path.GetFullPath(p), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));
            if (at >= 0) { list[at] = temp; return list; }
            // not there yet (first install): where the game will read it - after every lettered patch of Data whose name
            // sorts before it. "patch.MPQ" sorts after "patch-ZZZZZ.MPQ" by its characters, but the game reads it
            // first; only the lettered patches count here.
            string data = Path.GetFullPath(Os.Resolve(client, "Data"));
            Func<string, bool> lettered = p =>
            {
                string name = Path.GetFileName(p);
                return string.Equals(Path.GetDirectoryName(Path.GetFullPath(p)), data, StringComparison.OrdinalIgnoreCase) &&
                    name.StartsWith("patch-", StringComparison.OrdinalIgnoreCase) && !Regex.IsMatch(name, @"^patch-\d\.mpq$", RegexOptions.IgnoreCase);
            };
            at = list.FindIndex(p => lettered(p) && StringComparer.OrdinalIgnoreCase.Compare(Path.GetFileName(p), ArchiveName) > 0);
            if (at < 0) at = list.FindLastIndex(p => lettered(p)) + 1;
            if (at <= 0) list.Add(temp); else list.Insert(at, temp);
            return list;
        }

        /// <summary>Puts the patch into a ZIP for other players, as Data\patch-ZZZZZ.MPQ.</summary>
        public static void AddToZip(ZipArchive zip, Install inst, string client, Plan plan) { AddToZip(zip, inst, client, plan, null); }

        public static void AddToZip(ZipArchive zip, Install inst, string client, Plan plan, Action<string, int, int> progress)
        {
            if (progress == null) progress = (t, a, b) => { };
            ClientBinaryPatch.Prepare(plan);
            UseInstalledRootFiles(plan, client);
            AddInstalledRootFiles(plan, inst, client);
            // Installed files added from the record may still be unpatched originals; run the
            // binary patcher over them as well before they go into the ZIP.
            ClientBinaryPatch.Prepare(plan);
            progress(L.T("Building the client patch from your game client …"), 0, 0);
            var files = Build(inst, client, plan, null);
            string temp = Path.GetTempFileName();
            try
            {
                string what = L.F("Writing Data\\{0}", ArchiveName);
                progress(what + " …", 0, files.Count + 2);
                MpqArchive.Write(temp, files, (done, all) => progress(what + ": " + done + " of " + all + " files", done, all));
                zip.CreateEntryFromFile(temp, "Data/" + ArchiveName, CompressionLevel.Optimal);
            }
            finally { try { File.Delete(temp); } catch { } }
            CheckRootFiles(plan, client, inst);
            // the files next to the exe, CoA Custom's too (its Ascension.exe, EsteriaAppearance.dll, ...): a module may keep one out ("zip": false)
            foreach (var r in plan.RootFiles.Where(r => r.Zip)) zip.CreateEntryFromFile(r.From, r.To.Replace('\\', '/'), CompressionLevel.Optimal);
            var groups = RaceImport.Groups(plan.Imports);
            for (int i = 0; i < groups.Count; i++)
            {
                progress(L.F("Packing the files of {0} races into the ZIP …", groups[i].Count), i, groups.Count);
                string race = RaceImport.BuildGroupTemp(groups[i]);
                try { zip.CreateEntryFromFile(race, "Data/" + RaceImport.GroupName(i), CompressionLevel.Fastest); }
                finally { try { File.Delete(race); } catch { } }
            }
        }
    }
}
