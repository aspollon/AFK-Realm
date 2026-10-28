using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CoAInstaller
{
    /// <summary>
    /// A check of every custom race after the client patch is installed, for both sexes, against what the game client
    /// will read: the race table, the model, the skins, faces, hair and underwear rows, their HD rows, the textures they
    /// name, the helmets of the race and what an Esteria race needs. Each kind of problem it reports has been seen on a
    /// real client (the white bots of the Gnoll, the crash without a face for a skin colour, the High Elf's eyes beside
    /// its eyes, the placeholder cubes instead of helmets, the Mechagnome in pieces), so a new race or a new client shows
    /// them at once, before a player finds them. It changes nothing; it writes logs\race-check.txt and returns the lines.
    /// </summary>
    static class RaceCheck
    {
        /// <summary>What the check found about one race: what kind of problem, for which race (its key in the settings
        /// and its number) and the line for the user. RaceRepair decides from the kind what it can do about it.</summary>
        public sealed class Finding
        {
            public string Kind, Key, Text;
            public int Id, CoaNumber;
            public override string ToString() { return Text; }
        }

        // the kinds of problems
        public const string NoRow = "norow", NoModel = "nomodel", ModelMissing = "model", NoSkinFile = "skin", NoSkins = "skins",
            Textures = "textures", NoFace = "noface", Twice = "twice", HdWrong = "hdwrong", HdAlone = "hdalone", Helmets = "helmets",
            EsteriaExe = "esteriaexe", EsteriaNumber = "esterianumber", Stopped = "stopped";

        const string Conf = "mod_custom_races.conf";
        static readonly string[] StockPrefixes = { "Hu", "Or", "Dw", "Ni", "Sc", "Ta", "Gn", "Tr", "Be", "Dr" };
        static readonly Regex Helmet = new Regex(@"^item[\\/]objectcomponents[\\/]head[\\/](.+)_([a-z]{2})([mf])\.m2$", RegexOptions.IgnoreCase);

        static string M2(string path) { return Regex.Replace(path ?? "", @"\.(mdx|mdl)$", ".m2", RegexOptions.IgnoreCase); }

        /// <summary>Checks the races as the game client has them now and writes logs\race-check.txt (with the repairs
        /// made before, if any). Returns the problems found (empty: all fine).</summary>
        public static List<string> Run(Install inst, string client) { return Run(inst, client, null); }

        public static List<string> Run(Install inst, string client, IEnumerable<string> repaired)
        {
            var found = Inspect(inst, client, ClientPatch.ClientArchives(client));
            var problems = found.Select(f => f.Text).ToList();
            try
            {
                Directory.CreateDirectory(inst.LogDir);
                var text = new StringBuilder();
                text.AppendLine("AFK Realm " + Product.Version + " - " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                foreach (var r in repaired ?? Enumerable.Empty<string>()) text.AppendLine("* " + r);
                if (problems.Count == 0) text.AppendLine(L.T("No problems found."));
                foreach (var p in problems) text.AppendLine("- " + p);
                File.WriteAllText(Path.Combine(inst.LogDir, "race-check.txt"), text.ToString(), new UTF8Encoding(false));
            }
            catch { }
            return problems;
        }

        /// <summary>The check itself, over the given archives in the order the game reads them (the patch about to be
        /// installed among them). Changes nothing.</summary>
        public static List<Finding> Inspect(Install inst, string client, IEnumerable<string> archives)
        {
            var problems = new List<Finding>();
            string key = null, who = null; int id = 0;
            Action<string, string> add = (kind, text) => problems.Add(new Finding { Kind = kind, Key = key, Id = id, Text = text });
            var keys = (ClientPatch.ConfValue(inst, Conf, "CustomRaces.Races") ?? "")
                .Split(new[] { ' ', ',', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (keys.Count == 0) return problems;
            try
            {
                using (var a = new RacePacker.Archives(archives))
                {
                    Func<string, DbcTable> table = name =>
                    {
                        var b = a.Read("DBFilesClient\\" + name + ".dbc");
                        return b == null ? null : DbcTable.Parse(b, name + ".dbc");
                    };
                    var cr = table("ChrRaces");
                    var di = table("CreatureDisplayInfo");
                    var md = table("CreatureModelData");
                    var cs = table("CharSections");
                    var hd = table("HDCharSections");
                    if (cr == null || cs == null) return problems;
                    var displays = di == null ? new Dictionary<uint, byte[]>() : di.Records.GroupBy(r => di.Field(r, 0)).ToDictionary(g => g.Key, g => g.First());
                    var models = md == null ? new Dictionary<uint, byte[]>() : md.Records.GroupBy(r => md.Field(r, 0)).ToDictionary(g => g.Key, g => g.First());
                    var plainById = cs.Records.GroupBy(r => cs.Field(r, 0)).ToDictionary(g => g.Key, g => g.First());

                    // the helmets of the client by prefix and sex
                    var helmets = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                    foreach (var n in a.Names())
                    {
                        var m = Helmet.Match(n);
                        if (!m.Success) continue;
                        string k = m.Groups[2].Value + m.Groups[3].Value.ToUpperInvariant();
                        HashSet<string> set;
                        if (!helmets.TryGetValue(k, out set)) helmets[k] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        set.Add(m.Groups[1].Value);
                    }

                    // CoA Custom's numbers of its races, for the Esteria races (by the file name of their row)
                    var coaNumbers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    try
                    {
                        string root = ClientPatch.ConfValue(inst, Conf, "CustomRaces.ClientRootFolder") ?? "";
                        string dbc = root.Length == 0 ? "" : Os.Resolve(Path.GetDirectoryName(root.TrimEnd('\\', '/')), "dbc/ChrRaces.dbc");
                        if (dbc.Length > 0 && File.Exists(dbc))
                        {
                            var t = DbcTable.Parse(File.ReadAllBytes(dbc), "ChrRaces.dbc");
                            foreach (var row in t.Records)
                            {
                                string f = ClientRaces.Str(t, row, 11);
                                if (f.Length > 0 && !coaNumbers.ContainsKey(f)) coaNumbers[f] = (int)t.Field(row, 0);
                            }
                        }
                    }
                    catch { }
                    bool high = (ClientPatch.ConfValue(inst, Conf, "CustomRaces.HighRaces") ?? "0") == "1";

                    foreach (var raceKey in keys)
                    {
                        key = raceKey;
                        if (!int.TryParse(ClientPatch.ConfValue(inst, Conf, "CustomRaces." + key + ".Id") ?? "", out id) || id <= 0) continue;
                        string name = ClientPatch.ConfValue(inst, Conf, "CustomRaces." + key + ".Name");
                        if (string.IsNullOrEmpty(name)) name = key;
                        who = name + " (" + id + ")";
                        bool hidesHelmets = (ClientPatch.ConfValue(inst, Conf, "CustomRaces." + key + ".OwnHelmets") ?? "1") == "0";
                        var row = cr.Records.FirstOrDefault(r => cr.Field(r, 0) == (uint)id);
                        if (row == null) { add(NoRow, L.F("{0}: the race table of the game has no row for it.", who)); continue; }
                        bool maleOnly = (ClientPatch.ConfValue(inst, Conf, "CustomRaces." + key + ".MaleOnly") ?? "0") == "1";
                        bool femaleOnly = (ClientPatch.ConfValue(inst, Conf, "CustomRaces." + key + ".FemaleOnly") ?? "0") == "1";
                        var sexes = maleOnly ? new[] { 0 } : femaleOnly ? new[] { 1 } : new[] { 0, 1 };
                        bool native = false;

                        foreach (int sex in sexes)
                        {
                            string sexName = sex == 0 ? L.T("male") : L.T("female");
                            string whoSex = who + ", " + sexName;

                            // the model of the race and its first skin
                            byte[] d, m;
                            if (!displays.TryGetValue(cr.Field(row, 4 + sex), out d) || !models.TryGetValue(di.Field(d, 1), out m))
                                add(NoModel, L.F("{0}: the game knows no model for it.", whoSex));
                            else
                            {
                                string path = M2(ClientRaces.Str(md, m, 2));
                                if (path.IndexOf("\\native\\", StringComparison.OrdinalIgnoreCase) >= 0) native = true;
                                if (path.Length == 0 || !a.Has(path)) add(ModelMissing, L.F("{0}: its model {1} is missing.", whoSex, path));
                                else if (!a.Has(path.Substring(0, path.Length - 3) + "00.skin")) add(NoSkinFile, L.F("{0}: its model {1} has no .skin file; it is drawn in pieces or not at all.", whoSex, path));
                            }

                            // the sections: skins, faces, hair and underwear
                            var mine = cs.Records.Where(r => cs.Field(r, 1) == (uint)id && cs.Field(r, 2) == (uint)sex && cs.Field(r, 3) < 5).ToList();
                            if (mine.Count == 0) { add(NoSkins, L.F("{0}: no skins in CharSections; its characters (and bots) are drawn white.", whoSex)); continue; }
                            foreach (var path in mine.SelectMany(r => new[] { 4, 5, 6 }.Select(k => cs.Field(r, k) == 0 ? "" : ClientRaces.Str(cs, r, k)))
                                         .Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
                                if (path.IndexOf("\\native\\", StringComparison.OrdinalIgnoreCase) >= 0) native = true;
                            var missing = mine.SelectMany(r => new[] { 4, 5, 6 }.Select(k => cs.Field(r, k) == 0 ? "" : ClientRaces.Str(cs, r, k)))
                                .Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Where(p => !a.Has(p)).ToList();
                            if (missing.Count > 0)
                                add(Textures, L.F("{0}: {1} textures of its skins, faces or hair are missing, for example {2}.", whoSex, missing.Count, string.Join(", ", missing.Take(3))));
                            var colours = mine.Where(r => cs.Field(r, 3) == 0).Select(r => cs.Field(r, 9)).Distinct().ToList();
                            foreach (uint section in new uint[] { 1, 4 })
                            {
                                var have = new HashSet<uint>(mine.Where(r => cs.Field(r, 3) == section).Select(r => cs.Field(r, 9)));
                                var without = colours.Where(c => !have.Contains(c)).ToList();
                                if (have.Count > 0 && without.Count > 0)
                                    add(NoFace, L.F("{0}: skin colours without a {1} row ({2}); the character creation crashes on them.", whoSex,
                                        section == 1 ? L.T("face") : L.T("underwear"), string.Join(" ", without.Take(8))));
                            }
                            var twice = mine.GroupBy(r => cs.Field(r, 3) + "/" + cs.Field(r, 8) + "/" + cs.Field(r, 9)).Count(g => g.Count() > 1);
                            if (twice > 0) add(Twice, L.F("{0}: {1} looks have more than one row; the game takes one of them at random (Death Knight faces, wrong textures).", whoSex, twice));

                            // the HD rows replace the plain row with their id: it must be the row of the same look
                            if (hd != null)
                            {
                                int wrong = 0, alone = 0;
                                foreach (var h in hd.Records.Where(r => hd.Field(r, 1) == (uint)id && hd.Field(r, 2) == (uint)sex && hd.Field(r, 3) < 5))
                                {
                                    byte[] p;
                                    if (!plainById.TryGetValue(hd.Field(h, 0), out p)) { alone++; continue; }
                                    if (cs.Field(p, 1) != hd.Field(h, 1) || cs.Field(p, 2) != hd.Field(h, 2) || cs.Field(p, 3) != hd.Field(h, 3) ||
                                        cs.Field(p, 8) != hd.Field(h, 8) || cs.Field(p, 9) != hd.Field(h, 9)) wrong++;
                                }
                                if (wrong > 0) add(HdWrong, L.F("{0}: {1} HD rows replace the plain row of another look; faces and eyes sit in the wrong place.", whoSex, wrong));
                                if (alone > 0) add(HdAlone, L.F("{0}: {1} HD rows have no plain row; the game never uses them.", whoSex, alone));
                            }

                            // helmets: the prefix of its row must have (nearly) all of them, or every other helmet is a cube
                            string prefix = ClientRaces.Str(cr, row, 6);
                            string sx = sex == 0 ? "M" : "F";
                            HashSet<string> measure = null;
                            foreach (var p in StockPrefixes)
                            {
                                HashSet<string> s;
                                if (helmets.TryGetValue(p + sx, out s) && (measure == null || s.Count > measure.Count)) measure = s;
                            }
                            if (!hidesHelmets && measure != null && measure.Count > 0 && prefix.Length > 0)
                            {
                                HashSet<string> own;
                                helmets.TryGetValue(prefix + sx, out own);
                                int have = own == null ? 0 : measure.Count(x => own.Contains(x));
                                if (have < measure.Count * 0.97)
                                    add(Helmets, L.F("{0}: its helmet prefix {1} has {2} of {3} helmets; the others show the placeholder cube. Save the Custom races page and install the client patch again.", whoSex, prefix, have, measure.Count));
                            }
                        }

                        // an Esteria race: its looks are built by CoA Custom's EsteriaAppearance.dll, which knows it only by
                        // CoA Custom's number and only in CoA Custom's game exe; anything else draws it in pieces
                        if (native)
                        {
                            string file = ClientRaces.Str(cr, row, 11);
                            int coa;
                            if (!high)
                                add(EsteriaExe, L.F("{0}: an Esteria race needs CoA Custom's game exe (race numbers above 63) and its number there; without them it is drawn in pieces.", who));
                            else if (coaNumbers.TryGetValue(file, out coa) && coa != id)
                            {
                                add(EsteriaNumber, L.F("{0}: an Esteria race, but CoA Custom knows it as number {1}; under another number its looks are drawn in pieces. Give it number {1} on the Custom races page.", who, coa));
                                problems[problems.Count - 1].CoaNumber = coa;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                key = null; id = 0;
                add(Stopped, L.F("The race check stopped: {0}", ex.Message));
            }
            return problems;
        }
    }
}
