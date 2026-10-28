using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace CoAInstaller
{
    /// <summary>
    /// What AFK Realm does about the problems the race check finds, before the client patch goes into the game: every
    /// kind of problem has its repair, and only a race that cannot be repaired is switched off (its characters play
    /// their base race until it is on again), so no player gets a race in pieces, white or with placeholder helmets.
    ///  - helmets: the race wears the helmets of its base race (or hides them when it has no base race to borrow from);
    ///  - skins, faces, HD rows: the strict rules of the character creation are applied to its rows once more;
    ///  - an Esteria race: CoA Custom's exe and the 64-race patch are switched on, and the race gets the number CoA
    ///    Custom gives it (mod-custom-races moves its characters along);
    ///  - a missing model or textures of an imported race: the race is packed again from the client it came from;
    ///  - whatever is left after that: the race is switched off, with the reason.
    /// </summary>
    static class RaceRepair
    {
        const string Conf = "mod_custom_races.conf";
        static readonly Dictionary<string, string> BasePrefixes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Human", "Hu" }, { "Orc", "Or" }, { "Dwarf", "Dw" }, { "NightElf", "Ni" }, { "Undead", "Sc" }, { "Tauren", "Ta" },
            { "Gnome", "Gn" }, { "Troll", "Tr" }, { "BloodElf", "Be" }, { "Draenei", "Dr" },
        };

        static string Get(Install inst, string key) { return ClientPatch.ConfValue(inst, Conf, key) ?? ""; }

        static void Set(Install inst, Dictionary<string, string> values)
        {
            if (values.Count == 0) return;
            string path = Path.Combine(inst.ConfigDir, "modules", Conf);
            var file = ConfigFile.Load(path, "modules\\" + Conf);
            foreach (var v in values) file.Pending[v.Key] = v.Value;
            file.Save();
        }

        static string Quoted(string text) { return "\"" + (text ?? "").Replace("\"", "") + "\""; }

        /// <summary>Switches races off: out of CustomRaces.Races.</summary>
        public static void SwitchOff(Install inst, IEnumerable<string> keys)
        {
            var off = new HashSet<string>(keys.Where(k => !string.IsNullOrEmpty(k)), StringComparer.OrdinalIgnoreCase);
            if (off.Count == 0) return;
            var chosen = Get(inst, "CustomRaces.Races").Split(new[] { ' ', ',', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var left = chosen.Where(k => !off.Contains(k)).ToList();
            if (left.Count == chosen.Length) return;
            Set(inst, new Dictionary<string, string> { { "CustomRaces.Races", Quoted(string.Join(" ", left)) } });
        }

        /// <summary>
        /// Repairs what can be repaired. tried: the repairs made in the rounds before ("kind:key"); a problem that is still
        /// there after its repair is not repaired twice. strict: race numbers whose sections the build puts through the
        /// strict rules. off: the races to switch off, with the reason. done: lines for the user. Returns whether anything
        /// changed (then the patch is built again).
        /// </summary>
        public static bool Fix(Install inst, string client, List<RaceCheck.Finding> found, HashSet<string> tried, HashSet<int> strict,
            Dictionary<string, string> off, List<string> done, Action<string> progress)
        {
            bool changed = false;
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in found.Where(f => !string.IsNullOrEmpty(f.Key)).GroupBy(f => f.Key, StringComparer.OrdinalIgnoreCase))
            {
                string key = group.Key;
                string name = Get(inst, "CustomRaces." + key + ".Name");
                if (name.Length == 0) name = key;
                bool import = Get(inst, "CustomRaces." + key + ".Import") == "1";
                // the repairs first, then what cannot be repaired: a race that goes off still gets its other repairs,
                // so it is right when it is switched on again
                foreach (var f in group.GroupBy(x => x.Kind).Select(g => g.First()))
                {
                    string mark = f.Kind + ":" + key;
                    if (tried.Contains(mark)) { if (!off.ContainsKey(key)) off[key] = f.Text; continue; }
                    tried.Add(mark);
                    switch (f.Kind)
                    {
                        case RaceCheck.Helmets:
                        {
                            string prefix;
                            if (BasePrefixes.TryGetValue(Get(inst, "CustomRaces." + key + ".Base"), out prefix))
                            {
                                values["CustomRaces." + key + ".HelmetPrefix"] = Quoted(prefix);
                                values["CustomRaces." + key + ".OwnHelmets"] = "1";
                                done.Add(L.F("{0}: wears the helmets of its base race now (prefix {1}) instead of placeholder cubes.", name, prefix));
                            }
                            else
                            {
                                values["CustomRaces." + key + ".HelmetPrefix"] = Quoted("");
                                values["CustomRaces." + key + ".OwnHelmets"] = "0";
                                done.Add(L.F("{0}: has no helmets of its own and no base race to borrow them from; its helmets are hidden.", name));
                            }
                            changed = true;
                            break;
                        }
                        case RaceCheck.NoFace:
                        case RaceCheck.Twice:
                        case RaceCheck.HdWrong:
                        case RaceCheck.HdAlone:
                            if (strict.Add(f.Id)) done.Add(L.F("{0}: its skins, faces and HD rows were put in order.", name));
                            changed = true;
                            break;
                        case RaceCheck.EsteriaExe:
                        {
                            string root = Get(inst, "CustomRaces.ClientRootFolder");
                            string exe = root.Length > 0 ? ClientPatch.GameExeOf(root) : null;
                            string patch = root.Length > 0 ? ClientPatch.CoAPatchOf(root) : null;
                            if (exe != null && patch != null)
                            {
                                values["CustomRaces.ClientExe"] = Quoted(exe);
                                values["CustomRaces.MoreRacesFile"] = Quoted(patch);
                                values["CustomRaces.MoreRaces"] = "1";
                                values["CustomRaces.HighRaces"] = "1";
                                done.Add(L.F("CoA Custom's game exe and its 64-race patch are switched on, for {0}.", name));
                                changed = true;
                            }
                            else if (!off.ContainsKey(key)) off[key] = L.F("{0}: an Esteria race needs CoA Custom's game exe, and no CoA Custom folder with it is chosen on the Custom races page.", name);
                            break;
                        }
                        case RaceCheck.EsteriaNumber:
                        {
                            int target = f.CoaNumber;
                            if (target > 0 && !NumberTaken(inst, key, target))
                            {
                                values["CustomRaces." + key + ".Id"] = target.ToString();
                                // a race of the game client keeps its row where the client has it; under another number
                                // its rows are copied there (as for the High Elf), else the new number has no row at all
                                if (!import && SourceOf(inst, key) != target) values["CustomRaces." + key + ".Copy"] = "1";
                                if (import)
                                {
                                    values["CustomRaces." + key + ".MaleDisplay"] = (RaceImport.DisplayBase + target * 2).ToString();
                                    values["CustomRaces." + key + ".FemaleDisplay"] = (RaceImport.DisplayBase + target * 2 + 1).ToString();
                                }
                                done.Add(L.F("{0}: gets number {1}, the one CoA Custom's appearance DLL knows it by; its characters move along at the next server start.", name, target));
                                changed = true;
                            }
                            else if (!off.ContainsKey(key)) off[key] = L.F("{0}: an Esteria race needs number {1}, which another race of this realm has.", name, target);
                            break;
                        }
                        case RaceCheck.ModelMissing:
                        case RaceCheck.NoSkinFile:
                        case RaceCheck.Textures:
                        case RaceCheck.NoModel:
                        case RaceCheck.NoSkins:
                            if (import && !tried.Contains("repack:" + key))
                            {
                                tried.Add("repack:" + key);
                                string line = Repack(inst, client, key, progress);
                                if (line != null) { done.Add(line); changed = true; }
                                else if (!off.ContainsKey(key)) off[key] = f.Text;
                            }
                            else if (!import && !off.ContainsKey(key)) off[key] = f.Text;
                            break;
                        case RaceCheck.NoRow:
                        {
                            // a race of the game client under a number other than its row's (moved, or given CoA
                            // Custom's number), with its rows edited in place: copy them to its number instead
                            int source = SourceOf(inst, key);
                            // its row is under another number than the settings say (a move of the client races, or
                            // settings older than the client): take the rows from where they are
                            if (!import && f.FoundAt > 0 && f.FoundAt != source)
                            {
                                values["CustomRaces." + key + ".Source"] = f.FoundAt.ToString();
                                values["CustomRaces." + key + ".Copy"] = f.FoundAt == f.Id ? "0" : "1";
                                done.Add(L.F("{0}: its row is under number {1} of the game client; it is taken from there for its number {2}.", name, f.FoundAt, f.Id));
                                changed = true;
                            }
                            else if (!import && source > 0 && source != f.Id && Get(inst, "CustomRaces." + key + ".Copy") != "1")
                            {
                                values["CustomRaces." + key + ".Copy"] = "1";
                                done.Add(L.F("{0}: its rows are copied from number {1} of the game client to its number {2}.", name, source, f.Id));
                                changed = true;
                            }
                            else if (!off.ContainsKey(key)) off[key] = f.Text;
                            break;
                        }
                    }
                }
            }
            Set(inst, values);
            return changed;
        }

        /// <summary>
        /// Packs that are out of date are packed again from the client they came from, before the client patch is built:
        ///  - made by an older packer (RacePacker.PackerVersion): a fix of the packer reaches every race by itself;
        ///  - made from another version of that client (RacePacker.SourceStamp): a newer CoA Custom in its folder brings
        ///    its new models, textures and helmets to the races by itself.
        /// Nobody has to import a race again by hand. A pack whose client is not there any more stays as it is (it works
        /// as before); that is noted in its folder, so it is not tried again at every install until something changes.
        /// Returns lines for the user (empty: nothing done).
        /// </summary>
        public static List<string> RefreshOldPacks(Install inst, string client, IEnumerable<RaceImport.Wanted> imports, Action<string> progress)
        {
            var done = new List<string>();
            string source = SourceFolder(inst);
            string label = source == null ? "" : RacePacker.SourceLabel(source);
            string stamp = null;
            foreach (var pack in imports.Select(w => w.Pack).Where(p => p != null).GroupBy(p => p.Dir, StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
            {
                bool oldPacker = pack.Packer < RacePacker.PackerVersion;
                // only a pack of that same client is compared with it (a race of Ashen Order is not packed from CoA Custom)
                bool sameClient = source != null && (pack.ClientLabel.Length == 0 || pack.ClientLabel.Equals(label, StringComparison.OrdinalIgnoreCase));
                if (sameClient && stamp == null) stamp = RacePacker.SourceStamp(source);
                bool newerClient = sameClient && pack.SourceStamp.Length > 0 && stamp.Length > 0 && pack.SourceStamp != stamp;
                if (!oldPacker && !newerClient) continue;
                string marker = Path.Combine(pack.Dir, "afk-repack-tried.txt");
                string state = RacePacker.PackerVersion + "|" + (stamp ?? "");
                try { if (File.Exists(marker) && File.ReadAllText(marker).Trim() == state) continue; } catch { }
                string line = sameClient || pack.ClientLabel.Length == 0 ? Repack(inst, client, pack.Key, progress) : null;
                if (line != null)
                {
                    try { if (File.Exists(marker)) File.Delete(marker); } catch { }
                    done.Add(newerClient && !oldPacker
                        ? L.F("{0}: packed again from the newer {1} in {2}.", pack.Name, pack.Source, source)
                        : line);
                }
                else try { File.WriteAllText(marker, state); } catch { }
            }
            return done;
        }

        /// <summary>The client the imported races are packed again from: the files\client folder of CoA Custom next to
        /// its client_root (the Custom races page), else client_root itself; null when none is chosen or there.</summary>
        static string SourceFolder(Install inst)
        {
            string root = Get(inst, "CustomRaces.ClientRootFolder");
            if (root.Length == 0) return null;
            string files = Path.GetDirectoryName(root.TrimEnd('\\', '/'));
            if (files != null && Directory.Exists(Os.Resolve(files, "client"))) return Os.Resolve(files, "client");
            return Directory.Exists(root) ? root : null;
        }

        /// <summary>The number of a race's row in the game client (CustomRaces.X.Source; 0 for an imported race).</summary>
        static int SourceOf(Install inst, string key)
        {
            int n;
            return int.TryParse(Get(inst, "CustomRaces." + key + ".Source").Trim('"'), out n) ? n : 0;
        }

        /// <summary>Whether another race of the settings has the number already.</summary>
        static bool NumberTaken(Install inst, string key, int number)
        {
            string path = Path.Combine(inst.ConfigDir, "modules", Conf);
            if (!File.Exists(path)) return false;
            foreach (Match m in Regex.Matches(File.ReadAllText(path), @"(?m)^[ \t]*CustomRaces\.([A-Za-z0-9_]+)\.Id[ \t]*=[ \t]*""?(\d+)"))
                if (!m.Groups[1].Value.Equals(key, StringComparison.OrdinalIgnoreCase) && m.Groups[2].Value == number.ToString()) return true;
            return false;
        }

        /// <summary>Packs an imported race again from the client it came from (CoA Custom's folder of the Custom races
        /// page): a race folder made before a fix of the packer, or from an older CoA Custom, lacks files. Null when that
        /// client is not there or does not have the race.</summary>
        internal static string Repack(Install inst, string client, string key, Action<string> progress)
        {
            try
            {
                string folder = Get(inst, "CustomRaces.ImportFolder");
                var pack = folder.Length > 0 ? RaceImport.Find(folder, key) : null;
                string source = SourceFolder(inst);
                if (pack == null || source == null) return null;
                if (progress != null) progress(L.F("Packing {0} again from {1} …", pack.Name, source));
                using (var src = RacePacker.Open(source, client))
                using (var yours = new RacePacker.Archives(ClientPatch.ClientArchives(client)))
                {
                    var candidates = RacePacker.Races(src, client, folder);
                    var race = candidates.FirstOrDefault(c => pack.SourceRace > 0 && c.Id == pack.SourceRace && c.Key.Equals(pack.Key, StringComparison.OrdinalIgnoreCase))
                        ?? candidates.FirstOrDefault(c => c.Key.Equals(pack.Key, StringComparison.OrdinalIgnoreCase))
                        ?? candidates.FirstOrDefault(c => pack.SourceRace > 0 && c.Id == pack.SourceRace);
                    if (race == null) return null;
                    uint[] spells = ServerRacials.ClientSpells(inst.DataDir, race.Name, race.Id);
                    RacePacker.Pack(src, yours, race, folder, RacePacker.SourceLabel(source), spells, text => { if (progress != null) progress(text); });
                    return L.F("{0}: packed again from {1}, with the files it lacked.", pack.Name, source);
                }
            }
            catch { return null; }
        }
    }
}
