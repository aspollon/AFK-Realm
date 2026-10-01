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
        public ModuleState State;
        public string Status = "", InstalledOn = "";                      // from modules.txt
        public string[] CreatedTables = new string[0], ManualTables = new string[0];
        public bool Installed { get { return State == ModuleState.Managed || State == ModuleState.ByHand; } }
        /// <summary>Playerbots: AFK Realm always builds its CoA branch; catalog entries of the same name are other versions.</summary>
        public bool IsPlayerbots { get { return Name.Equals("mod-playerbots", StringComparison.OrdinalIgnoreCase); } }
    }

    /// <summary>What AFK Realm found out about a module before installing it.</summary>
    class ModuleAnalysis
    {
        public readonly List<KeyValuePair<int, string>> Checks = new List<KeyValuePair<int, string>>();   // 0 ok, 1 warning, 2 problem
        public string ReadmeText = "";
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
        static readonly Dictionary<string, ModuleAnalysis> analyses = new Dictionary<string, ModuleAnalysis>(StringComparer.OrdinalIgnoreCase);

        static string CacheFile(Install inst) { return Path.Combine(inst.Root, "Dependencies", "Cache", "module-catalog.txt"); }
        public static string ModulesDir(Install inst) { return Path.Combine(inst.SourceDir, "modules"); }

        static string Get(string url, string accept = "application/vnd.github+json")
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = Product.FileStem;
            req.Accept = accept;
            req.Timeout = 20000; req.ReadWriteTimeout = 20000;
            try
            {
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var rd = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) return rd.ReadToEnd();
            }
            catch (WebException ex)
            {
                var resp = ex.Response as HttpWebResponse;
                if (resp != null && (resp.StatusCode == HttpStatusCode.Forbidden || (int)resp.StatusCode == 429))
                    throw new InvalidOperationException("GitHub only allows a limited number of requests per hour without signing in, and that limit is used up. Please try again later.");
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
                if (cached != null) { warning = "The module list could not be updated (" + ex.Message + ") The list from " + File.GetLastWriteTime(cache).ToString("g") + " is shown."; return cached; }
                throw new InvalidOperationException("The module list could not be loaded from GitHub. " + ex.Message);
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
            if (list.Count == 0) throw new InvalidOperationException("GitHub returned an empty module list.");
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
                if (name.Equals("mod-playerbots", StringComparison.OrdinalIgnoreCase)) continue;
                string folder = Path.Combine(dir, name);
                string[] info; managed.TryGetValue(name, out info);
                bool hasGit = Directory.Exists(Path.Combine(folder, ".git"));
                var state = info != null ? ModuleState.Managed : hasGit ? ModuleState.ByHand : ModuleState.PartOfCoA;
                string url = info != null ? info[1] : OriginUrl(folder);
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
                        Description = info != null ? "" : "Added by hand" };
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
            // Playerbots is always built in, so the catalog's versions of it are shown as included, not installable.
            foreach (var bots in result.Where(e => e.IsPlayerbots)) bots.State = ModuleState.PartOfCoA;
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
                a.Add(1, "This module is not on GitHub, so " + Product.Name + " cannot check it beforehand. Read its description before installing.");
                return a;
            }
            if (!m.InCatalog)
            {
                var repo = json.DeserializeObject(Get("https://api.github.com/repos/" + full)) as Dictionary<string, object>;
                m.Branch = Text(repo, "default_branch"); m.Description = Text(repo, "description");
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
            lock (analyses) analyses[key] = a;
            return a;
        }

        /// <summary>The checks for a module's file list and README text.</summary>
        public static ModuleAnalysis Check(ModuleEntry m, List<string> paths, string readme)
        {
            var a = new ModuleAnalysis { ReadmeText = readme ?? "" };
            readme = a.ReadmeText;

            bool code = paths.Any(p => Regex.IsMatch(p, @"\.(cpp|h|hpp)$", RegexOptions.IgnoreCase));
            var sqlFolders = paths.Select(p => Regex.Match(p, @"^data/sql/([^/]+)/.+\.sql$", RegexOptions.IgnoreCase)).Where(x => x.Success)
                .Select(x => x.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var dbs = sqlFolders.Select(DbOfFolder).Where(d => d != null && d != "playerbots").Distinct().ToList();
            var ignored = sqlFolders.Where(f => DbOfFolder(f) == null || DbOfFolder(f) == "playerbots").ToList();
            var looseSql = paths.Where(p => p.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) && !p.StartsWith("data/sql/", StringComparison.OrdinalIgnoreCase)).ToList();
            bool conf = paths.Any(p => Regex.IsMatch(p, @"(^|/)conf/[^/]+\.conf\.dist$", RegexOptions.IgnoreCase));
            var patches = paths.Where(p => Regex.IsMatch(p, @"\.(patch|diff)$", RegexOptions.IgnoreCase)).ToList();
            bool lua = paths.Any(p => p.EndsWith(".lua", StringComparison.OrdinalIgnoreCase));

            if (!code && dbs.Count == 0)
                a.Add(2, "This does not look like a server module (no source code and no SQL files).");
            if (patches.Count > 0 || Regex.IsMatch(readme, @"git\s+apply|patch\s+-p\d|apply\s+(the\s+)?(core\s+)?patch|core\s+patch", RegexOptions.IgnoreCase))
                a.Add(2, "It needs changes to the server core (a patch). " + Product.Name + " cannot apply those, so the module will most likely not work or not compile.");
            if (Regex.IsMatch(readme, @"\.mpq\b|client[\s-]+(side\s+)?patch|patch-[a-z0-9]\.mpq|client\s+dbc|dbc\s+files?\s+(to|for|in)\s+(the\s+)?client", RegexOptions.IgnoreCase))
                a.Add(1, "The README mentions files for the game client (MPQ/DBC patches). Every player may need them; see the README.");
            if (!m.Name.Equals("mod-eluna", StringComparison.OrdinalIgnoreCase) && (lua || Regex.IsMatch(readme, @"\beluna\b", RegexOptions.IgnoreCase)))
                a.Add(1, "It uses Lua scripts, which need the Eluna module (mod-eluna) as well.");
            var needs = Regex.Matches(readme, @"(?i)(?:requires?|needs?|depends\s+on|dependency)[^\n]{0,80}?\b(mod-[a-z0-9\-_]+)")
                .Cast<Match>().Select(x => x.Groups[1].Value).Where(x => !x.Equals(m.Name, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (needs.Count > 0) a.Add(1, "The README says it needs: " + string.Join(", ", needs) + ".");
            if (m.Archived) a.Add(1, "Its author has archived it, so it gets no more fixes.");
            if (m.Pushed > DateTime.MinValue && m.Pushed < DateTime.UtcNow.AddYears(-2))
                a.Add(1, "Last changed " + m.Pushed.ToString("MMMM yyyy", CultureInfo.InvariantCulture) + ". Older modules often do not compile with the current core.");
            if (dbs.Count > 0)
                a.Add(0, "Database changes (" + string.Join(", ", dbs) + ") are applied and recorded automatically, so removing the module undoes them.");
            if (ignored.Count > 0)
                a.Add(1, "SQL files in data/sql/" + string.Join(", data/sql/", ignored) + " are not applied by the server (often old files that are no longer needed). Check the README.");
            if (looseSql.Count > 0 && dbs.Count == 0)
                a.Add(1, "It has SQL files outside data/sql (" + looseSql.Count + "), which the server does not apply by itself. Check the README.");
            if (conf) a.Add(0, "Its options appear in the server settings after installing.");
            if (code && dbs.Count == 0 && !conf) a.Add(0, "Source code only: nothing to set up.");
            return a;
        }
    }
}
