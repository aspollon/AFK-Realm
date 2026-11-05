using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;

namespace CoAInstaller.Web
{
    /// <summary>A small read-only armory for bots and players: search by name, then show the equipped gear.</summary>
    static class BotArmory
    {
        static string Escape(string text) { return (text ?? "").Replace("'", "''"); }

        static bool ValidName(string name) { return name != null && name.Length >= 2 && name.Length <= 40 && Regex.IsMatch(name, @"^[\p{L}][\p{L}' -]*$"); }

        public static object Search(Install inst, string query)
        {
            query = (query ?? "").Trim();
            if (query.Length < 2) return new { characters = Array.Empty<object>(), note = L.T("Type at least two letters of a character name.") };
            if (!ValidName(query)) throw new UserError(L.T("A character name is letters, with one space at most."));
            var login = DbLogin.FromConfig(inst);
            var names = BotNames.Load(inst, login);
            string bot = "UPPER(a.username) LIKE " + GameData.BotPattern(inst);
            var found = new List<object>();
            foreach (var row in MySql.Query(inst, login,
                "SELECT c.name, c.level, c.class, c.race, c.zone, c.online, IF(" + bot + ", 1, 0) " +
                "FROM acore_characters.characters c JOIN acore_auth.account a ON a.id = c.account " +
                "WHERE c.name LIKE '" + Escape(query) + "%' ORDER BY c.online DESC, c.name LIMIT 20;"))
            {
                var f = row.Split('\t');
                int level, cls, race, zone;
                if (f.Length < 7 || !int.TryParse(f[1], out level) || !int.TryParse(f[2], out cls) || !int.TryParse(f[3], out race) || !int.TryParse(f[4], out zone)) continue;
                found.Add(new
                {
                    name = f[0], level, className = names.ClassName(cls), raceName = names.RaceName(race),
                    zone = zone > 0 ? GameData.ZoneName(inst, zone) : "", online = f[5] != "0", bot = f[6] == "1"
                });
            }
            return new { characters = found, note = found.Count == 0 ? L.T("No character by that name.") : "" };
        }

        public static object Character(Install inst, string name)
        {
            name = (name ?? "").Trim();
            if (!ValidName(name)) throw new UserError(L.T("A character name is letters, with one space at most."));
            var login = DbLogin.FromConfig(inst);
            var names = BotNames.Load(inst, login);
            string bot = "UPPER(a.username) LIKE " + GameData.BotPattern(inst);
            var chars = MySql.Query(inst, login,
                "SELECT c.guid, c.name, c.level, c.class, c.race, c.gender, c.zone, c.online, IF(" + bot + ", 1, 0) " +
                "FROM acore_characters.characters c JOIN acore_auth.account a ON a.id = c.account WHERE c.name = '" + Escape(name) + "';");
            if (chars.Count == 0) throw new UserError(L.T("No character by that name."));
            var f = chars[0].Split('\t');
            int guid, level, cls, race, gender, zone;
            if (f.Length < 9 || !int.TryParse(f[0], out guid) || !int.TryParse(f[2], out level) || !int.TryParse(f[3], out cls) ||
                !int.TryParse(f[4], out race) || !int.TryParse(f[5], out gender) || !int.TryParse(f[6], out zone))
                throw new UserError(L.T("The character could not be read."));

            var gear = new List<object>();
            double itemLevelSum = 0;
            int itemLevelCount = 0;
            try
            {
                foreach (var row in MySql.Query(inst, login,
                    "SELECT ci.slot, it.name, it.Quality, it.ItemLevel, ii.count, ii.itemEntry " +
                    "FROM acore_characters.character_inventory ci " +
                    "JOIN acore_characters.item_instance ii ON ii.guid = ci.item " +
                    "JOIN acore_world.item_template it ON it.entry = ii.itemEntry " +
                    "WHERE ci.guid = " + guid.ToString(CultureInfo.InvariantCulture) + " AND ci.bag = 0 AND ci.slot BETWEEN 0 AND 18 " +
                    "ORDER BY ci.slot;"))
                {
                    var g = row.Split('\t');
                    int slot, quality, ilvl, count, itemId;
                    if (g.Length < 6 || !int.TryParse(g[0], out slot) || !int.TryParse(g[2], out quality) ||
                        !int.TryParse(g[3], out ilvl) || !int.TryParse(g[4], out count) || !int.TryParse(g[5], out itemId)) continue;
                    gear.Add(new { slot, item = g[1], quality, itemLevel = ilvl, count, itemId });
                    if (ilvl > 0) { itemLevelSum += ilvl; itemLevelCount++; }
                }
            }
            catch (Exception ex)
            {
                return new { name = f[1], level, className = names.ClassName(cls), raceName = names.RaceName(race), zone = zone > 0 ? GameData.ZoneName(inst, zone) : "", online = f[7] != "0", bot = f[8] == "1", gear = Array.Empty<object>(), averageItemLevel = 0, note = L.F("The gear could not be read: {0}", ex.Message) };
            }

            object stats = null;
            try
            {
                var sr = MySql.Query(inst, login,
                    "SELECT maxhealth, maxpower, strength, agility, stamina, intellect, spirit, armor FROM acore_characters.character_stats WHERE guid = " + guid.ToString(CultureInfo.InvariantCulture) + ";");
                if (sr.Count > 0)
                {
                    var s = sr[0].Split('\t');
                    int maxHealth, maxPower, str, agi, sta, inte, spi, armor;
                    if (s.Length >= 8 && int.TryParse(s[0], out maxHealth) && int.TryParse(s[1], out maxPower) &&
                        int.TryParse(s[2], out str) && int.TryParse(s[3], out agi) && int.TryParse(s[4], out sta) &&
                        int.TryParse(s[5], out inte) && int.TryParse(s[6], out spi) && int.TryParse(s[7], out armor))
                        stats = new { maxHealth, maxPower, strength = str, agility = agi, stamina = sta, intellect = inte, spirit = spi, armor };
                }
            }
            catch { }

            return new
            {
                name = f[1], level, className = names.ClassName(cls), raceName = names.RaceName(race),
                gender, zone = zone > 0 ? GameData.ZoneName(inst, zone) : "", online = f[7] != "0", bot = f[8] == "1",
                gear, averageItemLevel = itemLevelCount > 0 ? Math.Round(itemLevelSum / itemLevelCount, 1) : 0, stats,
                note = ""
            };
        }
    }

    static partial class Api
    {
        static void MapBotArmory(WebApplication app)
        {
            Get(app, "/api/bots/armory", c =>
            {
                try { return BotArmory.Search(Inst, c.Request.Query["q"]); }
                catch (Exception ex) { throw new UserError(L.F("The armory could not be read: {0}", ex.Message)); }
            });
            Get(app, "/api/bots/armory/character", c =>
            {
                try { return BotArmory.Character(Inst, c.Request.Query["name"]); }
                catch (Exception ex) { throw new UserError(L.F("The armory could not be read: {0}", ex.Message)); }
            });
        }
    }
}
