using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CoAInstaller
{
    /// <summary>
    /// The checks that follow a CoA Custom install: which races the random bots must not take, and a report per race
    /// (client and server agree, the race is on, its racials exist and have their code). Written to the install output
    /// and to logs/coa-custom-check.txt, so a problem is seen before a player meets it.
    /// </summary>
    static class CoaCheck
    {
        /// <summary>Races whose models make a client crash when it sees them on a bot, or that have no model, by the name
        /// the package gives them (numbers change between packages). From players' reports on CoA Custom 1.9.x.</summary>
        static readonly string[] BotCrashNames =
        {
            "Sylvari", "Faceless One", "Centaur", "Saberon", "Grummle", "Slime Giant", "Sporeling", "Nerubian",
            "Thin Human", "Forgotten", "Skyborne", "Test Race",
        };

        /// <summary>playerbots' own list of crashing races, kept besides the names above.</summary>
        static readonly int[] PlayerbotsDefault = { 32, 72, 77, 97 };

        public static List<KeyValuePair<int, string>> ServerRaces(Install inst)
        {
            var list = new List<KeyValuePair<int, string>>();
            string dbc = Path.Combine(inst.DataDir, "dbc", "ChrRaces.dbc");
            if (!File.Exists(dbc)) return list;
            try
            {
                var table = DbcTable.Parse(File.ReadAllBytes(dbc), "ChrRaces.dbc");
                foreach (var row in table.Records)
                {
                    string name = ClientRaces.Str(table, row, 14);
                    if (name.Length == 0) name = ClientRaces.Str(table, row, 11);
                    list.Add(new KeyValuePair<int, string>((int)table.Field(row, 0), name));
                }
            }
            catch { }
            return list;
        }

        /// <summary>Writes AiPlayerbot.CrashingBotRaces: playerbots' own list, the races named above as the package numbers
        /// them, and the package's own exclusions (bots-custom-races.conf). Returns the races left out.</summary>
        public static List<KeyValuePair<int, string>> WriteBotExclusions(Install inst, string packRoot)
        {
            var races = ServerRaces(inst);
            var ids = new SortedSet<int>(PlayerbotsDefault);
            foreach (var race in races)
                if (BotCrashNames.Any(n => race.Value.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0)) ids.Add(race.Key);
            try
            {
                string own = packRoot == null ? null : Path.Combine(packRoot, "bots-custom-races.conf");
                if (own != null && File.Exists(own))
                {
                    var m = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(own), @"(?m)^[ \t]*AiPlayerbot\.ExcludedBotRaces[ \t]*=[ \t]*""?([^""\r\n]*)");
                    string value = m.Success ? m.Groups[1].Value : null;
                    foreach (string part in (value ?? "").Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        int n;
                        if (int.TryParse(part, out n) && n > 0) ids.Add(n);
                    }
                }
            }
            catch { }

            string conf = Path.Combine(inst.ConfigDir, "modules", "playerbots.conf");
            if (File.Exists(conf) || File.Exists(conf + ".dist"))
            {
                var file = ConfigFile.Load(conf, "modules\\playerbots.conf");
                file.Pending["AiPlayerbot.CrashingBotRaces"] = "\"" + string.Join(",", ids) + "\"";
                file.Save();
            }
            var names = races.ToDictionary(r => r.Key, r => r.Value);
            return ids.Select(id => new KeyValuePair<int, string>(id, names.ContainsKey(id) ? names[id] : "")).ToList();
        }

        /// <summary>The ids of every row of a spell table, read from the file without loading it whole.</summary>
        static HashSet<uint> SpellIds(string dbc)
        {
            var ids = new HashSet<uint>();
            if (!File.Exists(dbc)) return ids;
            using (var f = File.OpenRead(dbc))
            using (var r = new BinaryReader(f))
            {
                if (new string(r.ReadChars(4)) != "WDBC") return ids;
                int count = r.ReadInt32(); r.ReadInt32(); int size = r.ReadInt32(); r.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    f.Position = 20L + (long)i * size;
                    ids.Add(r.ReadUInt32());
                }
            }
            return ids;
        }

        /// <summary>One line per playable race of the package, problems first. Also written to logs/coa-custom-check.txt.</summary>
        public static List<string> Report(Install inst, string client, IEnumerable<KeyValuePair<int, string>> botsOff)
        {
            var problems = new List<string>();
            var fine = new List<string>();
            var server = ServerRaces(inst).ToDictionary(r => r.Key, r => r.Value);
            var clientRaces = new Dictionary<int, string>();
            try
            {
                string from;
                byte[] bytes = ClientPatch.FromClient(client, "DBFilesClient\\ChrRaces.dbc", out from);
                if (bytes != null)
                {
                    var table = DbcTable.Parse(bytes, "ChrRaces.dbc");
                    foreach (var row in table.Records)
                        clientRaces[(int)table.Field(row, 0)] = ClientRaces.Str(table, row, 14);
                }
            }
            catch { }
            var missing = new HashSet<int>(CoaSetup.MissingServerRaces(inst).Select(m => m.Key));
            var spells = SpellIds(Path.Combine(inst.DataDir, "dbc", "Spell.dbc"));
            var bots = new HashSet<int>(botsOff.Select(b => b.Key));

            foreach (var race in CoaSetup.PackageRaces(inst))
            {
                var issues = new List<string>();
                string clientName;
                if (clientRaces.Count > 0 && !clientRaces.TryGetValue(race.Key, out clientName)) issues.Add(L.T("the game client does not know this race number"));
                else if (clientRaces.Count > 0 && clientRaces.TryGetValue(race.Key, out clientName) && clientName != race.Value)
                    issues.Add(L.F("the game client calls it {0}", clientName));
                if (missing.Contains(race.Key)) issues.Add(L.T("switched off in the race settings (characters cannot be created)"));
                uint[] racials = ServerRacials.Spells("", race.Value, race.Key);
                if (racials.Length == 0) issues.Add(L.T("no racials found"));
                else
                {
                    var unknown = racials.Where(s => spells.Count > 0 && !spells.Contains(s) && !ServerRacials.HasRow(s)).ToArray();
                    if (unknown.Length > 0) issues.Add(L.F("racial spell(s) {0} exist neither in Spell.dbc nor in the package", string.Join(", ", unknown)));
                    var code = ServerRacials.NeedCode(racials);
                    if (code.Length > 0) issues.Add(L.F("racial(s) {0} need code of the CoA Custom worldserver that AFK Realm does not have yet", string.Join(", ", code)));
                }
                string label = race.Value + " (" + race.Key + ")" + (bots.Contains(race.Key) ? "  " + L.T("[no random bots: crashes the client]") : "");
                if (issues.Count == 0) fine.Add("  OK   " + label + "  " + L.F("{0} racial(s)", racials.Length));
                else problems.Add("  !!   " + label + ": " + string.Join("; ", issues));
            }

            var lines = new List<string> { L.F("CoA Custom check: {0} race(s) fine, {1} with a problem.", fine.Count, problems.Count) };
            lines.AddRange(problems);
            lines.AddRange(fine);
            try
            {
                Directory.CreateDirectory(inst.LogDir);
                File.WriteAllLines(Path.Combine(inst.LogDir, "coa-custom-check.txt"), new[] { DateTime.Now.ToString("yyyy-MM-dd HH:mm") }.Concat(lines));
            }
            catch { }
            return lines;
        }
    }
}
