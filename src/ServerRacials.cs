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

        /// <summary>The helper spells the 1.9.8 Kyrian and Venthyr scripts cast. The client carries most of
        /// them, but their server rows are dummy rows; the core scripts turn the dummy spell into the
        /// effect. CoA's table only names the racial itself, so add these rows to the race file as well.</summary>
        static readonly Dictionary<uint, uint[]> ScriptHelpers = new Dictionary<uint, uint[]>
        {
            { 111100, new[] { 111101u, 111103u, 111109u, 111111u, 111112u, 111113u } }, // Kyrian Hover / Charge
            { 111200, new[] { 111205u, 111207u, 111210u, 111211u, 111212u } },           // Venthyr Feast / Hover / Bat
        };

        /// <summary>Rows of Qiraji's login appearance; they travel with its racial file, but are not learned
        /// (mod-custom-races casts them at login for male Qiraji).</summary>
        static readonly Dictionary<int, uint[]> LoginHelpers = new Dictionary<int, uint[]>
        {
            { 88, new[] { 13990151u } },
        };

        /// <summary>The 1.9.8 scripts that are compiled into the current server build.</summary>
        static readonly Dictionary<uint, string> ScriptBindings = new Dictionary<uint, string>
        {
            { 111100, "spell_kyrian_hover" },
            { 111101, "spell_kyrian_hover_wings" },
            { 111110, "spell_kyrian_ascendant_charge" },
            { 111200, "spell_venthyr_crimson_feast" },
            { 111203, "spell_venthyr_anima_hunger" },
            { 111206, "spell_venthyr_racial" },
            { 111207, "spell_venthyr_hover_height" },
            { 111209, "spell_venthyr_racial" },
            { 111210, "spell_venthyr_bat_form" },
            { 14001032, "spell_gen_cannibalize" }, // Grummle Trail Snack, same corpse script as Cannibalize
            { 15000914, "spell_titan_aegis" },     // 1.9.9 Titan: Aegis of the Keepers
        };

        /// <summary>The racials of CoA Custom's races (race number -> spells), those that need code of its server (none
        /// when the module has that code: ported), and the file of CoA Custom's rows for them.</summary>
        public static void UseCoaCustom(Dictionary<int, uint[]> racials, IEnumerable<uint> scripted, bool ported, string rowsFile)
        {
            coaRacials = racials ?? new Dictionary<int, uint[]>();
            coaScripted = ported ? new HashSet<uint>() : new HashSet<uint>(scripted ?? new uint[0]);
            UseCoaRows(rowsFile);
        }

        /// <summary>All client Spell.dbc ranks of the form "&lt;Race&gt; Racial", with one spell per name. CoA gives
        /// Kyrian and Venthyr their own rank; a future CoA race with the same convention is picked up here as well.
        /// Internal helper rows can be filtered by name.</summary>
        public static Dictionary<string, uint[]> ClientRankGroups(string client, Func<string, bool> skipName)
        {
            var result = new Dictionary<string, uint[]>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(client)) return result;
            try
            {
                string source;
                byte[] bytes = ClientPatch.FromClient(client, "DBFilesClient\\Spell.dbc", out source);
                if (bytes == null) return result;
                var table = DbcTable.Parse(bytes, "Spell.dbc");
                if (table.Fields != RaceImport.SpellFields) return result;
                var names = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                var ids = new Dictionary<string, List<uint>>(StringComparer.OrdinalIgnoreCase);
                foreach (var record in table.Records)
                {
                    string rank = Text(table, table.Field(record, 153)).Trim();
                    if (!rank.EndsWith("Racial", StringComparison.OrdinalIgnoreCase) ||
                        rank.Equals("Racial", StringComparison.OrdinalIgnoreCase) ||
                        rank.Equals("Racial Passive", StringComparison.OrdinalIgnoreCase) ||
                        rank.IndexOf("CoA Focus", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    string name = Text(table, table.Field(record, 136)).Trim();
                    if (name.Length == 0 || (skipName != null && skipName(name))) continue;
                    HashSet<string> seen;
                    if (!names.TryGetValue(rank, out seen)) { seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); names[rank] = seen; ids[rank] = new List<uint>(); }
                    if (!seen.Add(name)) continue;
                    ids[rank].Add(table.Field(record, 0));
                }
                foreach (var pair in ids) result[pair.Key] = pair.Value.OrderBy(x => x).ToArray();
            }
            catch { }
            return result;
        }

        /// <summary>Sets the racial spells of one race from an already discovered client rank.</summary>
        public static void SetRacials(int race, IEnumerable<uint> ids)
        {
            if (race <= 0 || ids == null) return;
            var list = ids.Where(x => x > 0).Distinct().OrderBy(x => x).ToArray();
            if (list.Length > 0) coaRacials[race] = list;
        }

        /// <summary>Reads the racial spells of one race from the rank column of the game client's Spell.dbc
        /// ("Kyrian Racial"). This follows a CoA client update even when the module's old races.json still has the
        /// previous racial set. Spell rows with the same name are one ability; internal helper rows are skipped by
        /// name (for example Ascended Descent).</summary>
        public static void UseClientRaceLabel(string client, int race, string label, params string[] skipNames)
        {
            if (string.IsNullOrEmpty(client) || race <= 0 || string.IsNullOrEmpty(label)) return;
            try
            {
                string source;
                byte[] bytes = ClientPatch.FromClient(client, "DBFilesClient\\Spell.dbc", out source);
                if (bytes == null) return;
                var table = DbcTable.Parse(bytes, "Spell.dbc");
                if (table.Fields != RaceImport.SpellFields) return;
                var skip = new HashSet<string>(skipNames ?? new string[0], StringComparer.OrdinalIgnoreCase);
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var ids = new List<uint>();
                foreach (var record in table.Records)
                {
                    string rank = Text(table, table.Field(record, 153));
                    if (!rank.Equals(label, StringComparison.OrdinalIgnoreCase)) continue;
                    string name = Text(table, table.Field(record, 136));
                    if (name.Length == 0 || skip.Contains(name) || !names.Add(name)) continue;
                    ids.Add(table.Field(record, 0));
                }
                if (ids.Count > 0) coaRacials[race] = ids.OrderBy(x => x).ToArray();
            }
            catch { }
        }

        /// <summary>Reads CoA Custom's race -> racial spell table from the installed core
        /// (src/server/shared/CoaCustomRacials.h). The module's races.json is only a copy; using the core header means a
        /// CoA update that adds races or changes racials is picked up here without a new AFK Realm or module release.
        /// Entries from the header win over the copy.</summary>
        public static void UseCoreHeader(string sourceDir)
        {
            if (string.IsNullOrEmpty(sourceDir)) return;
            string file = Path.Combine(sourceDir, "src", "server", "shared", "CoaCustomRacials.h");
            if (!File.Exists(file)) return;
            try
            {
                var rx = new System.Text.RegularExpressions.Regex(@"\{\s*(\d+)\s*,\s*\{([\d,\s]+)\}\s*\}");
                foreach (string line in File.ReadLines(file))
                {
                    var m = rx.Match(line);
                    if (!m.Success) continue;
                    int race;
                    if (!int.TryParse(m.Groups[1].Value, out race) || race <= 0) continue;
                    var ids = m.Groups[2].Value.Split(',')
                        .Select(x => { uint n; return uint.TryParse(x.Trim(), out n) ? n : 0; })
                        .Where(n => n > 0).Distinct().ToArray();
                    if (ids.Length > 0) coaRacials[race] = ids;
                }
            }
            catch { }
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
        public static void Sync(string serverDataFolder, string outDir, string client, List<Wanted> races, string serverSpellDbc = null)
        {
            if (races == null || string.IsNullOrEmpty(outDir)) return;
            var records = string.IsNullOrEmpty(serverDataFolder) || !Directory.Exists(serverDataFolder)
                ? new Dictionary<uint, List<KeyValuePair<string, object>>>() : Records(serverDataFolder);
            var allIds = new HashSet<uint>();
            foreach (var race in races)
            {
                allIds.UnionWith(WithHelpers(ClientSpells(serverDataFolder, race.Name, race.CoaRace)));
                uint[] login;
                if (LoginHelpers.TryGetValue(race.CoaRace, out login)) allIds.UnionWith(login);
            }
            var clientRows = allIds.Count > 0 ? ClientRows(client, allIds) : new Dictionary<uint, object[]>();
            Directory.CreateDirectory(outDir);
            // Remove generated files of races that are no longer active. The folder is dedicated to
            // generated SQL; only files with the generated header are touched.
            var activeFiles = new HashSet<string>(races.Select(r => r.Key + ".sql"), StringComparer.OrdinalIgnoreCase);
            foreach (string old in Directory.GetFiles(outDir, "*.sql"))
            {
                if (activeFiles.Contains(Path.GetFileName(old))) continue;
                try
                {
                    string first;
                    using (var reader = new StreamReader(old, Encoding.UTF8, true)) first = reader.ReadLine();
                    if (first != null && first.StartsWith("-- spells", StringComparison.OrdinalIgnoreCase)) File.Delete(old);
                }
                catch { }
            }
            foreach (var race in races)
            {
                var all = WithHelpers(ClientSpells(serverDataFolder, race.Name, race.CoaRace)).ToList();
                uint[] login;
                if (LoginHelpers.TryGetValue(race.CoaRace, out login)) all.AddRange(login);
                all = all.Distinct().OrderBy(id => id).ToList();
                var lines = new List<string>();
                var spells = new List<uint>();
                foreach (uint spell in all)
                {
                    object[] row;
                    string coa;
                    // These rows need a corrected client table. Prefer it over the package's own SQL
                    // (14001006's package row carries the broken centaur gallop SpellVisual).
                    bool corrected = spell == 111104 || spell == 14001006;
                    string insert;
                    if (corrected && clientRows.TryGetValue(spell, out row))
                        insert = InsertRow(CorrectRow(spell, row));
                    else
                        insert = records.ContainsKey(spell) ? Insert("spell_dbc", records[spell])
                            : coaSpellRows.TryGetValue(spell, out coa) ? "INSERT INTO `spell_dbc` VALUES " + coa + ";"
                            : race.Rows.TryGetValue(spell, out row) || clientRows.TryGetValue(spell, out row) ? InsertRow(CorrectRow(spell, row)) : null;
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
                    if (ScriptBindings.TryGetValue(spell, out script) && !coaScripts.ContainsKey(spell))
                    {
                        lines.Add("DELETE FROM `spell_script_names` WHERE `spell_id` = " + spell + ";");
                        lines.Add("INSERT INTO `spell_script_names` (`spell_id`, `ScriptName`) VALUES (" + spell + ",'" + script + "');");
                    }
                    // Gallop has two scripts in mod-custom-races: the aura (from CoA's rows) and the
                    // cast-again-to-dismiss check. CoA's SQL only names the aura; bind the second here.
                    if (spell == 14001006 && !lines.Any(l => l.Contains("'spell_centaur_gallop'")))
                    {
                        lines.Add("DELETE FROM `spell_script_names` WHERE `spell_id` = 14001006 AND `ScriptName` = 'spell_centaur_gallop';");
                        lines.Add("INSERT INTO `spell_script_names` (`spell_id`, `ScriptName`) VALUES (14001006,'spell_centaur_gallop');");
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
                // CoA's client shows a spell only when it has a row in this table; the package's
                // client Spell.dbc carries the texts, and every CoA update brings its own.
                foreach (uint spell in spells)
                {
                    object[] clientRow;
                    if (!clientRows.TryGetValue(spell, out clientRow)) continue;
                    string description = ClientText(clientRow, DescriptionField);
                    if (description.Length == 0) description = ClientText(clientRow, NameField);
                    string tooltip = ClientText(clientRow, ToolTipField);
                    lines.Add("DELETE FROM `coa_client_spell_description` WHERE `ID` = " + spell + ";");
                    lines.Add("INSERT INTO `coa_client_spell_description` (`ID`, `Description`, `ToolTip`) VALUES (" + spell + ", " +
                        Literal(description) + ", " + (tooltip.Length > 0 ? Literal(tooltip) : "NULL") + ");");
                }
                // The centaur client model crashes in its M2 animation track when this
                // SpellVisual is played. Keep the speed aura, but remove the visual.
                if (spells.Contains(14001006))
                    lines.Add("UPDATE `spell_dbc` SET `SpellVisualID_1` = 0 WHERE `ID` = 14001006;");
                File.WriteAllText(file, string.Join("\r\n", lines) + "\r\n", new UTF8Encoding(false));
            }
            if (!string.IsNullOrEmpty(serverSpellDbc))
                MergeServerSpellDbc(serverSpellDbc, outDir);
        }

        /// <summary>The server's Spell.dbc must know the racial spells, not only the client spell stream: learnRacials
        /// asks SpellMgr. The module writes the rows as spell_dbc SQL; those rows are copied into the server table here,
        /// so a CoA update with new races or racials needs no hand-built DBC asset.</summary>
        static void MergeServerSpellDbc(string serverSpellDbc, string sqlDir)
        {
            if (!File.Exists(serverSpellDbc) || !Directory.Exists(sqlDir)) return;
            var rows = new List<KeyValuePair<uint, string[]>>();
            foreach (string file in Directory.GetFiles(sqlDir, "*.sql"))
            {
                foreach (string raw in File.ReadLines(file, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (!line.StartsWith("INSERT INTO `spell_dbc`", StringComparison.OrdinalIgnoreCase)) continue;
                    int at = line.IndexOf("VALUES", StringComparison.OrdinalIgnoreCase);
                    if (at < 0) continue;
                    string values = line.Substring(at + 6).Trim();
                    if (!values.StartsWith("(")) continue;
                    var fields = SqlValues(values);
                    if (fields == null || fields.Count != RaceImport.SpellFields) continue;
                    uint id;
                    if (!uint.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out id)) continue;
                    if (id == 14001006 && fields.Count > 131) fields[131] = "0";
                    rows.Add(new KeyValuePair<uint, string[]>(id, fields.ToArray()));
                }
            }
            if (rows.Count == 0) return;

            DbcTable table;
            try { table = DbcTable.Parse(File.ReadAllBytes(serverSpellDbc), "Spell.dbc"); }
            catch { return; }
            if (table.Fields != RaceImport.SpellFields || table.RecordSize != RaceImport.SpellFields * 4) return;

            var byId = new Dictionary<uint, byte[]>();
            foreach (var record in table.Records) byId[table.Field(record, 0)] = record;
            bool changed = false;
            foreach (var row in rows)
            {
                byte[] record;
                if (!byId.TryGetValue(row.Key, out record))
                {
                    record = new byte[table.RecordSize];
                    table.Records.Add(record);
                    byId[row.Key] = record;
                    changed = true;
                }
                for (int field = 0; field < row.Value.Length; ++field)
                {
                    string value = row.Value[field];
                    uint bits = 0;
                    if (RaceImport.SpellTextFields.Contains(field))
                    {
                        bits = table.AddString(value ?? "");
                    }
                    else if (!string.IsNullOrEmpty(value) && !value.Equals("NULL", StringComparison.OrdinalIgnoreCase))
                    {
                        if (FloatFields.Contains(field))
                        {
                            float number;
                            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                                bits = BitConverter.ToUInt32(BitConverter.GetBytes(number), 0);
                        }
                        else if (SignedFields.Contains(field))
                        {
                            long number;
                            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
                                bits = unchecked((uint)number);
                        }
                        else
                        {
                            ulong number;
                            if (ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
                                bits = (uint)number;
                        }
                    }
                    if (table.Field(record, field) != bits) changed = true;
                    table.SetField(record, field, bits);
                }
            }
            if (!changed) return;

            string temp = serverSpellDbc + ".afk-racials";
            File.WriteAllBytes(temp, table.ToBytes());
            try { File.Copy(temp, serverSpellDbc, true); }
            finally { try { File.Delete(temp); } catch { } }
        }

        /// <summary>The values of a SQL VALUES (...) row, with quoted texts unescaped; NULL stays null.</summary>
        static List<string> SqlValues(string text)
        {
            var values = new List<string>();
            int i = 0;
            while (i < text.Length && char.IsWhiteSpace(text[i])) ++i;
            if (i < text.Length && text[i] == '(') ++i;
            while (i < text.Length)
            {
                while (i < text.Length && char.IsWhiteSpace(text[i])) ++i;
                if (i >= text.Length || text[i] == ')') break;
                if (text[i] == ',') { ++i; continue; }
                if (text[i] == '\'')
                {
                    ++i;
                    var value = new StringBuilder();
                    while (i < text.Length)
                    {
                        char c = text[i++];
                        if (c == '\'')
                        {
                            if (i < text.Length && text[i] == '\'') { value.Append('\''); ++i; continue; }
                            break;
                        }
                        if (c == '\\' && i < text.Length)
                        {
                            char escaped = text[i++];
                            value.Append(escaped == 'r' ? '\r' : escaped == 'n' ? '\n' : escaped == '0' ? '\0' : escaped);
                            continue;
                        }
                        value.Append(c);
                    }
                    values.Add(value.ToString());
                    continue;
                }
                int start = i;
                while (i < text.Length && text[i] != ',' && text[i] != ')') ++i;
                string token = text.Substring(start, i - start).Trim();
                values.Add(token.Equals("NULL", StringComparison.OrdinalIgnoreCase) ? null : token);
            }
            return values;
        }

        /// <summary>The racials with the spells their code casts besides (CoA Custom numbers them next to the racial,
        /// in blocks of 20: Bull Rush 110600 and its knockdown 110601).</summary>
        static uint[] WithHelpers(uint[] spells)
        {
            var result = new List<uint>(spells);
            foreach (var helper in ScriptHelpers)
                if (spells.Contains(helper.Key)) result.AddRange(helper.Value);
            var blocks = new HashSet<uint>(spells.Where(coaSpellRows.ContainsKey).Select(s => s / 20));
            result.AddRange(coaSpellRows.Keys.Where(id => blocks.Contains(id / 20)));
            return result.Distinct().OrderBy(id => id).ToArray();
        }

        /// <summary>The client's 1.9.8 rows already carry the correct effect and aura columns for the
        /// helper spells; the old parser looked at the second aura column and mistook them for dummy rows.
        /// Keep the client row unchanged and only add the missing helper IDs to the generated race file.</summary>
        static object[] CorrectRow(uint id, object[] row)
        {
            // 111104 is a dummy marker in the client row. The server needs the safe-fall aura that
            // its tooltip promises; the other passive rows are already handled by the 1.9.8 scripts.
            if (row != null && row.Length == RaceImport.SpellFields)
            {
                if (id == 111104)
                {
                    row[95] = 144u; // EFFECT_AURA_1 = SPELL_AURA_SAFE_FALL
                    row[80] = 5u;   // EFFECT_BASE_POINTS_1
                }
                else if (id == 14001006)
                {
                    // The custom centaur model crashes in Its M2 animation track when the
                    // gallop SpellVisual (1703) is played. The speed aura works without it.
                    row[131] = 0u; // SpellVisualID_1
                }
            }
            return row;
        }

        // The columns of spell_dbc follow the fields of the client's Spell.dbc one to one (234 of them); these are floats,
        // these signed, the texts are named by RaceImport.SpellTextFields, every other one is unsigned.
        const int NameField = 136, DescriptionField = 170, ToolTipField = 187;
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

        static string ClientText(object[] row, int field)
        {
            if (row == null || field < 0 || field >= row.Length) return "";
            return Convert.ToString(row[field] ?? "").Trim();
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
