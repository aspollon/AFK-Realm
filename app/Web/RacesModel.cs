using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace CoAInstaller.Web
{
    /// <summary>
    /// The logic of the "Custom races" page (mod-custom-races) without its window, ported from RacesSetup: which races
    /// of the game client and of the race folder can be played, their base races and race numbers. The page asks
    /// for every step; the model keeps the changes until it is saved, as the Windows page did.
    /// </summary>
    class RacesModel
    {
        class Known { public string Base = "", Looks = "", About = "", Rating = "", Note = ""; }

        public readonly Install inst;
        public readonly ConfigFile conf;
        readonly Dictionary<string, Known> known = new Dictionary<string, Known>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, KeyValuePair<int, string>> virtuals = new Dictionary<string, KeyValuePair<int, string>>();
        public readonly List<string> bases = new List<string>();
        public readonly List<ClientRace> races = new List<ClientRace>();
        readonly List<int> slots = new List<int>(), moreSlots = new List<int>(), highSlots = new List<int>();
        readonly HashSet<int> reservedNumbers = new HashSet<int>(), stockCopyNumbers = new HashSet<int>();
        readonly HashSet<string> helmetPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ClientRaces.Scan helmetScan;
        static readonly Dictionary<int, int> CoaExtraLooks = new Dictionary<int, int> { { 66, 1 }, { 68, 1 }, { 69, 1 }, { 70, 2 }, { 71, 2 } };
        static readonly Dictionary<string, string> BasePrefixes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Human", "Hu" }, { "Orc", "Or" }, { "Dwarf", "Dw" }, { "NightElf", "Ni" }, { "Undead", "Sc" }, { "Tauren", "Ta" },
            { "Gnome", "Gn" }, { "Troll", "Tr" }, { "BloodElf", "Be" }, { "Draenei", "Dr" },
        };
        public string folder, problem, knownError, coaNote, coaButton;
        Dictionary<int, string> coaRaceFiles;

        public RacesModel(Install i)
        {
            inst = i;
            string path = Path.Combine(inst.ConfigDir, "modules", "mod_custom_races.conf");
            conf = ConfigFile.Load(path, "modules\\mod_custom_races.conf");
            ReadKnown();
            problem = ReadClient();
            ApplyClientMoves();
            ReadFolder();
            HealNumbers();
            PruneChosen();
            AutoDetect();
            Refresh();
        }

        // ---------------------------------------------------------------- values (as SetupDialog)

        public string Raw(string key, string fallback)
        {
            string v;
            if (conf.Pending.TryGetValue(key, out v)) return ConfigOption.Unquote(v);
            if (conf.Removed.Contains(key)) return fallback;
            if (conf.Values.TryGetValue(key, out v)) return ConfigOption.Unquote(v);
            ConfigOption o;
            if (conf.ByKey.TryGetValue(key, out o) && o.DefaultRaw != null) return ConfigOption.Unquote(o.DefaultRaw);
            return fallback;
        }
        public bool Has(string key) { if (conf.Pending.ContainsKey(key)) return true; if (conf.Removed.Contains(key)) return false; return conf.Values.ContainsKey(key); }
        public void Set(string key, string value)
        {
            ConfigOption o;
            bool quoted = conf.ByKey.TryGetValue(key, out o) && o.Quoted;
            bool already = ConfigOption.IsQuoted(value);
            string raw = quoted && !already ? "\"" + value + "\"" : value;
            string bare = already ? ConfigOption.Unquote(value) : value;
            string current;
            bool k = conf.Values.TryGetValue(key, out current) || (o != null && o.DefaultRaw != null && (current = o.DefaultRaw) != null);
            conf.Removed.Remove(key);
            if (k && ConfigOption.Unquote(current) == bare && conf.Values.ContainsKey(key)) conf.Pending.Remove(key);
            else conf.Pending[key] = raw;
        }
        public void Unset(string key) { conf.Pending.Remove(key); if (conf.Values.ContainsKey(key)) conf.Removed.Add(key); }
        public int Unsaved { get { return conf.Pending.Count + conf.Removed.Count; } }

        public bool More { get { return Raw("CustomRaces.MoreRaces", "0") == "1"; } }
        public bool High { get { return Raw("CustomRaces.HighRaces", "0") == "1"; } }
        List<int> Slots { get { return More ? slots.Concat(moreSlots).ToList() : slots; } }
        public string RacialsFolder { get { return Path.Combine(inst.ConfigDir, "modules", "custom-races-racials"); } }
        static string DefaultRaceFolder { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "AFK-Rassen"); } }

        // ---------------------------------------------------------------- reading

        void ReadKnown()
        {
            string file = Path.Combine(ModuleCatalog.ModulesDir(inst), "mod-custom-races", "races.json");
            try
            {
                var data = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(file)) as Dictionary<string, object>;
                foreach (var b in (object[])data["bases"]) bases.Add(Convert.ToString(b));
                foreach (var kv in (Dictionary<string, object>)data["known"])
                {
                    var r = (Dictionary<string, object>)kv.Value;
                    Func<string, string> text = x => { object v; return r.TryGetValue(x, out v) ? Convert.ToString(v) : ""; };
                    known[kv.Key] = new Known { Base = text("base"), Looks = text("looks"), About = text("about"), Rating = text("rating"), Note = text("note") };
                    object virt;
                    if (r.TryGetValue("virtual", out virt) && virt is Dictionary<string, object>)
                    {
                        var v = (Dictionary<string, object>)virt;
                        virtuals[kv.Key] = new KeyValuePair<int, string>(Convert.ToInt32(v["source"]), Convert.ToString(v["name"]));
                    }
                }
                object coa;
                if (data.TryGetValue("coaCustom", out coa) && coa is Dictionary<string, object>)
                {
                    var c = (Dictionary<string, object>)coa;
                    var table = new Dictionary<int, uint[]>();
                    object racials, scripted, ported, rows;
                    if (c.TryGetValue("racials", out racials) && racials is Dictionary<string, object>)
                        foreach (var pair in (Dictionary<string, object>)racials)
                        {
                            int race;
                            if (int.TryParse(pair.Key, out race) && pair.Value is object[])
                                table[race] = ((object[])pair.Value).Select(x => (uint)Convert.ToInt64(x)).ToArray();
                        }
                    var code = c.TryGetValue("scripted", out scripted) && scripted is object[] ? ((object[])scripted).Select(x => (uint)Convert.ToInt64(x)).ToArray() : new uint[0];
                    bool hasCode = c.TryGetValue("ported", out ported) && ported is bool && (bool)ported;
                    string rowsFile = c.TryGetValue("rows", out rows) && rows is string ? Path.Combine(Path.GetDirectoryName(file), ((string)rows).Replace('/', Path.DirectorySeparatorChar)) : null;
                    ServerRacials.UseCoaCustom(table, code, hasCode, rowsFile);
                    string coaFolder = Raw("CustomRaces.ClientRootFolder", "");
                    string packRows = ServerRacials.PackRows(coaFolder);
                    if (packRows != null) ServerRacials.UseCoaRows(packRows, true);
                }
            }
            catch (Exception ex) { knownError = L.F("What mod-custom-races knows of the races could not be read ({0}): {1}", file, ex.Message); }
            if (bases.Count == 0) bases.AddRange(new[] { "Human", "Orc", "Dwarf", "NightElf", "Undead", "Tauren", "Gnome", "Troll", "BloodElf", "Draenei" });
        }

        string ReadClient()
        {
            string client = ClientAddons.ClientFolder;
            if (client == null || !ClientAddons.IsClientFolder(client))
                return L.T("No game client is chosen yet. Choose your game folder under Modules › Game client, then open this page again: the races are read from it.");
            try
            {
                var scan = ClientRaces.Read(client, virtuals);
                reservedNumbers.Clear(); reservedNumbers.UnionWith(scan.Reserved);
                stockCopyNumbers.Clear(); stockCopyNumbers.UnionWith(scan.StockCopies);
                races.AddRange(scan.Races);
                // a race made from the row of another (the High Elf from the Blood Elf) has the side of the race it is
                // meant to be, not that of the row: the High Elf is Alliance although the Blood Elf is Horde
                foreach (var v in races.Where(x => x.Virtual))
                {
                    var k = KnownOf(v);
                    if (k != null && bases.Contains(k.Base))
                        v.Side = new[] { "Orc", "Undead", "Tauren", "Troll", "BloodElf" }.Contains(k.Base) ? 1 : 0;
                }
                MarkOwnNumbers();
                slots.AddRange(scan.FreeNumbers); moreSlots.AddRange(scan.MoreNumbers); highSlots.AddRange(scan.HighNumbers);
                foreach (var hp in scan.HelmetPrefixes) helmetPrefixes.Add(hp);
                helmetScan = scan;
                return races.Count == 0 ? L.T("Your game client has no races beyond the stock ones.") : null;
            }
            catch (Exception ex) { return L.T("The races of your game client could not be read:") + " " + ex.Message; }
        }

        List<KeyValuePair<int, int>> ClientMoves()
        {
            var pairs = new List<KeyValuePair<int, int>>();
            foreach (string word in Raw("CustomRaces.MoveClientRaces", "").Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] p = word.Split(':'); int from, to;
                if (p.Length == 2 && int.TryParse(p[0], out from) && int.TryParse(p[1], out to) && from != to) pairs.Add(new KeyValuePair<int, int>(from, to));
            }
            return pairs;
        }

        /// <summary>The race numbers of the game client before the moves (the stock races and its own races).</summary>
        List<int> clientNumbers = new List<int>();

        /// <summary>The moves written short ("16:76 76:77 77:83" -> "16:83"), with the ones that do nothing left out.</summary>
        void TidyClientMoves()
        {
            string raw = Raw("CustomRaces.MoveClientRaces", "");
            if (raw.Trim().Length == 0) return;
            string tidy = RaceImport.NormalizeMoves(clientNumbers, raw);
            if (tidy != raw.Trim()) Set("CustomRaces.MoveClientRaces", "\"" + tidy + "\"");
        }

        void ApplyClientMoves()
        {
            clientNumbers = races.Where(r => r.Pack == null && !r.Virtual).Select(r => r.Source).Concat(ClientRaces.Stock).Distinct().ToList();
            TidyClientMoves();
            foreach (var pair in ClientMoves())
            {
                var race = races.FirstOrDefault(r => r.Pack == null && r.Source == pair.Key);
                if (race == null || races.Any(r => r.Pack == null && r.Source == pair.Value)) continue;
                race.Source = pair.Value;
                slots.Remove(pair.Value); moreSlots.Remove(pair.Value); highSlots.Remove(pair.Value);
                if (!slots.Contains(pair.Key) && !moreSlots.Contains(pair.Key) && !highSlots.Contains(pair.Key))
                {
                    if (pair.Key < 32) slots.Add(pair.Key); else if (pair.Key < 64) moreSlots.Add(pair.Key); else highSlots.Add(pair.Key);
                }
            }
        }

        void ReadFolder()
        {
            folder = Raw("CustomRaces.ImportFolder", "");
            if (folder.Length == 0) folder = Directory.Exists(DefaultRaceFolder) ? DefaultRaceFolder : null;
            if (folder == null || !Directory.Exists(folder)) { folder = null; return; }
            foreach (var p in RaceImport.Read(folder))
            {
                var bySource = p.SourceRace > 0 ? races.FirstOrDefault(r => r.Pack == null && r.Source == p.SourceRace) : null;
                var same = bySource != null
                    ? ((p.SourceFile.Length > 0 && p.SourceFile.Equals(bySource.File, StringComparison.OrdinalIgnoreCase)) ||
                       (p.Name.Length > 0 && (p.Name.Equals(bySource.Name, StringComparison.OrdinalIgnoreCase) || p.Name.Equals(bySource.Key, StringComparison.OrdinalIgnoreCase))) ? bySource : null)
                    : races.FirstOrDefault(r => r.Pack == null && (
                        r.Key.Equals(p.Key, StringComparison.OrdinalIgnoreCase) ||
                        (p.SourceFile.Length > 0 && p.SourceFile.Equals(r.File, StringComparison.OrdinalIgnoreCase) &&
                            (p.Name.Equals(r.Name, StringComparison.OrdinalIgnoreCase) || p.Name.Equals(r.Key, StringComparison.OrdinalIgnoreCase))) ||
                        (p.SourceRace > 0 && r.Source == p.SourceRace && p.SourceFile.Length > 0 && p.SourceFile.Equals(r.File, StringComparison.OrdinalIgnoreCase)) ||
                        (p.SourceRace > 0 && r.Source == p.SourceRace && (p.Name.Equals(r.Name, StringComparison.OrdinalIgnoreCase) || p.Key.Equals(r.Key, StringComparison.OrdinalIgnoreCase)))));
                if (same != null) { same.Alt = p; continue; }
                races.Add(new ClientRace { Key = p.Key, Name = p.Name, Prefix = p.Prefix, File = p.File, MaleOnly = p.MaleOnly, FemaleOnly = p.FemaleOnly, Pack = p });
            }
        }

        // ---------------------------------------------------------------- numbers

        void MarkOwnNumbers()
        {
            foreach (var r in races)
            {
                if (r.Pack != null || r.Virtual || r.Source < 32) { r.KeepsNumber = false; continue; }
                int n;
                bool other = int.TryParse(Raw("CustomRaces." + r.Key + ".Id", ""), out n) && n > 0 && n != r.Source;
                r.KeepsNumber = High && r.Source < 128 && !other;
            }
        }

        int ExtraLooksOf(ClientRace r, int id)
        {
            int kind;
            if (!High || !CoaExtraLooks.TryGetValue(id, out kind)) return 0;
            if (r.Pack == null) return r.KeepsNumber && r.Source == id ? kind : 0;
            return r.Pack.SourceRace == id && IsCoaNumber(r) ? kind : 0;
        }

        Dictionary<int, string> CoaRaceFiles()
        {
            if (coaRaceFiles != null) return coaRaceFiles;
            coaRaceFiles = new Dictionary<int, string>();
            try
            {
                string root = Raw("CustomRaces.ClientRootFolder", "");
                if (root.Length == 0) return coaRaceFiles;
                string dbc = Os.Resolve(Path.GetDirectoryName(root.TrimEnd('\\', '/')), "dbc/ChrRaces.dbc");
                if (!File.Exists(dbc)) return coaRaceFiles;
                var t = DbcTable.Parse(File.ReadAllBytes(dbc), "ChrRaces.dbc");
                foreach (var row in t.Records)
                {
                    int id = (int)t.Field(row, 0);
                    if (!coaRaceFiles.ContainsKey(id)) coaRaceFiles[id] = ClientRaces.Str(t, row, 11);
                }
            }
            catch { }
            return coaRaceFiles;
        }

        bool IsCoaNumber(ClientRace r)
        {
            if (r.Pack == null) return false;
            int n = r.Pack.SourceRace; string file;
            string packFile = r.Pack.File ?? r.Pack.Key ?? "";
            return n >= 32 && n < 128 && CoaRaceFiles().TryGetValue(n, out file) &&
                (string.Equals(file, r.Pack.SourceFile, StringComparison.OrdinalIgnoreCase) || string.Equals(file, packFile, StringComparison.OrdinalIgnoreCase));
        }

        int CoaNumberOf(ClientRace r)
        {
            if (!High || r == null || r.Pack == null || !r.Pack.Source.Equals("CoA Custom", StringComparison.OrdinalIgnoreCase)) return 0;
            int n = CoaRaceOf(r);
            if (n < 32 || n >= 128) return 0;
            if (CoaRaceFiles().Count > 0 && !IsCoaNumber(r) && r.Pack.SourceRace != n) return 0;
            bool slotFree = n < 64 ? moreSlots.Contains(n) : highSlots.Contains(n);
            bool sameVisible = races.Any(o => o != r && o.Source == n && SameRace(o, r));
            bool otherVisible = races.Any(o => o != r && !SameRace(o, r) && (o.Source == n || IdOf(o) == n));
            bool ownStockCopy = stockCopyNumbers.Contains(n) && r.Pack != null && (r.Pack.SourceRace == n || CoaRaceOf(r) == n);
            if (otherVisible || (reservedNumbers.Contains(n) && !sameVisible && !ownStockCopy) || (!slotFree && !sameVisible && races.Any(o => o.Source == n))) return 0;
            return n;
        }

        public int IdOf(ClientRace r)
        {
            int n;
            if (int.TryParse(Raw("CustomRaces." + r.Key + ".Id", ""), out n)) return n;
            return r.OwnNumber ? r.Source : 0;
        }

        int PreferredNumber(ClientRace r)
        {
            if (r.Pack != null) { int id = IdOf(r); return id > 0 ? id : r.Pack.SourceRace; }
            return r.Source;
        }

        ClientRace OtherAtNumber(int number, ClientRace self)
        {
            if (number <= 0) return null;
            return races.FirstOrDefault(x => !ReferenceEquals(x, self) && PreferredNumber(x) == number && !SameRace(self, x));
        }

        bool NumberTakenByOther(ClientRace r, int id)
        {
            if (id <= 0) return false;
            if (ClientRaces.Stock.Contains(id)) return true;
            foreach (var o in races) { if (ReferenceEquals(o, r) || IdOf(o) != id || SameRace(r, o)) continue; return true; }
            if (reservedNumbers.Contains(id))
            {
                bool ownStockCopy = stockCopyNumbers.Contains(id) && r.Pack != null && (r.Pack.SourceRace == id || CoaRaceOf(r) == id);
                if (!ownStockCopy) return true;
            }
            return false;
        }

        static bool SameRace(ClientRace a, ClientRace b)
        {
            if (a.Pack != null && b.Pack != null && a.Pack.Side != b.Pack.Side) return false;
            string af = a.Pack != null ? a.Pack.SourceFile : a.File, bf = b.Pack != null ? b.Pack.SourceFile : b.File;
            string an = a.Pack != null ? a.Pack.Name : a.Name, bn = b.Pack != null ? b.Pack.Name : b.Name;
            if (a.Pack != null && b.Pack == null && a.Pack.SourceRace > 0 && b.Source == a.Pack.SourceRace &&
                ((an.Length > 0 && an.Equals(bn, StringComparison.OrdinalIgnoreCase)) || (a.Pack.SourceFile.Length > 0 && a.Pack.SourceFile.Equals(b.File, StringComparison.OrdinalIgnoreCase))))
                return true;
            if (b.Pack != null && a.Pack == null && b.Pack.SourceRace > 0 && a.Source == b.Pack.SourceRace &&
                ((bn.Length > 0 && bn.Equals(an, StringComparison.OrdinalIgnoreCase)) || (b.Pack.SourceFile.Length > 0 && b.Pack.SourceFile.Equals(a.File, StringComparison.OrdinalIgnoreCase))))
                return true;
            return (af.Length > 0 && af.Equals(bf, StringComparison.OrdinalIgnoreCase)) || (an.Length > 0 && an.Equals(bn, StringComparison.OrdinalIgnoreCase));
        }

        void ClearOrphanNumbers()
        {
            foreach (var o in conf.Options.Where(o => o.Key.StartsWith("CustomRaces.", StringComparison.OrdinalIgnoreCase) && o.Key.EndsWith(".Id", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                string key = o.Key.Substring("CustomRaces.".Length, o.Key.Length - "CustomRaces.".Length - ".Id".Length);
                if (races.Any(r => r.Key.Equals(key, StringComparison.OrdinalIgnoreCase))) continue;
                int id;
                if (int.TryParse(Raw(o.Key, ""), out id) && id > 0) Unset(o.Key);
            }
        }

        public void HealNumbers()
        {
            ClearOrphanNumbers();
            MarkOwnNumbers();
            foreach (var r in races)
            {
                if (!r.OwnNumber) continue;
                int id = IdOf(r);
                if (id <= 0 || id == r.Source) continue;
                // copied on purpose to another number (CoA Custom's number of an Esteria race, given by the race check)
                if (Raw("CustomRaces." + r.Key + ".Copy", "0") == "1") continue;
                if (CharactersOf(id) != 0) continue;
                Set("CustomRaces." + r.Key + ".Id", "0");
            }
            var moved = new List<ClientRace>();
            foreach (var r in races)
            {
                if (r.OwnNumber) continue;
                if (!NumberTakenByOther(r, IdOf(r))) continue;
                Set("CustomRaces." + r.Key + ".Id", "0");
                moved.Add(r);
            }
            foreach (var r in moved)
            {
                int id = CoaNumberOf(r);
                if (id == 0) id = FreeSlot(r);
                if (id > 0) Set("CustomRaces." + r.Key + ".Id", id.ToString());
            }
            foreach (var r in races)
            {
                if (r.OwnNumber || r.Pack == null || !High || !r.Pack.Source.Equals("CoA Custom", StringComparison.OrdinalIgnoreCase)) continue;
                int id = IdOf(r), own = CoaNumberOf(r);
                if (id <= 0 || id == own || own == 0 || !Slots.Contains(id)) continue;
                if (CharactersOf(id) != 0) continue;
                Set("CustomRaces." + r.Key + ".Id", own.ToString());
            }
        }

        int FreeSlot(ClientRace r)
        {
            var list = Slots;
            if (High) list = list.Concat(highSlots).ToList();
            var used = new HashSet<int>(races.Where(x => !x.OwnNumber).Select(IdOf).Where(list.Contains));
            HashSet<int> coa = High && r != null && r.Pack != null && r.Pack.Source.Equals("CoA Custom", StringComparison.OrdinalIgnoreCase) ? new HashSet<int>(CoaRaceFiles().Keys) : null;
            foreach (int s in list) if (!used.Contains(s) && (coa == null || !coa.Contains(s))) return s;
            foreach (int s in list) if (!used.Contains(s)) return s;
            return 0;
        }

        int FreeHighNumber(ClientRace self)
        {
            var used = new HashSet<int>(races.Where(x => !ReferenceEquals(x, self)).Select(PreferredNumber));
            foreach (int slot in highSlots.Where(s => s >= 64).Concat(moreSlots.Where(s => s >= 32)).Concat(slots.Where(s => s >= 12)))
                if (!used.Contains(slot)) return slot;
            return 0;
        }

        bool MoveClientRace(ClientRace r, int to)
        {
            if (r == null || r.Pack != null || ClientRaces.Stock.Contains(r.Source) || to <= 0) return false;
            var pairs = ClientMoves().Where(p => p.Key != r.Source && p.Value != to).ToList();
            pairs.Add(new KeyValuePair<int, int>(r.Source, to));
            Set("CustomRaces.MoveClientRaces", "\"" + string.Join(" ", pairs.Select(p => p.Key + ":" + p.Value)) + "\"");
            TidyClientMoves();
            int from = r.Source;
            r.Source = to;
            slots.Remove(to); moreSlots.Remove(to); highSlots.Remove(to);
            if (from < 32) slots.Add(from); else if (from < 64) moreSlots.Add(from); else highSlots.Add(from);
            return true;
        }

        public int CharactersOf(int race)
        {
            try
            {
                var rows = MySql.Query(inst, DbLogin.FromConfig(inst), "SELECT COUNT(*) FROM acore_characters.characters WHERE race = " + race + ";");
                int n;
                return rows.Count > 0 && int.TryParse(rows[0], out n) ? n : -1;
            }
            catch { return -1; }
        }

        string EsteriaFile(ClientRace r)
        {
            try
            {
                string client = ClientAddons.ClientFolder;
                if (client == null || !Directory.Exists(client)) return null;
                string wanted = "Esteria" + (r.Name ?? "").Replace(" ", "");
                foreach (var file in Directory.GetFiles(client).Where(f => Path.GetFileName(f).StartsWith("Esteria", StringComparison.OrdinalIgnoreCase) && f.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)))
                    if (Path.GetFileNameWithoutExtension(file).Equals(wanted, StringComparison.OrdinalIgnoreCase)) return Path.GetFileName(file);
            }
            catch { }
            return null;
        }

        // ---------------------------------------------------------------- what a race is

        Known KnownOf(ClientRace r) { Known k; return known.TryGetValue(r.Key, out k) ? k : null; }

        public static string BaseName(string key)
        {
            switch (key)
            {
                case "Human": return L.T("Human"); case "Orc": return L.T("Orc"); case "Dwarf": return L.T("Dwarf"); case "NightElf": return L.T("Night Elf");
                case "Undead": return L.T("Undead"); case "Tauren": return L.T("Tauren"); case "Gnome": return L.T("Gnome"); case "Troll": return L.T("Troll");
                case "BloodElf": return L.T("Blood Elf"); case "Draenei": return L.T("Draenei");
                default: return key;
            }
        }

        bool UsesFolder(ClientRace r) { return r.Pack != null || (r.Alt != null && Raw("CustomRaces." + r.Key + ".Import", "0") == "1"); }
        RaceImport.Pack PackOf(ClientRace r) { return r.Pack ?? (UsesFolder(r) ? r.Alt : null); }

        string PackNumberConflict(ClientRace r)
        {
            var pack = r.Pack;
            if (pack == null || pack.SourceRace <= 0) return null;
            var other = OtherAtNumber(pack.SourceRace, r);
            if (other == null) return null;
            return L.F("Its source race number {0} is {1} in your game client, so it gets a free number. Looks that the other client keeps under {0} cannot follow it.", pack.SourceRace, other.Name);
        }

        public string DefaultBase(ClientRace r)
        {
            var k = KnownOf(r);
            if (k != null && bases.Contains(k.Base)) return Side(k.Base, r);
            string n = r.Name.ToLowerInvariant() + " " + r.File.ToLowerInvariant();
            // Darkfallen is an undead elf: night elf for the Alliance, blood elf for the Horde. Without this
            // the generic Horde fallback would make the Horde row an orc.
            if (n.Contains("darkfallen")) return Side("NightElf", r);
            foreach (var pair in new[] { new[] { "troll", "Troll" }, new[] { "orc", "Orc" }, new[] { "dwar", "Dwarf" }, new[] { "gnom", "Gnome" },
                new[] { "tauren", "Tauren" }, new[] { "taunka", "Tauren" }, new[] { "skelet", "Undead" }, new[] { "undead", "Undead" }, new[] { "scourge", "Undead" },
                new[] { "night", "NightElf" }, new[] { "blood", "BloodElf" }, new[] { "elf", "BloodElf" }, new[] { "draenei", "Draenei" } })
                if (n.Contains(pair[0])) return Side(pair[1], r);
            return Side("Human", r);
        }

        string Side(string guess, ClientRace r)
        {
            int side = r.Pack != null ? r.Pack.Side : r.Alt != null ? r.Alt.Side : r.Side;
            if (side == 2)
            {
                string name = (r.Name + " " + r.File + " " + r.Key).ToLowerInvariant();
                if (name.Contains("horde")) side = 1; else if (name.Contains("alliance")) side = 0;
            }
            var horde = new HashSet<string> { "Orc", "Undead", "Tauren", "Troll", "BloodElf" };
            if (side == 0 && horde.Contains(guess)) return guess == "BloodElf" ? "NightElf" : guess == "Troll" ? "Dwarf" : "Human";
            if (side == 1 && !horde.Contains(guess)) return guess == "Draenei" || guess == "NightElf" ? "BloodElf" : guess == "Dwarf" ? "Troll" : "Orc";
            return guess;
        }

        public List<string> Chosen() { return Raw("CustomRaces.Races", "").Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries).ToList(); }

        public void PruneChosen()
        {
            if (races.Count == 0) return;
            var chosen = Chosen();
            var valid = chosen.Where(k => races.Any(r => r.Key.Equals(k, StringComparison.OrdinalIgnoreCase))).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (valid.Count != chosen.Count) Set("CustomRaces.Races", string.Join(" ", valid));
        }

        int CoaRaceOf(ClientRace r)
        {
            var pack = PackOf(r);
            if (pack == null) return r.Virtual ? 0 : r.Source;
            int source = pack.SourceRace;
            if (pack.Source.Equals("CoA Custom", StringComparison.OrdinalIgnoreCase))
            {
                string n = (pack.Name + " " + pack.File + " " + pack.Key).ToLowerInvariant();
                if (n.Contains("pandaren")) return 20;
                if (n.Contains("dracthyr")) return source == 54 || source == 58 ? source : (pack.Side == 0 ? 54 : 58);
                if (n.Contains("vulperaesteria") || n.Contains("esteria")) return 74;
                if (n.Contains("vulpera")) return source > 0 ? source : 19;
                if (n.Contains("drakonid")) return pack.Side == 0 ? 100 : 98;
                if (n.Contains("darkfallen")) return source == 80 || source == 81 ? source : (pack.Side == 0 ? 80 : 81);
                if (n.Contains("earthen")) return pack.Side == 0 ? 68 : 69;
                if (n.Contains("haranir")) return pack.Side == 0 ? 71 : 70;
                if (n.Contains("murloc")) return 65;
                if (n.Contains("drakkin")) return 99;
                if (n.Contains("highmountain")) return 66;
                if (n.Contains("mechagnome")) return 67;
                if (n.Contains("gnoll")) return 83;
                if (n.Contains("sethrak")) return 82;
                if (n.Contains("saberon")) return 97;
            }
            return source;
        }

        static string PackHelmetPrefix(RaceImport.Pack pack)
        {
            var prefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in pack.Files)
            {
                if (!f.StartsWith("Item\\ObjectComponents\\Head\\", StringComparison.OrdinalIgnoreCase) || !f.EndsWith(".m2", StringComparison.OrdinalIgnoreCase)) continue;
                string stem = Path.GetFileNameWithoutExtension(f.Replace('\\', Path.DirectorySeparatorChar));
                if (stem.Length < 4) continue;
                char sex = stem[stem.Length - 1];
                if (sex != 'M' && sex != 'F') continue;
                prefixes.Add(stem.Substring(stem.Length - 3, 2));
            }
            if (prefixes.Count == 0) return "";
            if (pack.Prefix.Length > 0 && prefixes.Contains(pack.Prefix)) return pack.Prefix;
            return prefixes.Count == 1 ? prefixes.First() : "";
        }

        void Describe(ClientRace r, int id)
        {
            Action<string, string> put = (name, value) => { if (Raw("CustomRaces." + r.Key + "." + name, null) != value) Set("CustomRaces." + r.Key + "." + name, value); };
            Action<string, string> putText = (name, value) => { if (Raw("CustomRaces." + r.Key + "." + name, null) != value) Set("CustomRaces." + r.Key + "." + name, "\"" + value.Replace("\"", "") + "\""); };
            var pack = PackOf(r);
            string ownHelmetPrefix = "";
            if (pack != null)
            {
                put("Source", "0"); put("Id", id.ToString()); put("Copy", "0"); put("Import", "1");
                putText("Name", pack.Name); putText("Prefix", pack.Prefix); putText("File", pack.File.Length > 0 ? pack.File : pack.Key);
                put("MaleDisplay", id > 0 ? (RaceImport.DisplayBase + id * 2).ToString() : "0");
                put("FemaleDisplay", id > 0 ? (RaceImport.DisplayBase + id * 2 + 1).ToString() : "0");
                put("MaleOnly", (pack.MaleOnly || RaceImport.KnownMaleOnly(r.Key)) && !pack.FemaleOnly ? "1" : "0");
                put("FemaleOnly", pack.FemaleOnly ? "1" : "0");
                // its own helmets: those of its prefix in the game client and in its race pack - only when they are (nearly)
                // all there; a race with helmets for some items only shows the placeholder cube for every other one
                string own = PackHelmetPrefix(pack);
                if (own.Length == 0) own = pack.Prefix;
                bool full = own.Length > 0 && (helmetPrefixes.Count == 0 || helmetPrefixes.Contains(own) || pack.Files.Any(f => f.EndsWith("_" + own + "M.m2", StringComparison.OrdinalIgnoreCase) || f.EndsWith("_" + own + "F.m2", StringComparison.OrdinalIgnoreCase)))
                    && ClientRaces.FullHelmets(helmetScan, own, pack.MaleOnly, pack.Files, pack.FemaleOnly);
                put("OwnHelmets", full ? "1" : "0");
                ownHelmetPrefix = full ? own : "";
            }
            else
            {
                put("Source", r.Source.ToString()); put("Id", id.ToString()); put("Copy", r.OwnNumber && (id <= 0 || id == r.Source) ? "0" : "1"); put("Import", "0");
                putText("Name", r.Name); putText("Prefix", r.Prefix); putText("File", r.File);
                put("MaleDisplay", r.MaleDisplay.ToString()); put("FemaleDisplay", r.FemaleDisplay.ToString());
                put("MaleOnly", r.MaleOnly ? "1" : "0"); put("FemaleOnly", r.FemaleOnly ? "1" : "0"); put("OwnHelmets", r.HasHelmets ? "1" : "0");
            }
            string currentBase = Raw("CustomRaces." + r.Key + ".Base", "");
            if (currentBase.Length == 0 || !ClientRaces.BaseFitsSide(ClientRaces.SideOf(r), currentBase))
                putText("Base", DefaultBase(r));
            string borrowed;
            if (ownHelmetPrefix.Length > 0) { putText("HelmetPrefix", ownHelmetPrefix); put("OwnHelmets", "1"); }
            else if (Raw("CustomRaces." + r.Key + ".OwnHelmets", "1") == "0" && BasePrefixes.TryGetValue(Raw("CustomRaces." + r.Key + ".Base", ""), out borrowed))
            { putText("HelmetPrefix", borrowed); put("OwnHelmets", "1"); }
            else putText("HelmetPrefix", "");
            put("Appearance", ExtraLooksOf(r, id).ToString());
            var racials = ServerRacials.Spells("", pack != null ? pack.Name : r.Name, CoaRaceOf(r));
            if (racials.Length > 0) putText("Racials", string.Join(" ", racials));
            else Unset("CustomRaces." + r.Key + ".Racials");
            string icon = null;
            if (pack != null)
            {
                var f = pack.Files.FirstOrDefault(x => x.StartsWith("Interface\\Icons\\", StringComparison.OrdinalIgnoreCase));
                if (f != null) icon = Os.Resolve(Path.Combine(pack.Dir, "files"), f);
            }
            if ((icon == null || !File.Exists(icon)) && folder != null) icon = Path.Combine(folder, "_Icons", "CoA_Race_" + r.Key + ".blp");
            if (icon != null && File.Exists(icon)) { putText("IconFile", icon); putText("Icon", "Interface\\Icons\\CoA_Race_" + r.Key); }
            else { putText("IconFile", ""); putText("Icon", ""); }
        }

        void Refresh()
        {
            if (folder != null && Raw("CustomRaces.ImportFolder", "").Length == 0) Set("CustomRaces.ImportFolder", "\"" + folder + "\"");
            if (Raw("CustomRaces.RacialsFolder", "") != RacialsFolder) Set("CustomRaces.RacialsFolder", "\"" + RacialsFolder + "\"");
            foreach (var r in races) if (Raw("CustomRaces." + r.Key + ".Source", "").Length > 0) Describe(r, IdOf(r));
        }

        void AutoDetect()
        {
            string root = Raw("CustomRaces.ClientRootFolder", "");
            if (root.Length == 0)
            {
                string found = DefaultClientRoot();
                if (found != null) { Set("CustomRaces.ClientRootFolder", found); root = found; }
            }
            if (root.Length == 0) return;
            string currentExe = Raw("CustomRaces.ClientExe", "");
            string exe = ClientPatch.GameExeOf(root);
            if (exe != null && !SamePath(currentExe, exe) && (currentExe.Length == 0 || !IsInside(currentExe, root))) Set("CustomRaces.ClientExe", exe);
            string currentPatch = Raw("CustomRaces.MoreRacesFile", "");
            string patch = ClientPatch.CoAPatchOf(root);
            string files = Path.GetDirectoryName(root.TrimEnd('\\', '/'));
            bool pathMissing = string.IsNullOrEmpty(currentPatch) || !File.Exists(currentPatch);
            if (patch != null && RacePacker.IsRacePatch(patch) && !SamePath(currentPatch, patch) && (pathMissing || files == null || !IsInside(currentPatch, files)))
            {
                Set("CustomRaces.MoreRacesFile", patch);
                Set("CustomRaces.HighRaces", "1");
                Set("CustomRaces.MoreRaces", "1");
            }
        }

        static string DefaultClientRoot()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (string pack in new[] { Path.Combine(home, "Downloads", "CoA-Custom", "CoA-Custom"), Path.Combine(home, "Downloads", "CoA-Custom") })
            {
                string root = ClientPatch.ClientRootOf(pack);
                if (root != null) return root;
            }
            return null;
        }

        static bool SamePath(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try { return Path.GetFullPath(a).Equals(Path.GetFullPath(b), Os.Windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal); } catch { return false; }
        }

        static bool IsInside(string file, string folder)
        {
            if (string.IsNullOrEmpty(file) || string.IsNullOrEmpty(folder)) return false;
            try { string f = Path.GetFullPath(folder).TrimEnd('\\', '/') + Path.DirectorySeparatorChar; return Path.GetFullPath(file).StartsWith(f, StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        public string RaceFolder()
        {
            string configured = Raw("CustomRaces.ImportFolder", "");
            return !string.IsNullOrEmpty(folder) ? folder : configured.Length > 0 ? configured : DefaultRaceFolder;
        }

        // ---------------------------------------------------------------- what the page shows

        public object View()
        {
            int own = races.Count(r => r.OwnNumber);
            var s = Slots;
            int high = High ? highSlots.Count : 0;
            int used = races.Count(r => !r.OwnNumber && (s.Contains(IdOf(r)) || (High && highSlots.Contains(IdOf(r)))));
            int on = Chosen().Count(k => races.Any(r => r.Key.Equals(k, StringComparison.OrdinalIgnoreCase)));
            string usage = L.F("{0} of up to {1} races on", on, own + s.Count + high) + "  ·  " + (More ? L.T("64-race patch on") : L.T("64-race patch off")) + "  ·  " +
                L.F("{0} of {1} free race numbers in use", used, s.Count + high);
            string intro = L.T("These are the races your game client carries a body for, read from the client itself. Switch on the ones you want: each starts like its base race - where that race starts, with its quests, stats, racial abilities and faction - and keeps its own looks.") +
                (slots.Count > 0 ? " " + L.F("There are {0} free race numbers below 32 and {1} more above 31; together with the {2} races that bring their own number, up to {3} races can be on at once.", slots.Count, moreSlots.Count, own, own + slots.Count + moreSlots.Count) : "");
            string raceFolder = Raw("CustomRaces.ImportFolder", "");
            if (raceFolder.Length == 0) raceFolder = folder ?? "";
            int packs = races.Count(r => r.Pack != null || r.Alt != null);
            string client = ClientAddons.ClientFolder ?? L.T("no game client chosen");
            string root = Raw("CustomRaces.ClientRootFolder", "");
            string files;
            try { files = root.Length == 0 || !Directory.Exists(root) ? L.T("no extra client files") : L.F("{0} files", Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length); }
            catch { files = L.T("no extra client files"); }
            string import = L.F("Race folder: {0} ({1} races)  ·  Game client: {2}  ·  {3}  ·  Race numbers: automatic", raceFolder.Length == 0 ? L.T("not chosen") : raceFolder, packs, client, files) +
                (string.IsNullOrEmpty(coaNote) ? "" : "  ·  " + coaNote);
            var chosen = Chosen();
            Func<ClientRace, object> race = r =>
            {
                var k = KnownOf(r);
                int id = IdOf(r);
                bool isOn = chosen.Any(c => c.Equals(r.Key, StringComparison.OrdinalIgnoreCase));
                string about = r.Pack != null ? L.F("From your race folder ({0}, {1} MB).", r.Pack.Source.Length > 0 ? r.Pack.Source : L.T("another client"), r.Pack.Bytes / 1000000) +
                        (r.Pack.Missing > 0 ? " " + L.F("{0} of its files are missing; the choices that need them are left out.", r.Pack.Missing) : "")
                    : k != null && k.About.Length > 0 ? L.Outside(k.About) : r.Virtual ? L.T("A race of your game client.") : L.F("A race of your game client (row {0} of its race table).", r.Source);
                if (k != null && k.Note.Length > 0) about += " " + L.Outside(k.Note);
                string conflict = PackNumberConflict(r);
                if (conflict != null) about += " " + conflict;
                var racialSpells = ServerRacials.Spells("", r.Pack != null ? r.Pack.Name : r.Name, CoaRaceOf(r));
                var needCode = ServerRacials.NeedCode(racialSpells);
                if (needCode.Length > 0) about += " " + L.F("{0} of its {1} racial abilities need code of CoA Custom's server and do nothing here yet.", needCode.Length, racialSpells.Length);
                string b = Raw("CustomRaces." + r.Key + ".Base", DefaultBase(r));
                if (!ClientRaces.BaseFitsSide(ClientRaces.SideOf(r), b)) b = DefaultBase(r);
                int at = bases.FindIndex(x => x.Equals(b, StringComparison.OrdinalIgnoreCase));
                return new
                {
                    key = r.Key, name = L.Outside(r.Name), on = isOn, @base = at < 0 ? DefaultBase(r) : bases[at], about,
                    number = r.OwnNumber ? L.F("race number {0} (its own)", r.Source) : id > 0 ? L.F("race number {0}", id) : L.T("no race number yet"),
                    canFree = !r.OwnNumber, freeEnabled = !r.OwnNumber && id > 0 && !isOn,
                    canMove = r.Pack == null && !r.Virtual && !ClientRaces.Stock.Contains(r.Source),
                    pack = r.Pack != null,
                    alt = r.Alt == null ? null : new { text = L.F("Looks from the race folder ({0}, {1} MB) instead of your client", r.Alt.Source.Length > 0 ? r.Alt.Source : L.T("another client"), r.Alt.Bytes / 1000000), on = Raw("CustomRaces." + r.Key + ".Import", "0") == "1" }
                };
            };
            int capacity = own + s.Count + high;
            var simple = races.OrderBy(r => ClientRaces.SideOf(r)).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).Select(r => Simple(r, chosen)).ToList();
            var conflicts = new List<object>();
            foreach (var r in races.Where(r => chosen.Any(c => c.Equals(r.Key, StringComparison.OrdinalIgnoreCase))))
            {
                var c = Conflict(r);
                if (c != null) conflicts.Add(c);
            }
            if (on > capacity && capacity > 0)
                conflicts.Add(new
                {
                    id = "capacity", title = L.F("More races are on than the game has room for ({0} of {1})", on, capacity),
                    text = L.T("The game can only show a certain number of races. Switch off the races you want least; their characters keep playing as the race they start like until you switch them on again."),
                    answers = new object[0]
                });
            return new
            {
                list = simple, conflicts, capacity, onCount = on, clientFolder = ClientAddons.ClientFolder ?? "",
                hasCoa = Raw("CustomRaces.ClientRootFolder", "").Length > 0,
                setupNeeded = chosen.Count == 0 && !races.Any(r => r.Pack != null || r.Alt != null),
                problem, knownError, intro, usage, usageWarn = used >= s.Count + high, import, coaButton = coaButton ?? L.T("Download CoA Custom …"),
                enable = Raw("CustomRaces.Enable", "1") == "1", more = More, high = High, unsaved = Unsaved,
                bases = bases.Select(x => new { key = x, name = BaseName(x) }).ToArray(),
                imported = races.Where(r => r.Pack != null).OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).Select(race).ToArray(),
                client = races.Where(r => r.Pack == null).OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).Select(race).ToArray(),
                after = L.F("The race choice of the game shows the races of the client patch. After a change, open Modules › Game client and choose Install: {0} builds the patch anew. The other players need the new ZIP. A race switched off keeps its characters as their base race until it is on again; changing the base race of a race with characters may move them to the other faction. When the game client changes with an update, open this page again: it reads the races anew.", Product.Name)
            };
        }

        // ---------------------------------------------------------------- the simple view: one line per race

        /// <summary>Whether a race is an Esteria race: its looks are built by CoA Custom's appearance DLL, which needs CoA
        /// Custom's game exe and the race's number there.</summary>
        bool NeedsCoaExe(ClientRace r)
        {
            var p = PackOf(r);
            if (p != null) return p.Files.Any(f => f.IndexOf("\\native\\", StringComparison.OrdinalIgnoreCase) >= 0);
            return r.Source >= 64 || EsteriaFile(r) != null;
        }

        bool HasCoaExe { get { return High && Raw("CustomRaces.ClientExe", "").Length > 0; } }

        /// <summary>A race the user has to decide something about, in plain words with the ways out; null when AFK Realm
        /// handles everything itself.</summary>
        object Conflict(ClientRace r)
        {
            string name = L.Outside(r.Name);
            if (NeedsCoaExe(r) && !HasCoaExe)
                return new
                {
                    id = "coa:" + r.Key, key = r.Key, title = L.F("{0} needs CoA Custom", name),
                    text = L.F("The looks of {0} (fur, armour styles, extra choices) are drawn by a part of CoA Custom's game. Without it the race appears in pieces. Download CoA Custom once - AFK Realm sets everything up - or switch the race off.", name),
                    answers = new object[] { new { value = "coa-download", text = L.T("Download CoA Custom (recommended)"), primary = true },
                        new { value = "coa-folder", text = L.T("I have CoA Custom already: choose its folder"), primary = false },
                        new { value = "off:" + r.Key, text = L.F("Switch {0} off", name), primary = false } }
                };
            if (!r.OwnNumber && IdOf(r) <= 0)
            {
                var answers = new List<object>();
                if (!More && moreSlots.Count > 0) answers.Add(new { value = "more", text = L.T("Make room for more races (recommended)"), primary = true });
                answers.Add(new { value = "off:" + r.Key, text = L.F("Switch {0} off", name), primary = answers.Count == 0 });
                return new
                {
                    id = "room:" + r.Key, key = r.Key, title = L.F("No room for {0}", name),
                    text = !More && moreSlots.Count > 0
                        ? L.T("The game is full. AFK Realm can make room for more races: it adds a small file of CoA Custom next to the game for that.")
                        : L.T("The game is full. Switch off a race you want less, then switch this one on again."),
                    answers = answers.ToArray()
                };
            }
            if (r.Pack != null && NeedsCoaExe(r) && PackNumberConflict(r) != null)
                return new
                {
                    id = "number:" + r.Key, key = r.Key, title = L.F("{0} cannot get its place", name),
                    text = L.F("{0} has to be at place {1} to look right, but {2} of your game is there already. Switch one of the two off.", name, r.Pack.SourceRace, L.Outside(OtherAtNumber(r.Pack.SourceRace, r).Name)),
                    answers = new object[] { new { value = "off:" + r.Key, text = L.F("Switch {0} off", name), primary = true } }
                };
            return null;
        }

        /// <summary>One line of the simple list.</summary>
        object Simple(ClientRace r, List<string> chosen)
        {
            bool isOn = chosen.Any(c => c.Equals(r.Key, StringComparison.OrdinalIgnoreCase));
            int side = ClientRaces.SideOf(r);
            string b = Raw("CustomRaces." + r.Key + ".Base", DefaultBase(r));
            if (!ClientRaces.BaseFitsSide(side, b)) b = DefaultBase(r);
            bool female = r.Pack != null ? r.Pack.FemaleOnly : r.FemaleOnly;
            bool male = !female && (r.Pack != null ? r.Pack.MaleOnly : r.MaleOnly);
            string state = "ready", stateText = L.T("ready");
            if (isOn && Conflict(r) != null) { state = "decide"; stateText = L.T("needs your decision"); }
            else if (isOn && r.Pack != null && PackNumberConflict(r) != null) { state = "auto"; stateText = L.T("gets a free place automatically"); }
            else if (!isOn) { state = "off"; stateText = ""; }
            object variant = null;
            if (r.Alt != null)
            {
                bool usesFolder = Raw("CustomRaces." + r.Key + ".Import", "0") == "1";
                string other = r.Alt.Source.Length > 0 ? r.Alt.Source : L.T("another client");
                variant = new
                {
                    text = usesFolder ? L.F("Looks: {0} (more choices)", other) : L.T("Looks: your game"),
                    swap = usesFolder ? L.T("Use the looks of your game instead") : L.F("Use the looks of {0} instead", other),
                    on = usesFolder
                };
            }
            string from = r.Pack != null ? (r.Pack.Source.Length > 0 ? r.Pack.Source : L.T("another client")) : L.T("your game");
            return new
            {
                key = r.Key, name = L.Outside(r.Name), on = isOn, side = side == 0 ? "alliance" : side == 1 ? "horde" : "",
                sex = male ? L.T("men only") : female ? L.T("women only") : "",
                @base = b, from, state, stateText, variant,
                pack = r.Pack != null, canFree = !r.OwnNumber, freeEnabled = !r.OwnNumber && IdOf(r) > 0 && !isOn,
                canMove = r.Pack == null && !r.Virtual && !ClientRaces.Stock.Contains(r.Source),
                number = r.OwnNumber ? L.F("race number {0} (its own)", r.Source) : IdOf(r) > 0 ? L.F("race number {0}", IdOf(r)) : L.T("no race number yet")
            };
        }

        public string RaceIds()
        {
            var text = new System.Text.StringBuilder();
            text.AppendLine(L.T("Race IDs of this realm")); text.AppendLine();
            text.AppendLine(L.T("Source row = the row in the client's ChrRaces.dbc. Race number = the number the realm gives it (CustomRaces.<race>.Id)."));
            text.AppendLine(L.T("A custom/Esteria race is tied to its source row; the importer keeps it and replaces a stock-copy row (51-61) instead of moving the race."));
            text.AppendLine(); text.AppendLine("ID\tSource\tRace\tBase\tKind");
            foreach (var r in races.OrderBy(r => IdOf(r) <= 0 ? int.MaxValue : IdOf(r)).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                int id = IdOf(r), source = r.Pack != null ? r.Pack.SourceRace : r.Source;
                string kind = r.Pack != null ? L.T("imported") : r.Virtual ? L.T("virtual") : L.T("client");
                string b = Raw("CustomRaces." + r.Key + ".Base", DefaultBase(r));
                if (!ClientRaces.BaseFitsSide(ClientRaces.SideOf(r), b)) b = DefaultBase(r);
                text.AppendLine((id > 0 ? id.ToString() : "-") + "\t" + (source > 0 ? source.ToString() : "-") + "\t" + r.Name + "\t" + b + "\t" + kind);
            }
            return text.ToString();
        }

        // ---------------------------------------------------------------- what the page does

        public ClientRace Find(string key)
        {
            var r = races.FirstOrDefault(x => x.Key.Equals(key ?? "", StringComparison.OrdinalIgnoreCase));
            if (r == null) throw new UserError(L.T("The race was not found."));
            return r;
        }

        /// <summary>A question for the page: its text and the answers (value, label); the page sends the chosen value back.</summary>
        public class Ask : Exception
        {
            public readonly object Question;
            public Ask(string text, params string[] answers) : base(text)
            {
                var list = new List<object>();
                for (int i = 0; i + 1 < answers.Length; i += 2) list.Add(new { value = answers[i], text = answers[i + 1] });
                Question = new { text, answers = list };
            }
        }

        /// <summary>Switches a race on or off. answer: what the user chose when asked about a number another race has.</summary>
        public void Toggle(string key, bool on, string answer)
        {
            var r = Find(key);
            var chosen = Chosen().Where(c => !c.Equals(r.Key, StringComparison.OrdinalIgnoreCase)).ToList();
            if (on)
            {
                if (!ResolveNumberConflict(r, answer)) return;
                int id = IdOf(r);
                if (id == 0) id = CoaNumberOf(r);
                if (id == 0)
                {
                    id = FreeSlot(r);
                    if (id == 0)
                        throw new UserError(L.F("All {0} free race numbers are taken{1}. Free the number of a race you do not want any more (switch it off, then \"Free its number\") to make room for the {2}.", Slots.Count, More ? "" : " " + L.T("(switch on the 64-race patch for more)"), L.Outside(r.Name)));
                }
                if (Raw("CustomRaces." + r.Key + ".Base", "").Length == 0) Set("CustomRaces." + r.Key + ".Base", "\"" + DefaultBase(r) + "\"");
                // a race the game has and the race folder too (the Vulpera of the game and CoA Custom's): AFK Realm picks the
                // folder's looks (more choices) unless the user chose before; the line says so and can switch back
                if (r.Alt != null && Raw("CustomRaces." + r.Key + ".Import", "").Length == 0) Set("CustomRaces." + r.Key + ".Import", "1");
                Describe(r, id);
                chosen.Add(r.Key);
            }
            Set("CustomRaces.Races", string.Join(" ", chosen));
        }

        bool ResolveNumberConflict(ClientRace r, string answer)
        {
            int wanted = r.Pack != null ? r.Pack.SourceRace : 0;
            var other = wanted > 0 ? OtherAtNumber(wanted, r) : null;
            if (other == null) return true;
            int characters = other.Pack == null ? CharactersOf(other.Source) : 0;
            string esteria = other.Pack == null ? EsteriaFile(other) : null;
            bool canMove = other.Pack == null && !ClientRaces.Stock.Contains(other.Source) && characters == 0;
            int target = canMove ? FreeHighNumber(other) : 0;
            if (answer == null)
            {
                var lines = new List<string>
                {
                    L.F("The imported race {0} wants race number {1}.", r.Name, wanted),
                    L.F("That number belongs to {0} in your game client.", other.Name)
                };
                if (other.Pack != null) lines.Add(L.T("It is another imported race. Switch it off or give this race a free number."));
                else if (ClientRaces.Stock.Contains(other.Source)) lines.Add(L.T("It is one of the stock races. Its number cannot be moved."));
                else
                {
                    lines.Add(L.T("It is a race of your game client. Switching it off would not free its number; it has to be moved."));
                    lines.Add(characters < 0 ? L.T("Its characters could not be checked, so moving is not offered.") : characters == 0 ? L.T("It has no characters.") : L.F("It has {0} character(s); moving would change them.", characters));
                    if (esteria != null) lines.Add(L.F("It looks like an Esteria race ({0}); moving its number can break its appearance.", esteria));
                }
                lines.Add(L.F("With a free number for {0}, appearance data tied to {1} may be missing.", r.Name, wanted));
                var answers = new List<string> { "cancel", L.T("Cancel"), "free", L.T("Use a free number") };
                if (canMove && target > 0) { answers.Add("move"); answers.Add(L.F("Move {0} to {1}", other.Name, target)); }
                throw new Ask(string.Join("\n\n", lines), answers.ToArray());
            }
            if (answer == "cancel") return false;
            if (answer == "move")
            {
                if (!canMove || target <= 0 || !MoveClientRace(other, target)) return false;
                Set("CustomRaces." + r.Key + ".Id", wanted.ToString());
            }
            return true;
        }

        public void SetBase(string key, string value)
        {
            var r = Find(key);
            if (!bases.Contains(value)) throw new UserError("Unknown base race.");
            if (!ClientRaces.BaseFitsSide(ClientRaces.SideOf(r), value)) value = DefaultBase(r);
            Set("CustomRaces." + r.Key + ".Base", "\"" + value + "\"");
            if (Raw("CustomRaces." + r.Key + ".Source", "").Length > 0) Describe(r, IdOf(r));
        }

        public void Free(string key) { var r = Find(key); Set("CustomRaces." + r.Key + ".Id", "0"); }

        public void SetAlt(string key, bool on)
        {
            var r = Find(key);
            Set("CustomRaces." + r.Key + ".Import", on ? "1" : "0");
            if (Raw("CustomRaces." + r.Key + ".Source", "").Length > 0 || on) Describe(r, IdOf(r) > 0 ? IdOf(r) : r.OwnNumber ? r.Source : 0);
        }

        /// <summary>Moving a race of the client to a free number, step by step: step 0 checks, 1 after the Esteria question, 2 does it.</summary>
        public string Move(string key, int step)
        {
            var r = Find(key);
            if (r.Pack != null || r.Virtual || ClientRaces.Stock.Contains(r.Source)) throw new UserError(L.T("The race could not be moved."));
            int characters = CharactersOf(r.Source);
            if (characters < 0) throw new UserError(L.T("The characters of this race could not be checked. Start the database and try again; moving is not offered without that check."));
            if (characters > 0) throw new UserError(L.F("{0} still has {1} character(s). A race with characters cannot be moved.", L.Outside(r.Name), characters));
            int target = FreeHighNumber(r);
            if (target == 0) throw new UserError(L.T("There is no free race number left for this move."));
            string esteria = EsteriaFile(r);
            if (step == 0 && esteria != null) throw new Ask(L.F("{0} looks like an Esteria race ({1}). Moving it can break its appearance. Continue?", L.Outside(r.Name), esteria), "cancel", L.T("No"), "1", L.T("Yes"));
            if (step <= 1) throw new Ask(L.F("Move {0} from race number {1} to {2}? The client patch must be built again afterwards.", L.Outside(r.Name), r.Source, target), "cancel", L.T("No"), "2", L.T("Yes"));
            if (!MoveClientRace(r, target)) throw new UserError(L.T("The race could not be moved."));
            return L.F("{0} will use race number {1} after the next client patch.", L.Outside(r.Name), target) + "\n\n" + L.T("Save the page and build the client patch again.");
        }

        public string DeletePack(string key)
        {
            var r = Find(key);
            var pack = r.Pack;
            if (pack == null) throw new UserError(L.T("The race was not found."));
            try { Directory.Delete(pack.Dir, true); } catch (Exception ex) { throw new UserError(L.T("The race could not be deleted:") + "\n" + ex.Message); }
            Set("CustomRaces.Races", string.Join(" ", Chosen().Where(c => !c.Equals(r.Key, StringComparison.OrdinalIgnoreCase))));
            foreach (var option in new[] { "Source", "Id", "Copy", "Import", "Name", "Prefix", "File", "MaleDisplay", "FemaleDisplay", "MaleOnly", "FemaleOnly", "OwnHelmets", "HelmetPrefix", "Base", "Racials", "IconFile", "Icon", "Appearance" })
                Unset("CustomRaces." + r.Key + "." + option);
            races.Remove(r);
            return L.F("{0} was deleted from the race folder.", L.Outside(r.Name)) + "\n\n" + L.T("Save the page and build the client patch anew so the race disappears from the game.");
        }

        public string DeleteAlt(string key)
        {
            var r = Find(key);
            var pack = r.Alt;
            if (pack == null) throw new UserError(L.T("The race was not found."));
            try { Directory.Delete(pack.Dir, true); } catch (Exception ex) { throw new UserError(L.T("The race could not be deleted:") + "\n" + ex.Message); }
            Set("CustomRaces." + r.Key + ".Import", "0");
            r.Alt = null;
            return L.F("The race folder looks of {0} were deleted.", L.Outside(r.Name)) + "\n\n" + L.T("Save the page and build the client patch anew.");
        }

        public void ResetDefaults()
        {
            foreach (var o in conf.Options.Where(o => o.DefaultRaw != null)) Set(o.Key, ConfigOption.Unquote(o.DefaultRaw));
        }

        /// <summary>The import button, before the race selection: what the chosen folder brings. Returns the folder the races are read from.</summary>
        public string PrepareImport(string picked)
        {
            string root = ClientPatch.ClientRootNear(picked);
            string source = picked;
            string patch = root != null ? ClientPatch.CoAPatchOf(root) : RacePacker.FindRacePatch(picked);
            if (root != null)
            {
                string files = Path.GetDirectoryName(root.TrimEnd('\\', '/')) ?? "";
                string archives = Path.Combine(files, "client");
                if (Directory.Exists(archives)) source = archives;
                Set("CustomRaces.ClientRootFolder", "\"" + root + "\"");
                string exe = ClientPatch.GameExeOf(root);
                if (exe != null) Set("CustomRaces.ClientExe", "\"" + exe + "\"");
                Set("CustomRaces.HighRaces", "1");
                if (patch != null && RacePacker.IsRacePatch(patch)) { Set("CustomRaces.MoreRacesFile", "\"" + patch + "\""); Set("CustomRaces.MoreRaces", "1"); }
            }
            else
            {
                if (patch != null && RacePacker.IsRacePatch(patch)) { Set("CustomRaces.MoreRacesFile", "\"" + patch + "\""); Set("CustomRaces.MoreRaces", "1"); }
                try { if (RacePacker.ArchivesOf(picked).Count == 0) throw new UserError(L.T("This folder has no game archives (.MPQ) and no files\\client_root.")); }
                catch (UserError) { throw; }
                catch (Exception ex) { throw new UserError(L.T("This folder could not be read:") + " " + ex.Message); }
            }
            return source;
        }

        /// <summary>After the race selection made packs: the settings that follow.</summary>
        public void AfterImport(string target)
        {
            if (Raw("CustomRaces.MoreRacesFile", "").Length == 0)
            {
                string kept = Path.Combine(target, "_Client", "dinput8.dll");
                if (File.Exists(kept) && RacePacker.IsRacePatch(kept)) { Set("CustomRaces.MoreRacesFile", "\"" + kept + "\""); Set("CustomRaces.MoreRaces", "1"); }
            }
            if (Raw("CustomRaces.ImportFolder", "") != target) Set("CustomRaces.ImportFolder", "\"" + target + "\"");
        }

        public string Save()
        {
            HealNumbers(); PruneChosen();
            string name = conf.Name;
            try { conf.Save(); }
            catch (Exception ex) { throw new UserError(L.T("The settings could not be saved:") + "\n" + ex.Message); }
            string warn = null;
            try
            {
                HealNumbers();
                var on = new List<ServerRacials.Wanted>();
                foreach (var key in Chosen())
                {
                    var r = races.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
                    string nm = Raw("CustomRaces." + key + ".Name", "");
                    var wanted = new ServerRacials.Wanted { Key = key, Name = nm.Length > 0 ? nm : r != null ? r.Name : key, CoaRace = r != null ? CoaRaceOf(r) : 0 };
                    var pack = r != null ? PackOf(r) : null;
                    if (pack != null)
                        foreach (var spell in pack.Spells)
                        {
                            object id, row;
                            if (spell.TryGetValue("id", out id) && spell.TryGetValue("row", out row) && row is object[]) wanted.Rows[(uint)Convert.ToInt64(id)] = (object[])row;
                        }
                    on.Add(wanted);
                }
                ServerRacials.Sync("", RacialsFolder, ClientAddons.ClientFolder, on);
                if (Unsaved > 0) conf.Save();
            }
            catch (Exception ex) { warn = L.T("The racial rows could not be written:") + "\n" + ex.Message; }
            return L.F("Saved in {0}.", name) + "\n\n" + L.T("The changes take effect when the worldserver is started the next time (Restart server).") + (warn != null ? "\n\n" + warn : "");
        }
    }
}
