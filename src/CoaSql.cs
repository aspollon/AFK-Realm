using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CoAInstaller
{
    /// <summary>
    /// Runs the CoA Custom 2.0 world content that exists only as SQL (Fading Island, retail mounts and pets, the
    /// Forsaken slime, the spell fixes). Races and racials are not run: AFK's own module and ServerRacials carry those.
    /// The files are written to be re-runnable, so an update runs them again.
    /// </summary>
    static class CoaSql
    {
        // Which files are world content only. Everything else in the package belongs to the repack (1_world), AFK's
        // modules (2_bots, 4_ahbot_*, 60_vanilla_class_health) or is read as data by other install steps (racials).
        static readonly string[] Content =
        {
            "21_", "22_", "24_", "30_", "41_", "42_", "43_", "44_", "50_", "51_", "52_", "61_",
        };

        static bool Wanted(string name)
        {
            return Content.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        }

        static long Order(string path)
        {
            string name = Path.GetFileName(path);
            int end = 0;
            while (end < name.Length && char.IsDigit(name[end])) end++;
            return end > 0 && long.TryParse(name.Substring(0, end), out long n) ? n : long.MaxValue;
        }

        /// <summary>Applies the package's content SQL and returns how many files ran. A failing file is reported and
        /// skipped: the rest of the content still lands, and the log names what to look at.</summary>
        public static int Apply(Install inst)
        {
            string dir = Path.Combine(inst.Root, "CoA-Custom", "sql");
            if (!Directory.Exists(dir)) return 0;
            var files = Directory.GetFiles(dir, "*.sql")
                .Where(f => Wanted(Path.GetFileName(f)))
                .OrderBy(Order).ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (files.Length == 0) return 0;
            var login = DbLogin.FromConfig(inst);
            int ran = 0;
            foreach (string file in files)
            {
                string name = Path.GetFileName(file);
                try
                {
                    MySql.RunFile(inst, login, "acore_world", file);
                    Console.WriteLine("  " + name);
                    ran++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("  " + name + " -> " + ex.Message);
                }
            }
            return ran;
        }
    }
}
