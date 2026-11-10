using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace CoAInstaller
{
    enum ModuleState { Available, Managed, ByHand, PartOfCoA }

    /// <summary>One line of the module list: a catalog entry, an installed module, or both.</summary>
    class ModuleEntry
    {
        public string Name = "", FullName = "", Url = "", CloneUrl = "", Description = "", Branch = "";
        public int Stars;
        public DateTime Pushed;
        public bool Archived, InCatalog;
        /// <summary>Made for AFK Realm: listed first, in a section of its own.</summary>
        public bool Own;
        /// <summary>Not shown in the module list: a part another feature installs by itself (mod-subclass comes with CoA
        /// Custom when its package needs it, and does nothing on its own).</summary>
        public bool Hidden;
        /// <summary>A module by someone else that AFK Realm recommends (ExtraModules): its own tab in the module list.</summary>
        public bool Recommended;
        public ModuleState State;
        public string Status = "", InstalledOn = "";                      // from modules.txt
        public string[] CreatedTables = new string[0], ManualTables = new string[0];
        public bool Installed { get { return State == ModuleState.Managed || State == ModuleState.ByHand; } }
        /// <summary>Playerbots: AFK Realm always builds its CoA branch; catalog entries of the same name are other versions.</summary>
        public bool IsPlayerbots { get { return Name.Equals("mod-playerbots", StringComparison.OrdinalIgnoreCase); } }
        /// <summary>Dungeon Clear: Zyth45's module is part of every server, like Playerbots, and cannot be removed.</summary>
        public bool IsDungeonClear { get { return Name.Equals("mod-dungeon-clear", StringComparison.OrdinalIgnoreCase); } }
        public bool IsBuiltIn { get { return IsPlayerbots || IsDungeonClear; } }
    }

    /// <summary>What AFK Realm found out about a module before installing it.</summary>
    class ModuleAnalysis
    {
        public readonly List<KeyValuePair<int, string>> Checks = new List<KeyValuePair<int, string>>();   // 0 ok, 1 warning, 2 problem
        public string ReadmeText = "";
        /// <summary>The modules its afk-realm.json names under "requires" (repository names).</summary>
        public List<string> Requires = new List<string>();
        public void Add(int level, string text) { Checks.Add(new KeyValuePair<int, string>(level, text)); }
        public int Worst { get { return Checks.Count == 0 ? 0 : Checks.Max(c => c.Key); } }
    }

    /// <summary>
    /// The AzerothCore module catalog (GitHub repositories with the topic "azerothcore-module",
    /// the same list as azerothcore.org/catalogue), the installed modules, and a quick check of
    /// a module before it is installed. GitHub allows 60 requests an hour without sign-in, so
    /// the list is cached for a day and a module is only checked when it is looked at.
    /// </summary>
    static class ModuleCatalog
    {
        const string Topic = "azerothcore-module";
        /// <summary>A module can bring changes for the core or Playerbots; this file in its folder names them (see the engine).</summary>
        public const string ManifestName = "afk-realm.json";

        /// <summary>Modules made for AFK Realm. They are always in the list, whatever the catalog says.</summary>
        internal static ModuleEntry[] OwnModules()
        {
            return new[]
            {
                new ModuleEntry { Name = "mod-playerbots-auctions", FullName = "aspollon/mod-playerbots-auctions", Branch = "main", Own = true,
                    Url = "https://github.com/aspollon/mod-playerbots-auctions", CloneUrl = "https://github.com/aspollon/mod-playerbots-auctions.git",
                    Description = L.N("The bots use the auction house like players: they sell their loot, buy and bid, craft and gather") },
                new ModuleEntry { Name = "mod-playerbots-immersion", FullName = "aspollon/mod-playerbots-immersion", Branch = "main", Own = true,
                    Url = "https://github.com/aspollon/mod-playerbots-immersion", CloneUrl = "https://github.com/aspollon/mod-playerbots-immersion.git",
                    Description = L.N("City Life as AFK Realm continues it: city population, questing, PvP Life, Fishing Life and legendary residents for playerbots") },
                new ModuleEntry { Name = "mod-world-journey", FullName = "aspollon/mod-world-journey", Branch = "main", Own = true,
                    Url = "https://github.com/aspollon/mod-world-journey", CloneUrl = "https://github.com/aspollon/mod-world-journey.git",
                    Description = L.N("The whole world - old world, Outland and Northrend - as one journey from 1 to 60, for the classes of CoA") },
                new ModuleEntry { Name = "mod-classic-classes", FullName = "aspollon/mod-classic-classes", Branch = "main", Own = true,
                    Url = "https://github.com/aspollon/mod-classic-classes", CloneUrl = "https://github.com/aspollon/mod-classic-classes.git",
                    Description = L.N("The classic classes - Warrior to Druid, as Warcraft Reborn plays them - next to the classes of CoA, for players and bots") },
                new ModuleEntry { Name = "mod-custom-races", FullName = "aspollon/mod-custom-races", Branch = "main", Own = true,
                    Url = "https://github.com/aspollon/mod-custom-races", CloneUrl = "https://github.com/aspollon/mod-custom-races.git",
                    Description = L.N("Required for CoA Custom: makes its races (Dracthyr, Worgen, Vulpera, Sethrak, High elves and more) playable on your server, for players and bots. Brings mod-classic-classes along.") },
                new ModuleEntry { Name = "mod-subclass", FullName = "aspollon/mod-subclass", Branch = "main", Own = true, Hidden = true,
                    Url = "https://github.com/aspollon/mod-subclass", CloneUrl = "https://github.com/aspollon/mod-subclass.git",
                    Description = L.N("For CoA Custom 1.9.8 and newer: the second class of the Two-Headed Ogre and the Thin Twins (Stefan's subclassing). Needs mod-custom-races.") },
                new ModuleEntry { Name = "mod-afk-junk-to-gold", FullName = "aspollon/mod-afk-junk-to-gold", Branch = "main", Own = true,
                    Url = "https://github.com/aspollon/mod-afk-junk-to-gold", CloneUrl = "https://github.com/aspollon/mod-afk-junk-to-gold.git",
                    Description = L.N("Sells gray junk for real players, safely after the loot hooks; bots stay with the auction-house module") },
                new ModuleEntry { Name = "mod-azeroth-flying", FullName = "aspollon/mod-azeroth-flying", Branch = "main", Own = true,
                    Url = "https://github.com/aspollon/mod-azeroth-flying", CloneUrl = "https://github.com/aspollon/mod-azeroth-flying.git",
                    Description = L.N("Flying mounts in Eastern Kingdoms and Kalimdor - an optional, experimental setting for old Azeroth") },
            };
        }
        /// <summary>Modules by others that work on every server kind and are worth offering, even when the AzerothCore catalog
        /// does not list them (no "azerothcore-module" topic). They are listed like catalog entries, not as AFK Realm's own.</summary>
        internal static ModuleEntry[] ExtraModules()
        {
            return new[]
            {
                // The loader is Addmod_ilusixn_devmenuScripts: the engine puts the repository into modules\mod-ilusixn-devmenu
                // (Get-ModuleLoaderName). Its client add-on (client\DevMenu) is handed out like every module add-on.
                new ModuleEntry { Name = "ilusixn-dev-menu", FullName = "ilusixn/ilusixn-dev-menu", Branch = "main", Recommended = true,
                    Url = "https://github.com/ilusixn/ilusixn-dev-menu", CloneUrl = "https://github.com/ilusixn/ilusixn-dev-menu.git",
                    Description = L.N("Dev Menu by ilusixn: an in-game world editor for game masters (/dev) - spawn, move, dress and script NPCs, paths, shops, quests and objects; changes are saved to the world database and can be exported as SQL") },
            };
        }
        static readonly Dictionary<string, ModuleAnalysis> analyses = new Dictionary<string, ModuleAnalysis>(StringComparer.OrdinalIgnoreCase);

        static string CacheFile(Install inst) { return Path.Combine(inst.Root, "Dependencies", "Cache", "module-catalog.txt"); }
        public static string ModulesDir(Install inst) { return Path.Combine(inst.SourceDir, "modules"); }

        /// <summary>A repository name from "owner/name", an address or a plain name ("mod-classic-classes").</summary>
        public static string ShortName(string name)
        {
            string n = (name ?? "").Trim().TrimEnd('/');
            if (n.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 4);
            int slash = n.LastIndexOf('/');
            return slash >= 0 ? n.Substring(slash + 1) : n;
        }

        /// <summary>"requires" of an afk-realm.json: the modules a module does not work without (repository names).</summary>
        public static List<string> ParseRequires(string json)
        {
            var list = new List<string>();
            try
            {
                var data = new JavaScriptSerializer().DeserializeObject(json ?? "") as Dictionary<string, object>;
                object v;
                if (data != null && data.TryGetValue("requires", out v) && v is object[])
                    foreach (var item in (object[])v)
                    {
                        string name = ShortName(Convert.ToString(item));
                        if (Regex.IsMatch(name, @"^[\w.\-]+$") && !list.Contains(name, StringComparer.OrdinalIgnoreCase)) list.Add(name);
                    }
            }
            catch { }
            return list;
        }

        /// <summary>The "requires" of an installed module (its folder below modules).</summary>
        public static List<string> RequiresOf(string folder)
        {
            string file = Path.Combine(folder, ManifestName);
            try { return File.Exists(file) ? ParseRequires(File.ReadAllText(file)) : new List<string>(); }
            catch { return new List<string>(); }
        }

        /// <summary>Installed modules whose "requires" names a module that is not installed: (module, missing).</summary>
        public static List<KeyValuePair<string, string>> MissingRequirements(Install inst)
        {
            var missing = new List<KeyValuePair<string, string>>();
            string dir = ModulesDir(inst);
            if (!Directory.Exists(dir)) return missing;
            var installed = new HashSet<string>(Directory.GetDirectories(dir).Select(d => Path.GetFileName(d)), StringComparer.OrdinalIgnoreCase);
            foreach (string folder in Directory.GetDirectories(dir))
                foreach (string need in RequiresOf(folder))
                    if (!installed.Contains(need)) missing.Add(new KeyValuePair<string, string>(Path.GetFileName(folder), need));
            return missing;
        }

        static string Get(string url, string accept = "application/vnd.github+json")
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }
            string token = Settings.GitHubTokenActive;
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = Product.FileStem;
            req.Accept = accept;
            req.Timeout = 20000; req.ReadWriteTimeout = 20000;
            if (token != null) req.Headers["Authorization"] = "Bearer " + token;
            try
            {
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var rd = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) return rd.ReadToEnd();
            }
            catch (WebException ex)
            {
                var resp = ex.Response as HttpWebResponse;
                // an outdated or mistyped token: the same request once more, without it
                if (token != null && resp != null && resp.StatusCode == HttpStatusCode.Unauthorized)
                {
                    Settings.RefuseGitHubToken(token);
                    return Get(url, accept);
                }
                if (resp != null && (resp.StatusCode == HttpStatusCode.Forbidden || (int)resp.StatusCode == 429))
                {
                    string message = L.T("GitHub only allows a limited number of requests per hour without signing in, and that limit is used up. Please try again later.");
                    if (Settings.GitHubTokenActive == null) message += " " + L.T("You can enter a GitHub token on this page to raise the limit.");
                    throw new InvalidOperationException(message);
                }
                throw;
            }
        }

        static object Field(Dictionary<string, object> d, string key) { object v; return d != null && d.TryGetValue(key, out v) ? v : null; }
        static string Text(Dictionary<string, object> d, string key) { var v = Field(d, key); return v == null ? "" : Convert.ToString(v, CultureInfo.InvariantCulture); }

        /// <summary>"owner/repo" of a GitHub address, or null.</summary>
        public static string GitHubName(string url)
        {
            var m = Regex.Match(url ?? "", @"github\.com[/:]([\w.\-]+)/([\w.\-]+?)(?:\.git)?/?$", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value + "/" + m.Groups[2].Value : null;
        }

        /// <summary>Folder name the engine uses for a module address.</summary>
        public static string FolderName(string url)
        {
            string name = (url ?? "").Trim().TrimEnd('/');
            name = name.Substring(Math.Max(name.LastIndexOfAny(new[] { '/', ':' }) + 1, 0));
            if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
            return name;
        }

        // ---------------------------------------------------------------- catalog

        /// <summary>The catalog from the cache, or from GitHub when the cache is older than a day (or forced).</summary>
        public static List<ModuleEntry> LoadCatalog(Install inst, bool refresh, out string warning)
        {
            warning = null;
            string cache = CacheFile(inst);
            List<ModuleEntry> cached = ReadCache(cache);
            bool fresh = cached != null && File.Exists(cache) && File.GetLastWriteTimeUtc(cache) > DateTime.UtcNow.AddDays(-1);
            if (fresh && !refresh) return cached;
            try
            {
                var list = Download();
                WriteCache(cache, list);
                return list;
            }
            catch (Exception ex)
            {
                if (cached != null) { warning = L.F("The module list could not be updated ({0}) The list from {1} is shown.", ex.Message, File.GetLastWriteTime(cache).ToString("g")); return cached; }
                throw new InvalidOperationException(L.T("The module list could not be loaded from GitHub.") + " " + ex.Message);
            }
        }

        static List<ModuleEntry> Download()
        {
            var list = new List<ModuleEntry>();
            var json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            for (int page = 1; page <= 10; page++)
            {
                var root = json.DeserializeObject(Get("https://api.github.com/search/repositories?q=topic:" + Topic + "&sort=stars&order=desc&per_page=100&page=" + page)) as Dictionary<string, object>;
                var items = Field(root, "items") as object[];
                if (items == null) break;
                foreach (Dictionary<string, object> it in items.OfType<Dictionary<string, object>>())
                {
                    DateTime pushed;
                    DateTime.TryParse(Text(it, "pushed_at"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out pushed);
                    list.Add(new ModuleEntry
                    {
                        Name = Text(it, "name"), FullName = Text(it, "full_name"), Url = Text(it, "html_url"), CloneUrl = Text(it, "clone_url"),
                        Description = Text(it, "description").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim(),
                        Branch = Text(it, "default_branch"), Stars = Convert.ToInt32(Field(it, "stargazers_count") ?? 0),
                        Pushed = pushed, Archived = Text(it, "archived") == "True", InCatalog = true
                    });
                }
                if (items.Length < 100) break;
            }
            if (list.Count == 0) throw new InvalidOperationException(L.T("GitHub returned an empty module list."));
            return list;
        }

        static void WriteCache(string file, List<ModuleEntry> list)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            var lines = list.Select(m => string.Join("\t", new[] { m.Name, m.FullName, m.Url, m.CloneUrl, m.Branch, m.Stars.ToString(CultureInfo.InvariantCulture),
                m.Pushed.ToString("o", CultureInfo.InvariantCulture), m.Archived ? "1" : "0", m.Description }));
            File.WriteAllLines(file, new[] { "afk-module-catalog 1" }.Concat(lines), new UTF8Encoding(false));
        }

        static List<ModuleEntry> ReadCache(string file)
        {
            try
            {
                if (!File.Exists(file)) return null;
                var lines = File.ReadAllLines(file, Encoding.UTF8);
                if (lines.Length < 2 || lines[0] != "afk-module-catalog 1") return null;
                var list = new List<ModuleEntry>();
                foreach (var l in lines.Skip(1))
                {
                    var f = l.Split('\t');
                    if (f.Length < 9) continue;
                    DateTime pushed; DateTime.TryParse(f[6], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out pushed);
                    list.Add(new ModuleEntry { Name = f[0], FullName = f[1], Url = f[2], CloneUrl = f[3], Branch = f[4], Stars = int.Parse(f[5], CultureInfo.InvariantCulture),
                        Pushed = pushed, Archived = f[7] == "1", Description = f[8], InCatalog = true });
                }
                return list;
            }
            catch { return null; }
        }

        // ---------------------------------------------------------------- installed modules

        static string OriginUrl(string folder)
        {
            try
            {
                string config = Path.Combine(folder, ".git", "config");
                if (!File.Exists(config)) return "";
                var m = Regex.Match(File.ReadAllText(config), @"\[remote ""origin""\][^\[]*?url\s*=\s*(\S+)", RegexOptions.IgnoreCase);
                return m.Success ? m.Groups[1].Value : "";
            }
            catch { return ""; }
        }

        /// <summary>The catalog merged with what is in the server's modules folder and the module registry.</summary>
        public static List<ModuleEntry> Merge(Install inst, List<ModuleEntry> catalog)
        {
            var result = new List<ModuleEntry>(catalog ?? new List<ModuleEntry>());
            // The modules by AFK Realm are made for the CoA core: a stock AzerothCore server neither lists nor offers them.
            if (!inst.IsCoa)
            {
                var ownNames = new HashSet<string>(OwnModules().Select(o => o.FullName), StringComparer.OrdinalIgnoreCase);
                result.RemoveAll(e => ownNames.Contains(e.FullName));
            }
            foreach (var own in inst.IsCoa ? OwnModules() : new ModuleEntry[0])
            {
                var listed = result.FirstOrDefault(e => e.FullName.Equals(own.FullName, StringComparison.OrdinalIgnoreCase));
                if (listed != null) { listed.Own = true; listed.Hidden = own.Hidden; if (listed.Description.Length == 0) listed.Description = own.Description; }
                else result.Insert(0, own);
            }
            foreach (var extra in ExtraModules())
            {
                var listed = result.FirstOrDefault(e => e.FullName.Equals(extra.FullName, StringComparison.OrdinalIgnoreCase));
                if (listed == null) { result.Add(extra); continue; }
                listed.Recommended = true;
                listed.Description = extra.Description;
            }
            var managed = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            string listFile = Path.Combine(inst.Root, "Dependencies", "modules.txt");
            if (File.Exists(listFile))
                foreach (var line in File.ReadAllLines(listFile))
                {
                    var f = line.Split('|');
                    if (f.Length >= 8 && f[0].Length > 0) managed[f[0]] = f;
                }

            string dir = ModulesDir(inst);
            var folders = Directory.Exists(dir) ? Directory.GetDirectories(dir).Select(Path.GetFileName).ToList() : new List<string>();
            foreach (var name in folders.Concat(managed.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (name.Equals("mod-playerbots", StringComparison.OrdinalIgnoreCase) || name.Equals("mod-dungeon-clear", StringComparison.OrdinalIgnoreCase)) continue;
                string folder = Path.Combine(dir, name);
                string[] info; managed.TryGetValue(name, out info);
                bool hasGit = Directory.Exists(Path.Combine(folder, ".git"));
                // A folder without its own Git history is normally a module that ships with the core - unless it is one of ours, copied in by hand.
                bool own = OwnModules().Any(o => o.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                // An AFK-owned module with a Git checkout is managed by AFK even when an older
                // installation or a copied module folder left it out of modules.txt. Without this,
                // a perfectly updatable AFK module (Junk to Gold, mod-subclass, ...) was shown as
                // "added by hand" and looked unmanaged.
                var state = info != null || (own && hasGit) ? ModuleState.Managed : hasGit || own ? ModuleState.ByHand : ModuleState.PartOfCoA;
                string url = info != null ? info[1] : OriginUrl(folder);
                if (url.Length == 0 && own) url = OwnModules().First(o => o.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).CloneUrl;
                string full = GitHubName(url);
                // Prefer the catalog entry of the same repository, else one with the same name.
                var entry = result.FirstOrDefault(e => full != null && e.FullName.Equals(full, StringComparison.OrdinalIgnoreCase))
                         ?? (state == ModuleState.PartOfCoA ? null : result.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && full == null));
                if (state == ModuleState.PartOfCoA)
                {
                    // Modules that ship with the CoA core are only shown when the catalog lists them too.
                    foreach (var same in result.Where(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) same.State = ModuleState.PartOfCoA;
                    continue;
                }
                if (entry == null)
                {
                    entry = new ModuleEntry { Name = name, FullName = full ?? "", Url = full != null ? "https://github.com/" + full : url, CloneUrl = url,
                        Description = info != null ? "" : L.N("Added by hand") };
                    result.Add(entry);
                }
                entry.State = state;
                if (!entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) entry.Name = name;
                if (info != null)
                {
                    entry.Status = info[4]; entry.InstalledOn = info[5];
                    entry.CreatedTables = info[6].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    entry.ManualTables = info[7].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    if (!Directory.Exists(folder)) entry.Status = "missing";
                }
            }
            // Playerbots and Dungeon Clear are always built in, so catalog entries are shown as included, not installable.
            foreach (var bots in result.Where(e => e.IsPlayerbots)) bots.State = ModuleState.PartOfCoA;
            var dungeon = result.FirstOrDefault(e => e.IsDungeonClear);
            if (dungeon == null)
            {
                dungeon = new ModuleEntry
                {
                    Name = "mod-dungeon-clear", FullName = "Zyth45/mod-dungeon-clear", Branch = inst.IsCoa ? "coa" : "master",
                    Url = "https://github.com/Zyth45/mod-dungeon-clear", CloneUrl = "https://github.com/Zyth45/mod-dungeon-clear.git",
                    Description = L.N("Zyth45's Dungeon Clear for the Playerbots: the tank clears a dungeon boss by boss. Included in every AFK Realm server, like Playerbots; the DungeonClear add-on is installed automatically.")
                };
                result.Add(dungeon);
            }
            dungeon.State = ModuleState.PartOfCoA;
            return result;
        }

        // ---------------------------------------------------------------- checking a module

        static string DbOfFolder(string folder)
        {
            string n = folder.ToLowerInvariant();
            if (n.Contains("playerbot")) return "playerbots";
            if (n.Contains("world")) return "world";
            if (n.Contains("characters")) return "characters";
            if (n.Contains("auth")) return "auth";
            return null;
        }

        /// <summary>Looks at the module's files and README on GitHub (two requests, cached for the session).</summary>
        public static ModuleAnalysis Analyze(ModuleEntry m)
        {
            string key = string.IsNullOrEmpty(m.FullName) ? m.CloneUrl : m.FullName;
            lock (analyses) { ModuleAnalysis known; if (analyses.TryGetValue(key, out known)) return known; }
            var a = new ModuleAnalysis();
            var json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            string full = string.IsNullOrEmpty(m.FullName) ? GitHubName(m.CloneUrl) : m.FullName;
            if (full == null)
            {
                a.Add(1, L.F("This module is not on GitHub, so {0} cannot check it beforehand. Read its description before installing.", Product.Name));
                return a;
            }
            if (!m.InCatalog)
            {
                var repo = json.DeserializeObject(Get("https://api.github.com/repos/" + full)) as Dictionary<string, object>;
                m.Branch = Text(repo, "default_branch");
                if (!m.Own || m.Description.Length == 0) m.Description = Text(repo, "description");
                m.Archived = Text(repo, "archived") == "True";
                DateTime pushed; if (DateTime.TryParse(Text(repo, "pushed_at"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out pushed)) m.Pushed = pushed;
                m.FullName = full;
            }

            var tree = json.DeserializeObject(Get("https://api.github.com/repos/" + full + "/git/trees/" + Uri.EscapeDataString(m.Branch) + "?recursive=1")) as Dictionary<string, object>;
            var paths = ((Field(tree, "tree") as object[]) ?? new object[0]).OfType<Dictionary<string, object>>()
                .Where(t => Text(t, "type") == "blob").Select(t => Text(t, "path")).ToList();

            string readmePath = paths.FirstOrDefault(p => Regex.IsMatch(p, @"^readme(\.(md|markdown|txt|rst))?$", RegexOptions.IgnoreCase));
            string readmeText = "";
            if (readmePath != null)
            {
                try { readmeText = Get("https://raw.githubusercontent.com/" + full + "/" + m.Branch + "/" + readmePath, "text/plain"); } catch { }
            }
            a = Check(m, paths, readmeText);
            if (paths.Any(p => p.Equals(ManifestName, StringComparison.OrdinalIgnoreCase)))
            {
                try { a.Requires = ParseRequires(Get("https://raw.githubusercontent.com/" + full + "/" + m.Branch + "/" + ManifestName, "text/plain")); } catch { }
                if (a.Requires.Count > 0) a.Add(0, L.F("It needs {0}; {1} installs it together with it.", string.Join(", ", a.Requires), Product.Name));
            }
            lock (analyses) analyses[key] = a;
            return a;
        }

        /// <summary>The checks for a module's file list and README text.</summary>
        public static ModuleAnalysis Check(ModuleEntry m, List<string> paths, string readme)
        {
            var a = new ModuleAnalysis { ReadmeText = readme ?? "" };
            readme = a.ReadmeText;

            // "No core patch, no SQL, no client patch" is no request for one (Dev Menu's README): denied mentions are left out.
            readme = Regex.Replace(readme, @"(?i)\b(no|without|not|nor|never)\b[^.\n]{0,40}?\b(core|client|dbc)\s+patch(es)?\b", " ");
            bool code = paths.Any(p => Regex.IsMatch(p, @"\.(cpp|h|hpp)$", RegexOptions.IgnoreCase));
            var sqlFolders = paths.Select(p => Regex.Match(p, @"^data/sql/([^/]+)/.+\.sql$", RegexOptions.IgnoreCase)).Where(x => x.Success)
                .Select(x => x.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var dbs = sqlFolders.Select(DbOfFolder).Where(d => d != null && d != "playerbots").Distinct().ToList();
            var ignored = sqlFolders.Where(f => DbOfFolder(f) == null || DbOfFolder(f) == "playerbots").ToList();
            var looseSql = paths.Where(p => p.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) && !p.StartsWith("data/sql/", StringComparison.OrdinalIgnoreCase)).ToList();
            bool conf = paths.Any(p => Regex.IsMatch(p, @"(^|/)conf/[^/]+\.conf\.dist$", RegexOptions.IgnoreCase));
            var patches = paths.Where(p => Regex.IsMatch(p, @"\.(patch|diff)$", RegexOptions.IgnoreCase)).ToList();
            // Lua next to a .toc is an add-on for the game client, not a script for the server's Eluna.
            var addonFolders = new HashSet<string>(paths.Where(p => p.EndsWith(".toc", StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Contains("/") ? p.Substring(0, p.LastIndexOf('/')) : ""), StringComparer.OrdinalIgnoreCase);
            // So is Lua under client/, where a module made for AFK Realm keeps what goes into the game client.
            bool clientFiles = paths.Any(p => p.StartsWith("client/", StringComparison.OrdinalIgnoreCase));
            bool lua = paths.Any(p => p.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) && !p.StartsWith("client/", StringComparison.OrdinalIgnoreCase) &&
                !addonFolders.Any(f => f.Length > 0 && p.StartsWith(f + "/", StringComparison.OrdinalIgnoreCase)));

            if (!code && dbs.Count == 0)
                a.Add(2, L.T("This does not look like a server module (no source code and no SQL files)."));
            bool manifest = paths.Any(p => p.Equals(ManifestName, StringComparison.OrdinalIgnoreCase));
            if (manifest && patches.Count > 0)
                a.Add(0, patches.Count == 1
                    ? L.F("It brings 1 change for the CoA core or Playerbots. {0} applies it with every build and leaves it out when it no longer fits or is no longer needed.", Product.Name)
                    : L.F("It brings {0} changes for the CoA core or Playerbots. {1} applies them with every build and leaves one out when it no longer fits or is no longer needed.", patches.Count, Product.Name));
            else if (patches.Count > 0 || Regex.IsMatch(readme, @"git\s+apply|patch\s+-p\d|apply\s+(the\s+)?(core\s+)?patch|core\s+patch", RegexOptions.IgnoreCase))
                a.Add(2, L.F("It needs changes to the server core (a patch). {0} cannot apply those, so the module will most likely not work or not compile.", Product.Name));
            if (!(manifest && clientFiles) && Regex.IsMatch(readme, @"\.mpq\b|client[\s-]+(side\s+)?patch|patch-[a-z0-9]\.mpq|client\s+dbc|dbc\s+files?\s+(to|for|in)\s+(the\s+)?client", RegexOptions.IgnoreCase))
                a.Add(1, L.T("The README mentions files for the game client (MPQ/DBC patches). Every player may need them; see the README."));
            if (!m.Name.Equals("mod-eluna", StringComparison.OrdinalIgnoreCase) && (lua || Regex.IsMatch(readme, @"\beluna\b", RegexOptions.IgnoreCase)))
                a.Add(1, L.T("It uses Lua scripts, which need the Eluna module (mod-eluna) as well."));
            var needs = Regex.Matches(readme, @"(?i)(?:requires?|needs?|depends\s+on|dependency)[^\n]{0,80}?\b(mod-[a-z0-9\-_]+)")
                .Cast<Match>().Select(x => x.Groups[1].Value).Where(x => !x.Equals(m.Name, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (needs.Count > 0) a.Add(1, L.F("The README says it needs: {0}.", string.Join(", ", needs)));
            if (m.Archived) a.Add(1, L.T("Its author has archived it, so it gets no more fixes."));
            if (m.Pushed > DateTime.MinValue && m.Pushed < DateTime.UtcNow.AddYears(-2))
                a.Add(1, L.F("Last changed {0}. Older modules often do not compile with the current core.", m.Pushed.ToString("MMMM yyyy", L.Culture)));
            if (dbs.Count > 0)
                a.Add(0, L.F("Database changes ({0}) are applied and recorded automatically, so removing the module undoes them.", string.Join(", ", dbs)));
            if (ignored.Count > 0)
                a.Add(1, L.F("SQL files in {0} are not applied by the server (often old files that are no longer needed). Check the README.", "data/sql/" + string.Join(", data/sql/", ignored)));
            if (looseSql.Count > 0 && dbs.Count == 0)
                a.Add(1, L.F("It has SQL files outside data/sql ({0}), which the server does not apply by itself. Check the README.", looseSql.Count));
            var addons = paths.Select(p => Regex.Match(p, @"^(.+/)?([\w.\-]+)/\2\.toc$", RegexOptions.IgnoreCase)).Where(x => x.Success).Select(x => x.Groups[2].Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (manifest && addons.Count > 0)
                a.Add(0, L.F("It brings an add-on for the game client ({0}). {1} offers to put it into your game after installing, " +
                    "and can pack it into a ZIP for other players.", string.Join(", ", addons), Product.Name));
            if (conf) a.Add(0, L.T("Its options appear in the server settings after installing."));
            if (code && dbs.Count == 0 && !conf) a.Add(0, L.T("Source code only: nothing to set up."));
            return a;
        }
    }
}
