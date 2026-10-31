using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CoAInstaller
{
    /// <summary>A race of the game client that mod-custom-races can make playable.</summary>
    class ClientRace
    {
        public int Source;                  // its row in ChrRaces.dbc
        public int Side;                    // 0 Alliance, 1 Horde, 2 unknown (ChrRaces field 13)
        public string Key, Name, Prefix, File;
        public uint MaleDisplay, FemaleDisplay;
        public bool MaleOnly;
        public bool FemaleOnly;             // women only: no man of its own (CoA Custom's Banshee, Harpy, Alpha Goblin)
        public bool Virtual;                // made from the row of another race, under its own name (the High Elf)
        public bool Playable;               // the client's race table marks it for character creation (flags 4 and 8)
        public bool HasHelmets;             // the client has helmet models for its prefix (Item\ObjectComponents\Head\*_<prefix><sex>.m2)
        public RaceImport.Pack Pack;        // a race of the user's race folder, not of this client
        public RaceImport.Pack Alt;         // the same race in the race folder, for a race this client has too
        /// <summary>The game of CoA Custom (CustomRaces.HighRaces) knows this race under its own number above 31: it keeps it.</summary>
        public bool KeepsNumber;
        public bool OwnNumber { get { return Pack == null && !Virtual && (Source < 32 || KeepsNumber); } }
    }

    /// <summary>
    /// The races of a game client: read from its ChrRaces.dbc, as the game reads it (the archive it reads last wins),
    /// without the patch AFK Realm builds. A race counts when it is none of the stock races (nor a copy of one under the
    /// same name, as the HD rows of the CoA client are) and the client has its model. A race whose female has no skins
    /// is male only.
    /// </summary>
    static class ClientRaces
    {
        public static readonly int[] Stock = { 1, 2, 3, 4, 5, 6, 7, 8, 10, 11 };

        public class Scan
        {
            public List<ClientRace> Races = new List<ClientRace>();
            public List<int> FreeNumbers = new List<int>();      // race numbers below 32 no row has
            public List<int> MoreNumbers = new List<int>();      // race numbers 32 to 63 no row has (with the 64-race patch)
            public List<int> HighNumbers = new List<int>();      // race numbers 64 to 127 no row has (CoA Custom's game)
            /// <summary>Every race number the client's ChrRaces.dbc has a row for, including stock copies and
            /// NPC rows that do not appear in Races. An imported race must not take one of these.</summary>
            public HashSet<int> Reserved = new HashSet<int>();
            /// <summary>Rows whose name is that of a stock race but whose row is not the stock one (NightElf2
            /// at 54, Gnome2 at 57, Troll2 at 58 in the CoA client). They are not offered as races, but a custom
            /// race with that intended number may replace the copy.</summary>
            public HashSet<int> StockCopies = new HashSet<int>();
            public bool ModelsChecked;
            public HashSet<string> HelmetPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            /// <summary>The helmets of the client by prefix and sex ("HuM", "VuF"): the names of the helmet models
            /// (Helm_Plate_D_02 of Helm_Plate_D_02_HuM.m2), for how many of them a prefix has.</summary>
            public Dictionary<string, HashSet<string>> HelmetStems = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        }

        static readonly string[] StockHelmetPrefixes = { "Hu", "Or", "Dw", "Ni", "Sc", "Ta", "Gn", "Tr", "Be", "Dr" };
        static readonly Regex HelmetFile = new Regex(@"^item[\\/]objectcomponents[\\/]head[\\/](.+)_([a-z]{2})([mf])\.m2$", RegexOptions.IgnoreCase);

        /// <summary>
        /// Whether a prefix has (nearly) every helmet the stock races have, in this client and the given files of a race
        /// pack. The game finds the helmet of an item by the prefix of the race (Helm_X_&lt;prefix&gt;&lt;M|F&gt;.m2); one that is
        /// missing is drawn as the blue and white placeholder cube. A race that brings helmet models for some items only
        /// (the Vulpera of Esteria, a Pandaren of Eunoia) shows that cube for all the others - the CoA items above all,
        /// which only the stock races have. Such a race wears the helmets of its base race instead, as CoA Custom does it.
        /// Without a list of the client's files (an old scan) every prefix counts as complete, as before.
        /// </summary>
        public static bool FullHelmets(Scan scan, string prefix, bool maleOnly, IEnumerable<string> more, bool femaleOnly = false)
        {
            if (scan == null || scan.HelmetStems.Count == 0 || string.IsNullOrEmpty(prefix)) return true;
            var extra = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in more ?? Enumerable.Empty<string>())
            {
                var m = HelmetFile.Match(f ?? "");
                if (!m.Success) continue;
                string key = m.Groups[2].Value + m.Groups[3].Value.ToUpperInvariant();
                HashSet<string> set;
                if (!extra.TryGetValue(key, out set)) extra[key] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                set.Add(m.Groups[1].Value);
            }
            foreach (var sex in maleOnly ? new[] { "M" } : femaleOnly ? new[] { "F" } : new[] { "M", "F" })
            {
                // the stock race with the most helmets of this sex is the measure
                HashSet<string> measure = null;
                foreach (var p in StockHelmetPrefixes)
                {
                    HashSet<string> s;
                    if (scan.HelmetStems.TryGetValue(p + sex, out s) && (measure == null || s.Count > measure.Count)) measure = s;
                }
                if (measure == null || measure.Count == 0) continue;
                HashSet<string> own, added;
                scan.HelmetStems.TryGetValue(prefix + sex, out own);
                extra.TryGetValue(prefix + sex, out added);
                int have = measure.Count(stem => (own != null && own.Contains(stem)) || (added != null && added.Contains(stem)));
                if (have < measure.Count * 0.97) return false;
            }
            return true;
        }

        sealed class Archives : IDisposable
        {
            readonly List<MpqArchive> list = new List<MpqArchive>();   // the one the game reads last first
            public Archives(string client)
            {
                foreach (var path in Enumerable.Reverse(ClientPatch.ClientArchives(client)))
                    try { list.Add(new MpqArchive(path)); } catch { }
            }
            public byte[] Read(string name)
            {
                foreach (var a in list) if (a.Contains(name)) return a.Read(name);
                return null;
            }
            public bool Has(string name) { return list.Any(a => a.Contains(name)); }
            /// <summary>The names the archives list of themselves ((listfile)).</summary>
            public IEnumerable<string> Listed()
            {
                foreach (var a in list)
                {
                    byte[] bytes;
                    try { bytes = a.Read("(listfile)"); } catch { continue; }
                    if (bytes == null) continue;
                    foreach (var line in Encoding.UTF8.GetString(bytes).Split('\n')) yield return line.Trim();
                }
            }
            /// <summary>The base archives of the game (common, expansion, lichking) are there: a missing model is missing.</summary>
            public bool Complete { get { return list.Any(a => Regex.IsMatch(System.IO.Path.GetFileName(a.Path), @"^(common|common-2|expansion|lichking)\.mpq$", RegexOptions.IgnoreCase)); } }
            public void Dispose() { foreach (var a in list) a.Dispose(); }
        }

        internal static string Str(DbcTable t, byte[] row, int field)
        {
            uint at = t.Field(row, field);
            if (at >= t.Strings.Length) return "";
            int end = (int)at;
            while (end < t.Strings.Length && t.Strings[end] != 0) end++;
            return Encoding.UTF8.GetString(t.Strings, (int)at, end - (int)at);
        }

        public static string KeyOf(string name) { return Regex.Replace(name ?? "", "[^A-Za-z0-9]", ""); }

        static readonly HashSet<string> HordeBases = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "Orc", "Undead", "Tauren", "Troll", "BloodElf" };

        /// <summary>The faction of a race (0 Alliance, 1 Horde, 2 unknown), from its pack or its client row.</summary>
        public static int SideOf(ClientRace r)
        {
            if (r == null) return 2;
            if (r.Pack != null) return r.Pack.Side;
            if (r.Alt != null) return r.Alt.Side;
            return r.Side;
        }

        /// <summary>Whether a base race belongs to the race's faction. A dual-faction race must not keep the
        /// Alliance base chosen for its other row.</summary>
        public static bool BaseFitsSide(int side, string baseRace)
        {
            if (side != 0 && side != 1) return true;
            bool horde = HordeBases.Contains(baseRace ?? "");
            return side == 1 ? horde : !horde;
        }

        /// <summary>
        /// The key of a race row: the name, except when another row of the same client has that name already. Then the
        /// row's file name keeps them apart (Vulpera and VulperaEsteria). A file name that is itself the name of another
        /// row (the Murloc's row is called Troglodyte) would collide with that row, so the race number keeps it apart
        /// instead. reserved holds the name keys of all rows, used or not: an unused row's name must not be taken over.
        /// </summary>
        public static string KeyFor(string name, string file, int id, HashSet<string> used, HashSet<string> reserved)
        {
            string nameKey = KeyOf(name);
            if (nameKey.Length == 0) return "";
            string fileKey = KeyOf(file);
            string key = nameKey;
            if (used.Contains(key))
            {
                bool safeFile = fileKey.Length > 0 && !fileKey.Equals(nameKey, StringComparison.OrdinalIgnoreCase) &&
                    !used.Contains(fileKey) && !reserved.Contains(fileKey);
                key = safeFile ? fileKey : nameKey + id;
            }
            string baseKey = key;
            for (int n = 2; used.Contains(key); n++) key = baseKey + n.ToString();
            return key;
        }

        /// <summary>
        /// The name shown for a row whose name another row of the same client shares (two Vulpera, two Earthen).
        /// What the file name adds to the name is the best suffix (VulperaEsteria -> Esteria, EarthenHorde -> Horde,
        /// PandarenA -> A); a one-letter suffix is not worth much, and the side is clearer there. Without one, the
        /// side, then the race number, keeps the entries apart.
        /// </summary>
        public static string DisplayNameFor(string name, string file, int side, int id)
        {
            string nameKey = KeyOf(name), fileKey = KeyOf(file);
            string suffix = "";
            if (fileKey.Length > nameKey.Length && fileKey.StartsWith(nameKey, StringComparison.OrdinalIgnoreCase))
                suffix = fileKey.Substring(nameKey.Length);
            else if (fileKey.Length > nameKey.Length && fileKey.EndsWith(nameKey, StringComparison.OrdinalIgnoreCase))
                suffix = fileKey.Substring(0, fileKey.Length - nameKey.Length);
            if (suffix.Length <= 1 && (side == 0 || side == 1)) suffix = side == 0 ? "Alliance" : "Horde";
            if (suffix.Length == 0 && (side == 0 || side == 1)) suffix = side == 0 ? "Alliance" : "Horde";
            if (suffix.Length == 0) suffix = id.ToString();
            return name + " " + suffix;
        }

        /// <summary>The races of the client in the given folder. virtuals: races made from another row (key -> source, name).</summary>
        static readonly Dictionary<string, Scan> scans = new Dictionary<string, Scan>();

        /// <summary>The archives of a client as a key: they and their sizes and times (a read again only when they change).</summary>
        public static string ArchivesKey(string client)
        {
            var sb = new StringBuilder(client ?? "");
            foreach (var path in ClientPatch.ClientArchives(client))
            {
                var fi = new FileInfo(path);
                sb.Append('|').Append(fi.Name).Append(':').Append(fi.Length).Append(':').Append(fi.LastWriteTimeUtc.Ticks);
            }
            return sb.ToString();
        }

        public static Scan Read(string client, IDictionary<string, KeyValuePair<int, string>> virtuals)
        {
            string key = ArchivesKey(client) + "#" + string.Join(",", (virtuals ?? new Dictionary<string, KeyValuePair<int, string>>()).Select(v => v.Key + "=" + v.Value.Key + "/" + v.Value.Value));
            Scan cached;
            lock (scans) if (scans.TryGetValue(key, out cached)) return Copy(cached);
            var made = ReadNow(client, virtuals);
            lock (scans) { scans.Clear(); scans[key] = made; }
            return Copy(made);
        }

        /// <summary>A copy the setup page may change (it adds races of the race folder to its list).</summary>
        static Scan Copy(Scan s)
        {
            return new Scan { Races = s.Races.Select(r => new ClientRace { Source = r.Source, Side = r.Side, Key = r.Key, Name = r.Name, Prefix = r.Prefix, File = r.File,
                MaleDisplay = r.MaleDisplay, FemaleDisplay = r.FemaleDisplay, MaleOnly = r.MaleOnly, FemaleOnly = r.FemaleOnly, Virtual = r.Virtual, Playable = r.Playable, HasHelmets = r.HasHelmets }).ToList(),
                FreeNumbers = s.FreeNumbers.ToList(), MoreNumbers = s.MoreNumbers.ToList(), HighNumbers = s.HighNumbers.ToList(), ModelsChecked = s.ModelsChecked,
                Reserved = new HashSet<int>(s.Reserved), StockCopies = new HashSet<int>(s.StockCopies),
                HelmetPrefixes = new HashSet<string>(s.HelmetPrefixes, StringComparer.OrdinalIgnoreCase), HelmetStems = s.HelmetStems };
        }

        static Scan ReadNow(string client, IDictionary<string, KeyValuePair<int, string>> virtuals)
        {
            var scan = new Scan();
            using (var archives = new Archives(client))
            {
                var bytes = archives.Read("DBFilesClient\\ChrRaces.dbc");
                if (bytes == null) throw new InvalidOperationException(L.T("The game client has no ChrRaces.dbc. Is this the game folder of Conquest of Azeroth?"));
                var races = DbcTable.Parse(bytes, "ChrRaces.dbc");
                // by race number, the first row of a number a custom client names twice (as RacePacker.Races reads it)
                var rows = races.Records.GroupBy(r => (int)races.Field(r, 0)).ToDictionary(g => g.Key, g => g.First());
                for (int n = 1; n < 32; n++) if (!rows.ContainsKey(n)) scan.FreeNumbers.Add(n);
                for (int n = 32; n < 64; n++) if (!rows.ContainsKey(n)) scan.MoreNumbers.Add(n);
                for (int n = 64; n < 128; n++) if (!rows.ContainsKey(n)) scan.HighNumbers.Add(n);

                // Models: display -> model -> its file in the archives (only when the client's base archives are there).
                scan.ModelsChecked = archives.Complete;
                Dictionary<uint, uint> displayModel = null; Dictionary<uint, string> modelFile = null;
                if (scan.ModelsChecked)
                {
                    try
                    {
                        var di = DbcTable.Parse(archives.Read("DBFilesClient\\CreatureDisplayInfo.dbc"), "CreatureDisplayInfo.dbc");
                        var md = DbcTable.Parse(archives.Read("DBFilesClient\\CreatureModelData.dbc"), "CreatureModelData.dbc");
                        displayModel = new Dictionary<uint, uint>();
                        foreach (var r in di.Records) displayModel[di.Field(r, 0)] = di.Field(r, 1);
                        modelFile = new Dictionary<uint, string>();
                        foreach (var r in md.Records) modelFile[md.Field(r, 0)] = Str(md, r, 2);
                    }
                    catch { scan.ModelsChecked = false; }
                }
                Func<uint, bool> hasModel = display =>
                {
                    if (!scan.ModelsChecked) return true;
                    uint model; string file;
                    if (!displayModel.TryGetValue(display, out model) || !modelFile.TryGetValue(model, out file) || file.Length == 0) return false;
                    file = Regex.Replace(file, @"\.(mdx|mdl)$", ".m2", RegexOptions.IgnoreCase);
                    return archives.Has(file);
                };

                // Helmets are a model for each race and sex, named after the race's prefix: a race of the client without its
                // own shows a placeholder for every helmet, and wears those of another race instead.
                foreach (var name in archives.Listed())
                {
                    var m = HelmetFile.Match(name);
                    if (!m.Success) continue;
                    scan.HelmetPrefixes.Add(m.Groups[2].Value);
                    string key = m.Groups[2].Value + m.Groups[3].Value.ToUpperInvariant();
                    HashSet<string> stems;
                    if (!scan.HelmetStems.TryGetValue(key, out stems)) scan.HelmetStems[key] = stems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    stems.Add(m.Groups[1].Value);
                }
                if (!scan.ModelsChecked || scan.HelmetPrefixes.Count == 0)
                    foreach (var p in new[] { "Hu", "Or", "Dw", "Ni", "Sc", "Ta", "Gn", "Tr", "Be", "Dr", "Go" }) scan.HelmetPrefixes.Add(p);

                // Skins of each race and sex: a sex with hardly any has no body of its own.
                var skins = new Dictionary<int, int>();
                var sections = archives.Read("DBFilesClient\\CharSections.dbc");
                if (sections != null)
                {
                    var cs = DbcTable.Parse(sections, "CharSections.dbc");
                    foreach (var r in cs.Records)
                    {
                        int k = (int)cs.Field(r, 1) * 2 + (int)cs.Field(r, 2);
                        int c; skins.TryGetValue(k, out c); skins[k] = c + 1;
                    }
                }
                Func<int, int, int> skinsOf = (race, sex) => { int c; return skins.TryGetValue(race * 2 + sex, out c) ? c : 0; };

                var stockNames = new HashSet<string>(Stock.Where(rows.ContainsKey).Select(id => Str(races, rows[id], 14)), StringComparer.OrdinalIgnoreCase);
                scan.Reserved = new HashSet<int>(rows.Keys);
                foreach (int id in rows.Keys)
                    if (!Stock.Contains(id) && stockNames.Contains(Str(races, rows[id], 14).Split('|')[0].Trim()))
                        scan.StockCopies.Add(id);
                Func<int, string, string, ClientRace> make = (id, key, name) =>
                {
                    var row = rows[id];
                    var r = new ClientRace { Source = id, Side = (int)races.Field(row, 13), Key = key, Name = name, Prefix = Str(races, row, 6), File = Str(races, row, 11),
                        MaleDisplay = races.Field(row, 4), FemaleDisplay = races.Field(row, 5), Playable = (races.Field(row, 1) & 0xC) == 0xC };
                    // a race with a female body and at least one female section has a woman: some custom races
                    // bring only one plain row per section (the Esteria looks), not the ten rows of a stock race
                    r.MaleOnly = r.FemaleDisplay == r.MaleDisplay || r.FemaleDisplay == 0 || !hasModel(r.FemaleDisplay) ||
                        skinsOf(id, 1) == 0 || RaceImport.KnownMaleOnly(key);
                    r.FemaleOnly = (skinsOf(id, 0) == 0 && skinsOf(id, 1) > 0) || RaceImport.KnownFemaleOnly(r.File) || RaceImport.KnownFemaleOnly(key);
                    if (r.FemaleOnly) r.MaleOnly = false;
                    r.HasHelmets = scan.HelmetPrefixes.Contains(r.Prefix) && FullHelmets(scan, r.Prefix, r.MaleOnly, null, r.FemaleOnly);
                    return r;
                };
                // The player's client can carry NPC rows too, and a race the module made playable may have any flags;
                // this list is about the rows a client has, not about which of them the game offers. The other
                // client's candidates are filtered in RacePacker.Races, where the player rows are chosen.
                var eligible = new List<int>();
                foreach (var id in rows.Keys.OrderBy(i => i))
                {
                    if (Stock.Contains(id)) continue;
                    var row = rows[id];
                    string name = Str(races, row, 14).Trim();
                    if (name.Length == 0 || stockNames.Contains(name) || KeyOf(name).Length == 0) continue;     // a copy of a stock race
                    if (!hasModel(races.Field(row, 4))) continue;                                              // no body
                    eligible.Add(id);
                }
                // Every row's name is reserved, also that of an NPC row: the Murloc's file is called Troglodyte,
                // which is the name of a row of its own, and taking it would mix the two races up.
                var allNames = new HashSet<string>(rows.Values.Select(r => KeyOf(Str(races, r, 14).Trim())).Where(k => k.Length > 0), StringComparer.OrdinalIgnoreCase);
                var nameCounts = eligible.GroupBy(id => KeyOf(Str(races, rows[id], 14).Trim()), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var id in eligible)
                {
                    var row = rows[id];
                    string name = Str(races, row, 14).Trim();
                    string file = Str(races, row, 11);
                    string key = KeyFor(name, file, id, used, allNames);
                    used.Add(key);
                    int count;
                    nameCounts.TryGetValue(KeyOf(name), out count);
                    string shown = count > 1 ? DisplayNameFor(name, file, (int)races.Field(row, 13), id) : name;
                    scan.Races.Add(make(id, key, shown));
                }
                foreach (var v in virtuals ?? new Dictionary<string, KeyValuePair<int, string>>())
                {
                    if (!rows.ContainsKey(v.Value.Key) || scan.Races.Any(r => r.Key.Equals(v.Key, StringComparison.OrdinalIgnoreCase))) continue;
                    var made = make(v.Value.Key, v.Key, v.Value.Value);
                    made.Virtual = true;
                    scan.Races.Add(made);
                }
            }
            return scan;
        }
    }
}
