using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace CoAInstaller
{
    class CharacterInfo
    {
        public int Guid, Level, Zone, Map, Race, Class; public string Name, Account; public bool Online, Bot;
        public double X, Y, Z;
    }

    class QuestInfo
    {
        public int Id, Level, MinLevel; public string Title = "", Givers = "", Takers = "";
        public string Progress = "";       // the objectives with their counts, for a quest in the log
        public int Status = -1;            // -1 not taken, 1 complete, 3 in progress, 5 failed, 100 rewarded
        public double Distance = -1;       // to the nearest giver or taker, when searched by position
        public bool CanTake = true;        // level, race, class and the quest before it allow it
        public string StatusText
        {
            get { return Status == 100 ? "rewarded" : Status == 1 ? "complete" : Status == 3 ? "in progress" : Status == 5 ? "failed" : Status < 0 ? "" : "in log"; }
        }
    }

    /// <summary>Looks things up in the server's own databases: characters, quests and who gives them.</summary>
    static class GameData
    {
        static string N(long v) { return v.ToString(CultureInfo.InvariantCulture); }
        static string N(double v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }
        static string Like(string text) { return MySql.Quote("%" + text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%"); }

        static string BotPattern(Install inst)
        {
            string prefix = Conf.Get(Path.Combine(inst.ConfigDir, "modules", "playerbots.conf"), "AiPlayerbot.RandomBotAccountPrefix");
            if (string.IsNullOrEmpty(prefix)) prefix = "rndbot";
            return MySql.Quote(prefix.ToUpperInvariant().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%");
        }

        /// <summary>Characters of the server, online ones first. Random bots and offline characters only on request.</summary>
        public static List<CharacterInfo> Characters(Install inst, bool offline, bool bots)
        {
            string bot = "(UPPER(a.username) LIKE " + BotPattern(inst) + ")";
            string sql = "SELECT c.guid, c.name, c.level, c.zone, c.map, c.online, a.username, " + bot + ", c.position_x, c.position_y, c.position_z " +
                "FROM acore_characters.characters c JOIN acore_auth.account a ON a.id = c.account WHERE c.name <> ''" +
                (offline ? "" : " AND c.online = 1") + (bots ? "" : " AND NOT " + bot) +
                " ORDER BY c.online DESC, c.name LIMIT 3000;";
            var list = new List<CharacterInfo>();
            foreach (var row in MySql.Query(inst, DbLogin.FromConfig(inst), sql))
            {
                var f = row.Split('\t');
                if (f.Length < 11) continue;
                list.Add(new CharacterInfo
                {
                    Guid = int.Parse(f[0], CultureInfo.InvariantCulture), Name = f[1], Level = int.Parse(f[2], CultureInfo.InvariantCulture),
                    Zone = int.Parse(f[3], CultureInfo.InvariantCulture), Map = int.Parse(f[4], CultureInfo.InvariantCulture),
                    Online = f[5] != "0", Account = f[6], Bot = f[7] != "0",
                    X = double.Parse(f[8], CultureInfo.InvariantCulture), Y = double.Parse(f[9], CultureInfo.InvariantCulture), Z = double.Parse(f[10], CultureInfo.InvariantCulture)
                });
            }
            return list;
        }

        /// <summary>Position as last saved by the server (after "saveall" it is the current one).</summary>
        public static CharacterInfo Character(Install inst, int guid)
        {
            return Characters(inst, true, true, "c.guid = " + N(guid)).FirstOrDefault();
        }
        static List<CharacterInfo> Characters(Install inst, bool offline, bool bots, string where)
        {
            string sql = "SELECT c.guid, c.name, c.level, c.zone, c.map, c.online, a.username, 0, c.position_x, c.position_y, c.position_z, c.race, c.class " +
                "FROM acore_characters.characters c JOIN acore_auth.account a ON a.id = c.account WHERE " + where + ";";
            var list = new List<CharacterInfo>();
            foreach (var row in MySql.Query(inst, DbLogin.FromConfig(inst), sql))
            {
                var f = row.Split('\t');
                if (f.Length < 13) continue;
                list.Add(new CharacterInfo
                {
                    Guid = int.Parse(f[0], CultureInfo.InvariantCulture), Name = f[1], Level = int.Parse(f[2], CultureInfo.InvariantCulture),
                    Zone = int.Parse(f[3], CultureInfo.InvariantCulture), Map = int.Parse(f[4], CultureInfo.InvariantCulture), Online = f[5] != "0", Account = f[6],
                    X = double.Parse(f[8], CultureInfo.InvariantCulture), Y = double.Parse(f[9], CultureInfo.InvariantCulture), Z = double.Parse(f[10], CultureInfo.InvariantCulture),
                    Race = int.Parse(f[11], CultureInfo.InvariantCulture), Class = int.Parse(f[12], CultureInfo.InvariantCulture)
                });
            }
            return list;
        }

        // ---------------------------------------------------------------- quests

        const string W = "acore_world.", C = "acore_characters.";

        static string StatusColumn(int guid)
        {
            if (guid <= 0) return "-1";
            return "IF(EXISTS (SELECT 1 FROM " + C + "character_queststatus_rewarded r WHERE r.guid = " + N(guid) + " AND r.quest = q.ID), 100, " +
                   "IFNULL((SELECT s.status FROM " + C + "character_queststatus s WHERE s.guid = " + N(guid) + " AND s.quest = q.ID), -1))";
        }

        static string Names(string relation, string template, string separator)
        {
            return "IFNULL((SELECT GROUP_CONCAT(DISTINCT t.name ORDER BY t.name SEPARATOR '" + separator + "') FROM " + W + relation + " x JOIN " + W + template +
                   " t ON t.entry = x.id WHERE x.quest = q.ID), '')";
        }

        static string Join(string a, string b) { return a.Length > 0 && b.Length > 0 ? a + ", " + b : a + b; }

        /// <summary>Quests by quest id, quest title, or the name of the NPC or object that gives or takes them.</summary>
        public static List<QuestInfo> SearchQuests(Install inst, string text, int characterGuid)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return new List<QuestInfo>();
            string like = Like(text);
            int id; bool numeric = int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out id);
            Func<string, string, string> byName = (relation, template) =>
                "q.ID IN (SELECT x.quest FROM " + W + relation + " x JOIN " + W + template + " t ON t.entry = x.id WHERE t.name LIKE " + like + ")";
            string where = (numeric ? "q.ID = " + N(id) + " OR " : "") + "q.LogTitle LIKE " + like +
                " OR " + byName("creature_queststarter", "creature_template") + " OR " + byName("creature_questender", "creature_template") +
                " OR " + byName("gameobject_queststarter", "gameobject_template") + " OR " + byName("gameobject_questender", "gameobject_template");
            string sql = "SELECT q.ID, q.LogTitle, q.QuestLevel, q.MinLevel, " +
                Names("creature_queststarter", "creature_template", ", ") + ", " + Names("gameobject_queststarter", "gameobject_template", ", ") + ", " +
                Names("creature_questender", "creature_template", ", ") + ", " + Names("gameobject_questender", "gameobject_template", ", ") + ", " +
                StatusColumn(characterGuid) +
                " FROM " + W + "quest_template q WHERE " + where + " ORDER BY q.QuestLevel, q.LogTitle LIMIT 400;";
            var list = new List<QuestInfo>();
            foreach (var row in MySql.Query(inst, DbLogin.FromConfig(inst), "SET SESSION group_concat_max_len = 4000;\n" + sql))
            {
                var f = row.Split('\t');
                if (f.Length < 9) continue;
                list.Add(new QuestInfo
                {
                    Id = int.Parse(f[0], CultureInfo.InvariantCulture), Title = f[1], Level = int.Parse(f[2], CultureInfo.InvariantCulture), MinLevel = int.Parse(f[3], CultureInfo.InvariantCulture),
                    Givers = Join(f[4], f[5]), Takers = Join(f[6], f[7]), Status = int.Parse(f[8], CultureInfo.InvariantCulture)
                });
            }
            return list;
        }

        /// <summary>The query behind <see cref="QuestLog"/>: one row per quest in the character's log.</summary>
        internal static string QuestLogSql(int guid)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("SELECT q.ID, q.LogTitle, q.QuestLevel, q.MinLevel, ");
            sb.Append(Names("creature_queststarter", "creature_template", ", ")).Append(", ").Append(Names("gameobject_queststarter", "gameobject_template", ", ")).Append(", ");
            sb.Append(Names("creature_questender", "creature_template", ", ")).Append(", ").Append(Names("gameobject_questender", "gameobject_template", ", ")).Append(", s.status");
            // Four things to kill, use or talk to (a negative id is an object), then six things to bring.
            for (int i = 1; i <= 4; i++)
                sb.Append(", q.RequiredNpcOrGoCount").Append(i).Append(", s.mobcount").Append(i).Append(", REPLACE(IFNULL(IFNULL(NULLIF(q.ObjectiveText").Append(i).Append(", ''), IF(q.RequiredNpcOrGo").Append(i)
                  .Append(" > 0, (SELECT t.name FROM ").Append(W).Append("creature_template t WHERE t.entry = q.RequiredNpcOrGo").Append(i)
                  .Append("), (SELECT t.name FROM ").Append(W).Append("gameobject_template t WHERE t.entry = -q.RequiredNpcOrGo").Append(i)
                  .Append("))), CONCAT(IF(q.RequiredNpcOrGo").Append(i).Append(" > 0, 'creature ', 'object '), ABS(q.RequiredNpcOrGo").Append(i).Append("))), '\t', ' ')");
            for (int i = 1; i <= 6; i++)
                sb.Append(", q.RequiredItemCount").Append(i).Append(", s.itemcount").Append(i).Append(", REPLACE(IFNULL((SELECT t.name FROM ").Append(W)
                  .Append("item_template t WHERE t.entry = q.RequiredItemId").Append(i).Append("), ''), '\t', ' ')");
            sb.Append(" FROM ").Append(C).Append("character_queststatus s JOIN ").Append(W).Append("quest_template q ON q.ID = s.quest WHERE s.guid = ").Append(N(guid))
              .Append(" AND s.status <> 0 ORDER BY q.QuestLevel, q.LogTitle LIMIT 100;");
            return sb.ToString();
        }

        /// <summary>
        /// What the character has in its quest log right now, with how far each objective is. For a
        /// character that is online this is the state of the server's last save.
        /// </summary>
        public static List<QuestInfo> QuestLog(Install inst, int guid)
        {
            var list = new List<QuestInfo>();
            if (guid <= 0) return list;
            foreach (var row in MySql.Query(inst, DbLogin.FromConfig(inst), "SET SESSION group_concat_max_len = 4000;\n" + QuestLogSql(guid)))
            {
                var q = ParseLogRow(row);
                if (q != null) list.Add(q);
            }
            return list;
        }

        internal static QuestInfo ParseLogRow(string row)
        {
            var f = row.Split('\t');
            if (f.Length < 9 + 30) return null;
            var q = new QuestInfo
            {
                Id = int.Parse(f[0], CultureInfo.InvariantCulture), Title = f[1], Level = int.Parse(f[2], CultureInfo.InvariantCulture), MinLevel = int.Parse(f[3], CultureInfo.InvariantCulture),
                Givers = Join(f[4], f[5]), Takers = Join(f[6], f[7]), Status = int.Parse(f[8], CultureInfo.InvariantCulture)
            };
            var parts = new List<string>();
            for (int i = 0; i < 10; i++)
            {
                int need, have; string name = f[9 + i * 3 + 2];
                if (!int.TryParse(f[9 + i * 3], NumberStyles.Integer, CultureInfo.InvariantCulture, out need) || need <= 0) continue;
                int.TryParse(f[9 + i * 3 + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out have);
                if (name == "NULL" || name.Length == 0) name = i < 4 ? "objective " + (i + 1) : "item";
                // The server stops counting when the quest is complete; the log then shows it as done.
                parts.Add(name + " " + (q.Status == 1 ? need : Math.Min(have, need)) + "/" + need);
            }
            q.Progress = parts.Count > 0 ? string.Join(", ", parts.ToArray()) : (q.Status == 1 ? "done" : "nothing to count (talk, travel or explore)");
            return q;
        }

        /// <summary>
        /// Quests whose giver stands within <paramref name="radius"/> yards of the character and that
        /// it neither has in its log nor has done. With
        /// <paramref name="takeableOnly"/> quests are left out that its level, race or class do not
        /// allow or that need an earlier quest first.
        /// </summary>
        public static List<QuestInfo> QuestsNear(Install inst, CharacterInfo who, int radius, bool takeableOnly)
        {
            var login = DbLogin.FromConfig(inst);
            // The spawn table names the creature in "id1" (AzerothCore) or "id" (CoA).
            bool id1 = MySql.Query(inst, login, "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = 'acore_world' AND table_name = 'creature' AND column_name = 'id1';")[0] != "0";
            string cid = id1 ? "id1" : "id";
            string x = N(who.X), y = N(who.Y), z = N(who.Z), r = N(radius);
            Func<string, string, string, string, string, string> part = (kind, spawn, idColumn, relation, template) =>
                "SELECT x.quest AS quest, '" + kind + "' AS kind, t.name AS name, MIN(SQRT(POW(s.position_x - " + x + ", 2) + POW(s.position_y - " + y + ", 2) + POW(s.position_z - " + z + ", 2))) AS dist " +
                "FROM " + W + spawn + " s JOIN " + W + relation + " x ON x.id = s." + idColumn + " JOIN " + W + template + " t ON t.entry = s." + idColumn +
                " WHERE s.map = " + N(who.Map) + " AND s.position_x BETWEEN " + x + " - " + r + " AND " + x + " + " + r +
                " AND s.position_y BETWEEN " + y + " - " + r + " AND " + y + " + " + r + " GROUP BY x.quest, t.name";
            string race = who.Race >= 1 && who.Race <= 32 ? N(1L << (who.Race - 1)) : "4294967295";
            string cls = who.Class >= 1 && who.Class <= 32 ? N(1L << (who.Class - 1)) : "4294967295";
            string can = "(q.MinLevel <= " + N(who.Level) + " AND (q.AllowableRaces = 0 OR (q.AllowableRaces & " + race + ") <> 0) AND " +
                "IFNULL((SELECT (a.AllowableClasses = 0 OR (a.AllowableClasses & " + cls + ") <> 0) AND (a.PrevQuestID <= 0 OR EXISTS (SELECT 1 FROM " + C +
                "character_queststatus_rewarded r WHERE r.guid = " + N(who.Guid) + " AND r.quest = a.PrevQuestID)) FROM " + W + "quest_template_addon a WHERE a.ID = q.ID), 1))";
            string sql = "SELECT q.ID, q.LogTitle, q.QuestLevel, q.MinLevel, g.kind, g.name, g.dist, " + StatusColumn(who.Guid) + " AS st, " + can + " FROM (" +
                part("give", "creature", cid, "creature_queststarter", "creature_template") + " UNION ALL " +
                part("take", "creature", cid, "creature_questender", "creature_template") + " UNION ALL " +
                part("give", "gameobject", "id", "gameobject_queststarter", "gameobject_template") + " UNION ALL " +
                part("take", "gameobject", "id", "gameobject_questender", "gameobject_template") +
                ") g JOIN " + W + "quest_template q ON q.ID = g.quest WHERE g.dist <= " + r + " HAVING st <> 100 ORDER BY g.dist LIMIT 2000;";
            var byId = new Dictionary<int, QuestInfo>();
            var order = new List<QuestInfo>();
            foreach (var row in MySql.Query(inst, login, sql))
            {
                var f = row.Split('\t');
                if (f.Length < 9) continue;
                int id = int.Parse(f[0], CultureInfo.InvariantCulture);
                QuestInfo q;
                if (!byId.TryGetValue(id, out q))
                {
                    q = new QuestInfo { Id = id, Title = f[1], Level = int.Parse(f[2], CultureInfo.InvariantCulture), MinLevel = int.Parse(f[3], CultureInfo.InvariantCulture),
                        Status = int.Parse(f[7], CultureInfo.InvariantCulture), CanTake = f[8] != "0" };
                    byId[id] = q; order.Add(q);
                }
                double d = double.Parse(f[6], CultureInfo.InvariantCulture);
                if (q.Distance < 0 || d < q.Distance) q.Distance = d;
                if (f[4] == "give") q.Givers = Join(q.Givers, f[5]); else q.Takers = Join(q.Takers, f[5]);
            }
            // What is in the log already is shown by the quest log; here only what it could still take, from a giver nearby.
            return order.Where(q => q.Status < 0 && q.Givers.Length > 0 && (!takeableOnly || q.CanTake))
                        .OrderBy(q => q.Distance).ToList();
        }

        // ---------------------------------------------------------------- zone names

        static Dictionary<int, string> areas;
        static string areasFor;

        /// <summary>Zone name from the server's AreaTable.dbc; "Zone 12" when it cannot be read.</summary>
        public static string ZoneName(Install inst, int zone)
        {
            if (zone <= 0) return "";
            if (areas == null || areasFor != inst.Root)
            {
                areasFor = inst.Root;
                try { areas = ReadAreaNames(Path.Combine(inst.DataDir, "dbc", "AreaTable.dbc")); }
                catch { areas = new Dictionary<int, string>(); }
            }
            string name;
            return areas.TryGetValue(zone, out name) && name.Length > 0 ? name : "Zone " + zone;
        }

        /// <summary>Reads id and name from a WDBC file: field 0 is the id, fields 11 to 26 hold the name per client language.</summary>
        static Dictionary<int, string> ReadAreaNames(string file)
        {
            var result = new Dictionary<int, string>();
            byte[] d = File.ReadAllBytes(file);
            if (d.Length < 20 || d[0] != 'W' || d[1] != 'D' || d[2] != 'B' || d[3] != 'C') return result;
            int records = BitConverter.ToInt32(d, 4), fields = BitConverter.ToInt32(d, 8), size = BitConverter.ToInt32(d, 12), strings = BitConverter.ToInt32(d, 16);
            int stringStart = 20 + records * size;
            if (fields < 27 || size < 27 * 4 || stringStart + strings > d.Length) return result;
            for (int i = 0; i < records; i++)
            {
                int row = 20 + i * size;
                int id = BitConverter.ToInt32(d, row);
                for (int f = 11; f < 27; f++)
                {
                    int offset = BitConverter.ToInt32(d, row + f * 4);
                    if (offset <= 0 || offset >= strings) continue;
                    int start = stringStart + offset, end = start;
                    while (end < d.Length && d[end] != 0) end++;
                    if (end > start) { result[id] = Encoding.UTF8.GetString(d, start, end - start); break; }
                }
            }
            return result;
        }
    }
}
