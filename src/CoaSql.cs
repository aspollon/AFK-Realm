using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace CoAInstaller
{
    /// <summary>
    /// Runs the CoA Custom 2.0 world content that exists only as SQL (Fading Island, retail mounts and pets, the
    /// Forsaken slime, the spell fixes). Races and racials are not run: AFK's own module and ServerRacials carry those.
    /// Each file runs once per content: acore_world.afk_content_sql keeps its hash, so a reinstall skips it and a new
    /// package version of the file runs again. A re-imported world database has no journal and gets everything again.
    /// </summary>
    static class CoaSql
    {
        // Which files are world content only. Everything else in the package belongs to the repack (1_world), AFK's
        // modules (2_bots, 4_ahbot_*, 60_vanilla_class_health) or is read as data by other install steps (racials).
        // 24_ is left out: it is a copy of a CoA core update (manastorm cache brackets) and would undo newer core values.
        static readonly string[] Content =
        {
            "21_", "22_", "30_", "41_", "42_", "43_", "44_", "50_", "51_", "52_", "61_",
        };

        const string Journal = "acore_world.afk_content_sql";

        static bool Wanted(string name)
        {
            return Content.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        }

        static string Hash(string file)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(file))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        static Dictionary<string, string> Applied(Install inst, DbLogin login)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            MySql.Query(inst, login, "CREATE TABLE IF NOT EXISTS " + Journal +
                " (`name` VARCHAR(190) NOT NULL PRIMARY KEY, `hash` CHAR(64) NOT NULL, `applied` DATETIME NOT NULL)");
            foreach (string line in MySql.Query(inst, login, "SELECT `name`, `hash` FROM " + Journal))
            {
                var cols = line.Split('\t');
                if (cols.Length >= 2) result[cols[0]] = cols[1];
            }
            return result;
        }

        /// <summary>Applies the package's content SQL that is new or changed and returns how many files ran. A failing
        /// file is reported and skipped (not journaled, so the next install tries it again).</summary>
        public static int Apply(Install inst)
        {
            string dir = Path.Combine(inst.Root, "CoA-Custom", "sql");
            if (!Directory.Exists(dir)) return 0;
            var files = Directory.GetFiles(dir, "*.sql")
                .Where(f => Wanted(Path.GetFileName(f)))
                .OrderBy(ServerRacials.SqlOrder).ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (files.Length == 0) return 0;
            var login = DbLogin.FromConfig(inst);
            Dictionary<string, string> applied;
            try { applied = Applied(inst, login); }
            catch (Exception ex) { Console.WriteLine("  journal -> " + ex.Message); applied = new Dictionary<string, string>(); }
            int ran = 0, skipped = 0;
            foreach (string file in files)
            {
                string name = Path.GetFileName(file);
                string hash = Hash(file);
                if (applied.TryGetValue(name, out string had) && had == hash) { skipped++; continue; }
                try
                {
                    MySql.RunFile(inst, login, "acore_world", file);
                    Console.WriteLine("  " + name);
                    ran++;
                    try
                    {
                        MySql.Query(inst, login, "REPLACE INTO " + Journal + " (`name`, `hash`, `applied`) VALUES ('" +
                            name.Replace("\\", "\\\\").Replace("'", "''") + "', '" + hash + "', NOW())");
                    }
                    catch { }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("  " + name + " -> " + ex.Message);
                }
            }
            if (skipped > 0) Console.WriteLine("  " + skipped + " unchanged, not run again");
            return ran;
        }
    }
}
