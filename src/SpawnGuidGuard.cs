using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace CoAInstaller
{
    /// <summary>
    /// A CoA core update that inserts creatures or gameobjects under fixed guids stops the worldserver ("Duplicate entry
    /// for key creature.PRIMARY") when a spawn of another entry already holds one of them, as CoA Custom's and other
    /// added content does. Before the updates run, such a spawn moves to a free guid with its addon, event and pool
    /// rows; the update then inserts its own. The engine does the same before dbimport (Repair-SpawnGuidCollisions).
    /// </summary>
    static class SpawnGuidGuard
    {
        static readonly Regex Insert = new Regex(@"INSERT\s+(?:IGNORE\s+)?INTO\s+`?(creature|gameobject)`?\s*\(\s*`?guid`?\s*,\s*`?id1?`?\s*,[^)]*\)\s*VALUES\s*(.*?);",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        static readonly Regex Row = new Regex(@"\(\s*(\d+)\s*,\s*(\d+)\s*,");
        static readonly Regex Delete = new Regex(@"DELETE\s+FROM\s+`?(creature|gameobject)`?\s+WHERE\s+(.*?);", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        static readonly Regex ById = new Regex(@"`?\bid1?`?\s*(=|IN\b)", RegexOptions.IgnoreCase);
        static readonly Regex Between = new Regex(@"(\d+)\s+AND\s+(\d+)", RegexOptions.IgnoreCase);

        // Tables that name a spawn by its guid, besides the spawn table itself.
        static readonly Dictionary<string, string[]> Linked = new Dictionary<string, string[]>
        {
            { "creature", new[] { "creature_addon.guid", "game_event_creature.guid", "pool_creature.guid", "creature_formations.memberGUID", "creature_formations.leaderGUID" } },
            { "gameobject", new[] { "gameobject_addon.guid", "game_event_gameobject.guid", "pool_gameobject.guid" } },
        };

        public static int Clear(Install inst)
        {
            var login = DbLogin.FromConfig(inst);
            var applied = new HashSet<string>(MySql.Query(inst, login, "SELECT name FROM acore_world.updates"), StringComparer.OrdinalIgnoreCase);
            var wanted = new Dictionary<string, Dictionary<uint, uint>> { { "creature", new Dictionary<uint, uint>() }, { "gameobject", new Dictionary<uint, uint>() } };
            string updates = Path.Combine(inst.SourceDir, "data", "sql", "updates");
            foreach (string folder in new[] { "db_world", "pending_db_world" })
            {
                string dir = Path.Combine(updates, folder);
                if (!Directory.Exists(dir)) continue;
                foreach (string file in Directory.GetFiles(dir, "*.sql"))
                {
                    if (applied.Contains(Path.GetFileName(file))) continue;
                    string text = File.ReadAllText(file);
                    var freed = new HashSet<string>();
                    // an update that deletes these guids whatever spawn holds them replaces that spawn on purpose
                    foreach (Match d in Delete.Matches(text))
                        if (!ById.IsMatch(d.Groups[2].Value))
                        {
                            string table = d.Groups[1].Value.ToLowerInvariant();
                            foreach (Match n in Regex.Matches(d.Groups[2].Value, @"\d+")) freed.Add(table + n.Value);
                            foreach (Match b in Between.Matches(d.Groups[2].Value))
                                if (ulong.Parse(b.Groups[2].Value) - ulong.Parse(b.Groups[1].Value) < 100000)
                                    for (ulong g = ulong.Parse(b.Groups[1].Value); g <= ulong.Parse(b.Groups[2].Value); g++) freed.Add(table + g);
                        }
                    foreach (Match m in Insert.Matches(text))
                    {
                        string table = m.Groups[1].Value.ToLowerInvariant();
                        foreach (Match r in Row.Matches(m.Groups[2].Value))
                            if (!freed.Contains(table + r.Groups[1].Value))
                                wanted[table][uint.Parse(r.Groups[1].Value)] = uint.Parse(r.Groups[2].Value);
                    }
                }
            }
            int moved = 0;
            foreach (var kind in wanted)
            {
                if (kind.Value.Count == 0) continue;
                string table = kind.Key;
                string idColumn = MySql.Query(inst, login, "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = 'acore_world' AND table_name = '" +
                    table + "' AND column_name = 'id1'").FirstOrDefault() == "1" ? "id1" : "id";
                var held = new List<uint>();
                foreach (var chunk in kind.Value.Keys.Select((g, i) => new { g, i }).GroupBy(x => x.i / 500, x => x.g))
                    foreach (string line in MySql.Query(inst, login, "SELECT guid, " + idColumn + " FROM acore_world." + table + " WHERE guid IN (" + string.Join(",", chunk) + ")"))
                    {
                        var cols = line.Split('\t');
                        if (cols.Length >= 2 && uint.TryParse(cols[0], out uint guid) && uint.TryParse(cols[1], out uint id) && kind.Value[guid] != id)
                            held.Add(guid);
                    }
                if (held.Count == 0) continue;
                uint next = Math.Max(uint.Parse(MySql.Query(inst, login, "SELECT COALESCE(MAX(guid), 0) FROM acore_world." + table).FirstOrDefault() ?? "0"), kind.Value.Keys.Max()) + 1;
                foreach (uint guid in held)
                {
                    var sql = new List<string> { "UPDATE acore_world." + table + " SET guid = " + next + " WHERE guid = " + guid };
                    foreach (string link in Linked[table])
                    {
                        string[] tc = link.Split('.');
                        sql.Add("UPDATE IGNORE acore_world." + tc[0] + " SET " + tc[1] + " = " + next + " WHERE " + tc[1] + " = " + guid);
                    }
                    if (table == "creature")
                        sql.Add("UPDATE IGNORE acore_world.smart_scripts SET entryorguid = -" + next + " WHERE entryorguid = -" + guid + " AND source_type = 0");
                    foreach (string statement in sql)
                        try { MySql.Query(inst, login, statement); }
                        catch (Exception ex) { if (statement == sql[0]) throw; Console.WriteLine("  " + ex.Message); }
                    Console.WriteLine("  " + table + " " + guid + " -> " + next + " (a CoA update needs guid " + guid + ")");
                    next++;
                    moved++;
                }
            }
            return moved;
        }
    }
}
