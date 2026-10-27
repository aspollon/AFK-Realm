using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.AspNetCore.Builder;

namespace CoAInstaller.Web
{
    /// <summary>What the random bots of the server are: how many, which side, class, race, level and where.</summary>
    class BotStats
    {
        public int Total, InWorld, Players, Counted;
        public int Alliance, Horde;
        public double AverageLevel;
        public readonly Dictionary<string, int> Classes = new Dictionary<string, int>();
        public readonly Dictionary<string, int> Races = new Dictionary<string, int>();
        public readonly int[] Levels = new int[LevelBands.Length];
        public readonly Dictionary<string, int> Zones = new Dictionary<string, int>();

        public static readonly string[] LevelBands = { "1–9", "10–19", "20–29", "30–39", "40–49", "50–59", "60–69", "70–79", "80" };
        static readonly HashSet<int> AllianceRaces = new HashSet<int> { 1, 3, 4, 7, 11 };
        static readonly HashSet<int> HordeRaces = new HashSet<int> { 2, 5, 6, 8, 10 };
        static readonly string[] RaceNames = { "", L.T("Human"), L.T("Orc"), L.T("Dwarf"), L.T("Night Elf"), L.T("Undead"), L.T("Tauren"), L.T("Gnome"), L.T("Troll"), L.T("Goblin"), L.T("Blood Elf"), L.T("Draenei") };
        static readonly string[] ClassNames = { "", L.T("Warrior"), L.T("Paladin"), L.T("Hunter"), L.T("Rogue"), L.T("Priest"), L.T("Death Knight"), L.T("Shaman"), L.T("Mage"), L.T("Warlock"), L.T("Hero"), L.T("Druid") };

        static string N(int v) { return v.ToString(CultureInfo.InvariantCulture); }

        /// <summary>Reads the bots from the database. <paramref name="inWorldOnly"/> counts only the bots in the world.</summary>
        public static BotStats Load(Install inst, bool inWorldOnly)
        {
            var login = DbLogin.FromConfig(inst);
            string bot = "UPPER(a.username) LIKE " + GameData.BotPattern(inst);
            var s = new BotStats();

            // The names the game shows for the classes of CoA (client_name; "name" is the internal one); the classic ones are fixed.
            var classNames = new Dictionary<int, string>();
            try
            {
                foreach (var row in MySql.Query(inst, login, "SELECT class, client_name FROM acore_world.ascension_custom_class;"))
                {
                    var f = row.Split('\t');
                    int id;
                    if (f.Length >= 2 && int.TryParse(f[0], out id) && f[1].Length > 0) classNames[id] = f[1];
                }
            }
            catch { }

            // races a module added (mod-custom-races writes their rows into chrraces_dbc): their names and sides
            var raceNames = new Dictionary<int, string>();
            var raceSide = new Dictionary<int, int>();
            try
            {
                foreach (var row in MySql.Query(inst, login, "SELECT ID, Name_Lang_enUS, Alliance FROM acore_world.chrraces_dbc;"))
                {
                    var f = row.Split('\t');
                    int id, side;
                    if (f.Length < 3 || !int.TryParse(f[0], out id)) continue;
                    if (f[1].Length > 0 && f[1] != "NULL") raceNames[id] = L.Outside(f[1]);
                    if (int.TryParse(f[2], out side)) raceSide[id] = side;
                }
            }
            catch { }

            var players = MySql.Query(inst, login, "SELECT COUNT(*) FROM acore_characters.characters c JOIN acore_auth.account a ON a.id = c.account " +
                "WHERE c.online = 1 AND NOT (" + bot + ");");
            if (players.Count > 0) int.TryParse(players[0], out s.Players);

            long levelSum = 0;
            foreach (var row in MySql.Query(inst, login, "SELECT c.race, c.class, c.level, c.zone, c.online FROM acore_characters.characters c " +
                "JOIN acore_auth.account a ON a.id = c.account WHERE " + bot + ";"))
            {
                var f = row.Split('\t');
                int race, cls, level, zone;
                if (f.Length < 5 || !int.TryParse(f[0], out race) || !int.TryParse(f[1], out cls) || !int.TryParse(f[2], out level) || !int.TryParse(f[3], out zone))
                    continue;
                bool online = f[4] != "0";
                s.Total++;
                if (online) s.InWorld++;
                if (inWorldOnly && !online) continue;

                s.Counted++;
                levelSum += level;
                int side;
                if (AllianceRaces.Contains(race) || (raceSide.TryGetValue(race, out side) && side == 0)) s.Alliance++;
                else if (HordeRaces.Contains(race) || (raceSide.TryGetValue(race, out side) && side == 1)) s.Horde++;

                string className;
                if (!classNames.TryGetValue(cls, out className)) className = cls < ClassNames.Length ? ClassNames[cls] : L.F("Class {0}", N(cls));
                Add(s.Classes, className);
                string raceName;
                if (!raceNames.TryGetValue(race, out raceName)) raceName = race < RaceNames.Length && RaceNames[race].Length > 0 ? RaceNames[race] : L.F("Race {0}", N(race));
                Add(s.Races, raceName);
                s.Levels[Math.Min(LevelBands.Length - 1, Math.Max(0, level >= 80 ? 8 : level / 10))]++;
                if (zone > 0) Add(s.Zones, GameData.ZoneName(inst, zone));
            }
            s.AverageLevel = s.Counted > 0 ? (double)levelSum / s.Counted : 0;
            return s;
        }

        static void Add(Dictionary<string, int> d, string key)
        {
            int n;
            d[key] = d.TryGetValue(key, out n) ? n + 1 : 1;
        }
    }


    static partial class Api
    {
        static void MapBots(WebApplication app)
        {
            Get(app, "/api/bots/stats", c =>
            {
                bool inWorldOnly = c.Request.Query["all"] != "1";
                BotStats s;
                try { s = BotStats.Load(Inst, inWorldOnly); }
                catch (Exception ex) { throw new UserError(L.F("Could not read the bots – is the database running? ({0})", ex.Message)); }
                Func<Dictionary<string, int>, object> list = d => d.OrderByDescending(k => k.Value).ThenBy(k => k.Key).Select(k => new { name = k.Key, value = k.Value }).ToArray();
                int last = Array.FindLastIndex(s.Levels, v => v > 0);
                var zones = s.Zones.OrderByDescending(k => k.Value).ThenBy(k => k.Key).ToList();
                var shown = zones.Take(15).Select(k => new { name = k.Key, value = k.Value, other = false }).ToList();
                int rest = zones.Skip(15).Sum(k => k.Value);
                if (rest > 0) shown.Add(new { name = L.F("Other zones ({0})", zones.Count - 15), value = rest, other = true });
                return new
                {
                    total = s.Total, inWorld = s.InWorld, players = s.Players, counted = s.Counted, alliance = s.Alliance, horde = s.Horde,
                    average = s.Counted > 0 ? s.AverageLevel.ToString("0.0", CultureInfo.InvariantCulture) : "–",
                    classes = list(s.Classes), races = list(s.Races),
                    levels = BotStats.LevelBands.Take(Math.Max(1, last + 1)).Select((b, i) => new { name = L.F("Level {0}", b), value = s.Levels[i] }).ToArray(),
                    zones = shown, time = DateTime.Now.ToString("HH:mm:ss")
                };
            });
        }
    }
}
