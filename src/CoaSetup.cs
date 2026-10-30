using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace CoAInstaller
{
    /// <summary>
    /// CoA Custom setup.
    ///
    /// Copies the finished CoA Custom package files instead of rebuilding race packs. Client
    /// files and settings are copied, together with the CoA server DBCs/SQL that carry the
    /// custom racial abilities and race data. The AFK core keeps its own Classic Classes.
    /// </summary>
    static class CoaSetup
    {
        public sealed class Options
        {
            public string Package;
            public string Server;
            public string Client;
            public bool Check;
            public bool ClientPatchOnly;
            public bool ClientPatch = true;
            public bool NoBackup;
            public bool Help;
        }

        public static int Run(string[] args)
        {
            L.Init();
            Options o = Parse(args);
            if (o.Help)
            {
                PrintHelp();
                return 0;
            }
            try
            {
                return Run(o);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                return 1;
            }
        }

        static int Run(Options o)
        {
            if (string.IsNullOrEmpty(o.Package)) return Fail(L.T("--package is missing (the extracted CoA Custom 1.9.3 folder)."));
            if (string.IsNullOrEmpty(o.Server)) return Fail(L.T("--server is missing (the AFK Realm installation root)."));
            if (string.IsNullOrEmpty(o.Client)) return Fail(L.T("--client is missing (the game folder with Data and Ascension.exe)."));
            if (!Directory.Exists(o.Package)) return Fail(L.F("Package folder not found: {0}", o.Package));
            if (!Directory.Exists(o.Client) || !File.Exists(Path.Combine(o.Client, "Ascension.exe"))) return Fail(L.F("No game client in: {0}", o.Client));
            var inst = new Install(o.Server);
            if (!inst.IsInstalled) return Fail(L.F("No complete AFK Realm server in: {0}", o.Server));

            string packRoot = PackageRoot(o.Package);
            if (packRoot == null) return Fail(L.F("No CoA-Custom\\files tree found in: {0}", o.Package));
            string files = Path.Combine(packRoot, "files");
            string clientSource = Path.Combine(files, "client");
            string clientRoot = Path.Combine(files, "client_root");
            if (!Directory.Exists(clientSource) || !Directory.Exists(clientRoot))
                return Fail(L.F("The package has no files\\client or files\\client_root: {0}", packRoot));

            string manifestVersion = ReadVersion(Path.Combine(packRoot, "manifest.json"));
            Console.WriteLine(L.F("CoA Custom package: {0}", packRoot + (manifestVersion.Length > 0 ? " (" + L.F("version {0}", manifestVersion) + ")" : "")));
            Console.WriteLine(L.F("Server: {0}", inst.Root));
            Console.WriteLine(L.F("Client: {0}", o.Client));
            Console.WriteLine(L.F("Mode: {0}", o.ClientPatchOnly ? L.T("client patch only") : L.T("copy package files")));
            Console.WriteLine();

            if (o.Check)
            {
                PrintCheck(o, inst, files, clientSource, clientRoot);
                return 0;
            }

            if (o.ClientPatchOnly)
            {
                Console.WriteLine(L.T("Installing AFK client patch only ..."));
                ClientAddons.ClientFolder = o.Client;
                var plan = ClientPatch.Collect(inst);
                string result = ClientPatch.Install(inst, o.Client, plan,
                    delegate(string text, int done, int total) { if (total > 0) Console.WriteLine("  " + text); });
                Console.WriteLine("  " + result);
                return 0;
            }

            return CopyPackage(o, inst, files, clientSource, clientRoot);
        }

        /// <summary>
        /// Packs the complete CoA Custom client as one ZIP that can be unpacked into the game
        /// folder on another computer, like the add-on ZIP of AFK Realm.
        /// </summary>
        public static string ExportClientZip(string file, string package, Install inst, string client, Action<string, int, int> progress)
        {
            if (string.IsNullOrEmpty(file)) throw new ArgumentException("file");
            string packRoot = PackageRoot(package);
            if (packRoot == null) throw new InvalidOperationException(L.T("No CoA-Custom\\files tree found in the package."));
            string files = Path.Combine(packRoot, "files");
            string clientSource = Path.Combine(files, "client");
            string clientRoot = Path.Combine(files, "client_root");
            if (!Directory.Exists(clientSource) || !Directory.Exists(clientRoot))
                throw new InvalidOperationException(L.T("The package has no files\\client or files\\client_root."));
            if (inst == null || !inst.IsInstalled) throw new InvalidOperationException(L.T("No complete AFK Realm server was chosen."));
            if (string.IsNullOrEmpty(client) || !Directory.Exists(client))
                throw new InvalidOperationException(L.T("No game client was chosen."));
            if (File.Exists(file)) File.Delete(file);
            if (progress == null) progress = (t, a, b) => { };
            var rootFiles = Directory.GetFiles(clientRoot, "*", SearchOption.AllDirectories);
            var addonFiles = Directory.Exists(Path.Combine(files, "addons")) ? Directory.GetFiles(Path.Combine(files, "addons"), "*", SearchOption.AllDirectories) : new string[0];
            var mpqs = Directory.GetFiles(clientSource, "*.MPQ", SearchOption.TopDirectoryOnly);
            int total = rootFiles.Length + addonFiles.Length + mpqs.Length;
            int done = 0;
            using (var zip = ZipFile.Open(file, ZipArchiveMode.Create))
            {
                foreach (string path in mpqs.OrderBy(x => x))
                {
                    progress(L.F("Packing CoA client files ({0}/{1}) ...", done + 1, total), done, total);
                    zip.CreateEntryFromFile(path, "Data/" + Path.GetFileName(path), CompressionLevel.NoCompression);
                    done++;
                }
                string rootPrefix = Path.GetFullPath(clientRoot).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                foreach (string path in rootFiles.OrderBy(x => x))
                {
                    progress(L.F("Packing CoA client files ({0}/{1}) ...", done + 1, total), done, total);
                    string rel = path.Substring(rootPrefix.Length).Replace('\\', '/');
                    zip.CreateEntryFromFile(path, rel, CompressionLevel.NoCompression);
                    done++;
                }
                if (addonFiles.Length > 0)
                {
                    string addonPrefix = Path.GetFullPath(Path.Combine(files, "addons")).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                    foreach (string path in addonFiles.OrderBy(x => x))
                    {
                        progress(L.F("Packing CoA client files ({0}/{1}) ...", done + 1, total), done, total);
                        string rel = path.Substring(addonPrefix.Length).Replace('\\', '/');
                        zip.CreateEntryFromFile(path, "Interface/AddOns/" + rel, CompressionLevel.NoCompression);
                        done++;
                    }
                }
                // The AFK client patch is generated from the chosen server and the installed client.
                var plan = ClientPatch.Collect(inst);
                if (plan != null && !plan.Empty)
                {
                    progress(L.T("Adding the AFK client patch ..."), done, total);
                    ClientPatch.AddPatchToZip(zip, inst, client, plan, progress);
                }
                var entry = zip.CreateEntry("READ ME FIRST.txt");
                using (var writer = new StreamWriter(entry.Open(), new System.Text.UTF8Encoding(false)))
                    writer.Write(ClientZipReadMe());
            }
            return file;
        }

        static string ClientZipReadMe()
        {
            var text = new System.Text.StringBuilder();
            text.AppendLine(L.T("CoA Custom client files for AFK Realm"));
            text.AppendLine();
            text.AppendLine(L.T("1. Close the game and its launcher."));
            text.AppendLine(L.T("2. Unpack this ZIP into your game folder (the folder with the \"Data\" folder). Replace files that are already there."));
            text.AppendLine(L.T("3. Start CoA64.exe for the 64-bit client, or Ascension.exe for the 32-bit client."));
            text.AppendLine(L.T("4. Delete the folder \"Cache\" in your game folder if the game still shows old races or classes."));
            text.AppendLine();
            text.AppendLine(L.T("The ZIP contains the CoA Custom client files, its add-ons and the AFK Realm client patch."));
            return text.ToString();
        }

        static int CopyPackage(Options o, Install inst, string files, string clientSource, string clientRoot)
        {
            int copied = 0, skipped = 0;
            Console.WriteLine(L.T("Copying client MPQs ..."));
            CopyFiles(clientSource, Path.Combine(o.Client, "Data"), "*.MPQ", o.NoBackup, ref copied, ref skipped);

            string dinput = Path.Combine(clientSource, "dinput8.dll");
            if (File.Exists(dinput))
            {
                CopyFile(dinput, Path.Combine(o.Client, "dinput8.dll"), o.NoBackup);
                copied++;
            }

            Console.WriteLine(L.T("Copying client_root (CoA64, Xvid, Esteria, Retail) ..."));
            CopyTree(clientRoot, o.Client, o.NoBackup, ref copied, ref skipped);

            string addons = Path.Combine(files, "addons");
            if (Directory.Exists(addons))
            {
                Console.WriteLine(L.T("Copying addons ..."));
                CopyTree(addons, Path.Combine(o.Client, "Interface", "AddOns"), o.NoBackup, ref copied, ref skipped);
            }

            // The server-side race data lives in these DBCs (Spell, SkillLineAbility,
            // SkillRaceClassInfo, ChrRaces, ...), so they are part of the CoA install, not an
            // optional extra. SQL files are copied for the matching server data setup.
            Console.WriteLine(L.T("Copying server DBCs ..."));
            CopyFiles(Path.Combine(files, "dbc"), Path.Combine(inst.DataDir, "dbc"), "*.dbc", o.NoBackup, ref copied, ref skipped);
            string clientDbc = Path.Combine(files, "dbc_clientset", "CreatureDisplayInfo.dbc");
            if (File.Exists(clientDbc))
            {
                CopyFile(clientDbc, Path.Combine(inst.DataDir, "dbc_clientset", "CreatureDisplayInfo.dbc"), o.NoBackup);
                copied++;
            }
            string moduleConf = Path.Combine(files, "modules", "coa_custom_races.conf.dist");
            if (File.Exists(moduleConf))
            {
                CopyFile(moduleConf, Path.Combine(inst.ConfigDir, "modules", "coa_custom_races.conf.dist"), o.NoBackup);
                copied++;
            }
            string sqlSource = Path.Combine(files, "sql");
            if (Directory.Exists(sqlSource))
            {
                string sqlTarget = Path.Combine(inst.Root, "CoA-Custom", "sql");
                Console.WriteLine(L.F("Copying SQL to {0} ...", sqlTarget));
                CopyFiles(sqlSource, sqlTarget, "*.sql", o.NoBackup, ref copied, ref skipped);
                Console.WriteLine(L.T("SQL files copied. Automatic execution is disabled; the included module data is used."));
            }

            Console.WriteLine(L.T("Writing client settings ..."));
            WriteRaceDefaults(inst, files);
            WriteClientSettings(inst, clientRoot, clientSource);

            if (o.ClientPatch)
            {
                Console.WriteLine(L.T("Installing AFK client patch ..."));
                var plan = ClientPatch.Collect(inst);
                string result = ClientPatch.Install(inst, o.Client, plan,
                    delegate(string text, int done, int total) { if (total > 0) Console.WriteLine("  " + text); });
                Console.WriteLine("  " + result);
            }

            Console.WriteLine();
            Console.WriteLine(L.F("Done. Copied: {0}, skipped unchanged: {1}.", copied, skipped));
            Console.WriteLine(L.T("Start CoA64.exe for the 64-bit client, or Ascension.exe for the 32-bit client."));
            return 0;
        }

        static void PrintCheck(Options o, Install inst, string files, string clientSource, string clientRoot)
        {
            Console.WriteLine(L.T("Check only. Would copy:"));
            Console.WriteLine(L.F("  - {0} MPQs from {1} to {2}", Count(clientSource, "*.MPQ"), clientSource, Path.Combine(o.Client, "Data")));
            Console.WriteLine(L.F("  - client_root files from {0} to {1}", clientRoot, o.Client));
            Console.WriteLine(L.F("  - addons from {0}", Path.Combine(files, "addons")));
            Console.WriteLine(L.F("  - server DBCs to {0}", Path.Combine(inst.DataDir, "dbc")));
            Console.WriteLine(L.F("  - server SQL to {0}", Path.Combine(inst.Root, "CoA-Custom", "sql")));
            Console.WriteLine(L.F("  - settings in {0}", Path.Combine(inst.ConfigDir, "modules", "mod_custom_races.conf")));
            if (o.ClientPatch) Console.WriteLine("  - AFK client patch");
        }

        static readonly HashSet<int> HiddenCoaRaces = new HashSet<int> { 19, 27, 65, 72, 77, 80, 81 };

        // The base race CoA Custom gives its races: its RACIAL table for the first 31 races and a
        // same-faction stock race for the Esteria races above them.
        static readonly Dictionary<int, string> CoaBaseByRace = new Dictionary<int, string>
        {
            { 9, "Orc" }, { 12, "Troll" }, { 13, "NightElf" }, { 14, "Human" }, { 15, "Dwarf" },
            { 16, "Human" }, { 17, "Tauren" }, { 18, "Human" }, { 20, "Tauren" }, { 21, "Undead" },
            { 22, "Draenei" }, { 23, "Orc" }, { 24, "Troll" }, { 25, "Troll" }, { 26, "Undead" },
            { 28, "Troll" }, { 29, "Draenei" }, { 30, "Human" }, { 31, "Undead" },
            { 47, "NightElf" }, { 48, "Dwarf" }, { 49, "Orc" }, { 50, "NightElf" }, { 51, "Undead" },
            { 53, "Orc" }, { 54, "Human" }, { 55, "Orc" }, { 56, "BloodElf" }, { 57, "Orc" },
            { 58, "Orc" }, { 59, "NightElf" }, { 60, "BloodElf" }, { 61, "NightElf" }, { 62, "Draenei" },
            { 63, "BloodElf" }, { 66, "Tauren" }, { 67, "Gnome" }, { 68, "Dwarf" }, { 69, "Tauren" },
            { 70, "Orc" }, { 71, "Human" }, { 74, "Troll" }, { 75, "Human" }, { 78, "Human" },
            { 79, "Human" }, { 82, "Human" }, { 83, "Orc" }, { 87, "Orc" }, { 90, "Undead" },
            { 97, "Human" }, { 98, "Orc" }, { 99, "Human" }, { 100, "Human" }, { 101, "Undead" },
            { 102, "Tauren" }, { 103, "Human" }, { 104, "Orc" }, { 106, "Orc" }, { 107, "Draenei" },
            { 109, "Human" }, { 110, "Human" }, { 112, "Orc" },
        };

        /// <summary>
        /// Adds base-race entries for the playable races of CoA Custom to a fresh server config.
        /// Existing entries and the user's base-race choice are left alone; the CoA UI decides
        /// which of the races are visible in the game.
        /// </summary>
        static void WriteRaceDefaults(Install inst, string files)
        {
            string dbc = Path.Combine(files, "dbc", "ChrRaces.dbc");
            if (!File.Exists(dbc)) return;
            string confPath = Path.Combine(inst.ConfigDir, "modules", "mod_custom_races.conf");
            if (!File.Exists(confPath)) return;
            DbcTable table;
            try { table = DbcTable.Parse(File.ReadAllBytes(dbc), "ChrRaces.dbc"); }
            catch { return; }
            ConfigFile conf = ConfigFile.Load(confPath, "modules\\mod_custom_races.conf");
            var keys = new List<string>();
            HashSet<string> known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in table.Records)
            {
                int id = (int)table.Field(row, 0);
                uint flags = table.Field(row, 1);
                if (id <= 11 || id > 127 || (flags & 0xC) != 0xC || HiddenCoaRaces.Contains(id)) continue;
                string file = ClientRaces.Str(table, row, 11);
                string name = ClientRaces.Str(table, row, 14);
                if (name.Length == 0) name = file;
                string key = RaceKey(conf, id, file);
                if (!known.Add(key)) key = RaceKey(conf, id, file + id);
                string prefix = "CustomRaces." + key + ".";
                if (!Has(conf, prefix + "Source"))
                {
                    Set(conf, prefix + "Source", id.ToString());
                    Set(conf, prefix + "Id", id.ToString());
                    Set(conf, prefix + "Copy", "0");
                    Set(conf, prefix + "Import", "0");
                    Set(conf, prefix + "Name", Quote(name));
                    Set(conf, prefix + "Prefix", Quote(ClientRaces.Str(table, row, 6)));
                    Set(conf, prefix + "File", Quote(file));
                    Set(conf, prefix + "MaleDisplay", table.Field(row, 4).ToString());
                    Set(conf, prefix + "FemaleDisplay", table.Field(row, 5).ToString());
                    Set(conf, prefix + "MaleOnly", "0");
                    Set(conf, prefix + "FemaleOnly", "0");
                    Set(conf, prefix + "OwnHelmets", "1");
                    Set(conf, prefix + "HelmetPrefix", "\"\"");
                    Set(conf, prefix + "IconFile", "\"\"");
                    Set(conf, prefix + "Icon", "\"\"");
                    Set(conf, prefix + "Racials", "\"\"");
                    Set(conf, prefix + "Base", Quote(BaseFor(id, (int)table.Field(row, 13))));
                    if (id == 66 || id == 68 || id == 69) Set(conf, prefix + "Appearance", "1");
                    else if (id == 70 || id == 71) Set(conf, prefix + "Appearance", "2");
                }
                else
                {
                    // Repair old generated entries whose base race belongs to the other faction
                    // (for example Forest Troll 40 was set to the Horde's Orc).
                    int side = (int)table.Field(row, 13);
                    string current = Has(conf, prefix + "Base") ? ConfigOption.Unquote(conf.ByKey[prefix + "Base"].CurrentRaw) : "";
                    if (!BaseFitsSide(current, side)) Set(conf, prefix + "Base", Quote(BaseFor(id, side)));
                }
                keys.Add(key);
            }
            if (keys.Count > 0)
            {
                string current = Has(conf, "CustomRaces.Races") ? ConfigOption.Unquote(conf.ByKey["CustomRaces.Races"].CurrentRaw) : "";
                var list = new List<string>(current.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));
                foreach (String key in keys)
                    if (!list.Exists(x => x.Equals(key, StringComparison.OrdinalIgnoreCase))) list.Add(key);
                Set(conf, "CustomRaces.Races", Quote(string.Join(" ", list.ToArray())));
            }
            conf.Save();
        }

        static bool Has(ConfigFile conf, string key)
        {
            ConfigOption option;
            return conf.ByKey.TryGetValue(key, out option) && option.CurrentRaw.Length > 0;
        }

        static string RaceKey(ConfigFile conf, int id, string file)
        {
            string key = "";
            foreach (char c in file ?? "")
                if (char.IsLetterOrDigit(c)) key += c;
            if (key.Length == 0) key = "Race" + id;
            foreach (var pair in conf.ByKey)
            {
                if (!pair.Key.StartsWith("CustomRaces.", StringComparison.OrdinalIgnoreCase) ||
                    !pair.Key.EndsWith(".Source", StringComparison.OrdinalIgnoreCase)) continue;
                string prefix = pair.Key.Substring("CustomRaces.".Length);
                prefix = prefix.Substring(0, prefix.Length - ".Source".Length);
                if (prefix.Equals(key, StringComparison.OrdinalIgnoreCase) &&
                    ConfigOption.Unquote(pair.Value.CurrentRaw) == id.ToString())
                    return prefix;
            }
            return key;
        }

        static string BaseFor(int id, int side)
        {
            string b;
            if (CoaBaseByRace.TryGetValue(id, out b)) return b;
            return side == 1 ? "Orc" : "Human";
        }

        static bool BaseFitsSide(string baseRace, int side)
        {
            if (string.IsNullOrEmpty(baseRace)) return false;
            bool horde = string.Equals(baseRace, "Orc", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(baseRace, "Undead", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(baseRace, "Tauren", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(baseRace, "Troll", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(baseRace, "BloodElf", StringComparison.OrdinalIgnoreCase);
            return side == 1 ? horde : !horde;
        }

        static void WriteClientSettings(Install inst, string clientRoot, string clientSource)
        {
            string confPath = Path.Combine(inst.ConfigDir, "modules", "mod_custom_races.conf");
            if (!File.Exists(confPath)) return;
            ConfigFile conf = ConfigFile.Load(confPath, "modules\\mod_custom_races.conf");
            Set(conf, "CustomRaces.Enable", "1");
            Set(conf, "CustomRaces.ClientRootFolder", Quote(clientRoot));
            string exe = Path.Combine(clientRoot, "Ascension.exe");
            if (File.Exists(exe)) Set(conf, "CustomRaces.ClientExe", Quote(exe));
            Set(conf, "CustomRaces.MoreRaces", "1");
            Set(conf, "CustomRaces.HighRaces", "1");
            Set(conf, "CustomRaces.UseCoAUI", "1");
            string patch = Path.Combine(clientSource, "dinput8.dll");
            if (File.Exists(patch)) Set(conf, "CustomRaces.MoreRacesFile", Quote(patch));
            Set(conf, "CoACustomRaces.BotsEnable", "0");
            conf.Save();
        }

        static void Set(ConfigFile conf, string key, string value)
        {
            ConfigOption option;
            bool quoted = conf.ByKey.TryGetValue(key, out option) && option.Quoted;
            conf.Pending[key] = quoted && !ConfigOption.IsQuoted(value) ? Quote(value) : value;
        }

        static string Quote(string value)
        {
            return "\"" + (value ?? "").Replace("\"", "") + "\"";
        }

        static int Count(string folder, string pattern)
        {
            try { return Directory.Exists(folder) ? Directory.GetFiles(folder, pattern).Length : 0; }
            catch { return 0; }
        }

        static void CopyFiles(string source, string target, string pattern, bool noBackup, ref int copied, ref int skipped)
        {
            if (!Directory.Exists(source)) return;
            Directory.CreateDirectory(target);
            // Windows matches *.MPQ case-insensitively; Linux does not. Match the
            // suffix explicitly so CoA's upper-case client MPQs are copied there too.
            string suffix = pattern.StartsWith("*", StringComparison.Ordinal) ? pattern.Substring(1) : null;
            foreach (string file in Directory.GetFiles(source)
                .Where(f => suffix == null || Path.GetFileName(f).EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f))
            {
                if (CopyFile(file, Path.Combine(target, Path.GetFileName(file)), noBackup)) copied++;
                else skipped++;
            }
        }

        static void CopyTree(string source, string target, bool noBackup, ref int copied, ref int skipped)
        {
            if (!Directory.Exists(source)) return;
            string root = Path.GetFullPath(source).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories).OrderBy(x => x))
            {
                string rel = file.Substring(root.Length);
                string dest = Path.Combine(target, rel);
                if (CopyFile(file, dest, noBackup)) copied++;
                else skipped++;
            }
        }

        static bool SameFile(string a, string b)
        {
            try
            {
                FileInfo fa = new FileInfo(a), fb = new FileInfo(b);
                if (fa.Length != fb.Length) return false;
                using (FileStream sa = File.OpenRead(a))
                using (FileStream sb = File.OpenRead(b))
                {
                    byte[] ba = new byte[1024 * 1024], bb = new byte[1024 * 1024];
                    while (true)
                    {
                        int na = sa.Read(ba, 0, ba.Length), nb = sb.Read(bb, 0, bb.Length);
                        if (na != nb) return false;
                        if (na == 0) return true;
                        for (int i = 0; i < na; i++) if (ba[i] != bb[i]) return false;
                    }
                }
            }
            catch { return false; }
        }

        static bool CopyFile(string source, string target, bool noBackup)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                if (File.Exists(target))
                {
                    if (SameFile(source, target)) return false;
                    if (!noBackup)
                    {
                        string backup = target + ".coa-patcher-backup";
                        if (!File.Exists(backup)) File.Copy(target, backup, false);
                    }
                }
                File.Copy(source, target, true);
                Console.WriteLine("  " + target);
                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("  " + target + ": " + ex.Message);
                return false;
            }
        }

        static string PackageRoot(string picked)
        {
            string direct = Path.Combine(picked, "files");
            if (Directory.Exists(direct)) return picked;
            string root = CoaCustom.PackageRoot(picked);
            if (root != null) return root;
            foreach (string dir in Directory.GetDirectories(picked))
            {
                if (Directory.Exists(Path.Combine(dir, "files"))) return dir;
                foreach (string nested in Directory.GetDirectories(dir))
                    if (Directory.Exists(Path.Combine(nested, "files"))) return nested;
            }
            return null;
        }

        static string ReadVersion(string manifest)
        {
            try
            {
                string text = File.ReadAllText(manifest);
                int at = text.IndexOf("\"version\"", StringComparison.OrdinalIgnoreCase);
                if (at < 0) return "";
                at = text.IndexOf(':', at);
                if (at < 0) return "";
                int start = text.IndexOf('"', at + 1);
                int end = start < 0 ? -1 : text.IndexOf('"', start + 1);
                return start >= 0 && end > start ? text.Substring(start + 1, end - start - 1) : "";
            }
            catch { return ""; }
        }

        static int Fail(string text)
        {
            Console.Error.WriteLine("ERROR: " + text);
            return 2;
        }

        static Options Parse(string[] args)
        {
            var o = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "--package" && i + 1 < args.Length) o.Package = args[++i];
                else if (a == "--server" && i + 1 < args.Length) o.Server = args[++i];
                else if (a == "--client" && i + 1 < args.Length) o.Client = args[++i];
                else if (a == "--check") o.Check = true;
                else if (a == "--client-patch-only") o.ClientPatchOnly = true;
                else if (a == "--client-patch") o.ClientPatch = true;
                else if (a == "--no-client-patch") o.ClientPatch = false;
                else if (a == "--no-backup") o.NoBackup = true;
                else if (a == "--help" || a == "-h") o.Help = true;
            }
            return o;
        }

        static void PrintHelp()
        {
            Console.WriteLine("CoA Custom setup (integrated in AFK Realm)");
            Console.WriteLine();
            Console.WriteLine("  --package <CoA-Custom> --server <AFK server> --client <game folder> [--check]");
            Console.WriteLine();
            Console.WriteLine("  default          copy CoA MPQs, client_root, addons and settings; install AFK client patch");
            Console.WriteLine("  --check          only report what would be done");
            Console.WriteLine("  --client-patch   install AFK Realm's generated client patch (default)");
            Console.WriteLine("  --no-client-patch  leave the AFK client patch alone");
            Console.WriteLine("  --client-patch-only  rebuild/reinstall just the AFK client patch");
            Console.WriteLine("  --no-backup      do not keep .coa-patcher-backup files");
        }
    }
}
