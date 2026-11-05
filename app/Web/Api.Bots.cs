using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;

namespace CoAInstaller.Web
{
    /// <summary>The names of classes and races the server knows, for the bot statistics and the bot roster.</summary>
    class BotNames
    {
        public readonly Dictionary<int, string> Classes = new Dictionary<int, string>();
        public readonly Dictionary<int, string> Races = new Dictionary<int, string>();
        public readonly Dictionary<int, int> Sides = new Dictionary<int, int>();

        static readonly HashSet<int> AllianceRaces = new HashSet<int> { 1, 3, 4, 7, 11 };
        static readonly HashSet<int> HordeRaces = new HashSet<int> { 2, 5, 6, 8, 10 };
        static readonly string[] FixedRaceNames = { "", L.T("Human"), L.T("Orc"), L.T("Dwarf"), L.T("Night Elf"), L.T("Undead"), L.T("Tauren"), L.T("Gnome"), L.T("Troll"), L.T("Goblin"), L.T("Blood Elf"), L.T("Draenei") };
        static readonly string[] FixedClassNames = { "", L.T("Warrior"), L.T("Paladin"), L.T("Hunter"), L.T("Rogue"), L.T("Priest"), L.T("Death Knight"), L.T("Shaman"), L.T("Mage"), L.T("Warlock"), L.T("Hero"), L.T("Druid") };

        public static BotNames Load(Install inst, DbLogin login)
        {
            var names = new BotNames();

            // The names the game shows for the classes of CoA (client_name; "name" is the internal one); the classic ones are fixed.
            try
            {
                foreach (var row in MySql.Query(inst, login, "SELECT class, client_name FROM acore_world.ascension_custom_class;"))
                {
                    var f = row.Split('\t');
                    int id;
                    if (f.Length >= 2 && int.TryParse(f[0], out id) && f[1].Length > 0) names.Classes[id] = f[1];
                }
            }
            catch { }

            // races a module added (mod-custom-races writes their rows into chrraces_dbc): their names and sides
            try
            {
                foreach (var row in MySql.Query(inst, login, "SELECT ID, Name_Lang_enUS, Alliance FROM acore_world.chrraces_dbc;"))
                {
                    var f = row.Split('\t');
                    int id, side;
                    if (f.Length < 3 || !int.TryParse(f[0], out id)) continue;
                    if (f[1].Length > 0 && f[1] != "NULL") names.Races[id] = L.Outside(f[1]);
                    if (int.TryParse(f[2], out side)) names.Sides[id] = side;
                }
            }
            catch { }

            return names;
        }

        public string ClassName(int cls)
        {
            string name;
            if (Classes.TryGetValue(cls, out name)) return name;
            return cls < FixedClassNames.Length ? FixedClassNames[cls] : L.F("Class {0}", cls.ToString(CultureInfo.InvariantCulture));
        }

        public string RaceName(int race)
        {
            string name;
            if (Races.TryGetValue(race, out name)) return name;
            return race < FixedRaceNames.Length && FixedRaceNames[race].Length > 0 ? FixedRaceNames[race] : L.F("Race {0}", race.ToString(CultureInfo.InvariantCulture));
        }

        public int? Side(int race)
        {
            if (AllianceRaces.Contains(race)) return 0;
            if (HordeRaces.Contains(race)) return 1;
            int side;
            return Sides.TryGetValue(race, out side) ? (int?)side : null;
        }
    }

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

