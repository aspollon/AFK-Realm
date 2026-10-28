using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace CoAInstaller
{
    /// <summary>The racial rows a race of the server pack brings. The server data folder holds them as its own dump
    /// (races\racial-spell-records.json); mod-custom-races reads the rows of every race it has turned on from
    /// afk-realm\races\&lt;key&gt;.sql of that folder. This writer keeps those files in step with the races that are on,
    /// and takes the file of a race without racials away, so a race that is turned off leaves its rows behind.</summary>
    static class ServerRacials
    {
        /// <summary>The ported racial abilities mod-custom-races has code for: its script, the spell the script runs, and
        /// the spells its code needs besides (their rows travel with the race as well, see src\CustomRaces.cpp).</summary>
        static readonly string[][] Ported =
        {
            new[] { "spell_race_bull_rush", "110600", "110601" },
        };

        /// <summary>Racials of CoA Custom whose code mod-custom-races has (src\\CustomRaces.cpp) besides the scripts above:
        /// the passives of the Highmountain Tauren, Rugged Tenacity and Waste Not, Want Not.</summary>
        static readonly HashSet<uint> Handled = new HashSet<uint> { 110603, 110605 };

        // CoA Custom's racials, by its race numbers: its server has them compiled in (CoaCustomRacials.h), so
        // mod-custom-races carries the table in its races.json ("coaCustom", tools\coa_custom_racials.py).
        static Dictionary<int, uint[]> coaRacials = new Dictionary<int, uint[]>();
        static HashSet<uint> coaScripted = new HashSet<uint>();

        // CoA Custom's own rows of its racials (data\coa-custom\racials.sql of mod-custom-races): by spell, the values of
        // its spell_dbc row, its spell_script_names (script name) and spell_proc (values).
        static Dictionary<uint, string> coaSpellRows = new Dictionary<uint, string>();
        static Dictionary<uint, string> coaScripts = new Dictionary<uint, string>();
        static Dictionary<uint, string> coaProcs = new Dictionary<uint, string>();

        /// <summary>The racials of CoA Custom's races (race number -> spells), those that need code of its server (none
        /// when the module has that code: ported), and the file of CoA Custom's rows for them.</summary>
        public static void UseCoaCustom(Dictionary<int, uint[]> racials, IEnumerable<uint> scripted, bool ported, string rowsFile)
        {
            coaRacials = racials ?? new Dictionary<int, uint[]>();
            coaScripted = ported ? new HashSet<uint>() : new HashSet<uint>(scripted ?? new uint[0]);
            UseCoaRows(rowsFile);
        }

        /// <summary>The file of CoA Custom's rows of its racials in its server pack (files\sql\10_esteria_racials_*.sql, the
        /// newest), from the CoA Custom folder chosen on the races page (the pack, its files folder or files\client_root);
        /// null when there is none.</summary>
        public static string PackRows(string coaCustomFolder)
        {
            if (string.IsNullOrEmpty(coaCustomFolder)) return null;
            string root = ClientPatch.ClientRootOf(coaCustomFolder);
            string files = root != null ? Path.GetDirectoryName(root.TrimEnd('\\', '/')) : null;
            foreach (var dir in new[] { files == null ? null : Os.Resolve(files, "sql"), Os.Resolve(coaCustomFolder, "files/sql"), Os.Resolve(coaCustomFolder, "sql") })
            {
                if (dir == null || !Directory.Exists(dir)) continue;
                var found = Directory.GetFiles(dir).Where(f => Path.GetFileName(f).IndexOf("esteria_racials", StringComparison.OrdinalIgnoreCase) >= 0 && f.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).LastOrDefault();
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>Reads CoA Custom's rows of its racials from a file of its own (its server pack's, or the copy of
        /// mod-custom-races): spell_dbc, spell_script_names and spell_proc, by spell. With add, the rows go on top of
        /// those read before: the server pack of an older CoA Custom (1.8: 10_esteria_racials_1.8.sql) has no rows of
        /// the races that came later (1.9: Hozen, Sylvari, Centaur and the others), and replacing the module's newer
        /// rows with it left those races without racials.</summary>
        public static void UseCoaRows(string rowsFile, bool add = false)
        {
            if (!add)
            {
                coaSpellRows = new Dictionary<uint, string>();
                coaScripts = new Dictionary<uint, string>();
                coaProcs = new Dictionary<uint, string>();
            }
            if (string.IsNullOrEmpty(rowsFile) || !File.Exists(rowsFile)) return;
            // INSERT INTO <table> ... VALUES then one row per line, "(<id>,...)," up to the last one "(...);"
            string table = null;
            foreach (var raw in File.ReadLines(rowsFile, Encoding.UTF8))
            {
                string line = raw.Trim();
                if (line.StartsWith("INSERT INTO", StringComparison.OrdinalIgnoreCase))
                {
                    var m = System.Text.RegularExpressions.Regex.Match(line, @"^INSERT INTO\s+`?(\w+)`?");
                    table = m.Success ? m.Groups[1].Value.ToLowerInvariant() : null;
                    continue;
                }
                if (!line.StartsWith("(")) { table = null; continue; }
                if (table == null) continue;
                bool last = line.EndsWith(");");
                string row = line.TrimEnd(';', ',');
                var id = System.Text.RegularExpressions.Regex.Match(row, @"^\((\d+)\s*,");
                if (id.Success)
                {
                    uint spell = uint.Parse(id.Groups[1].Value);
                    if (table == "spell_dbc") coaSpellRows[spell] = row;
                    else if (table == "spell_proc") coaProcs[spell] = row;
                    else if (table == "spell_script_names")
                    {
                        var name = System.Text.RegularExpressions.Regex.Match(row, @"'([A-Za-z0-9_]+)'");
                        if (name.Success) coaScripts[spell] = name.Groups[1].Value;
                    }
                }
                if (last) table = null;
            }
        }

        /// <summary>The racial spell ids of a race: as the entry of the server pack writes them (script_grants), or else
        /// those CoA Custom gives the race number it has there (coaRace: its number in CoA Custom's client).</summary>
        public static uint[] Spells(string serverDataFolder, string raceName, int coaRace = 0)
        {
            var grants = Grants(serverDataFolder);
            List<uint> found;
            foreach (var key in Keys(raceName))
                if (grants.TryGetValue(key, out found)) return found.ToArray();
            uint[] coa;
            if (coaRace > 0 && coaRacials.TryGetValue(coaRace, out coa)) return coa.ToArray();
            return new uint[0];
        }

        /// <summary>The racials of a race that do nothing without code of CoA Custom's server (not ported to the module).</summary>
        public static uint[] NeedCode(IEnumerable<uint> spells)
        {
            return spells.Where(s => coaScripted.Contains(s) && !Handled.Contains(s) && !Ported.Any(p => uint.Parse(p[1]) == s || p.Skip(2).Any(x => uint.Parse(x) == s))).Distinct().ToArray();
        }

        /// <summary>The racial spells of a race as far as the client needs them too: the ones the server grants and the
        /// spells its ported scripts need besides (they come as an aura onto their targets, see CustomRaces.cpp).</summary>
        public static uint[] ClientSpells(string serverDataFolder, string raceName, int coaRace = 0)
        {
            var spells = Spells(serverDataFolder, raceName, coaRace).ToList();
            foreach (var ported in Ported)
                if (spells.Contains(uint.Parse(ported[1]))) spells.AddRange(ported.Skip(2).Select(uint.Parse));
            return spells.Distinct().OrderBy(s => s).ToArray();
        }

        /// <summary>A race that is on, for the racial files: its key and name, its number in CoA Custom's client, and the
        /// client rows of spells it brings itself (a race of the race folder: the rows of its pack).</summary>
        public class Wanted
        {
            public string Key, Name;
            public int CoaRace;
            public Dictionary<uint, object[]> Rows = new Dictionary<uint, object[]>();
        }

        /// <summary>Writes the racial file of every race that is on into outDir, and takes the file of a race without racial
        /// rows away. A spell's row comes from the server data folder (its own dump), or else from the client's spell
        /// table: the race's pack, or the game client (client: its folder).</summary>
        public static void Sync(string serverDataFolder, string outDir, string client, List<Wanted> races)
        {
            if (races == null || string.IsNullOrEmpty(outDir)) return;
            var records = string.IsNullOrEmpty(serverDataFolder) || !Directory.Exists(serverDataFolder)
                ? new Dictionary<uint, List<KeyValuePair<string, object>>>() : Records(serverDataFolder);
            var wantedIds = new HashSet<uint>();
            foreach (var race in races) wantedIds.UnionWith(ClientSpells(serverDataFolder, race.Name, race.CoaRace));
            wantedIds.ExceptWith(records.Keys);
            wantedIds.ExceptWith(coaSpellRows.Keys);
            foreach (var race in races) foreach (var id in race.Rows.Keys) wantedIds.Remove(id);
            var fromClient = wantedIds.Count > 0 ? ClientRows(client, wantedIds) : new Dictionary<uint, object[]>();
            Directory.CreateDirectory(outDir);
            foreach (var race in races)
            {
                var all = WithHelpers(ClientSpells(serverDataFolder, race.Name, race.CoaRace));
                var lines = new List<string>();
                var spells = new List<uint>();
                foreach (uint spell in all)
                {
                    object[] row;
                    string coa;
                    string insert = records.ContainsKey(spell) ? Insert("spell_dbc", records[spell])
                        : coaSpellRows.TryGetValue(spell, out coa) ? "INSERT INTO `spell_dbc` VALUES " + coa + ";"
                        : race.Rows.TryGetValue(spell, out row) || fromClient.TryGetValue(spell, out row) ? InsertRow(row) : null;
                    if (insert == null) continue;
                    spells.Add(spell);
                    lines.Add("DELETE FROM `spell_dbc` WHERE `ID` = " + spell + ";");
                    lines.Add(insert);
                }
                string file = Path.Combine(outDir, race.Key + ".sql");
                if (spells.Count == 0) { if (File.Exists(file)) File.Delete(file); continue; }
                lines.Insert(0, "-- spells " + string.Join(" ", spells));
                var needCode = NeedCode(all);
                if (needCode.Length > 0) lines.Insert(1, "-- needs code of CoA Custom's server, does nothing yet: " + string.Join(" ", needCode));
                // CoA Custom's bindings of its racials to their code (the module has it, src\coa_racials) and their proc rows
                foreach (uint spell in spells)
                {
                    string script, proc;
                    if (coaScripts.TryGetValue(spell, out script))
                    {
                        lines.Add("DELETE FROM `spell_script_names` WHERE `spell_id` = " + spell + ";");
                        lines.Add("INSERT INTO `spell_script_names` (`spell_id`, `ScriptName`) VALUES (" + spell + ",'" + script + "');");
                    }
                    if (coaProcs.TryGetValue(spell, out proc))
                    {
                        lines.Add("DELETE FROM `spell_proc` WHERE `SpellId` = " + spell + ";");
                        lines.Add("INSERT INTO `spell_proc` VALUES " + proc + ";");
                    }
                }
                // the scripts stay with the spells the module has ported; other races keep their rows plain
                foreach (var ported in Ported.Where(p => spells.Contains(uint.Parse(p[1])) && !coaScripts.ContainsKey(uint.Parse(p[1]))))
                {
                    lines.Add("DELETE FROM `spell_script_names` WHERE `spell_id` = " + ported[1] + ";");
                    lines.Add("INSERT INTO `spell_script_names` (`spell_id`, `ScriptName`) VALUES (" + ported[1] + ",'" + ported[0] + "');");
                }
                File.WriteAllText(file, string.Join("\r\n", lines) + "\r\n", new UTF8Encoding(false));
            }
        }

        /// <summary>The racials with the spells their code casts besides (CoA Custom numbers them next to the racial,
        /// in blocks of 20: Bull Rush 110600 and its knockdown 110601).</summary>
        static uint[] WithHelpers(uint[] spells)
        {
            var blocks = new HashSet<uint>(spells.Where(coaSpellRows.ContainsKey).Select(s => s / 20));
            return spells.Concat(coaSpellRows.Keys.Where(id => blocks.Contains(id / 20)).OrderBy(id => id)).Distinct().ToArray();
        }

        // The columns of spell_dbc follow the fields of the client's Spell.dbc one to one (234 of them); these are floats,
        // these signed, the texts are named by RaceImport.SpellTextFields, every other one is unsigned.
        static readonly HashSet<int> FloatFields = new HashSet<int> { 47, 77, 78, 79, 101, 102, 103, 119, 120, 121, 216, 217, 218, 229, 230, 231 };
        static readonly HashSet<int> SignedFields = new HashSet<int> { 0, 13, 15, 41, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61, 62, 63, 64, 65, 66, 67,
            68, 69, 70, 74, 75, 76, 80, 81, 82, 110, 111, 112, 113, 114, 115, 224, 228 };

        /// <summary>A row of the client's spell table (texts as text) as a row of spell_dbc.</summary>
        static string InsertRow(object[] row)
        {
            if (row == null || row.Length != RaceImport.SpellFields) return null;
            var values = new string[row.Length];
            for (int i = 0; i < row.Length; i++)
            {
                object v = row[i];
                if (RaceImport.SpellTextFields.Contains(i)) { values[i] = Literal(Convert.ToString(v ?? "")); continue; }
                uint bits = v is string ? 0 : (uint)(Convert.ToInt64(v ?? 0, CultureInfo.InvariantCulture) & 0xFFFFFFFF);
                if (FloatFields.Contains(i)) values[i] = BitConverter.ToSingle(BitConverter.GetBytes(bits), 0).ToString("R", CultureInfo.InvariantCulture);
                else if (SignedFields.Contains(i)) values[i] = unchecked((int)bits).ToString(CultureInfo.InvariantCulture);
                else values[i] = bits.ToString(CultureInfo.InvariantCulture);
            }
            return "INSERT INTO `spell_dbc` VALUES (" + string.Join(", ", values) + ");";
        }

        /// <summary>The rows of the given spells in the game client's spell table (the archive the game reads last wins).</summary>
        static Dictionary<uint, object[]> ClientRows(string client, HashSet<uint> ids)
        {
            var rows = new Dictionary<uint, object[]>();
            if (string.IsNullOrEmpty(client) || ids.Count == 0) return rows;
            string source;
            byte[] bytes;
            try { bytes = ClientPatch.FromClient(client, "DBFilesClient\\Spell.dbc", out source); } catch { return rows; }
            if (bytes == null) return rows;
            var table = DbcTable.Parse(bytes, "Spell.dbc");
            if (table.Fields != RaceImport.SpellFields) return rows;
            foreach (var record in table.Records)
            {
                uint id = table.Field(record, 0);
                if (!ids.Contains(id)) continue;
                var values = new object[table.Fields];
                for (int k = 0; k < table.Fields; k++)
                {
                    uint field = table.Field(record, k);
                    values[k] = RaceImport.SpellTextFields.Contains(k) ? (object)Text(table, field) : field;
                }
                rows[id] = values;
            }
            return rows;
        }

        static string Text(DbcTable t, uint at)
        {
            if (at == 0 || at >= t.Strings.Length) return "";
            int end = (int)at;
            while (end < t.Strings.Length && t.Strings[end] != 0) end++;
            return Encoding.UTF8.GetString(t.Strings, (int)at, end - (int)at);
        }

        /// <summary>A spell record as SQL writes it: it carries its own column names, whoever wrote the dump.</summary>
        static string Insert(string table, List<KeyValuePair<string, object>> fields)
        {
            var cols = new List<string>();
            var values = new List<string>();
            foreach (var field in fields)
            {
                cols.Add("`" + field.Key + "`");
                values.Add(Literal(field.Value));
            }
            return "INSERT INTO `" + table + "` (" + string.Join(", ", cols) + ") VALUES (" + string.Join(", ", values) + ");";
        }

        /// <summary>A value as SQL writes it: a number plain, a text in quotes.</summary>
        static string Literal(object value)
        {
            if (value == null) return "NULL";
            // one statement per line, so the line breaks of a text are written as SQL reads them (\n), never as they are
            if (value is string) return "'" + ((string)value).Replace("\\", "\\\\").Replace("'", "''").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\0", "\\0") + "'";
            if (value is bool) return (bool)value ? "1" : "0";
            if (value is double || value is float || value is decimal) return Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture);
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        // ---------------------------------------------------------------- the race data of the folder

        static string readIn;                        // the folder the last read was taken from
        static Dictionary<string, List<uint>> readGrants;
        static string recordsIn;
        static Dictionary<uint, List<KeyValuePair<string, object>>> readRecords;

        /// <summary>The racial spell ids of the folder's races, by every shape their name may be written in.</summary>
        static Dictionary<string, List<uint>> Grants(string serverDataFolder)
        {
            serverDataFolder = serverDataFolder ?? "";
            if (readGrants != null && readIn == serverDataFolder) return readGrants;
            var map = new Dictionary<string, List<uint>>();
            string dir = Path.Combine(serverDataFolder, "races");
            if (Directory.Exists(dir))
            {
                var json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                foreach (var file in Directory.GetFiles(dir, "*.json"))
                {
                    if (file.EndsWith("racial-spell-records.json", StringComparison.OrdinalIgnoreCase)) continue;
                    object name, spells;
                    try
                    {
                        var entry = json.DeserializeObject(File.ReadAllText(file, Encoding.UTF8)) as Dictionary<string, object>;
                        if (entry == null || !entry.TryGetValue("name", out name) || !entry.TryGetValue("script_grants", out spells)) continue;
                    }
                    catch { continue; }   // what is not a race entry is left out
                    var ids = (spells as object[] ?? new object[0]).Select(s => (uint)Convert.ToInt64(s)).ToList();
                    if (ids.Count == 0) continue;
                    foreach (var key in Keys(name as string).Concat(Keys(Path.GetFileNameWithoutExtension(file))))
                        if (!map.ContainsKey(key)) map[key] = ids;
                }
            }
            readIn = serverDataFolder; readGrants = map;
            return map;
        }

        /// <summary>The spell records of the folder (its overrides of the spell table), by spell id. Records that only
        /// repeat the client's own spell table are left out: their rows already live on the server.</summary>
        static Dictionary<uint, List<KeyValuePair<string, object>>> Records(string serverDataFolder)
        {
            serverDataFolder = serverDataFolder ?? "";
            if (readRecords != null && recordsIn == serverDataFolder) return readRecords;
            var map = new Dictionary<uint, List<KeyValuePair<string, object>>>();
            string file = Path.Combine(serverDataFolder, "races", "racial-spell-records.json");
            if (File.Exists(file))
            {
                var json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                foreach (var item in json.DeserializeObject(File.ReadAllText(file, Encoding.UTF8)) as object[] ?? new object[0])
                {
                    var entry = item as Dictionary<string, object>;
                    object source, id, fields;
                    if (entry == null || !entry.TryGetValue("source", out source) || !(source is string) || (string)source != "spell_dbc override") continue;
                    if (!entry.TryGetValue("spell_id", out id) || !entry.TryGetValue("fields", out fields)) continue;
                    var values = fields as Dictionary<string, object>;
                    if (values != null) map[(uint)Convert.ToInt64(id)] = values.ToList();
                }
            }
            recordsIn = serverDataFolder; readRecords = map;
            return map;
        }

        /// <summary>The shapes a race name may be written in: "Highmountain Tauren", "HighmountainTauren", or a file
        /// name as "46-highmountain-tauren-horde" - all three give one word of letters and digits.</summary>
        static IEnumerable<string> Keys(string text)
        {
            string flat = Flat(text);
            if (flat.Length > 0) yield return flat;
            var parts = (text ?? "").Split(new[] { '-', '_', ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (parts.Count > 0 && parts[0].All(char.IsDigit)) parts.RemoveAt(0);
            if (parts.Count > 0 && (parts[parts.Count - 1].Equals("horde", StringComparison.OrdinalIgnoreCase) || parts[parts.Count - 1].Equals("alliance", StringComparison.OrdinalIgnoreCase))) parts.RemoveAt(parts.Count - 1);
            string bare = Flat(string.Concat(parts));
            if (bare.Length > 0 && bare != flat) yield return bare;
        }

        static string Flat(string text) { return new string((text ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray()); }
    }
}
