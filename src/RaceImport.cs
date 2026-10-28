using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace CoAInstaller
{
    /// <summary>
    /// Races brought in from other game clients, through a race folder: one folder per race with its race.json (its rows
    /// of the client tables, texts as text) and files\ (its models and textures). The rows go into the client patch
    /// (patch-ZZZZZ.MPQ) with new numbers; the files go into an archive of their own for each race (patch-ZY&lt;race&gt;.MPQ),
    /// built once and kept while the race is chosen.
    ///
    /// Also the rules the character creation of the Ascension client needs for the sections (skins, faces, hair,
    /// underwear) of every added race, whether imported or copied from the client's own rows; without them it crashes or
    /// paints the wrong textures:
    ///  - every row playable (flag 1), none for Death Knights only (4) or pre-composed (8) - a Death Knight row that has
    ///    an ordinary twin (same sex, section, style and colour) goes instead;
    ///  - a face and an underwear for every skin colour, and no style without rows (the creation screen reads them
    ///    without checking: 0x004EA77F);
    ///  - female rows for races that have only men (the creation looks them up before the switch to male);
    ///  - the rows of a race and sex in one block, sorted;
    ///  - an HD row has the id of its plain row (the HD layer replaces rows by id).
    /// Found out by ilusixn for CoA Custom (github.com/ilusixn/azerothcore-wotlk-coa).
    /// </summary>
    static class RaceImport
    {
        public const string ArchivePrefix = "patch-ZY";
        // Several races share one archive: one archive per race kept the client loading dozens of MPQ files and
        // made a client with many active races fail to find its WotLK data. Groups keep the count small without
        // building one >2 GB archive.
        const string GroupPrefix = "patch-ZYG";
        const int GroupMaxRaces = 16;                 // small races share an archive; the byte limit splits large ones
        const long GroupMaxBytes = 1500000000;
        const string StampName = "AFK-Realm\\race.txt";
        public const int DisplayBase = 980000;          // display and model ids of imported races: 980000 + race * 2 + sex

        /// <summary>A row of the client's spell table, the 234-field layout this client family (CoA and Descension
        /// alike) has: field 133 shows which icon (a SpellIcon) it wears; 136-151 are its name, 153-168 its subtext
        /// ("Racial"), 170-185 its description and 187-202 its tooltip - each of them 16 language texts - and every
        /// other field is a number. Proofed against both clients' tables field by field.</summary>
        public const int SpellFields = 234, SpellIconField = 133;
        public static readonly HashSet<int> SpellTextFields = new HashSet<int>(
            Enumerable.Range(136, 16).Concat(Enumerable.Range(153, 16)).Concat(Enumerable.Range(170, 16)).Concat(Enumerable.Range(187, 16)));

        public static string ArchiveName(int race) { return ArchivePrefix + race.ToString("00") + ".MPQ"; }

        /// <summary>The archive of a group of races. The name keeps the race prefix so the client patch never
        /// reads it as a source archive and the old per-race archives can be found and removed.</summary>
        public static string GroupName(int group) { return GroupPrefix + group.ToString("00") + ".MPQ"; }

        public static bool IsOurArchive(string fileName)
        {
            return Regex.IsMatch(fileName ?? "", "^" + ArchivePrefix + @"(?:G\d{2}|\d{2,3})\.mpq$", RegexOptions.IgnoreCase);
        }

        /// <summary>
        /// A race of the race folder. Its race.json can be large (tens of megabytes for a race with many skins), so what
        /// the setup page and the checks need of it - names, files, sizes - is kept in a small race.info.json beside it,
        /// made the first time and again when race.json changes; race.json itself is read only when a patch is built.
        /// </summary>
        public class Pack
        {
            public string Key, Dir;
            internal Dictionary<string, object> Info;
            Dictionary<string, object> data;
            public Dictionary<string, object> Data
            {
                get
                {
                    if (data == null)
                        data = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(System.IO.File.ReadAllText(Path.Combine(Dir, "race.json"), Encoding.UTF8)) as Dictionary<string, object>
                            ?? new Dictionary<string, object>();
                    return data;
                }
                internal set { data = value; }
            }
            public string Name { get { return Str(Info, "name"); } }
            public string Prefix { get { return Str(Info, "prefix"); } }
            /// <summary>The race's file name: the folder of its model (Character\VoidElf\...), not the file name of the row
            /// it had in its client, which a client may have reused (Ashen's Void Elf sits in the row of the Fel Orc).</summary>
            public string File { get { string f = Str(Info, "file"); return f.Length > 0 ? f : Key; } }
            public string Icon { get { return Str(Info, "icon"); } }
            /// <summary>The file name of its row in the client it came from (CoA Custom's DrakonidHorde for the Drakonid).</summary>
            public string SourceFile { get { return Str(Info, "sourceFile"); } }
            /// <summary>The files of the pack. The race.info.json beside race.json is only a cache for the page; the
            /// files folder is what the archive is made of. A cache written while the folder was still being copied
            /// (or an older pack whose list was not rewritten) must not leave the archive half empty.</summary>
            public List<string> Files
            {
                get
                {
                    var listed = List(Info, "files").Select(Convert.ToString).Where(f => !string.IsNullOrEmpty(f)).ToList();
                    var disk = FilesOnDisk();
                    if (listed.Count == 0) return disk;
                    var known = new HashSet<string>(listed, StringComparer.OrdinalIgnoreCase);
                    listed.AddRange(disk.Where(f => !known.Contains(f)));
                    return listed;
                }
            }
            /// <summary>The files that are really in the pack's files folder, by their path in the client.</summary>
            public List<string> FilesOnDisk()
            {
                var files = new List<string>();
                string root = Path.Combine(Dir, "files");
                if (!Directory.Exists(root)) return files;
                string top = Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                foreach (var path in Directory.GetFiles(top, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                    files.Add(Path.GetFullPath(path).Substring(top.Length).Replace('/', '\\'));
                return files;
            }
            public List<object[]> Rows(string name) { return List(Data, name).OfType<object[]>().ToList(); }
            /// <summary>The labels of the hair, facial hair and extra-feature buttons of its ChrRaces row (fields 65-67).</summary>
            public List<string> Labels { get { return List(Data, "labels").Select(Convert.ToString).ToList(); } }
            /// <summary>The racial spells the pack carries: its id, its row of the other client's spell table (texts
            /// as text) and the texture of its icon.</summary>
            public List<Dictionary<string, object>> Spells { get { return List(Data, "spells").OfType<Dictionary<string, object>>().ToList(); } }
            /// <summary>A race with a woman only (CoA Custom's Banshee, Harpy and Alpha Goblin): its male body is
            /// missing or broken (the Alpha Goblin's man has no head), so its characters are women.</summary>
            public bool FemaleOnly
            {
                get
                {
                    string marked = Str(Info, "femaleOnly");
                    return marked == "1" || (marked.Length == 0 && KnownFemaleOnly(Key));
                }
            }
            public bool MaleOnly
            {
                get
                {
                    if (FemaleOnly) return false;
                    string marked = Str(Info, "maleOnly");
                    if (marked.Length > 0) return marked == "1";
                    object v;
                    return !(Info.TryGetValue("female", out v) && v is bool && (bool)v);
                }
            }
            /// <summary>The side it had in its client: 0 Alliance, 1 Horde, 2 unknown.</summary>
            public int Side { get { object v; return Info.TryGetValue("side", out v) && v != null ? Convert.ToInt32(v) : 2; } }
            /// <summary>The short name of the client it was packed from ("cc" for CoA Custom; RacePacker.SourceLabel).</summary>
            public string ClientLabel { get { return Str(Info, "client"); } }
            public string Source
            {
                get
                {
                    string c = Str(Info, "client");
                    switch (c.ToLowerInvariant()) { case "cc": return "CoA Custom"; case "ashen": return "Ashen Order"; case "fta": return "From the Ashes"; default: return c; }
                }
            }
            /// <summary>Its race number in the client it was taken from (0 when unknown).</summary>
            public int SourceRace { get { object v; return Info.TryGetValue("race", out v) && v != null ? Convert.ToInt32(v) : 0; } }
            public int Missing { get { object v; return Info.TryGetValue("missing", out v) && v != null ? Convert.ToInt32(v) : 0; } }
            /// <summary>The version of the packer that made it (RacePacker.PackerVersion); 0 for a pack made before the
            /// version was written.</summary>
            public int Packer { get { object v; return Info.TryGetValue("packer", out v) && v != null ? Convert.ToInt32(v) : 0; } }
            /// <summary>What the client it was packed from was then (RacePacker.SourceStamp); empty for older packs.</summary>
            public string SourceStamp { get { return Str(Info, "sourceStamp"); } }
            public long Bytes { get { object v; return Info.TryGetValue("bytes", out v) && v != null ? Convert.ToInt64(v) : 0; } }
        }

        static string Str(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) && v != null ? Convert.ToString(v) : ""; }
        static object[] List(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) ? (v as object[]) ?? (v is System.Collections.ArrayList ? ((System.Collections.ArrayList)v).ToArray() : new object[0]) : new object[0]; }
        static uint Num(object v) { return v == null ? 0 : v is string ? 0 : Convert.ToUInt32(Convert.ToInt64(v) & 0xFFFFFFFF); }
        static string Str(DbcTable t, byte[] row, int field)
        {
            uint at = t.Field(row, field);
            if (at == 0 || at >= t.Strings.Length) return "";
            int end = (int)at;
            while (end < t.Strings.Length && t.Strings[end] != 0) end++;
            return Encoding.UTF8.GetString(t.Strings, (int)at, end - (int)at);
        }

        const string InfoName = "race.info.json";
        const int InfoVersion = 8;      // 5: the client file for custom models, and male-only races; 6: female-only races; 7: packer version; 8: source stamp

        /// <summary>Races whose client tables still carry a female row or display although the game has no
        /// female model. The race overview in the module README lists them as "male only": the client must
        /// not offer the female button for them.</summary>
        public static bool KnownMaleOnly(string key)
        {
            switch (ClientRaces.KeyOf(key).ToLowerInvariant())
            {
                case "felorc": case "broken": case "vrykul": case "tuskarr": case "taunka":
                case "foresttroll": case "icetroll": case "skeleton": case "northrendskeleton":
                case "troglodyte": case "thinhuman": case "gnoll": case "saberon":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Races that are female only although their source has male rows (CoA Custom: the Banshee 90, the Harpy
        /// 56 since 1.8.1, the Alpha Goblin 57 whose man has no head).</summary>
        public static bool KnownFemaleOnly(string key)
        {
            string k = Regex.Replace(key ?? "", "[^A-Za-z]", "").ToLowerInvariant();
            return k.StartsWith("banshee") || k.StartsWith("harpy") || k == "goblina" || k.StartsWith("alphagoblin");
        }

        /// <summary>What the page needs of a race, from its full race.json.</summary>
        static Dictionary<string, object> MakeInfo(string dir, Dictionary<string, object> data, string stamp)
        {
            var models = data.ContainsKey("models") ? data["models"] as Dictionary<string, object> : null;
            var m = models == null ? null : models.Values.OfType<Dictionary<string, object>>().FirstOrDefault();
            var parts = (m == null ? "" : Str(m, "path")).Split('\\');
            // A model under custom\... has no Character\<file> folder. Its source row still has the file name the
            // client looks the race up by (GnollHorde, not Gnoll); that name goes into ChrRaces.ClientFilestring.
            string file = parts.Length > 2 && parts[0].Equals("Character", StringComparison.OrdinalIgnoreCase) ? parts[1] : "";
            if (file.Length == 0) file = Str(data, "sourceFile").Length > 0 ? Str(data, "sourceFile") : Str(data, "file");
            var source = data.ContainsKey("source") ? data["source"] as Dictionary<string, object> : null;
            var files = List(data, "files").Select(Convert.ToString).ToList();
            long bytes = files.Sum(f => { var p = Os.Resolve(Path.Combine(dir, "files"), f); return System.IO.File.Exists(p) ? new FileInfo(p).Length : 0; });
            bool maleOnly = data.ContainsKey("maleOnly") ? Num(data["maleOnly"]) != 0 : KnownMaleOnly(Str(data, "key"));
            bool female = !maleOnly && List(data, "sections").OfType<object[]>().Any(r => r.Length > 3 && Num(r[2]) == 1 && Num(r[3]) == 0);
            object side; data.TryGetValue("side", out side);
            return new Dictionary<string, object>
            {
                { "version", InfoVersion }, { "stamp", stamp }, { "key", Str(data, "key") }, { "name", Str(data, "name") }, { "prefix", Str(data, "prefix") },
                { "file", file }, { "sourceFile", Str(data, "sourceFile").Length > 0 ? Str(data, "sourceFile") : Str(data, "file") }, { "icon", Str(data, "icon") }, { "client", source == null ? "" : Str(source, "client") }, { "race", source == null ? 0 : Num(source.ContainsKey("race") ? source["race"] : null) }, { "side", side ?? 2 },
                { "female", female }, { "maleOnly", maleOnly ? "1" : "0" }, { "packer", data.ContainsKey("packer") ? Num(data["packer"]) : 0 }, { "sourceStamp", Str(data, "sourceStamp") },
                { "femaleOnly", data.ContainsKey("femaleOnly") ? (Num(data["femaleOnly"]) != 0 ? "1" : "0") : (KnownFemaleOnly(Str(data, "key")) ? "1" : "0") }, { "missing", List(data, "missing").Length }, { "bytes", bytes }, { "files", files },
            };
        }

        static readonly Dictionary<string, Pack> cache = new Dictionary<string, Pack>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The races of a race folder, by their key. Read once per change of their race.json.</summary>
        public static List<Pack> Read(string folder)
        {
            var packs = new List<Pack>();
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return packs;
            var json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            foreach (var dir in Directory.GetDirectories(folder).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                string file = Path.Combine(dir, "race.json");
                if (!System.IO.File.Exists(file)) continue;
                var fi = new FileInfo(file);
                string stamp = fi.Length + "/" + fi.LastWriteTimeUtc.Ticks;
                Pack pack;
                lock (cache)
                    if (cache.TryGetValue(dir, out pack) && Str(pack.Info, "stamp") == stamp) { packs.Add(pack); continue; }
                try
                {
                    Dictionary<string, object> info = null, data = null;
                    string infoFile = Path.Combine(dir, InfoName);
                    try
                    {
                        if (System.IO.File.Exists(infoFile)) info = json.DeserializeObject(System.IO.File.ReadAllText(infoFile, Encoding.UTF8)) as Dictionary<string, object>;
                    }
                    catch { info = null; }
                    if (info == null || Str(info, "stamp") != stamp || Str(info, "version") != InfoVersion.ToString())
                    {
                        data = json.DeserializeObject(System.IO.File.ReadAllText(file, Encoding.UTF8)) as Dictionary<string, object>;
                        if (data == null) continue;
                        info = MakeInfo(dir, data, stamp);
                        try { System.IO.File.WriteAllText(infoFile, json.Serialize(info), new UTF8Encoding(false)); } catch { }
                    }
                    pack = new Pack { Key = Str(info, "key"), Dir = dir, Info = info };
                    if (data != null) pack.Data = data;
                    lock (cache) cache[dir] = pack;
                    packs.Add(pack);
                }
                catch { }
            }
            return packs;
        }

        public static Pack Find(string folder, string key)
        {
            return Read(folder).FirstOrDefault(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        }

        // ---------------------------------------------------------------------------------------- the rows

        /// <summary>The names the client knows for the three character-creation style buttons. A row of another client
        /// may spell them in another case ("Normal"); the client compares them as written, so they are put in the one
        /// spelling it expects. A name it does not know would hide the button, and the base race's row would be wrong
        /// for the imported race anyway, so an unknown name falls back to the button without styles.</summary>
        static readonly HashSet<string> RaceLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "NORMAL", "FEATURES", "TUSKS", "PIERCINGS", "MARKINGS", "HAIR", "EARRINGS", "NONE", "HORNS", "MURLOCARMOR",
        };

        static string RaceLabel(string value)
        {
            string label = (value ?? "").Trim().ToUpperInvariant();
            if (label.Length == 0) return "";
            return RaceLabels.Contains(label) ? label : "NORMAL";
        }

        /// <summary>Adds an imported race to the client tables: its race row (the base race's, with its own model, file
        /// name and prefix), models and displays, and its sections, hair and beards with new numbers.</summary>
        /// <summary>Whether an existing client row is the same race as the pack: by the file name of its row, or its
        /// name. A pack may replace the row of the race it was taken from (CoA Custom's Gnoll at 83), but never the
        /// row of another race: 63 is Nightborne in CoA Custom, not Gnoll.</summary>
        static bool SameRace(Pack pack, DbcTable cr, byte[] row)
        {
            string file = Str(cr, row, 11);
            string name = Str(cr, row, 14).Split('|')[0].Trim();
            return (pack.SourceFile.Length > 0 && file.Equals(pack.SourceFile, StringComparison.OrdinalIgnoreCase)) ||
                (pack.File.Length > 0 && file.Equals(pack.File, StringComparison.OrdinalIgnoreCase)) ||
                (pack.Name.Length > 0 && name.Equals(pack.Name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>A duplicate row of a stock race (NightElf2 at 54, Gnome2 at 57, Troll2 at 58). CoA Custom's
        /// Esteria races have their intended numbers there; the copy is replaced by the race, not the race moved
        /// away from its appearance row.</summary>
        static bool StockCopy(DbcTable cr, byte[] row)
        {
            string name = Str(cr, row, 14).Split('|')[0].Trim();
            if (name.Length == 0) return false;
            foreach (int id in ClientRaces.Stock)
            {
                byte[] stock = cr.Records.FirstOrDefault(r => cr.Field(r, 0) == (uint)id);
                if (stock != null && name.Equals(Str(cr, stock, 14).Split('|')[0].Trim(), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public static void AddRace(Func<string, byte[]> load, Action<string, byte[]> save, Pack pack, int race, int baseRace, string name, Action<string> note = null, string helmetPrefix = null)
        {
            var cr = DbcTable.Parse(load("DBFilesClient\\ChrRaces.dbc"), "ChrRaces.dbc");
            var occupied = cr.Records.FirstOrDefault(r => cr.Field(r, 0) == race);
            bool replaceStockCopy = occupied != null && !ClientRaces.Stock.Contains(race) && StockCopy(cr, occupied);
            if (occupied != null && !SameRace(pack, cr, occupied) && !replaceStockCopy)
                throw new InvalidOperationException(L.F("Race number {0} is already {1} in this client; the {2} needs another number.",
                    race, Str(cr, occupied, 14).Split('|')[0].Trim(), name));
            var di = DbcTable.Parse(load("DBFilesClient\\CreatureDisplayInfo.dbc"), "CreatureDisplayInfo.dbc");
            var md = DbcTable.Parse(load("DBFilesClient\\CreatureModelData.dbc"), "CreatureModelData.dbc");
            var baseRow = cr.Records.FirstOrDefault(r => cr.Field(r, 0) == baseRace);
            if (baseRow == null) throw new InvalidOperationException(L.F("The game client has no race {0} for the {1} to lean on.", baseRace, name));
            var displays = ById(di, "CreatureDisplayInfo.dbc", note);
            var models = ById(md, "CreatureModelData.dbc", note);
            var modelsOfPack = pack.Data.ContainsKey("models") ? pack.Data["models"] as Dictionary<string, object> : null;
            var newDisplay = new uint[2];
            for (int sex = 0; sex < 2; sex++)
            {
                var m = modelsOfPack == null ? null : (modelsOfPack.ContainsKey(sex == 0 ? "male" : "female") ? modelsOfPack[sex == 0 ? "male" : "female"] as Dictionary<string, object> : null);
                if (m == null) m = modelsOfPack == null ? null : modelsOfPack.Values.OfType<Dictionary<string, object>>().FirstOrDefault();
                if (m == null) throw new InvalidOperationException(L.F("The race folder of the {0} has no model.", name));
                uint id = (uint)(DisplayBase + race * 2 + sex);
                if (displays.ContainsKey(id) || models.ContainsKey(id))
                    throw new InvalidOperationException(L.F("The game client already uses display or model {0}; the {1} cannot take it.", id, name));
                // the base race's display and model, with the model file and size of the imported race
                byte[] baseDisplay; displays.TryGetValue(cr.Field(baseRow, 4 + sex), out baseDisplay);
                byte[] baseModel = baseDisplay == null ? null : models.ContainsKey(di.Field(baseDisplay, 1)) ? models[di.Field(baseDisplay, 1)] : null;
                if (baseDisplay == null || baseModel == null) throw new InvalidOperationException(L.F("The game client has no model for race {0}.", baseRace));
                var model = (byte[])baseModel.Clone();
                md.SetField(model, 0, id);
                md.SetField(model, 2, md.AddString(Str(m, "path")));
                var src = List(m, "model");
                for (int k = 14; k <= 25 && k < src.Length; k++) md.SetField(model, k, Num(src[k]));     // collision and size
                // the race's own numbers where the base race's would show: flags, size class, scale, footsteps,
                // foley and sounds (its voice and its steps were the base race's before)
                foreach (int k in new[] { 1, 3, 4, 6, 7, 8, 9, 10, 13 })
                    if (k < src.Length && k < md.Fields) md.SetField(model, k, Num(src[k]));
                md.Records.Add(model);
                var display = (byte[])baseDisplay.Clone();
                di.SetField(display, 0, id);
                di.SetField(display, 1, id);
                // and its own display numbers: extended display, scale, opacity, geoset data (the textures stay the
                // base race's: the skins and faces of the race come from its sections)
                var dsrc = List(m, "display");
                foreach (int k in new[] { 3, 4, 5, 14 })
                    if (k < dsrc.Length && k < di.Fields) di.SetField(display, k, Num(dsrc[k]));
                // and its own textures (TextureVariation 1-3) where it has some: texture slots of its model that take them
                // (horns, eyes, a blindfold); race packs made before had none, so the base race's stay
                var own = m.ContainsKey("textures") ? List(m, "textures") : new object[0];
                if (own.Any(t => !string.IsNullOrEmpty(Convert.ToString(t))))
                    for (int k = 0; k < 3 && k < own.Length && 6 + k < di.Fields; k++)
                        di.SetField(display, 6 + k, di.AddString(Convert.ToString(own[k] ?? "")));
                di.Records.Add(display);
                newDisplay[sex] = id;
            }
            var row = (byte[])baseRow.Clone();
            cr.Records.RemoveAll(r => cr.Field(r, 0) == race);
            cr.SetField(row, 0, (uint)race);
            cr.SetField(row, 1, 0xC);                       // playable
            cr.SetField(row, 4, newDisplay[0]);
            cr.SetField(row, 5, newDisplay[1]);
            // the prefix the client finds the helmets of the race by: its own, or the base race's when it has no helmets
            cr.SetField(row, 6, cr.AddString(!string.IsNullOrEmpty(helmetPrefix) ? helmetPrefix : pack.Prefix.Length > 0 ? pack.Prefix : "Hu"));
            cr.SetField(row, 11, cr.AddString(pack.File.Length > 0 ? pack.File : pack.Key));
            // the three style buttons (hair, facial hair, extra features): its source row's names, not the base race's
            var labels = pack.Labels;
            if (cr.Fields > 67 && labels.Count >= 3)
                for (int k = 0; k < 3; k++) cr.SetField(row, 65 + k, cr.AddString(RaceLabel(labels[k])));
            foreach (int k in new[] { 14, 31, 48 }) cr.SetField(row, k, cr.AddString(name));
            cr.Records.Add(row);
            save("DBFilesClient\\ChrRaces.dbc", cr.ToBytes());
            save("DBFilesClient\\CreatureDisplayInfo.dbc", di.ToBytes());
            save("DBFilesClient\\CreatureModelData.dbc", md.ToBytes());

            // the voice of the race: its source's own rows where it has some, else the base race's
            AddVoice(load, save, pack, race, baseRace);

            // the client rows of its racial spells: a client that never had them knows nothing of the spells the
            // server grants the race - no name, no icon, and nothing on the targets their effects land on
            AddSpells(load, save, pack, name, note);

            // the character window of this client paints a background per race: a race it never had has none, and
            // the window complains when it opens. The race takes the picture of the race it leans on.
            AddRaceFrame(load, save, pack, cr, baseRow, name, note);

            // sections: plain rows and their HD rows with the same new ids
            var cs = DbcTable.Parse(load("DBFilesClient\\CharSections.dbc"), "CharSections.dbc");
            var hd = DbcTable.Parse(load("DBFilesClient\\HDCharSections.dbc"), "HDCharSections.dbc");
            cs.Records.RemoveAll(r => cs.Field(r, 1) == race);
            hd.Records.RemoveAll(r => hd.Field(r, 1) == race);
            uint next = Math.Max(MaxId(cs), MaxId(hd)) + 1;
            Func<object[], string> key = r => Num(r[2]) + "/" + Num(r[3]) + "/" + Num(r[8]) + "/" + Num(r[9]);
            var plain = pack.Rows("sections");
            var hdRows = pack.Rows("hd_sections");
            var idOf = new Dictionary<string, uint>();
            foreach (var r in plain.OrderBy(r => Num(r[2])).ThenBy(r => Num(r[3])).ThenBy(r => Num(r[8])).ThenBy(r => Num(r[9])))
            {
                string k = key(r);
                if (idOf.ContainsKey(k)) continue;
                idOf[k] = next;
                cs.Records.Add(Section(cs, r, next++, race));
            }
            // a race without HD rows of its own keeps none: the client draws it with its plain rows, the way the client
            // the pack was made from drew it. Writing the plain rows into the HD table laid their old-layout textures
            // on the HD model (skew, parts missing) for races whose source client has no HD table at all
            var hdIds = new HashSet<uint>(hd.Records.Select(x => hd.Field(x, 0)));
            // A plain row without an HD row of its own, in a race that has HD rows: its texture is laid out for the old
            // model and lands askew on the HD one (eyes and mouth in the wrong place: the first skin colours of CoA
            // Custom's Undead Gnome). It takes the HD row of the same style in the nearest colour, else of the nearest style.
            var hdUse = new List<object[]>(hdRows);
            if (hdRows.Count > 0)
            {
                var hdKeys = new HashSet<string>(hdRows.Select(key));
                foreach (var r in plain)
                {
                    if (hdKeys.Contains(key(r))) continue;
                    var near = hdRows.Where(h => Num(h[2]) == Num(r[2]) && Num(h[3]) == Num(r[3]))
                        .OrderBy(h => Num(h[8]) == Num(r[8]) ? 0 : 1)
                        .ThenBy(h => Math.Abs((long)Num(h[9]) - Num(r[9])))
                        .ThenBy(h => Math.Abs((long)Num(h[8]) - Num(r[8]))).FirstOrDefault();
                    if (near == null) continue;
                    var copy = (object[])near.Clone();
                    copy[8] = r[8]; copy[9] = r[9];
                    hdUse.Add(copy);
                    hdKeys.Add(key(copy));
                }
            }
            if (hdRows.Count > 0)
            {
                foreach (var r in hdUse)
                {
                    string k = key(r);
                    uint id;
                    if (!idOf.TryGetValue(k, out id)) { id = next++; idOf[k] = id; }
                    if (!hdIds.Add(id)) continue;
                    hd.Records.Add(Section(hd, r, id, race));
                }
            }
            save("DBFilesClient\\CharSections.dbc", cs.ToBytes());
            save("DBFilesClient\\HDCharSections.dbc", hd.ToBytes());

            // hair geosets (by id, paired with the HD table), beards (no id) and their HD rows
            CopyRows(load, save, "CharHairGeosets", "HDCharHairGeosets", pack.Rows("hair_geosets"), pack.Rows("hd_hair_geosets"), race, 1, r => Num(r[2]) + "/" + Num(r[3]));
            var fh = DbcTable.Parse(load("DBFilesClient\\CharacterFacialHairStyles.dbc"), "CharacterFacialHairStyles.dbc");
            fh.Records.RemoveAll(r => fh.Field(r, 0) == race);
            foreach (var r in pack.Rows("facial_hair"))
            {
                var rec = new byte[fh.RecordSize];
                for (int k = 0; k < fh.Fields && k < r.Length; k++) fh.SetField(rec, k, Num(r[k]));
                fh.SetField(rec, 0, (uint)race);
                fh.Records.Add(rec);
            }
            save("DBFilesClient\\CharacterFacialHairStyles.dbc", fh.ToBytes());
            var hfh = load("DBFilesClient\\HDCharacterFacialHairStyles.dbc");
            if (hfh != null)
            {
                var t = DbcTable.Parse(hfh, "HDCharacterFacialHairStyles.dbc");
                t.Records.RemoveAll(r => t.Field(r, 1) == race);
                uint n = MaxId(t) + 1;
                foreach (var r in pack.Rows("hd_facial_hair"))
                {
                    var rec = new byte[t.RecordSize];
                    for (int k = 0; k < t.Fields && k < r.Length; k++) t.SetField(rec, k, Num(r[k]));
                    t.SetField(rec, 0, n++);
                    t.SetField(rec, 1, (uint)race);
                    t.Records.Add(rec);
                }
                save("DBFilesClient\\HDCharacterFacialHairStyles.dbc", t.ToBytes());
            }

            // The camera rows of the race's classes: without them the customize screen keeps the camera of the
            // class shown before, and a custom model can fill the whole view or show its background. Older race
            // packs do not carry them; its base race's rows are the safe stand-in then.
            var cameraRows = pack.Rows("ui_camera");
            var cameraBytes = load("DBFilesClient\\UICameraAppearanceChrRaces.dbc");
            if (cameraBytes != null)
            {
                var camera = DbcTable.Parse(cameraBytes, "UICameraAppearanceChrRaces.dbc");
                camera.Records.RemoveAll(r => camera.Field(r, 1) == race);
                uint cameraId = MaxId(camera) + 1;
                if (cameraRows.Count > 0)
                {
                    foreach (var r in cameraRows)
                    {
                        var rec = new byte[camera.RecordSize];
                        for (int k = 0; k < camera.Fields && k < r.Length; k++) camera.SetField(rec, k, Num(r[k]));
                        camera.SetField(rec, 0, cameraId++);
                        camera.SetField(rec, 1, (uint)race);
                        camera.Records.Add(rec);
                    }
                }
                else
                {
                    foreach (var donor in camera.Records.Where(r => camera.Field(r, 1) == baseRace).ToList())
                    {
                        var rec = (byte[])donor.Clone();
                        camera.SetField(rec, 0, cameraId++);
                        camera.SetField(rec, 1, (uint)race);
                        camera.Records.Add(rec);
                    }
                }
                save("DBFilesClient\\UICameraAppearanceChrRaces.dbc", camera.ToBytes());
            }

            // hair textures: rows of numbers only, under the race's new numbers (its hair pointed at the base
            // race's textures before)
            var htBytes = load("DBFilesClient\\CharHairTextures.dbc");
            var htRows = pack.Rows("hair_textures");
            if (htBytes != null && htRows.Count > 0)
            {
                var ht = DbcTable.Parse(htBytes, "CharHairTextures.dbc");
                ht.Records.RemoveAll(r => ht.Field(r, 1) == race);
                uint n = MaxId(ht) + 1;
                foreach (var r in htRows)
                {
                    var rec = new byte[ht.RecordSize];
                    for (int k = 0; k < ht.Fields && k < r.Length; k++) ht.SetField(rec, k, Num(r[k]));
                    ht.SetField(rec, 0, n++);
                    ht.SetField(rec, 1, (uint)race);
                    ht.Records.Add(rec);
                }
                save("DBFilesClient\\CharHairTextures.dbc", ht.ToBytes());
            }
        }

        /// <summary>Moves existing races of the game client to other numbers: "62:64 63:65". Used for a race of the
        /// client that is not played and whose number an imported race needs (Earthen/Human Cultist beside the
        /// Lightforged/Nightborne of another client). Only the client tables are changed; Stock races never.</summary>
        public static void MoveClientRaces(Func<string, byte[]> load, Action<string, byte[]> save, string moves, Action<string> note = null)
        {
            var pairs = new List<KeyValuePair<int, int>>();
            foreach (string word in (moves ?? "").Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] p = word.Split(':');
                int from, to;
                if (p.Length != 2 || !int.TryParse(p[0], out from) || !int.TryParse(p[1], out to) ||
                    from < 12 || from > 127 || to < 12 || to > 127 || from == to)
                    throw new InvalidOperationException(L.F("The race number move \"{0}\" is not \"old:new\" (12 to 127).", word));
                pairs.Add(new KeyValuePair<int, int>(from, to));
            }
            if (pairs.Count == 0) return;
            var tables = new[]
            {
                new { Name = "ChrRaces", Field = 0 }, new { Name = "CharSections", Field = 1 },
                new { Name = "HDCharSections", Field = 1 }, new { Name = "CharHairGeosets", Field = 1 },
                new { Name = "HDCharHairGeosets", Field = 1 }, new { Name = "CharacterFacialHairStyles", Field = 0 },
                new { Name = "HDCharacterFacialHairStyles", Field = 1 }, new { Name = "CharHairTextures", Field = 1 },
                new { Name = "VocalUISounds", Field = 2 }, new { Name = "EmotesTextSound", Field = 2 },
                new { Name = "HDEmotesTextSound", Field = 2 }, new { Name = "CharStartOutfit", Field = 0 },
                new { Name = "UICameraAppearanceChrRaces", Field = 0 },
            };
            foreach (var table in tables)
            {
                byte[] bytes = load("DBFilesClient\\" + table.Name + ".dbc");
                if (bytes == null) continue;
                var t = DbcTable.Parse(bytes, table.Name + ".dbc");
                int moved = 0;
                foreach (var pair in pairs)
                    foreach (var row in t.Records)
                        if (t.Field(row, table.Field) == (uint)pair.Key)
                        {
                            t.SetField(row, table.Field, (uint)pair.Value);
                            moved++;
                        }
                if (moved == 0) continue;
                save("DBFilesClient\\" + table.Name + ".dbc", t.ToBytes());
                if (note != null) note(L.F("{0}: {1} race row(s) moved", table.Name, moved));
            }
        }

        static byte[] Section(DbcTable t, object[] r, uint id, int race)
        {
            var rec = new byte[t.RecordSize];
            t.SetField(rec, 0, id);
            t.SetField(rec, 1, (uint)race);
            t.SetField(rec, 2, Num(r[2]));
            t.SetField(rec, 3, Num(r[3]));
            // an empty texture must point at a NUL byte: offset 0 of Ascension's tables is the Human male skin
            for (int k = 4; k <= 6; k++) t.SetField(rec, k, t.AddString(r[k] as string ?? ""));
            t.SetField(rec, 7, Num(r[7]));
            t.SetField(rec, 8, Num(r[8]));
            t.SetField(rec, 9, Num(r[9]));
            return rec;
        }

        static void CopyRows(Func<string, byte[]> load, Action<string, byte[]> save, string table, string hdTable, List<object[]> rows, List<object[]> hdRows,
                             int race, int raceField, Func<object[], string> key)
        {
            var t = DbcTable.Parse(load("DBFilesClient\\" + table + ".dbc"), table);
            var hb = load("DBFilesClient\\" + hdTable + ".dbc");
            var h = hb == null ? null : DbcTable.Parse(hb, hdTable);
            t.Records.RemoveAll(r => t.Field(r, raceField) == race);
            if (h != null) h.Records.RemoveAll(r => h.Field(r, raceField) == race);
            uint next = Math.Max(MaxId(t), h == null ? 0 : MaxId(h)) + 1;
            var idOf = new Dictionary<string, uint>();
            foreach (var r in rows)
            {
                var rec = new byte[t.RecordSize];
                for (int k = 0; k < t.Fields && k < r.Length; k++) t.SetField(rec, k, Num(r[k]));
                idOf[key(r)] = next;
                t.SetField(rec, 0, next++);
                t.SetField(rec, raceField, (uint)race);
                t.Records.Add(rec);
            }
            save("DBFilesClient\\" + table + ".dbc", t.ToBytes());
            if (h == null) return;
            foreach (var r in hdRows)
            {
                var rec = new byte[h.RecordSize];
                for (int k = 0; k < h.Fields && k < r.Length; k++) h.SetField(rec, k, Num(r[k]));
                uint id;
                if (!idOf.TryGetValue(key(r), out id)) id = next++;
                h.SetField(rec, 0, id);
                h.SetField(rec, raceField, (uint)race);
                h.Records.Add(rec);
            }
            save("DBFilesClient\\" + hdTable + ".dbc", h.ToBytes());
        }

        /// <summary>The voice rows of a race: the rows its source brought with it, for the sounds this client has, or,
        /// without any, its base race's rows. VocalUISounds is the voice of the UI (clicks, "your inventory is full"),
        /// EmotesTextSound and its HD table are the text emotes (/silly, /flirt and the like). Every row a copy under
        /// the race's number with a new id; without any of this the race is mute.</summary>
        static void AddVoice(Func<string, byte[]> load, Action<string, byte[]> save, Pack pack, int race, int baseRace)
        {
            // the sounds of its own voice the race pack brings (rows of the sound table this client lacks), so that the rows
            // below that name them are kept
            var soundRows = pack.Rows("sound_entries");
            if (soundRows.Count > 0)
            {
                var sb = load("DBFilesClient\\SoundEntries.dbc");
                var se = sb == null ? null : DbcTable.Parse(sb, "SoundEntries.dbc");
                if (se != null)
                {
                    var have = new HashSet<uint>(se.Records.Select(r => se.Field(r, 0)));
                    int added = 0;
                    foreach (var r in soundRows)
                    {
                        uint id = Num(r[0]);
                        if (id == 0 || have.Contains(id) || r.Length != se.Fields) continue;
                        var rec = new byte[se.RecordSize];
                        for (int k = 0; k < se.Fields; k++)
                            se.SetField(rec, k, r[k] is string ? se.AddString((string)r[k]) : Num(r[k]));
                        se.Records.Add(rec);
                        have.Add(id);
                        added++;
                    }
                    if (added > 0) save("DBFilesClient\\SoundEntries.dbc", se.ToBytes());
                }
            }
            HashSet<uint> sounds = null;
            Func<uint, bool> hasSound = id =>
            {
                if (id == 0 || id == 0xFFFFFFFF) return true;
                if (sounds == null)
                {
                    sounds = new HashSet<uint>();
                    var bytes = load("DBFilesClient\\SoundEntries.dbc");
                    var st = bytes == null ? null : DbcTable.Parse(bytes, "SoundEntries.dbc");
                    if (st != null) foreach (var r in st.Records) sounds.Add(st.Field(r, 0));
                }
                return sounds.Contains(id);
            };
            foreach (var table in new[]
            {
                new { Name = "VocalUISounds", Key = "vocal_ui_sounds", SoundFields = new[] { 3, 4 } },
                new { Name = "EmotesTextSound", Key = "emotes_text_sound", SoundFields = new[] { 4 } },
                new { Name = "HDEmotesTextSound", Key = "hd_emotes_text_sound", SoundFields = new[] { 4 } },
            })
            {
                var bytes = load("DBFilesClient\\" + table.Name + ".dbc");
                if (bytes == null) continue;
                var t = DbcTable.Parse(bytes, table.Name + ".dbc");
                t.Records.RemoveAll(r => t.Field(r, 2) == race);
                uint next = MaxId(t) + 1;
                int added = 0;
                // the race's own rows, kept only while this client has every sound they name (a sound missing here
                // would make it mute with the wrong voice's silence); rows of the race's own source, never the base's
                foreach (var r in pack.Rows(table.Key))
                    if (table.SoundFields.All(f => f >= r.Length || hasSound(Num(r[f]))))
                    {
                        var rec = new byte[t.RecordSize];
                        for (int k = 0; k < t.Fields && k < r.Length; k++) t.SetField(rec, k, Num(r[k]));
                        t.SetField(rec, 0, next++);
                        t.SetField(rec, 2, (uint)race);
                        t.Records.Add(rec);
                        added++;
                    }
                if (added == 0)
                    foreach (var r in t.Records.Where(r => t.Field(r, 2) == baseRace).ToList())
                    {
                        var rec = (byte[])r.Clone();
                        t.SetField(rec, 0, next++);
                        t.SetField(rec, 2, (uint)race);
                        t.Records.Add(rec);
                    }
                save("DBFilesClient\\" + table.Name + ".dbc", t.ToBytes());
            }
        }

        /// <summary>The client rows of a race's racial spells: into the client's spell table (with new strings) and
        /// their icons into its icon table. A picture this client already lists keeps its number; only a picture it
        /// does not have gets a new row, so the rows of imported races stay apart from the client's own. Both tables
        /// are written every time: the patched archive of this build must hold them, not only the grown one.</summary>
        public static void AddSpells(Func<string, byte[]> load, Action<string, byte[]> save, Pack pack, string name, Action<string> note)
        {
            var spells = pack.Spells;
            if (spells.Count == 0) return;
            var t = DbcTable.Parse(load("DBFilesClient\\Spell.dbc"), "Spell.dbc");
            if (t.Fields != SpellFields)
            {
                if (note != null) note(L.F("The spell table of your client ({0} fields) is not the 234-field kind the racial spells of the {1} come in; they stay out.", t.Fields, name));
                return;
            }
            var iconBytes = load("DBFilesClient\\SpellIcon.dbc");
            if (iconBytes == null)
            {
                if (note != null) note(L.F("Your game client has no SpellIcon.dbc, so the racial spells of the {0} cannot get their icons and stay out.", name));
                return;
            }
            var icons = DbcTable.Parse(iconBytes, "SpellIcon.dbc");
            var iconOf = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in icons.Records)
            {
                string path = Str(icons, r, 1);
                if (path.Length > 0 && !iconOf.ContainsKey(path)) iconOf[path] = icons.Field(r, 0);
            }
            uint nextIcon = MaxId(icons) + 1;
            var ids = new HashSet<uint>(spells.Select(s => Num(s["id"])));
            t.Records.RemoveAll(r => ids.Contains(t.Field(r, 0)));
            int added = 0, iconsAdded = 0, skipped = 0;
            foreach (var spell in spells)
            {
                var values = List(spell, "row");
                if (values.Length != SpellFields) { skipped++; continue; }
                var row = new byte[t.RecordSize];
                for (int k = 0; k < SpellFields; k++)
                    t.SetField(row, k, SpellTextFields.Contains(k) ? t.AddString(Convert.ToString(values[k]) ?? "") : Num(values[k]));
                string texture = Str(spell, "icon");
                if (texture.Length > 0)
                {
                    uint iconId;
                    if (!iconOf.TryGetValue(texture, out iconId))
                    {
                        var irec = new byte[icons.RecordSize];
                        icons.SetField(irec, 0, iconId = nextIcon++);
                        icons.SetField(irec, 1, icons.AddString(texture));
                        icons.Records.Add(irec);
                        iconOf[texture] = iconId;
                        iconsAdded++;
                    }
                    t.SetField(row, SpellIconField, iconId);
                }
                t.Records.Add(row);
                added++;
            }
            save("DBFilesClient\\Spell.dbc", t.ToBytes());
            save("DBFilesClient\\SpellIcon.dbc", icons.ToBytes());
            if (note != null)
            {
                note(L.F("The racial spells of the {0}: {1} rows of the spell table and {2} new icon rows.", name, added, iconsAdded));
                if (skipped > 0) note(L.F("{0} rows of the {1}'s racial spells were not the right shape and stayed out.", skipped, name));
            }
        }
        static uint MaxId(DbcTable t) { return t.Records.Count == 0 ? 0 : t.Records.Max(r => t.Field(r, 0)); }

        /// <summary>The rows of a table by their id, the first row of an id a table names twice (custom clients do);
        /// read the same way as the races (RacePacker.Races) and the client's tables (ClientRaces).</summary>
        static Dictionary<uint, byte[]> ById(DbcTable t, string label, Action<string> note)
        {
            var byId = new Dictionary<uint, byte[]>();
            int twice = 0;
            foreach (var r in t.Records)
            {
                uint id = t.Field(r, 0);
                if (byId.ContainsKey(id)) { twice++; continue; }
                byId[id] = r;
            }
            if (twice > 0 && note != null) note(L.F("{0} has {1} rows with an id used more than once; the first of each is used.", label, twice));
            return byId;
        }

        // ---------------------------------------------------------------------------------------- the rules

        /// <summary>The sections of a race put in order when the race check found them wrong: one row for each sex,
        /// section, style and colour (an ordinary one before a Death Knight one, the lower id before the higher), and only
        /// HD rows that belong to a plain row of the same look. SectionRules then adds what is missing.</summary>
        public static void StrictSections(Func<string, byte[]> load, Action<string, byte[]> save, int race)
        {
            var csBytes = load("DBFilesClient\\CharSections.dbc");
            if (csBytes == null) return;
            var cs = DbcTable.Parse(csBytes, "CharSections.dbc");
            Func<DbcTable, byte[], string> key = (t, r) => t.Field(r, 2) + "/" + t.Field(r, 3) + "/" + t.Field(r, 8) + "/" + t.Field(r, 9);
            var keep = new HashSet<byte[]>(cs.Records.Where(r => cs.Field(r, 1) == race).GroupBy(r => key(cs, r))
                .Select(g => g.OrderBy(r => (cs.Field(r, 7) & 12u) != 0 ? 1 : 0).ThenBy(r => cs.Field(r, 0)).First()));
            cs.Records.RemoveAll(r => cs.Field(r, 1) == race && !keep.Contains(r));
            save("DBFilesClient\\CharSections.dbc", cs.ToBytes());
            var hdBytes = load("DBFilesClient\\HDCharSections.dbc");
            if (hdBytes == null) return;
            var hd = DbcTable.Parse(hdBytes, "HDCharSections.dbc");
            var plain = keep.ToDictionary(r => cs.Field(r, 0), r => key(cs, r));
            var seen = new HashSet<uint>();
            hd.Records.RemoveAll(r =>
            {
                if (hd.Field(r, 1) != race) return false;
                string k;
                uint id = hd.Field(r, 0);
                return !plain.TryGetValue(id, out k) || k != key(hd, r) || !seen.Add(id);
            });
            save("DBFilesClient\\HDCharSections.dbc", hd.ToBytes());
        }

        static void DropSpecialTwins(DbcTable t, int race)
        {
            Func<byte[], string> key = r => t.Field(r, 2) + "/" + t.Field(r, 3) + "/" + t.Field(r, 8) + "/" + t.Field(r, 9);
            Func<byte[], bool> special = r => (t.Field(r, 7) & 12u) != 0;
            var ordinary = new HashSet<string>(t.Records.Where(r => t.Field(r, 1) == race && !special(r)).Select(key));
            t.Records.RemoveAll(r => t.Field(r, 1) == race && special(r) && ordinary.Contains(key(r)));
        }

        /// <summary>The rules of the character creation for the sections of one added race (see the class summary).</summary>
        public static void SectionRules(Func<string, byte[]> load, Action<string, byte[]> save, int race, bool maleOnly, Action<string> note = null, bool femaleOnly = false)
        {
            var cs = DbcTable.Parse(load("DBFilesClient\\CharSections.dbc"), "CharSections.dbc");
            var hdBytes = load("DBFilesClient\\HDCharSections.dbc");
            var hd = hdBytes == null ? null : DbcTable.Parse(hdBytes, "HDCharSections.dbc");
            // Rows only for Death Knights (flag 4) or pre-composed (8) next to an ordinary row of the same sex, section,
            // style and colour go: a stock race carries a Death Knight face (glowing eyes, scars) for every ordinary one,
            // under the same numbers. Made playable below, a race copied from it (the High Elf from the Blood Elf) showed
            // the Death Knight face and, with the HD rows paired to the wrong one, the eyes beside the eyes. Only a row
            // that has no ordinary twin is kept and made playable.
            DropSpecialTwins(cs, race);
            if (hd != null) DropSpecialTwins(hd, race);
            var mine = cs.Records.Where(r => cs.Field(r, 1) == race).ToList();
            if (mine.Count == 0) return;
            uint next = Math.Max(MaxId(cs), hd == null ? 0 : MaxId(hd)) + 1;
            var added = new List<byte[]>();
            Func<byte[], uint, uint, byte[]> clone = (r, field, value) =>
            {
                var c = (byte[])r.Clone();
                cs.SetField(c, 0, next++);
                cs.SetField(c, (int)field, value);
                added.Add(c);
                return c;
            };
            // men only: the women get the men's rows
            var sexes = new HashSet<uint>(mine.Where(r => cs.Field(r, 3) == 0).Select(r => cs.Field(r, 2)));
            // women only (the Banshee, the Harpy): the men get the women's rows the same way
            if (maleOnly || femaleOnly || sexes.Count == 1)
            {
                uint only = femaleOnly && sexes.Contains(1) ? 1u : sexes.Contains(0) ? 0u : 1u;
                cs.Records.RemoveAll(r => cs.Field(r, 1) == race && cs.Field(r, 2) != only);
                mine = cs.Records.Where(r => cs.Field(r, 1) == race).ToList();
                foreach (var r in mine.Where(r => cs.Field(r, 3) < 5).ToList()) clone(r, 2, 1 - only);
                cs.Records.AddRange(added); mine.AddRange(added); added.Clear();
            }
            // a face and an underwear for each skin colour; no style without rows
            foreach (uint sex in new uint[] { 0, 1 })
            {
                var skins = mine.Where(r => cs.Field(r, 2) == sex && cs.Field(r, 3) == 0 && cs.Field(r, 8) == 0).Select(r => cs.Field(r, 9)).ToList();
                if (skins.Count == 0) continue;
                uint colours = skins.Max() + 1;
                foreach (uint sec in new uint[] { 1, 2, 3, 4 })
                {
                    var rows = mine.Where(r => cs.Field(r, 2) == sex && cs.Field(r, 3) == sec).ToList();
                    if (rows.Count == 0) continue;
                    // by style and colour, the first row of each: a client's table repeats rows now and then
                    var styles = new Dictionary<uint, Dictionary<uint, byte[]>>();
                    int twice = 0;
                    foreach (var r in rows)
                    {
                        Dictionary<uint, byte[]> style;
                        if (!styles.TryGetValue(cs.Field(r, 8), out style)) styles[cs.Field(r, 8)] = style = new Dictionary<uint, byte[]>();
                        if (style.ContainsKey(cs.Field(r, 9))) { twice++; continue; }
                        style[cs.Field(r, 9)] = r;
                    }
                    if (twice > 0 && note != null) note(L.F("CharSections.dbc of the added race has {0} rows with a style and colour used more than once; the first of each is used.", twice));
                    uint top = styles.Keys.Max();
                    for (uint v = 0; v <= top; v++)
                        if (!styles.ContainsKey(v))
                        {
                            var copy = styles[styles.Keys.Min()].Values.Select(r => clone(r, 8, v)).ToList();
                            styles[v] = copy.ToDictionary(r => cs.Field(r, 9));
                        }
                    if (sec != 1 && sec != 4) continue;
                    foreach (var style in styles.Values.ToList())
                        for (uint c = 0; c < colours; c++)
                            if (!style.ContainsKey(c))
                            {
                                var near = style.Keys.Where(k => k < c).DefaultIfEmpty(style.Keys.Min()).Max();
                                style[c] = clone(style[near], 9, c);
                            }
                }
            }
            cs.Records.AddRange(added);
            // flags and order
            foreach (var r in cs.Records.Where(r => cs.Field(r, 1) == race && cs.Field(r, 3) < 5))
                cs.SetField(r, 7, (cs.Field(r, 7) | 1u) & ~4u & ~8u);
            var block = cs.Records.Where(r => cs.Field(r, 1) == race)
                .OrderBy(r => cs.Field(r, 2)).ThenBy(r => cs.Field(r, 3)).ThenBy(r => cs.Field(r, 8)).ThenBy(r => cs.Field(r, 9)).ToList();
            cs.Records.RemoveAll(r => cs.Field(r, 1) == race);
            cs.Records.AddRange(block);
            save("DBFilesClient\\CharSections.dbc", cs.ToBytes());
            if (hd == null) return;
            // HD rows of the race: the id of the plain row with the same race, sex, section, style and colour
            var idOf = block.GroupBy(r => cs.Field(r, 2) + "/" + cs.Field(r, 3) + "/" + cs.Field(r, 8) + "/" + cs.Field(r, 9)).ToDictionary(g => g.Key, g => cs.Field(g.First(), 0));
            // An HD row that already has the id of a plain row of the race keeps it: the copy made them pairs by id, and
            // two rows of the same numbers (an ordinary face and its Death Knight twin) must not both be paired by numbers
            // to the first plain row. Only the others (rows of new plain clones) are paired by sex, section, style and
            // colour, and only where that plain row has no HD row yet; what is left over goes.
            var raceIds = new HashSet<uint>(block.Select(r => cs.Field(r, 0)));
            var mineHd = hd.Records.Where(r => hd.Field(r, 1) == race).ToList();
            var paired = new HashSet<uint>(mineHd.Where(r => raceIds.Contains(hd.Field(r, 0))).Select(r => hd.Field(r, 0)));
            var orphans = new HashSet<byte[]>();
            foreach (var r in mineHd)
            {
                hd.SetField(r, 7, (hd.Field(r, 7) | 1u) & ~4u & ~8u);
                if (raceIds.Contains(hd.Field(r, 0))) continue;
                uint id;
                if (idOf.TryGetValue(hd.Field(r, 2) + "/" + hd.Field(r, 3) + "/" + hd.Field(r, 8) + "/" + hd.Field(r, 9), out id) && paired.Add(id))
                    hd.SetField(r, 0, id);
                else
                    orphans.Add(r);
            }
            var seen = new HashSet<uint>();
            hd.Records.RemoveAll(r => hd.Field(r, 1) == race && (orphans.Contains(r) || !seen.Add(hd.Field(r, 0))));
            save("DBFilesClient\\HDCharSections.dbc", hd.ToBytes());
        }

        /// <summary>The character window of this client paints a background per race and knows none for a race it
        /// never had (the window then complains when it opens). The race takes the line of the race it leans on.</summary>
        static void AddRaceFrame(Func<string, byte[]> load, Action<string, byte[]> save, Pack pack, DbcTable cr, byte[] baseRow, string name, Action<string> note)
        {
            var bytes = load("Interface\\SharedXML\\AtlasInfo.lua");
            if (bytes == null) return;                          // a client without that window has none to paint
            string file = pack.File.Length > 0 ? pack.File : pack.Key;
            string baseFile = Str(cr, baseRow, 11);
            bool added, orphan;
            bytes = FrameAtlas(bytes, file, baseFile, out added, out orphan);
            if (note != null && added) note(L.F("The background of the character window for the {0} (taken from the {1}).", name, baseFile));
            if (note != null && orphan) note(L.F("This client paints its character window per race and has no background for the {0}: the window will keep complaining.", name));
            save("Interface\\SharedXML\\AtlasInfo.lua", bytes);
        }

        /// <summary>Adds a line for a race to the atlas list of a client's character window (the
        /// Interface\SharedXML\AtlasInfo.lua), a copy of the line of the race it leans on. Says whether a line was
        /// added or the base race has none at all.</summary>
        public static byte[] FrameAtlas(byte[] bytes, string file, string baseFile, out bool added, out bool orphan)
        {
            added = false; orphan = false;
            string text = Encoding.UTF8.GetString(bytes);
            string wanted = "[\"DressUpBackground-" + file + "\"]";
            if (file.Length == 0 || text.IndexOf(wanted, StringComparison.Ordinal) >= 0) return bytes;
            string from = "[\"DressUpBackground-" + baseFile + "\"]";
            int at = baseFile.Length == 0 ? -1 : text.IndexOf(from, StringComparison.Ordinal);
            int start = at < 0 ? 0 : text.LastIndexOf('\n', at) + 1;
            int end = at < 0 ? -1 : text.IndexOf('\n', at);
            if (end < 0) { orphan = true; return bytes; }
            string line = text.Substring(start, end - start);
            bool crlf = line.EndsWith("\r");
            if (crlf) line = line.Substring(0, line.Length - 1);
            added = true;
            return Encoding.UTF8.GetBytes(text.Insert(end + 1, line.Replace(from, wanted) + (crlf ? "\r\n" : "\n")));
        }

        // ---------------------------------------------------------------------------------------- the files

        public class Wanted { public int Race; public Pack Pack; }

        static long PackBytes(Wanted w)
        {
            long bytes = 0;
            foreach (var f in w.Pack.FilesOnDisk())
            {
                string p = Os.Resolve(Path.Combine(w.Pack.Dir, "files"), f);
                try { if (File.Exists(p)) bytes += new FileInfo(p).Length; } catch { }
            }
            return bytes;
        }

        /// <summary>Groups the races into a few archives, four races or about 1.5 GB each. A client with many
        /// active races otherwise loads one MPQ per race and can lose the base WotLK archives.</summary>
        public static List<List<Wanted>> Groups(IEnumerable<Wanted> wanted)
        {
            var groups = new List<List<Wanted>>();
            var current = new List<Wanted>();
            long bytes = 0;
            foreach (var w in (wanted ?? Enumerable.Empty<Wanted>()).OrderBy(w => w.Race))
            {
                long size = PackBytes(w);
                if (current.Count > 0 && (current.Count >= GroupMaxRaces || bytes + size > GroupMaxBytes))
                {
                    groups.Add(current);
                    current = new List<Wanted>();
                    bytes = 0;
                }
                current.Add(w);
                bytes += size;
            }
            if (current.Count > 0) groups.Add(current);
            return groups;
        }

        /// <summary>A stamp of what an archive of a race holds: its race number and its files with their sizes and times.</summary>
        static string Stamp(Wanted w)
        {
            var sb = new StringBuilder();
            sb.Append("race ").Append(w.Race).Append(' ').Append(w.Pack.Key).Append('\n');
            foreach (var f in w.Pack.FilesOnDisk())
            {
                var p = Os.Resolve(Path.Combine(w.Pack.Dir, "files"), f);
                var fi = new FileInfo(p);
                sb.Append(f).Append(' ').Append(fi.Length).Append(' ').Append(fi.LastWriteTimeUtc.Ticks).Append('\n');
            }
            using (var sha = SHA256.Create())
                return "Made by " + Product.Name + " for race " + w.Race + " (" + w.Pack.Key + ").\r\nContents " +
                    BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()))).Replace("-", "").Substring(0, 16) + "\r\n";
        }

        static string InstalledStamp(string path)
        {
            if (!File.Exists(path)) return null;
            try { using (var mpq = new MpqArchive(path)) { var b = mpq.Read(StampName); return b == null ? "" : Encoding.UTF8.GetString(b); } }
            catch { return ""; }
        }

        /// <summary>The stamp of a group: the stamps of all races in it, in order.</summary>
        static string GroupStamp(List<Wanted> group)
        {
            var sb = new StringBuilder();
            foreach (var w in group) sb.Append(Stamp(w));
            using (var sha = SHA256.Create())
                return "Made by " + Product.Name + " for race group " +
                    string.Join(",", group.Select(w => w.Race)) + ".\r\nContents " +
                    BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()))).Replace("-", "").Substring(0, 16) + "\r\n";
        }

        /// <summary>Whether the archives of the imported races in the game folder are as they should be.</summary>
        public static bool Current(string client, List<Wanted> wanted)
        {
            string data = Os.Resolve(client, "Data");
            var groups = Groups(wanted);
            for (int i = 0; i < groups.Count; i++)
                if (InstalledStamp(Path.Combine(data, GroupName(i))) != GroupStamp(groups[i])) return false;
            var names = new HashSet<string>(groups.Select((g, i) => GroupName(i)), StringComparer.OrdinalIgnoreCase);
            return !Directory.Exists(data) || !Directory.GetFiles(data).Select(Path.GetFileName).Any(n => IsOurArchive(n) && !names.Contains(n) && InstalledStamp(Path.Combine(data, n)) != "");
        }

        /// <summary>Builds the archives the imported races need and removes those of races no longer chosen.</summary>
        public static List<string> Sync(string client, List<Wanted> wanted) { return Sync(client, wanted, null); }

        /// <summary>The same, reporting each race and how far its archive is (text, step, steps).</summary>
        public static List<string> Sync(string client, List<Wanted> wanted, Action<string, int, int> progress)
        {
            var notes = new List<string>();
            string data = Os.Resolve(client, "Data");
            Directory.CreateDirectory(data);
            var groups = Groups(wanted);
            for (int gi = 0; gi < groups.Count; gi++)
            {
                var group = groups[gi];
                string name = GroupName(gi);
                string path = Path.Combine(data, name);
                string stamp = GroupStamp(group);
                if (InstalledStamp(path) == stamp) continue;
                if (InstalledStamp(path) == "") throw new InvalidOperationException(L.F("Data\\{0} in the game folder was not made by {1}. Rename or remove it, then install again.", name, Product.Name));
                var files = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var w in group)
                    foreach (var f in w.Pack.FilesOnDisk())
                    {
                        var p = Os.Resolve(Path.Combine(w.Pack.Dir, "files"), f);
                        if (File.Exists(p)) files[f] = p;
                    }
                string temp = path + ".new";
                string what = L.F("Writing the files of {0} races (Data\\{1})", group.Count, name);
                if (progress != null) progress(what + " …", 0, files.Count + 2);
                MpqArchive.WriteFromFiles(temp, files, new Dictionary<string, byte[]> { { StampName, Encoding.UTF8.GetBytes(stamp) } },
                    progress == null ? null : (Action<int, int>)((done, all) => progress(what + ": " + done + " of " + all + " files", done, all)));
                try
                {
                    if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
                }
                catch (IOException)
                {
                    try { File.Delete(temp); } catch { }
                    throw new InvalidOperationException(L.F("Data\\{0} is in use. Close the game and its launcher first.", name));
                }
                notes.Add(name + " (" + string.Join(", ", group.Select(w => w.Pack.Name)) + ")");
            }
            var names = new HashSet<string>(groups.Select((g, i) => GroupName(i)), StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(data))
                foreach (var path in Directory.GetFiles(data).Where(p => IsOurArchive(Path.GetFileName(p)) && !names.Contains(Path.GetFileName(p))))
                    if (!string.IsNullOrEmpty(InstalledStamp(path)))
                        try { File.Delete(path); notes.Add(Path.GetFileName(path) + " removed"); } catch (IOException) { }
            return notes;
        }

        /// <summary>The archive of a race, for the ZIP of the other players.</summary>
        public static string BuildTemp(Wanted w) { return BuildGroupTemp(new List<Wanted> { w }); }

        /// <summary>The archive of a group of races, for the ZIP of the other players.</summary>
        public static string BuildGroupTemp(List<Wanted> group)
        {
            var files = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var w in group)
                foreach (var f in w.Pack.FilesOnDisk())
                {
                    var p = Os.Resolve(Path.Combine(w.Pack.Dir, "files"), f);
                    if (File.Exists(p)) files[f] = p;
                }
            string temp = Path.GetTempFileName();
            MpqArchive.WriteFromFiles(temp, files, new Dictionary<string, byte[]> { { StampName, Encoding.UTF8.GetBytes(GroupStamp(group)) } });
            return temp;
        }
    }
}
