using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.AspNetCore.Builder;

namespace CoAInstaller.Web
{
    /// <summary>Small read-only overviews that do not need their own bot module: battlegrounds/arenas and auctions.</summary>
    static class BotMore
    {
        static int I(string value)
        {
            int n;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : 0;
        }

        static long Long(string value)
        {
            long n;
            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : 0;
        }

        static double D(string value)
        {
            double n;
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out n) ? n : 0;
        }

        public static object Pvp(Install inst)
        {
            var login = DbLogin.FromConfig(inst);
            var bg = new List<object>();
            var arena = new List<object>();
            var teams = new List<object>();
            var notes = new List<string>();
            bool bgAvailable = false, arenaAvailable = false, teamAvailable = false;

            try
            {
                foreach (var row in MySql.Query(inst, login,
                    "SELECT type, COUNT(*), SUM(winner_faction = 1), SUM(winner_faction = 0), SUM(winner_faction NOT IN (0,1)) " +
                    "FROM acore_characters.pvpstats_battlegrounds WHERE date >= NOW() - INTERVAL 1 DAY GROUP BY type ORDER BY type;"))
                {
                    var f = row.Split('\t');
                    if (f.Length < 5) continue;
                    int type = I(f[0]);
                    bg.Add(new
                    {
                        type,
                        name = type == 1 ? "Alterac Valley" : type == 2 ? "Warsong Gulch" : type == 3 ? "Arathi Basin" : L.F("Battleground {0}", type),
                        games = I(f[1]), alliance = I(f[2]), horde = I(f[3]), other = I(f[4])
                    });
                }
                bgAvailable = true;
            }
            catch (Exception ex) { notes.Add(L.F("Battleground statistics are not available: {0}", ex.Message)); }

            try
            {
                foreach (var row in MySql.Query(inst, login,
                    "SELECT type, COUNT(*), AVG(duration), SUM(winner = 0), SUM(winner = 1) FROM acore_characters.log_arena_fights " +
                    "WHERE time >= NOW() - INTERVAL 1 DAY GROUP BY type ORDER BY type;"))
                {
                    var f = row.Split('\t');
                    if (f.Length < 5) continue;
                    int type = I(f[0]);
                    arena.Add(new
                    {
                        type,
                        name = type == 1 ? "1v1" : type == 2 ? "2v2" : type == 3 ? "3v3" : L.F("Arena {0}", type),
                        fights = I(f[1]), averageSeconds = Math.Round(D(f[2])), wins = I(f[3]), losses = I(f[4])
                    });
                }
                arenaAvailable = true;
            }
            catch (Exception ex) { notes.Add(L.F("Arena statistics are not available: {0}", ex.Message)); }

            try
            {
                foreach (var row in MySql.Query(inst, login,
                    "SELECT type, COUNT(*), ROUND(AVG(rating)), SUM(seasonGames), SUM(seasonWins) FROM acore_characters.arena_team GROUP BY type ORDER BY type;"))
                {
                    var f = row.Split('\t');
                    if (f.Length < 5) continue;
                    int type = I(f[0]);
                    teams.Add(new
                    {
                        type,
                        name = type == 5 ? "1v1" : type == 2 ? "2v2" : type == 3 ? "3v3" : L.F("Arena {0}", type),
                        count = I(f[1]), averageRating = I(f[2]), games = I(f[3]), wins = I(f[4])
                    });
                }
                teamAvailable = true;
            }
            catch (Exception ex) { notes.Add(L.F("Arena teams are not available: {0}", ex.Message)); }

            return new { bg, arena, teams, bgAvailable, arenaAvailable, teamAvailable, note = string.Join("\n", notes) };
        }

        public static object Auction(Install inst)
        {
            var login = DbLogin.FromConfig(inst);
            string bot = "UPPER(a.username) LIKE " + GameData.BotPattern(inst);
            var houses = new List<object>();
            var sellers = new List<object>();
            var top = new List<object>();
            var notes = new List<string>();
            long total = 0;

            try
            {
                foreach (var row in MySql.Query(inst, login,
                    "SELECT ah.houseid, COUNT(*), SUM(IF(" + bot + ", 1, 0)), SUM(IF(a.id IS NULL, 1, 0)) " +
                    "FROM acore_characters.auctionhouse ah " +
                    "LEFT JOIN acore_characters.characters c ON c.guid = ah.itemowner " +
                    "LEFT JOIN acore_auth.account a ON a.id = c.account GROUP BY ah.houseid ORDER BY ah.houseid;"))
                {
                    var f = row.Split('\t');
                    if (f.Length < 4) continue;
                    int id = I(f[0]);
                    int count = I(f[1]);
                    total += count;
                    houses.Add(new
                    {
                        id,
                        name = id == 1 || id == 2 ? "Alliance" : id == 6 ? "Horde" : id == 7 ? "Neutral" : L.F("House {0}", id),
                        count, bots = I(f[2]), other = I(f[3])
                    });
                }

                foreach (var row in MySql.Query(inst, login,
                    "SELECT COALESCE(c.name, 'Auction house'), IF(" + bot + ", 1, 0), COUNT(*), SUM(ah.buyoutprice) " +
                    "FROM acore_characters.auctionhouse ah " +
                    "LEFT JOIN acore_characters.characters c ON c.guid = ah.itemowner " +
                    "LEFT JOIN acore_auth.account a ON a.id = c.account GROUP BY c.name, a.username " +
                    "ORDER BY COUNT(*) DESC, c.name LIMIT 20;"))
                {
                    var f = row.Split('\t');
                    if (f.Length < 4) continue;
                    sellers.Add(new { name = f[0], bot = f[1] == "1", count = I(f[2]), buyout = Long(f[3]) });
                }

                try
                {
                    foreach (var row in MySql.Query(inst, login,
                        "SELECT ah.buyoutprice, COALESCE(c.name, '—'), COALESCE(it.name, CONCAT('Item ', ii.itemEntry)) " +
                        "FROM acore_characters.auctionhouse ah " +
                        "JOIN acore_characters.item_instance ii ON ii.guid = ah.itemguid " +
                        "LEFT JOIN acore_characters.characters c ON c.guid = ah.itemowner " +
                        "LEFT JOIN acore_world.item_template it ON it.entry = ii.itemEntry " +
                        "WHERE ah.buyoutprice > 0 ORDER BY ah.buyoutprice DESC LIMIT 10;"))
                    {
                        var f = row.Split('\t');
                        if (f.Length < 3) continue;
                        top.Add(new { buyout = Long(f[0]), seller = f[1], item = f[2] });
                    }
                }
                catch (Exception ex) { notes.Add(L.F("The most expensive auctions could not be read: {0}", ex.Message)); }
            }
            catch (Exception ex)
            {
                notes.Add(L.F("The auction house could not be read: {0}", ex.Message));
            }

            return new { total, houses, sellers, top, note = string.Join("\n", notes) };
        }
    }

    static partial class Api
    {
        static void MapBotMore(WebApplication app)
        {
            Get(app, "/api/bots/pvp", c =>
            {
                try { return BotMore.Pvp(Inst); }
                catch (Exception ex) { throw new UserError(L.F("Could not read PvP statistics ({0})", ex.Message)); }
            });
            Get(app, "/api/bots/auction", c =>
            {
                try { return BotMore.Auction(Inst); }
                catch (Exception ex) { throw new UserError(L.F("Could not read the auction house ({0})", ex.Message)); }
            });
        }
    }
}