        /// <summary>Reads the bots from the database. <paramref name="inWorldOnly"/> counts only the bots in the world.</summary>
        public static BotStats Load(Install inst, bool inWorldOnly)
        {
            var login = DbLogin.FromConfig(inst);
            string bot = "UPPER(a.username) LIKE " + GameData.BotPattern(inst);
            var names = BotNames.Load(inst, login);
            var s = new BotStats();

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
                int? side = names.Side(race);
                if (side == 0) s.Alliance++;
                else if (side == 1) s.Horde++;

                Add(s.Classes, names.ClassName(cls));
                Add(s.Races, names.RaceName(race));
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

    /// <summary>One row of the bot roster: who it is, where it is, and what it is doing when live status is on.</summary>
    class BotInfo
    {
        public string Name { get; set; } = "";
        public string ClassName { get; set; } = "";
        public string RaceName { get; set; } = "";
        public string Zone { get; set; } = "";
        public string Task { get; set; } = "";
        public string Leader { get; set; } = "";
        public int Level { get; set; }
        public int Health { get; set; }
        public int Power { get; set; }
        public int Kills { get; set; }
        public int Group { get; set; }
        public int Map { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public bool Online { get; set; }
        public bool Live { get; set; }
        public bool Dead { get; set; }
        public bool Combat { get; set; }
        public bool Mounted { get; set; }
        public string[] Quests { get; set; } = Array.Empty<string>();
    }

    /// <summary>The bots as a list for the dashboard, with the optional live snapshot of the Playerbots status file.</summary>
    class BotRoster
    {
        public readonly List<BotInfo> Bots = new List<BotInfo>();
        public bool Live, Configured;
        public long LiveAt;
        public string LiveFile = "", Note = "";

        static readonly TimeSpan Fresh = TimeSpan.FromSeconds(60);

        public static BotRoster Load(Install inst, bool inWorldOnly)
        {
            var login = DbLogin.FromConfig(inst);
            string bot = "UPPER(a.username) LIKE " + GameData.BotPattern(inst);
            var names = BotNames.Load(inst, login);
            var r = new BotRoster();

            foreach (var row in MySql.Query(inst, login, "SELECT c.name, c.race, c.class, c.level, c.zone, c.online FROM acore_characters.characters c " +
                "JOIN acore_auth.account a ON a.id = c.account WHERE " + bot + (inWorldOnly ? " AND c.online = 1" : "") +
                " ORDER BY c.online DESC, c.level DESC, c.name LIMIT 5000;"))
            {
                var f = row.Split('\t');
                int race, cls, level, zone;
                if (f.Length < 6 || !int.TryParse(f[1], out race) || !int.TryParse(f[2], out cls) || !int.TryParse(f[3], out level) || !int.TryParse(f[4], out zone))
                    continue;
                r.Bots.Add(new BotInfo
                {
                    Name = f[0],
                    ClassName = names.ClassName(cls),
                    RaceName = names.RaceName(race),
                    Level = level,
                    Zone = zone > 0 ? GameData.ZoneName(inst, zone) : "",
                    Online = f[5] != "0"
                });
            }

            ReadLive(inst, names, r);
            r.Bots.Sort((a, b) =>
            {
                int c = b.Online.CompareTo(a.Online);
                if (c == 0) c = b.Live.CompareTo(a.Live);
                if (c == 0) c = b.Level.CompareTo(a.Level);
                if (c == 0) c = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                return c;
            });
            return r;
        }

        static void ReadLive(Install inst, BotNames names, BotRoster r)
        {
            string conf = Path.Combine(inst.ConfigDir, "modules", "playerbots.conf");
            string raw = Conf.Get(conf, "AiPlayerbot.CoaStatusFile");
            if (!string.IsNullOrWhiteSpace(raw))
            {
                r.Configured = true;
                r.LiveFile = Resolve(inst, raw);
            }
            else
            {
                // AFK knows the usual place even when the setting has not been written yet.
                string usual = Path.Combine(inst.LogDir, "bot-status.json");
                if (File.Exists(usual)) r.LiveFile = usual;
            }

            if (r.LiveFile.Length == 0)
            {
                r.Note = L.T("Live status is off. AFK Realm can switch it on for the Playerbots status file; the roster then shows what every bot is doing.");
                return;
            }
            if (!File.Exists(r.LiveFile))
            {
                r.Note = r.Configured
                    ? L.F("The live status file {0} is not there yet. Restart the worldserver or run .reload config.", r.LiveFile)
                    : L.T("Live status is off. AFK Realm can switch it on for the Playerbots status file; the roster then shows what every bot is doing.");
                return;
            }

            DateTime written = File.GetLastWriteTimeUtc(r.LiveFile);
            if (DateTime.UtcNow - written > Fresh)
            {
                r.Note = L.F("The live status is stale (last written {0}). Is the worldserver running?", written.ToLocalTime().ToString("HH:mm:ss"));
                return;
            }

            try
            {
                using (var doc = JsonDocument.Parse(File.ReadAllText(r.LiveFile)))
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("at", out var at) && at.TryGetInt64(out var seconds)) r.LiveAt = seconds;
                    if (r.LiveAt == 0) r.LiveAt = new DateTimeOffset(written).ToUnixTimeSeconds();
                    if (!root.TryGetProperty("bots", out var list) || list.ValueKind != JsonValueKind.Array)
                    {
                        r.Note = L.T("The live status file has no bot list.");
                        return;
                    }

                    var byName = new Dictionary<string, BotInfo>(StringComparer.OrdinalIgnoreCase);
                    foreach (var b in r.Bots) byName[b.Name] = b;
                    foreach (var e in list.EnumerateArray())
                    {
                        string name = Text(e, "n");
                        if (name.Length == 0) continue;
                        BotInfo b;
                        if (!byName.TryGetValue(name, out b))
                        {
                            b = new BotInfo { Name = name, Online = true };
                            byName[name] = b;
                            r.Bots.Add(b);
                        }
                        b.Live = true;
                        b.Online = true;
                        b.Level = Int(e, "l", b.Level);
                        b.Health = Int(e, "hp", b.Health);
                        b.Power = Int(e, "pw", b.Power);
                        b.Map = Int(e, "m", b.Map);
                        int zone = Int(e, "zone", 0);
                        if (zone > 0) b.Zone = GameData.ZoneName(inst, zone);
                        b.X = Number(e, "x", b.X);
                        b.Y = Number(e, "y", b.Y);
                        b.Combat = Bool(e, "combat");
                        b.Dead = Bool(e, "dead");
                        b.Mounted = Bool(e, "mnt");
                        b.Group = Int(e, "grp", 0);
                        b.Leader = Text(e, "lead");
                        b.Kills = Int(e, "k", 0);
                        b.Task = Text(e, "task");
                        if (e.TryGetProperty("quests", out var q) && q.ValueKind == JsonValueKind.Array)
                            b.Quests = q.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToArray();
                    }
                    r.Live = true;
                }
            }
            catch (Exception ex)
            {
                r.Note = L.F("The live status file could not be read: {0}", ex.Message);
            }
        }

        static string Resolve(Install inst, string path)
        {
            path = (path ?? "").Trim();
            if (path.Length == 0) return "";
            if (Path.IsPathRooted(path)) return path;
            string fromServer = Path.Combine(inst.ServerDir, path);
            if (File.Exists(fromServer)) return fromServer;
            string fromRoot = Path.Combine(inst.Root, path);
            return File.Exists(fromRoot) ? fromRoot : fromServer;
        }

        static string Text(JsonElement e, string name)
        {
            JsonElement v;
            return e.TryGetProperty(name, out v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : "";
        }

        static int Int(JsonElement e, string name, int fallback)
        {
            JsonElement v;
            int n;
            return e.TryGetProperty(name, out v) && v.TryGetInt32(out n) ? n : fallback;
        }

        static double Number(JsonElement e, string name, double fallback)
        {
            JsonElement v;
            double n;
            return e.TryGetProperty(name, out v) && v.TryGetDouble(out n) ? n : fallback;
        }

        static bool Bool(JsonElement e, string name)
        {
            JsonElement v;
            return e.TryGetProperty(name, out v) && v.ValueKind == JsonValueKind.True;
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

            Get(app, "/api/bots/roster", c =>
            {
                bool inWorldOnly = c.Request.Query["all"] != "1";
                try
                {
                    var r = BotRoster.Load(Inst, inWorldOnly);
                    return new
                    {
                        bots = r.Bots, live = r.Live, liveAt = r.LiveAt, liveFile = r.LiveFile,
                        configured = r.Configured, note = r.Note
                    };
                }
                catch (Exception ex)
                {
                    throw new UserError(L.F("Could not read the bots – is the database running? ({0})", ex.Message));
                }
            });

            Post(app, "/api/bots/live", a =>
            {
                var inst = Inst;
                string conf = Path.Combine(inst.ConfigDir, "modules", "playerbots.conf");
                if (!File.Exists(conf)) throw new UserError(L.T("Playerbots is not installed. Install mod-playerbots first."));
                string file = Path.Combine(inst.LogDir, "bot-status.json");
                Directory.CreateDirectory(inst.LogDir);
                string shown = file.Replace("\\", "/");
                Conf.Set(conf, "AiPlayerbot.CoaStatusFile", "\"" + shown + "\"");
                Conf.Set(conf, "AiPlayerbot.CoaStatusIntervalSeconds", "5");
                return new
                {
                    ok = true,
                    path = file,
                    note = L.F("Live status is now written to {0} every 5 seconds. Restart the worldserver or run .reload config, then refresh this page.", file)
                };
            });
        }
    }
}
