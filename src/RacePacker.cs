using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace CoAInstaller
{
    /// <summary>
    /// Makes race packs for the race folder out of another game client or repack: for each race its rows of the client
    /// tables (race, models and displays, skins and faces, hair, beards and their HD rows) and the files it needs (its
    /// models with their skins, animations and textures, the textures of its rows, its icon, and everything else its own
    /// folder in the client holds: an appearance patch of a creator loads further files itself, they are not all named in
    /// the tables or in the model header). Files the player's own game client has byte for byte are left out: the game has
    /// them anyway. After that, the other client can be deleted.
    ///
    /// The other client may be a whole game folder (with Data\), just a folder of patch archives (CoA Custom ships its
    /// races as patches for the CoA client), or the raw files of a creator (an installer package's "files" tree for
    /// example): its archives are read before those of the player's client, an archive of the player's client with the same
    /// name as one of it is left out (a patch it replaces), and its raw files step in for what the archives do not have.
    /// </summary>
    static class RacePacker
    {
        /// <summary>The version of the packer, written into every race pack (race.json "packer"). Raise it whenever the
        /// packer takes other or more files than before: a pack made by an older one is packed again by itself at the next
        /// install of the client patch, as long as the client it came from is still there (RaceRepair.RefreshOldPacks).
        /// 1: until 1.0.9; 2: helmets, display textures, the own model folder and voice (1.0.10); 3: files another client
        /// changed under the same name, female-only races (1.1.5); 4: hair shapes of the model that
        /// CharHairGeosets never named (1.1.15); 5: the original hair textures for models made for them, where an HD patch
        /// replaced them (1.1.16); 6: all textures borrowed from such a stock race - skin, face, scalp, beard, underwear
        /// (1.1.17), and no HD scalp layers where the original has none; 7: Darkfallen's own skin no longer keeps the
        /// HD eye that landed on the classic model's forehead (1.1.18); 8: its painted red eye is removed from the face
        /// rows too, so only the eye-glow layer is still red (1.1.19); 9: the male Darkfallen model uses the red eye
        /// glow its female model uses (1.1.20); 10: Darkfallen is packed on the player's HD Blood Elf model and HD
        /// sections instead of its own classic model (1.1.21).</summary>
        public const int PackerVersion = 10;

        /// <summary>A set of archives, read as the game reads them: the one it reads last wins. Files that lie around
        /// unpacked (the raw files some creators ship) are read after all archives, they only step in for what the
        /// archives do not have.</summary>
        public sealed class Archives : IDisposable
        {
            readonly List<MpqArchive> list = new List<MpqArchive>();      // the one read last first
            readonly List<Loose> raw = new List<Loose>();
            public readonly List<string> Unreadable = new List<string>();
            public readonly Dictionary<string, DbcTable> Tables = new Dictionary<string, DbcTable>(StringComparer.OrdinalIgnoreCase);
            /// <summary>What the other client was when it was opened (SourceStamp); empty for the player's own.</summary>
            public string Stamp = "";
            List<string> known;                                           // every name the archives know, the most important first
            Dictionary<string, List<string>> byName;                      // file name -> the names that share it

            public Archives(IEnumerable<string> inLoadOrder)
            {
                foreach (var path in Enumerable.Reverse(inLoadOrder))
                    try { list.Add(new MpqArchive(path)); } catch { Unreadable.Add(Path.GetFileName(path)); }
            }
            public void AddRaw(Loose files)
            {
                if (files.Count == 0) return;
                raw.Add(files);
                known = null;
                byName = null;
            }
            public int Count { get { return list.Count + raw.Count; } }
            object Holder(string name)
            {
                foreach (var a in list) if (a.Contains(name)) return a;
                foreach (var r in raw) if (r.Has(name)) return r;
                return null;
            }
            public bool Has(string name) { return Holder(name) != null; }
            public long SizeOf(string name)
            {
                object h = Holder(name);
                if (h is MpqArchive) return ((MpqArchive)h).SizeOf(name);
                return h is Loose ? ((Loose)h).SizeOf(name) : -1;
            }
            public byte[] Read(string name)
            {
                object h = Holder(name);
                if (h is MpqArchive) return ((MpqArchive)h).Read(name);
                return h is Loose ? ((Loose)h).Read(name) : null;
            }
            /// <summary>A file as Blizzard shipped it: from the game's own archives (common, expansion, lichking, patch,
            /// patch-2, patch-3 and their locale archives), not from an HD or other patch put on top. Null when none has it.</summary>
            public byte[] ReadOriginal(string name)
            {
                foreach (var a in list)
                    if (Blizzard.IsMatch(Path.GetFileName(a.Path)) && a.Contains(name)) return a.Read(name);
                return null;
            }
            static readonly Regex Blizzard = new Regex(@"^((common|common-2|expansion|lichking|patch|patch-2|patch-3)|((locale|expansion-locale|lichking-locale|speech|expansion-speech|lichking-speech|patch)-(enUS|enGB|deDE|frFR|esES|esMX|ruRU|koKR|zhCN|zhTW|ptBR|itIT)(-2|-3)?))\.mpq$", RegexOptions.IgnoreCase);

            /// <summary>Every file name the archives list in their (listfile) and the raw folders hold, the most
            /// important first.</summary>
            public List<string> Names()
            {
                if (known != null) return known;
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var result = new List<string>();
                foreach (var a in list)
                {
                    List<string> names;
                    try { names = a.Names(); } catch { continue; }
                    foreach (var n in names) if (seen.Add(n)) result.Add(n);
                }
                foreach (var r in raw)
                    foreach (var n in r.Names) if (seen.Add(n)) result.Add(n);
                return known = result;
            }

            /// <summary>The names of the files below a folder.</summary>
            public List<string> Under(string folder)
            {
                string under = folder.Replace('/', '\\').TrimEnd('\\') + "\\";
                var result = new List<string>();
                foreach (var n in Names()) if (n.StartsWith(under, StringComparison.OrdinalIgnoreCase)) result.Add(n);
                return result;
            }

            static string FolderOf(string name) { int at = name.LastIndexOf('\\'); return at < 0 ? "" : name.Substring(0, at); }
            static string BaseOf(string name) { int at = name.LastIndexOf('\\'); return name.Substring(at + 1); }

            /// <summary>The name of a file, or the closest name the archives have instead (the same file name from
            /// another folder, or under .mdx), or null.</summary>
            public string Find(string name)
            {
                if (Has(name)) return name;
                var tries = new List<string> { name };
                string m2 = M2(name);
                if (!m2.Equals(name, StringComparison.OrdinalIgnoreCase)) tries.Add(m2);
                if (name.EndsWith(".m2", StringComparison.OrdinalIgnoreCase)) tries.Add(name.Substring(0, name.Length - 3) + ".mdx");
                foreach (var want in tries) if (!want.Equals(name, StringComparison.OrdinalIgnoreCase) && Has(want)) return want;
                if (byName == null)
                {
                    byName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                    foreach (var n in Names())
                    {
                        string b = BaseOf(n);
                        List<string> had;
                        if (!byName.TryGetValue(b, out had)) byName[b] = had = new List<string>();
                        had.Add(n);
                    }
                }
                foreach (var want in tries)
                {
                    List<string> candidates;
                    if (!byName.TryGetValue(BaseOf(want), out candidates)) continue;
                    // of files with the same name the one from the most similar folder
                    var wantParts = FolderOf(want).Split('\\');
                    string best = null;
                    int score = -1;
                    foreach (var c in candidates)
                    {
                        var cParts = FolderOf(c).Split('\\');
                        int s = 0;
                        while (s < wantParts.Length && s < cParts.Length &&
                            wantParts[wantParts.Length - 1 - s].Equals(cParts[cParts.Length - 1 - s], StringComparison.OrdinalIgnoreCase)) s++;
                        if (s > score) { score = s; best = c; }
                    }
                    if (best != null) return best;
                }
                return null;
            }

            public void Dispose() { foreach (var a in list) a.Dispose(); }
        }

        /// <summary>Files that lie around unpacked, by their path from the folder they were given in.</summary>
        public sealed class Loose : IDisposable
        {
            readonly Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public Loose(string root) { Add(root, "", 12); }

            void Add(string folder, string under, int depth)
            {
                if (depth == 0) return;
                string[] files, folders;
                try { files = Directory.GetFiles(folder); } catch { return; }
                try { folders = Directory.GetDirectories(folder); } catch { folders = new string[0]; }
                foreach (var f in files) map[under + Path.GetFileName(f)] = f;
                foreach (var d in folders) Add(d, under + Path.GetFileName(d) + "\\", depth - 1);
            }

            static string Key(string name) { return name.Replace('/', '\\').TrimStart('\\'); }
            public int Count { get { return map.Count; } }
            public bool Has(string name) { return map.ContainsKey(Key(name)); }
            public long SizeOf(string name)
            {
                string f;
                if (!map.TryGetValue(Key(name), out f)) return -1;
                try { return new FileInfo(f).Length; } catch { return -1; }
            }
            public byte[] Read(string name)
            {
                string f;
                if (!map.TryGetValue(Key(name), out f)) return null;
                try { return File.ReadAllBytes(f); } catch { return null; }
            }
            public IEnumerable<string> Names { get { foreach (var name in map.Keys) yield return name; } }
            public void Dispose() { }
        }

        /// <summary>A race the other client offers.</summary>
        public class Candidate
        {
            public int Id;
            public string Key, Name;
            public bool MaleOnly;
            public bool FemaleOnly;       // a woman only: no man of its own (CoA Custom's Banshee, Harpy, Alpha Goblin)
            public bool InYourClient;     // the player's client has a race of this name too
            public bool InRaceFolder;     // the race folder has it already
            public override string ToString() { return Name; }
        }

        /// <summary>The archives of a folder in the order the game reads them: a game folder (Data\ and its language
        /// folder), the Data folder itself, or any folder with patch archives somewhere below it.</summary>
        public static List<string> ArchivesOf(string folder)
        {
            if (Directory.Exists(Os.Resolve(folder, "Data"))) return ClientPatch.ClientArchives(folder, true);
            if (Path.GetFileName(folder.TrimEnd('\\', '/')).Equals("Data", StringComparison.OrdinalIgnoreCase)) return ClientPatch.ClientArchives(Path.GetDirectoryName(folder.TrimEnd('\\', '/')), true);
            // a game folder further down (a repack unpacked into a folder of its own)
            foreach (var dir in Below(folder, 3))
                if (Directory.Exists(Os.Resolve(dir, "Data")) && Directory.GetFiles(Os.Resolve(dir, "Data")).Any(f => f.EndsWith(".mpq", StringComparison.OrdinalIgnoreCase))) return ClientPatch.ClientArchives(dir, true);
            var found = new List<string>();
            foreach (var dir in new[] { folder }.Concat(Below(folder, 4)))
                found.AddRange(Directory.GetFiles(dir).Where(f => f.EndsWith(".mpq", StringComparison.OrdinalIgnoreCase)));
            Func<string, int> rank = path =>
            {
                string name = Path.GetFileName(path).ToLowerInvariant();
                if (!name.StartsWith("patch")) return 0;
                return name == "patch.mpq" ? 1 : Regex.IsMatch(name, @"^patch-\d\.mpq$") ? 2 : 3;
            };
            return found.OrderBy(rank).ThenBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase).ToList();
        }

        static IEnumerable<string> Below(string folder, int depth)
        {
            if (depth == 0) yield break;
            string[] dirs;
            try { dirs = Directory.GetDirectories(folder); } catch { yield break; }
            foreach (var d in dirs.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                yield return d;
                foreach (var e in Below(d, depth - 1)) yield return e;
            }
        }

        /// <summary>The other client on top of the player's: its archives first, then those of the player's client it
        /// does not replace. A folder that is no game client (a package with raw files in it, the raw files of a creator)
        /// has its files added, they step in for what the archives do not have.</summary>
        public static Archives Open(string other, string yourClient)
        {
            var theirs = ArchivesOf(other);
            var names = new HashSet<string>(theirs.Select(Path.GetFileName), StringComparer.OrdinalIgnoreCase);
            var yours = yourClient == null ? new List<string>() : ClientPatch.ClientArchives(yourClient).Where(p => !names.Contains(Path.GetFileName(p)));
            // read order: the player's archives first, then the other client's (the last read wins)
            var archives = new Archives(yours.Concat(theirs).ToList());
            if (!IsClient(other)) archives.AddRaw(new Loose(other));
            if (archives.Count == 0) throw new InvalidOperationException(L.F("There are no game archives (.MPQ) in {0}.", other));
            archives.Stamp = SourceStamp(other);
            return archives;
        }

        /// <summary>
        /// What a client the races are packed from is right now: the CoA Custom version AFK Realm downloaded into the
        /// folder (when it did) and the names and sizes of its archives. Written into every race pack ("sourceStamp"):
        /// when it is different at a client install, a newer CoA Custom (or another version of the other client) is
        /// there, and its races are packed again by themselves (RaceRepair.RefreshOldPacks).
        /// </summary>
        public static string SourceStamp(string other)
        {
            try
            {
                if (string.IsNullOrEmpty(other) || !Directory.Exists(other)) return "";
                string version = null;
                string dir = other.TrimEnd('\\', '/');
                for (int up = 0; up < 4 && !string.IsNullOrEmpty(dir) && version == null; up++, dir = Path.GetDirectoryName(dir))
                    version = CoaCustom.Installed(dir);
                var text = new StringBuilder();
                foreach (var f in ArchivesOf(other).Select(f => new FileInfo(f)).OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
                    text.Append(f.Name.ToLowerInvariant()).Append(':').Append(f.Length).Append(';');
                using (var sha = System.Security.Cryptography.SHA1.Create())
                {
                    string hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-", "").Substring(0, 16).ToLowerInvariant();
                    return (version != null ? version + "/" : "") + hash;
                }
            }
            catch { return ""; }
        }

        /// <summary>Whether a folder is a game client: its archives hold everything it has, files lying around next to
        /// them (logs, notes, tools) are not game content.</summary>
        static bool IsClient(string folder)
        {
            if (Path.GetFileName(folder.TrimEnd('\\', '/')).Equals("Data", StringComparison.OrdinalIgnoreCase)) return true;
            foreach (var dir in new[] { folder }.Concat(Below(folder, 3)))
            {
                string data = Os.Resolve(dir, "Data");
                if (Directory.Exists(data) && Directory.GetFiles(data).Any(f => f.EndsWith(".mpq", StringComparison.OrdinalIgnoreCase))) return true;
            }
            return false;
        }

        /// <summary>Whether a DLL is a 64-race patch of the game client (it names itself so in its texts).</summary>
        public static bool IsRacePatch(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length > 8000000) return false;
                return Encoding.ASCII.GetString(File.ReadAllBytes(path)).Contains("64-race support");
            }
            catch { return false; }
        }

        /// <summary>The 64-race patch an other client brings (CoA Custom: dinput8.dll), or null.</summary>
        public static string FindRacePatch(string folder)
        {
            foreach (var dir in new[] { folder }.Concat(Below(folder, 4)))
            {
                string f = Os.Resolve(dir, "dinput8.dll");
                if (File.Exists(f) && IsRacePatch(f)) return f;
            }
            return null;
        }

        /// <summary>Keeps the 64-race patch of an other client in the race folder (_Client\dinput8.dll). A note, or null.</summary>
        public static string KeepRacePatch(string folder, string raceFolder)
        {
            string found = FindRacePatch(folder);
            if (found == null) return null;
            string to = Path.Combine(raceFolder, "_Client", "dinput8.dll");
            if (File.Exists(to) && File.ReadAllBytes(to).SequenceEqual(File.ReadAllBytes(found))) return null;
            Directory.CreateDirectory(Path.GetDirectoryName(to));
            File.Copy(found, to, true);
            return L.T("Its 64-race patch (dinput8.dll) is kept in your race folder too (_Client).");
        }

        /// <summary>A short name of the other client for the race folder.</summary>
        public static string SourceLabel(string folder)
        {
            string f = folder.ToLowerInvariant();
            if (f.Contains("coa-custom") || f.Contains("coa_custom")) return "cc";
            if (f.Contains("ashen")) return "ashen";
            if (f.Contains("from_the_ashes") || f.Contains("from the ashes")) return "fta";
            return Path.GetFileName(folder.TrimEnd('\\', '/'));
        }

        static string Str(DbcTable t, byte[] row, int field)
        {
            uint at = t.Field(row, field);
            if (at >= t.Strings.Length) return "";
            int end = (int)at;
            while (end < t.Strings.Length && t.Strings[end] != 0) end++;
            return Encoding.UTF8.GetString(t.Strings, (int)at, end - (int)at);
        }

        static DbcTable Table(Archives a, string name)
        {
            DbcTable t;
            if (a.Tables.TryGetValue(name, out t)) return t;
            var bytes = a.Read("DBFilesClient\\" + name + ".dbc");
            return a.Tables[name] = bytes == null ? null : DbcTable.Parse(bytes, name + ".dbc");
        }

        static string M2(string path) { return Regex.Replace(path ?? "", @"\.(mdx|mdl)$", ".m2", RegexOptions.IgnoreCase); }

        /// <summary>Whether the archives have a file, wherever it lies (see Archives.Find).</summary>
        static bool HasFile(Archives source, string path)
        {
            path = M2(path);
            try { return source.Has(path) || source.Find(path) != null; } catch { return false; }
        }

        static string NameOf(DbcTable races, byte[] row) { return Str(races, row, 14).Split('|')[0].Trim(); }

        /// <summary>The races of the other client beyond the stock ones that have a body there.</summary>
        public static List<Candidate> Races(Archives source, string yourClient, string raceFolder)
        {
            var races = Table(source, "ChrRaces");
            if (races == null) throw new InvalidOperationException(L.T("These archives have no race table (DBFilesClient\\ChrRaces.dbc)."));
            var di = Table(source, "CreatureDisplayInfo");
            var md = Table(source, "CreatureModelData");
            var displayModel = di == null ? new Dictionary<uint, uint>() : di.Records.GroupBy(r => di.Field(r, 0)).ToDictionary(g => g.Key, g => di.Field(g.First(), 1));
            var modelPath = md == null ? new Dictionary<uint, string>() : md.Records.GroupBy(r => md.Field(r, 0)).ToDictionary(g => g.Key, g => Str(md, g.First(), 2));
            Func<uint, bool> hasBody = display =>
            {
                uint m; string p;
                return displayModel.TryGetValue(display, out m) && modelPath.TryGetValue(m, out p) && p.Length > 0 && HasFile(source, p);
            };
            var sections = Table(source, "CharSections");
            var skins = new Dictionary<long, int>();
            if (sections != null)
                foreach (var r in sections.Records)
                {
                    long k = sections.Field(r, 1) * 2L + sections.Field(r, 2);
                    int c; skins.TryGetValue(k, out c); skins[k] = c + 1;
                }

            var yourRaces = new List<ClientRace>();
            if (yourClient != null)
                try { yourRaces = ClientRaces.Read(yourClient, null).Races; } catch { }
            // The packs already in the folder: their keys are kept when the same source race is packed again, and
            // their source row identifies them even when an older version named the key differently.
            var packs = RaceImport.Read(raceFolder).ToList();
            var packByRace = new Dictionary<int, RaceImport.Pack>();
            foreach (var p in packs)
                if (p.SourceRace > 0 && !packByRace.ContainsKey(p.SourceRace)) packByRace[p.SourceRace] = p;

            var rows = races.Records.GroupBy(r => (int)races.Field(r, 0)).ToDictionary(g => g.Key, g => g.First());
            var stockNames = new HashSet<string>(ClientRaces.Stock.Where(rows.ContainsKey).Select(id => NameOf(races, rows[id])), StringComparer.OrdinalIgnoreCase);
            // Only player races: the client tables also carry NPC rows under names of their own (Northrend
            // Skeleton, Leper Troll) and old copies of playable rows (Taunka 41 beside Taunka 17). Flags 4 and 8
            // are the ones the game's character creation checks.
            var eligible = new List<byte[]>();
            foreach (var id in rows.Keys.OrderBy(i => i))
            {
                if (ClientRaces.Stock.Contains(id)) continue;
                var row = rows[id];
                string name = NameOf(races, row);
                if (ClientRaces.KeyOf(name).Length == 0 || stockNames.Contains(name)) continue;
                if ((races.Field(row, 1) & 0xC) != 0xC) continue;
                if (name.Equals("Test Race", StringComparison.OrdinalIgnoreCase)) continue;
                if (!hasBody(races.Field(row, 4))) continue;
                eligible.Add(row);
            }
            // All names, also those of NPC rows, are reserved: the Murloc's file is called Troglodyte, the name of
            // a row of its own, and taking that as a key would mix the two races up.
            var allNames = new HashSet<string>(rows.Values.Select(r => ClientRaces.KeyOf(NameOf(races, r))).Where(k => k.Length > 0), StringComparer.OrdinalIgnoreCase);
            var nameCounts = eligible.GroupBy(r => ClientRaces.KeyOf(NameOf(races, r)), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<Candidate>();
            foreach (var row in eligible.OrderBy(r => (int)races.Field(r, 0)))
            {
                int id = (int)races.Field(row, 0);
                string name = NameOf(races, row);
                string file = Str(races, row, 11);
                RaceImport.Pack existing;
                string key;
                if (packByRace.TryGetValue(id, out existing) && existing.Key.Length > 0 && !used.Contains(existing.Key))
                    key = existing.Key;                         // the pack of this very race keeps its key
                else
                    key = ClientRaces.KeyFor(name, file, id, used, allNames);
                used.Add(key);
                int count; nameCounts.TryGetValue(ClientRaces.KeyOf(name), out count);
                string displayName = count > 1 ? ClientRaces.DisplayNameFor(name, file, (int)races.Field(row, 13), id) : name;
                uint male = races.Field(row, 4), female = races.Field(row, 5);
                // Whether the player's client has this race: the same key, the same row name and file, or the same
                // two displays. The key alone is not enough (both clients may use "Earthen" for different rows).
                bool inClient = yourRaces.Any(r => r.Key.Equals(key, StringComparison.OrdinalIgnoreCase) ||
                    (r.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && r.File.Equals(file, StringComparison.OrdinalIgnoreCase)) ||
                    (r.Source == id && r.File.Equals(file, StringComparison.OrdinalIgnoreCase)) ||
                    (male != 0 && r.MaleDisplay == male && r.FemaleDisplay == female));
                bool inFolder = packs.Any(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase) ||
                    (p.SourceRace == id && (p.SourceFile.Length == 0 || p.SourceFile.Equals(file, StringComparison.OrdinalIgnoreCase) ||
                        p.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || p.Name.Equals(displayName, StringComparison.OrdinalIgnoreCase))) ||
                    (p.SourceFile.Length > 0 && p.SourceFile.Equals(file, StringComparison.OrdinalIgnoreCase) &&
                        (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || p.Name.Equals(displayName, StringComparison.OrdinalIgnoreCase))));
                int femaleSkins; skins.TryGetValue(id * 2L + 1, out femaleSkins);
                // Esteria's Gnoll and Saberon have female rows that only reuse the male body; the runtime draws no
                // woman for them. Their source says "male only" outside the tables, so they are named here.
                bool maleOnly = femaleSkins == 0 || !hasBody(female) || RaceImport.KnownMaleOnly(key);
                // The other way round: a race with women only (no male rows, or one CoA Custom names female only)
                int maleSkins; skins.TryGetValue(id * 2L, out maleSkins);
                bool femaleOnly = (maleSkins == 0 && femaleSkins > 0) || RaceImport.KnownFemaleOnly(key) || RaceImport.KnownFemaleOnly(file);
                if (femaleOnly) maleOnly = false;
                list.Add(new Candidate { Id = id, Key = key, Name = displayName, MaleOnly = maleOnly, FemaleOnly = femaleOnly,
                    InYourClient = inClient, InRaceFolder = inFolder });
            }
            return list;
        }

        /// <summary>Whether the other client has a file of another content than the player's client under the same name:
        /// by size first, then byte by byte.</summary>
        static bool Differs(Archives source, Archives yours, string name)
        {
            try
            {
                long a = source.SizeOf(name), b = yours.SizeOf(name);
                if (a < 0 || b < 0) return false;
                if (a != b) return true;
                var x = source.Read(name);
                var y = yours.Read(name);
                if (x == null || y == null || x.Length != y.Length) return x != null && y != null;
                for (int i = 0; i < x.Length; i++) if (x[i] != y[i]) return true;
                return false;
            }
            catch { return false; }
        }

        /// <summary>The files a model needs: itself, its skins, its animations kept outside it and the textures named in it.</summary>
        static IEnumerable<string> ModelFiles(Archives source, string path)
        {
            path = M2(path);
            var files = new List<string> { path };
            byte[] b = null;
            try { b = source.Read(path); } catch { }
            if (b == null)
            {
                // the model may lie elsewhere (see Archives.Find); its skins and textures are read from it wherever it is
                string found = null;
                try { found = source.Find(path); } catch { }
                if (found != null) try { b = source.Read(found); } catch { b = null; }
            }
            if (b == null || b.Length < 0x60 || Encoding.ASCII.GetString(b, 0, 4) != "MD20") return files;
            string stem = path.Substring(0, path.Length - 3);
            Func<int, uint> u32 = at => at + 4 <= b.Length ? BitConverter.ToUInt32(b, at) : 0;
            uint views = u32(0x44);
            // the skins it has, wherever they lie, and a guess for each view it needs (which lands in the missing list
            // when the archives have none)
            foreach (var s in source.Under(stem))
                if (s.EndsWith(".skin", StringComparison.OrdinalIgnoreCase)) files.Add(s);
            for (int i = 0; i < views && i < 16; i++) files.Add(stem + i.ToString("00") + ".skin");
            uint count = u32(0x1C), at0 = u32(0x20);
            for (long i = 0; i < count && at0 + i * 64 + 64 <= b.Length; i++)
            {
                long at = at0 + i * 64;
                ushort anim = BitConverter.ToUInt16(b, (int)at), sub = BitConverter.ToUInt16(b, (int)at + 2);
                uint flags = u32((int)at + 12);
                if ((flags & 0x20) == 0 && (flags & 0x40) == 0) files.Add(stem + anim.ToString("0000") + "-" + sub.ToString("00") + ".anim");
            }
            count = u32(0x50); at0 = u32(0x54);
            for (long i = 0; i < count && at0 + i * 16 + 16 <= b.Length; i++)
            {
                long at = at0 + i * 16;
                uint type = u32((int)at), length = u32((int)at + 8), offset = u32((int)at + 12);
                if (type != 0 || length < 2 || offset + length > b.Length) continue;
                // the name up to its first NUL: some models pad it, some count no NUL at all
                int end = (int)offset;
                while (end < offset + length && b[end] != 0) end++;
                string name = Encoding.UTF8.GetString(b, (int)offset, end - (int)offset).Trim();
                if (name.Length > 0) files.Add(name);
            }
            return files;
        }

        /// <summary>
        /// The hair shapes of a race: its CharHairGeosets rows, and more where its model has hair shapes the table never
        /// named while its sections offer those styles. CoA Custom's Darkfallen woman has 20 hair shapes in her model
        /// (geosets 2-21, those of the Blood Elf) and 40 hair styles in CharSections, but only styles 0-9 in
        /// CharHairGeosets: every other style froze the creation and was drawn with a wrong shape after the next login.
        /// Only rows that follow one rule (shape = style + a fixed step) are carried on, up to the last shape of the model.
        /// </summary>
        static List<object> HairGeosets(Archives source, List<object> rows, List<object> sections, Dictionary<string, object> models)
        {
            var result = new List<object>(rows);
            if (rows.Count == 0) return result;
            foreach (int sex in new[] { 0, 1 })
            {
                var mine = rows.OfType<object[]>().Where(r => Convert.ToInt32(r[2]) == sex).OrderBy(r => Convert.ToInt32(r[3])).ToList();
                if (mine.Count == 0) continue;
                int step = Convert.ToInt32(mine[0][4]) - Convert.ToInt32(mine[0][3]);
                if (mine.Any(r => Convert.ToInt32(r[4]) - Convert.ToInt32(r[3]) != step)) continue;      // no single rule
                object m;
                if (!models.TryGetValue(sex == 0 ? "male" : "female", out m) || !(m is Dictionary<string, object>)) continue;
                string path = M2(Convert.ToString(((Dictionary<string, object>)m)["path"]));
                var shapes = HairShapes(source, path);
                if (shapes.Count == 0) continue;
                var styles = new HashSet<int>(sections.OfType<object[]>().Where(r => Convert.ToInt32(r[2]) == sex && Convert.ToInt32(r[3]) == 3).Select(r => Convert.ToInt32(r[8])));
                var have = new HashSet<int>(mine.Select(r => Convert.ToInt32(r[3])));
                uint id = rows.OfType<object[]>().Select(r => Convert.ToUInt32(r[0])).DefaultIfEmpty(0u).Max() + 1;
                foreach (int style in styles.OrderBy(x => x))
                {
                    if (have.Contains(style) || !shapes.Contains(style + step)) continue;
                    var copy = (object[])mine.Last().Clone();
                    copy[0] = id++;
                    copy[3] = (uint)style;
                    copy[4] = (uint)(style + step);
                    result.Add(copy);
                }
            }
            return result;
        }

        /// <summary>
        /// A race whose model is a stock race's model as Blizzard shipped it, in a client where an HD patch replaced that
        /// race's model and textures with others of another layout. CoA Custom's Darkfallen is the classic Blood Elf model
        /// under another name and borrows the Blood Elf's skins, faces, scalps and hair (Character\BloodElf\...); the
        /// Descension client's HD patch (patch-Q) has those laid out for the HD Blood Elf: on the Darkfallen the hair,
        /// face, ears, brows, beards and earrings showed pieces of other textures. Whether a model was made for the
        /// original or the replacement is told by where it lays its body and its hair on their textures, compared with
        /// the stock race's original and replaced model. When it fits the original, every texture it borrows from that
        /// race is the original one: the file of the game's own archives, or - for a name only the HD patch has - the
        /// texture Blizzard's own CharSections gives the stock race in the same place, or no layer where Blizzard has none
        /// (the classic Blood Elf has no scalp textures). They go into the race's own
        /// folder (&lt;model folder&gt;\&lt;stock race&gt;&lt;file name&gt;) and its rows point there; the stock race keeps the HD ones.
        /// Returns the files to add to the pack.
        /// </summary>
        static Dictionary<string, byte[]> OriginalTextures(Archives source, DbcTable cr, Dictionary<string, object> models, List<object> sections, List<object> hdSections, HashSet<string> dropped)
        {
            var added = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            DbcTable blizzard = null;
            bool blizzardRead = false;
            for (int sex = 0; sex < 2; sex++)
            {
                try
                {
                    object m;
                    if (!models.TryGetValue(sex == 0 ? "male" : "female", out m) || !(m is Dictionary<string, object>)) continue;
                    string model = M2(Convert.ToString(((Dictionary<string, object>)m)["path"]));
                    if (!model.EndsWith(".m2", StringComparison.OrdinalIgnoreCase) || !model.Contains("\\")) continue;
                    string modelDir = model.Substring(0, model.LastIndexOf('\\') + 1);
                    var mine = ModelCells(source.Read(model), source.Read(model.Substring(0, model.Length - 3) + "00.skin"));
                    if (mine.Count == 0) continue;
                    string sexName = sex == 0 ? "Male" : "Female";
                    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    var verdict = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);    // stock folder -> use the originals
                    var stray = new HashSet<string>(StringComparer.OrdinalIgnoreCase);    // textures only the HD patch has, in no original place
                    var rows = sections.Concat(hdSections).OfType<object[]>().Where(r => Convert.ToInt32(r[2]) == sex).ToList();
                    foreach (var r in rows)
                        for (int k = 4; k <= 6 && k < r.Length; k++)
                        {
                            string tex = r[k] as string;
                            if (string.IsNullOrEmpty(tex) || tex.StartsWith(modelDir, StringComparison.OrdinalIgnoreCase) || map.ContainsKey(tex)) continue;
                            var parts = tex.Split('\\');
                            if (parts.Length < 3 || !parts[0].Equals("Character", StringComparison.OrdinalIgnoreCase)) continue;   // Character\<stock race>\...
                            string folder = parts[1];
                            bool original;
                            if (!verdict.TryGetValue(folder, out original))
                            {
                                string stock = "Character\\" + folder + "\\" + sexName + "\\" + folder + sexName;
                                byte[] beforeM2 = source.ReadOriginal(stock + ".m2"), nowM2 = source.Read(stock + ".m2");
                                original = false;
                                if (beforeM2 != null && nowM2 != null && !beforeM2.SequenceEqual(nowM2))
                                {
                                    var before = ModelCells(beforeM2, source.ReadOriginal(stock + "00.skin"));
                                    var now = ModelCells(nowM2, source.Read(stock + "00.skin"));
                                    original = before.Count > 0 && now.Count > 0 && Jaccard(mine, before) > Jaccard(mine, now) + 0.2;
                                }
                                verdict[folder] = original;
                            }
                            if (!original) continue;
                            byte[] top = source.Read(tex), first = source.ReadOriginal(tex);
                            if (first == null)
                            {
                                // a name only the HD patch has (or a copy of an HD file under a name of the original): the
                                // texture Blizzard gives the stock race in this place
                                if (!blizzardRead)
                                {
                                    blizzardRead = true;
                                    var b = source.ReadOriginal("DBFilesClient\\CharSections.dbc");
                                    if (b != null) try { blizzard = DbcTable.Parse(b, "CharSections.dbc"); } catch { }
                                }
                                string instead = StockTexture(blizzard, cr, folder, r, k);
                                if (instead != null) first = source.ReadOriginal(instead);
                                if (first == null && k != 4 && blizzard != null)
                                {
                                    // the original has nothing there (the classic Blood Elf has no scalp layers at all, the
                                    // HD ones painted the HD hairline into the classic face): the layer is left empty
                                    map[tex] = "";
                                    continue;
                                }
                            }
                            if (first == null && k == 4) { stray.Add(tex); continue; }
                            if (first == null || (top != null && first.SequenceEqual(top))) continue;
                            string to = modelDir + folder + tex.Substring(tex.LastIndexOf('\\') + 1);
                            map[tex] = to;
                            added[to] = first;
                        }
                    if (map.Count == 0) continue;
                    foreach (var r in rows)
                        for (int k = 4; k <= 6 && k < r.Length; k++)
                        {
                            string tex = r[k] as string, to;
                            if (tex != null && map.TryGetValue(tex, out to)) r[k] = to;
                        }
                    // a colour only the HD patch has (the HD Blood Elf man's hair colours 13-15): the original texture of
                    // the nearest colour of the same look, so the slider keeps its steps
                    var originalNow = new HashSet<string>(added.Keys, StringComparer.OrdinalIgnoreCase);
                    foreach (var r in rows.Where(r => stray.Contains(r[4] as string ?? "")))
                    {
                        var near = rows.Where(o => Convert.ToInt32(o[3]) == Convert.ToInt32(r[3]) && originalNow.Contains(o[4] as string ?? ""))
                            .OrderBy(o => Convert.ToInt64(o[8]) == Convert.ToInt64(r[8]) ? 0 : 1)
                            .ThenBy(o => Math.Abs(Convert.ToInt64(o[9]) - Convert.ToInt64(r[9])))
                            .FirstOrDefault();
                        if (near != null) r[4] = near[4];
                    }
                    foreach (var e in map) if (e.Value.Length == 0) dropped.Add(e.Key);
                }
                catch { }
            }
            return added;
        }

        /// <summary>The texture Blizzard's CharSections gives a stock race (by its folder) in the place of a row:
        /// same sex, section, variation and colour - or, without such a row, the same sex, section and colour.</summary>
        static string StockTexture(DbcTable blizzard, DbcTable cr, string folder, object[] row, int field)
        {
            if (blizzard == null || cr == null) return null;
            var stock = cr.Records.FirstOrDefault(c => Str(cr, c, 11).Equals(folder, StringComparison.OrdinalIgnoreCase));
            if (stock == null) return null;
            uint race = cr.Field(stock, 0), sex = Convert.ToUInt32(row[2]), section = Convert.ToUInt32(row[3]), variation = Convert.ToUInt32(row[8]), colour = Convert.ToUInt32(row[9]);
            var mine = blizzard.Records.Where(b => blizzard.Field(b, 1) == race && blizzard.Field(b, 2) == sex && blizzard.Field(b, 3) == section).ToList();
            var hit = mine.FirstOrDefault(b => blizzard.Field(b, 8) == variation && blizzard.Field(b, 9) == colour)
                      ?? mine.Where(b => blizzard.Field(b, 9) == colour).OrderBy(b => blizzard.Field(b, 8)).FirstOrDefault();
            if (hit == null || blizzard.Field(hit, field) == 0) return null;
            string tex = Str(blizzard, hit, field);
            return tex.Length == 0 ? null : tex;
        }

        static double Jaccard(HashSet<int> a, HashSet<int> b)
        {
            int both = a.Count(b.Contains);
            int any = a.Count + b.Count - both;
            return any == 0 ? 0 : (double)both / any;
        }

        /// <summary>Where a model lays its body (type 1, the skin texture) and its hair (geosets 1-99, type 6) on their
        /// textures: the cells of a 32x32 grid they reach, the body's below 10000 and the hair's above. A model made for
        /// other textures differs in both. Empty when the model cannot be read.</summary>
        static HashSet<int> ModelCells(byte[] m2, byte[] skin) { return Cells(m2, skin, 32, true); }

        static HashSet<int> Cells(byte[] m2, byte[] skin, int grid, bool body)
        {
            var cells = new HashSet<int>();
            try
            {
                if (m2 == null || skin == null || m2.Length < 140 || skin.Length < 48) return cells;
                if (Encoding.ASCII.GetString(m2, 0, 4) != "MD20" || Encoding.ASCII.GetString(skin, 0, 4) != "SKIN") return cells;
                Func<byte[], int, int> i32 = (b, o) => BitConverter.ToInt32(b, o);
                Func<byte[], int, int> u16 = (b, o) => BitConverter.ToUInt16(b, o);
                int nv = i32(m2, 60), ov = i32(m2, 64), nt = i32(m2, 80), ot = i32(m2, 84), nl = i32(m2, 128), ol = i32(m2, 132);
                int ni = i32(skin, 4), oi = i32(skin, 8), nsub = i32(skin, 28), osub = i32(skin, 32), nb = i32(skin, 36), ob = i32(skin, 40);
                for (int i = 0; i < nb; i++)
                {
                    int at = ob + i * 24;
                    int section = u16(skin, at + 4), count = u16(skin, at + 14), combo = u16(skin, at + 16);
                    if (section >= nsub || count == 0 || combo >= nl) continue;
                    int texture = u16(m2, ol + combo * 2);
                    int type = texture >= nt ? -1 : i32(m2, ot + texture * 16);
                    int sub = osub + section * 48;
                    int id = u16(skin, sub);
                    bool hair = type == 6 && id >= 1 && id <= 99;
                    if (!hair && !(body && type == 1)) continue;
                    int start = u16(skin, sub + 4) + (u16(skin, sub + 2) << 16), n = u16(skin, sub + 6);
                    for (int k = start; k < start + n && k < ni; k++)
                    {
                        int v = u16(skin, oi + k * 2);
                        if (v >= nv) continue;
                        float u = BitConverter.ToSingle(m2, ov + v * 48 + 32), w = BitConverter.ToSingle(m2, ov + v * 48 + 36);
                        if (float.IsNaN(u) || float.IsNaN(w)) continue;
                        u -= (float)Math.Floor(u); w -= (float)Math.Floor(w);
                        cells.Add((hair ? 10000 : 0) + Math.Min(grid - 1, (int)(w * grid)) * grid + Math.Min(grid - 1, (int)(u * grid)));
                    }
                }
            }
            catch { cells.Clear(); }
            return cells;
        }

        /// <summary>The hair shapes (geosets 1-99) the first view (.skin) of a model has.</summary>
        static HashSet<int> HairShapes(Archives source, string m2)
        {
            var shapes = new HashSet<int>();
            try
            {
                if (!m2.EndsWith(".m2", StringComparison.OrdinalIgnoreCase)) return shapes;
                byte[] b = source.Read(m2.Substring(0, m2.Length - 3) + "00.skin");
                if (b == null || b.Length < 36 || Encoding.ASCII.GetString(b, 0, 4) != "SKIN") return shapes;
                int count = BitConverter.ToInt32(b, 28), offset = BitConverter.ToInt32(b, 32);
                for (int i = 0; i < count && offset + i * 48 + 2 <= b.Length; i++)
                {
                    int id = BitConverter.ToUInt16(b, offset + i * 48);
                    if (id > 0 && id < 100) shapes.Add(id);
                }
            }
            catch { }
            return shapes;
        }

        static List<object> Rows(DbcTable t, int raceField, int race, params int[] strings)
        {
            var list = new List<object>();
            if (t == null) return list;
            foreach (var r in t.Records)
            {
                if (t.Field(r, raceField) != race) continue;
                var row = new object[t.Fields];
                for (int k = 0; k < t.Fields; k++) row[k] = strings.Contains(k) ? (object)(t.Field(r, k) == 0 ? "" : Str(t, r, k)) : t.Field(r, k);
                list.Add(row);
            }
            return list;
        }

        /// <summary>Whether a sex of the race has a body texture of its own, not a compositor or the neutral placeholder.</summary>
        static bool HasRealSkin(List<object> rows, int sex)
        {
            foreach (object[] r in rows)
            {
                if (Convert.ToInt32(r[2]) != sex || Convert.ToInt32(r[3]) != 0) continue;
                for (int k = 4; k <= 6 && k < r.Length; k++)
                {
                    string texture = Convert.ToString(r[k] ?? "").Trim();
                    if (texture.Length == 0) continue;
                    string name = Path.GetFileName(texture);
                    if (name.Equals("neutral.blp", StringComparison.OrdinalIgnoreCase)) continue;
                    if (name.IndexOf("compositor", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Whether a sex has a compositor as its body texture: the appearance DLL paints it, a client
        /// without that DLL would show the model's neutral placeholders.</summary>
        static bool HasCompositorSkin(List<object> rows, int sex)
        {
            foreach (object[] r in rows)
            {
                if (Convert.ToInt32(r[2]) != sex || Convert.ToInt32(r[3]) != 0) continue;
                for (int k = 4; k <= 6 && k < r.Length; k++)
                {
                    string texture = Convert.ToString(r[k] ?? "").Trim();
                    if (texture.IndexOf("compositor", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
            }
            return false;
        }

        static string ModelStem(string path) { return Path.GetFileNameWithoutExtension(M2(path ?? "")).ToLowerInvariant(); }

        /// <summary>
        /// Replaces compositor-only skins with the section rows of another race on the same model. The source rows
        /// only paint through the appearance DLL; a client that does not know the race number still shows the race's
        /// own skin, face and HD layers if another row of the same client has them (VulperaEsteria 74 and Vulpera 19).
        /// Neutral placeholders (Haranir 70/71) are left alone: they are the race's own texture, not a layer.
        /// </summary>
        static void RepairSections(Archives source, DbcTable cr, DbcTable di, DbcTable md, int raceId,
            Dictionary<string, object> models, List<object> sections, List<object> hdSections)
        {
            var cs = Table(source, "CharSections");
            var hd = Table(source, "HDCharSections");
            if (cs == null) return;
            var raceRow = cr.Records.First(r => cr.Field(r, 0) == raceId);
            string racePrefix = Str(cr, raceRow, 6), raceName = NameOf(cr, raceRow);
            uint raceSide = cr.Field(raceRow, 13);
            var stems = new string[2];
            for (int sex = 0; sex < 2; sex++)
            {
                object m;
                if (models.TryGetValue(sex == 0 ? "male" : "female", out m))
                    stems[sex] = ModelStem(Convert.ToString(((Dictionary<string, object>)m)["path"]));
            }
            Func<object[], int> sexOf = r => Convert.ToInt32(r[2]);
            for (int sex = 0; sex < 2; sex++)
            {
                if (string.IsNullOrEmpty(stems[sex])) continue;
                var mine = sections.Where(r => sexOf((object[])r) == sex).ToList();
                if (HasRealSkin(mine, sex) || !HasCompositorSkin(mine, sex)) continue;
                object donor = null;
                List<object> donorSections = null, donorHd = null;
                int best = -1;
                foreach (var other in cr.Records)
                {
                    int id = (int)cr.Field(other, 0);
                    if (id == raceId || id == 0) continue;
                    uint display = cr.Field(other, 4 + sex);
                    var d = di.Records.FirstOrDefault(r => di.Field(r, 0) == display);
                    if (d == null) continue;
                    var m = md.Records.FirstOrDefault(r => md.Field(r, 0) == di.Field(d, 1));
                    if (m == null || ModelStem(Str(md, m, 2)) != stems[sex]) continue;
                    var candidate = Rows(cs, 1, id, 4, 5, 6);
                    if (!HasRealSkin(candidate, sex)) continue;
                    int score = 0;
                    if (Str(cr, other, 6).Equals(racePrefix, StringComparison.OrdinalIgnoreCase)) score += 4;
                    if (NameOf(cr, other).Equals(raceName, StringComparison.OrdinalIgnoreCase)) score += 2;
                    if (cr.Field(other, 13) == raceSide) score += 1;
                    if (score <= best) continue;
                    best = score;
                    donor = other;
                    donorSections = candidate;
                    donorHd = Rows(hd, 1, id, 4, 5, 6);
                }
                if (donor == null) continue;
                sections.RemoveAll(r => sexOf((object[])r) == sex);
                foreach (var row in donorSections.Where(r => sexOf((object[])r) == sex))
                {
                    var copy = (object[])((object[])row).Clone();
                    copy[1] = (uint)raceId;
                    sections.Add(copy);
                }
                hdSections.RemoveAll(r => sexOf((object[])r) == sex);
                if (donorHd != null)
                    foreach (var row in donorHd.Where(r => sexOf((object[])r) == sex))
                    {
                        var copy = (object[])((object[])row).Clone();
                        copy[1] = (uint)raceId;
                        hdSections.Add(copy);
                    }
            }
        }

        /// <summary>Puts one race of the other client into the race folder (RaceFolder\Key\race.json and files\). Returns a
        /// line saying what it holds. The racial spells the server data names (spells) travel with it: the rows of the
        /// other client's spell table and the picture each of their icons shows, so a game client without them can be
        /// given them (without them the client knows nothing of the spells the server grants the race).</summary>
        public static string Pack(Archives source, Archives yours, Candidate race, string raceFolder, string sourceLabel, uint[] spells, Action<string> progress)
        {
            var cr = Table(source, "ChrRaces");
            var row = cr.Records.First(r => cr.Field(r, 0) == race.Id);
            var di = Table(source, "CreatureDisplayInfo");
            var md = Table(source, "CreatureModelData");
            // Darkfallen's own model is the classic Blood Elf model; in a client with an HD Blood Elf the classic one
            // has the old face and no real eye glow. Its own skin/face textures are the HD Blood Elf's anyway, so use
            // the HD model and the HD section rows of the stock Blood Elf. The race keeps its name, number and icon.
            bool hdDarkfallen = (race.Key + " " + race.Name).IndexOf("darkfallen", StringComparison.OrdinalIgnoreCase) >= 0;
            var stockRow = hdDarkfallen ? cr.Records.FirstOrDefault(r => cr.Field(r, 0) == 10) : null;
            var models = new Dictionary<string, object>();
            var files = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var roots = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);      // the race's own trees in the client
            var partial = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);     // trees the player's client already partly has
            string modelFolder = hdDarkfallen ? Str(cr, row, 11) : "";                 // Darkfallen keeps its own file string
            for (int sex = 0; sex < 2; sex++)
            {
                uint display = cr.Field(stockRow ?? row, 4 + sex);
                var d = di.Records.FirstOrDefault(r => di.Field(r, 0) == display);
                var m = d == null ? null : md.Records.FirstOrDefault(r => md.Field(r, 0) == di.Field(d, 1));
                if (m == null) continue;
                string path = Str(md, m, 2);
                // the textures of the display (TextureVariation 1-3): a model may have texture slots of its own that take
                // them (horns, eyes, a blindfold), named after the model's folder
                var textures = new[] { 6, 7, 8 }.Select(k => di.Field(d, k) == 0 ? "" : Str(di, d, k)).ToArray();
                models[sex == 0 ? "male" : "female"] = new Dictionary<string, object>
                {
                    { "display", Enumerable.Range(0, di.Fields).Select(k => (object)di.Field(d, k)).ToArray() },
                    { "model", Enumerable.Range(0, md.Fields).Select(k => (object)md.Field(m, k)).ToArray() },
                    { "path", path },
                    { "textures", textures },
                };
                string modelDir = M2(path).Contains("\\") ? M2(path).Substring(0, M2(path).LastIndexOf('\\') + 1) : "";
                foreach (var t in textures.Where(t => t.Length > 0)) files.Add(modelDir + t + ".blp");
                progress(L.F("Reading the model of the {0} …", race.Name));
                foreach (var f in ModelFiles(source, path)) files.Add(f);
                // the race's folder in the client (custom\highmountain for example) holds more than the tables and the
                // model header name: an appearance patch of a creator loads further files itself, so its whole tree goes in
                var parts = M2(path).Split('\\');
                if (parts.Length >= 3 && modelFolder.Length == 0)
                    modelFolder = parts[1];
                if (hdDarkfallen)
                {
                    // the stock Blood Elf folder belongs to the player's client; only the model files are added
                    continue;
                }
                if (parts.Length >= 3 && parts[0].Equals("custom", StringComparison.OrdinalIgnoreCase)) roots.Add(parts[0] + "\\" + parts[1] + "\\");
                // The same for a folder of its own elsewhere (Character\IllidariNightElf\, Creature\WhimMurloc\):
                // textures its model or its creator's code loads by name. A client that has some of the folder
                // (often the male files) must still get the files it lacks - the female textures and meshes.
                // Existing files of the player's client are not replaced.
                else if (parts.Length >= 3)
                {
                    string root = parts[0] + "\\" + parts[1] + "\\";
                    if (yours == null || yours.Under(root).Count == 0) roots.Add(root);
                    else partial.Add(root);
                }
            }
            if (models.Count == 0) throw new InvalidOperationException(L.F("The {0} has no model in these archives.", race.Name));

            // a folder of the race's own under custom\ (custom\drakonid\ for the Drakonid), wherever its model lies: the
            // creators of the races put textures, item parts and looks there that the model never names - found by the
            // race's name, its key, its file name in the race table or the folder of its model, letters only
            Func<string, string> letters = t => Regex.Replace(t ?? "", "[^A-Za-z]", "").ToLowerInvariant();
            var names = new HashSet<string>(new[] { race.Name, race.Key, Str(cr, row, 11) }.Select(letters).Where(n => n.Length > 2));
            foreach (var m in models.Values.OfType<Dictionary<string, object>>())
            {
                var parts = M2(Convert.ToString(m["path"])).Split('\\');
                if (parts.Length >= 3) names.Add(letters(parts[1]));
            }
            foreach (var n in source.Under("custom"))
            {
                var parts = n.Split('\\');
                if (parts.Length >= 3 && names.Contains(letters(parts[1]))) roots.Add(parts[0] + "\\" + parts[1] + "\\");
            }

            // everything else the race's own folder in the client holds
            foreach (var root in roots)
                foreach (var f in source.Under(root)) files.Add(f);
            // A folder the player's client already has in part: add the source files it does not have, and those the
            // other client changed under the same name. This is where the female-only meshes and textures of a race
            // went missing - and the fixed files of a client like CoA Custom (Creature\Banshee, Character\Worgen:
            // models with the helmet points set right, retextured skins) stayed the old ones of the player's client.
            foreach (var root in partial)
                foreach (var f in source.Under(root))
                    if (!yours.Has(f) || Differs(source, yours, f)) files.Add(f);

            // the items fitted to the race: the game finds a helmet (and any other item part made per race) by the prefix
            // of the race - Item\ObjectComponents\Head\<helmet>_<prefix><M|F>.m2, its skins, textures named so; without
            // them every helmet is a checkered box. A client like CoA Custom brings them for some of its races.
            string prefix = Str(cr, row, 6);
            int helmets = 0;
            if (!string.IsNullOrEmpty(prefix))
            {
                progress(L.F("Reading the helmets of the {0} …", race.Name));
                var fitted = new Regex(@"_" + Regex.Escape(prefix) + @"[MF](\d\d)?\.(m2|skin|blp)$", RegexOptions.IgnoreCase);
                foreach (var f in source.Under("Item"))
                {
                    if (!fitted.IsMatch(f)) continue;
                    if (f.EndsWith(".m2", StringComparison.OrdinalIgnoreCase))
                    {
                        if (f.StartsWith("Item\\ObjectComponents\\Head\\", StringComparison.OrdinalIgnoreCase)) helmets++;
                        foreach (var m in ModelFiles(source, f)) files.Add(m);
                    }
                    else files.Add(f);
                }
            }

            // the voice of the race: the sounds its emotes and its voice in the interface name. Those the player's client
            // does not have (a voice of its own, as CoA Custom gives the Grummle) travel along - their rows of the sound
            // table and their files - so that the race is not given its base race's voice for want of them.
            var vocal = Rows(Table(source, "VocalUISounds"), 2, race.Id);
            var emotes = Rows(Table(source, "EmotesTextSound"), 2, race.Id);
            var hdEmotes = Rows(Table(source, "HDEmotesTextSound"), 2, race.Id);
            var soundIds = new HashSet<uint>();
            foreach (object[] r in vocal) foreach (int k in new[] { 3, 4 }) if (k < r.Length) soundIds.Add(Convert.ToUInt32(r[k]));
            foreach (object[] r in emotes.Concat(hdEmotes)) if (r.Length > 4) soundIds.Add(Convert.ToUInt32(r[4]));
            soundIds.Remove(0);
            var soundRows = new List<object>();
            var sourceSounds = Table(source, "SoundEntries");
            var yourSounds = yours == null ? null : Table(yours, "SoundEntries");
            if (sourceSounds != null && yourSounds != null && soundIds.Count > 0)
            {
                var have = new HashSet<uint>(yourSounds.Records.Select(r => yourSounds.Field(r, 0)));
                var texts = new HashSet<int>(new[] { 2, 23 }.Concat(Enumerable.Range(3, 10)));
                foreach (var r in sourceSounds.Records)
                {
                    uint id = sourceSounds.Field(r, 0);
                    if (!soundIds.Contains(id) || have.Contains(id) || sourceSounds.Fields < 24) continue;
                    var row2 = new object[sourceSounds.Fields];
                    for (int k = 0; k < sourceSounds.Fields; k++)
                        row2[k] = texts.Contains(k) ? (object)(sourceSounds.Field(r, k) == 0 ? "" : Str(sourceSounds, r, k)) : sourceSounds.Field(r, k);
                    soundRows.Add(row2);
                    string dir = ((string)row2[23]).TrimEnd('\\');
                    for (int k = 3; k <= 12; k++)
                    {
                        var file = (string)row2[k];
                        if (file.Length > 0) files.Add(dir.Length > 0 ? dir + "\\" + file : file);
                    }
                }
            }

            int sectionRace = hdDarkfallen ? 10 : race.Id;
            var sections = Rows(Table(source, "CharSections"), 1, sectionRace, 4, 5, 6);
            var hdSections = Rows(Table(source, "HDCharSections"), 1, sectionRace, 4, 5, 6);
            RepairSections(source, cr, di, md, race.Id, models, sections, hdSections);
            foreach (object[] r in sections.Concat(hdSections))
                for (int k = 4; k <= 6; k++) { var t = r[k] as string; if (!string.IsNullOrEmpty(t)) files.Add(t); }
            string icon = "Interface\\Icons\\CoA_Race_" + race.Key;
            // Its icon: from the other client, or from the player's (CoA has icons for races it does not offer).
            // Some creators name it after the race without the CoA_Race_ prefix (Arakkoa: Race_Arakkoa_Male), and an
            // Esteria row can use the icon of its base row (VulperaEsteria: CoA_Race_Vulpera); both are copied under
            // the canonical name so the race keeps its icon wherever the archive order puts it.
            string iconFileKey = ClientRaces.KeyOf(Str(cr, row, 11));
            string rawNameKey = ClientRaces.KeyOf(NameOf(cr, row));
            var iconNames = new List<string>
            {
                "CoA_Race_" + race.Key,
                "CoA_Race_" + ClientRaces.KeyOf(race.Name),
                "CoA_Race_" + rawNameKey,
                "CoA_Race_" + iconFileKey,
                "Race_" + race.Key + "_Male",
                "Race_" + race.Key + "_Female",
                "Race_" + ClientRaces.KeyOf(race.Name) + "_Male",
                "Race_" + ClientRaces.KeyOf(race.Name) + "_Female",
                "Race_" + rawNameKey + "_Male",
                "Race_" + rawNameKey + "_Female",
            };
            Archives iconFrom = null;
            string iconFound = null;
            foreach (var n in iconNames.Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string candidate = "Interface\\Icons\\" + n + ".blp";
                if (source.Has(candidate)) { iconFrom = source; iconFound = candidate; break; }
                if (yours != null && yours.Has(candidate)) { iconFrom = yours; iconFound = candidate; break; }
            }
            bool hasIcon = iconFound != null;

            // which files the race folder needs: those the player's client lacks, or has otherwise
            var missing = new List<string>();
            var keep = new List<string>();
            var same = new List<string>();
            string dest = Path.Combine(raceFolder, race.Key);
            string filesDir = Path.Combine(dest, "files");
            if (Directory.Exists(filesDir)) Directory.Delete(filesDir, true);
            if (hasIcon)
            {
                string to = Os.Resolve(filesDir, icon + ".blp");
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                File.WriteAllBytes(to, iconFrom.Read(iconFound));
                keep.Add(icon + ".blp");
            }
            int done = 0;
            long bytes = 0;
            foreach (var f in files)
            {
                done++;
                if (done % 20 == 0) progress(L.F("The {0}: file {1} of {2} …", race.Name, done, files.Count));
                byte[] data;
                try { data = source.Read(f); }
                catch { data = null; }
                if (data == null)
                {
                    // the file may lie elsewhere in the archives or among the raw files: the race asks for it under this
                    // name, so it is packed under it
                    string found = null;
                    try { found = source.Find(f); } catch { }
                    if (found != null && !found.Equals(f, StringComparison.OrdinalIgnoreCase))
                        try { data = source.Read(found); } catch { data = null; }
                }
                if (data == null) { missing.Add(f); continue; }
                if (yours != null && yours.SizeOf(f) == data.Length)
                {
                    byte[] mine;
                    try { mine = yours.Read(f); } catch { mine = null; }
                    if (mine != null && mine.SequenceEqual(data)) { same.Add(f); continue; }
                }
                string to = Os.Resolve(filesDir, f);
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                File.WriteAllBytes(to, data);
                keep.Add(f);
                bytes += data.Length;
            }

            RepairDecorationTextures(filesDir, keep);
            var dropped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var originals = OriginalTextures(source, cr, models, sections, hdSections, dropped);
            // layers left empty: their files go when no row names them any more
            var named = new HashSet<string>(sections.Concat(hdSections).OfType<object[]>().SelectMany(r => new[] { r[4], r[5], r[6] }).OfType<string>(), StringComparer.OrdinalIgnoreCase);
            foreach (var f in dropped.Where(f => !named.Contains(f)))
            {
                if (keep.RemoveAll(k => k.Equals(f, StringComparison.OrdinalIgnoreCase)) == 0) continue;
                try { File.Delete(Os.Resolve(filesDir, f)); } catch { }
            }
            foreach (var extra in originals)
            {
                string to = Os.Resolve(filesDir, extra.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                File.WriteAllBytes(to, extra.Value);
                keep.Add(extra.Key);
                bytes += extra.Value.Length;
            }

            // the client rows of the race's racial spells (the ids the server data names): the texts as text and the
            // icon as the picture it stands for - the player's client may give the same picture another number
            var racialSpells = new List<object>();
            var spellsMissing = new List<object>();
            var spellTable = Table(source, "Spell");
            if (spellTable != null && spellTable.Fields == RaceImport.SpellFields && spells != null && spells.Length > 0)
            {
                var icons = Table(source, "SpellIcon");
                progress(L.T("Reading the racial spells …"));
                foreach (uint id in spells.Where(s => s != 0).Distinct())
                {
                    var spellRow = spellTable.Records.FirstOrDefault(r => spellTable.Field(r, 0) == id);
                    if (spellRow == null) { spellsMissing.Add(id); continue; }
                    var values = new object[spellTable.Fields];
                    for (int k = 0; k < spellTable.Fields; k++)
                        values[k] = RaceImport.SpellTextFields.Contains(k)
                            ? (object)(spellTable.Field(spellRow, k) == 0 ? "" : Str(spellTable, spellRow, k))
                            : spellTable.Field(spellRow, k);
                    string iconTexture = "";
                    if (icons != null)
                    {
                        uint iconId = spellTable.Field(spellRow, RaceImport.SpellIconField);
                        var iconRow = icons.Records.FirstOrDefault(r => icons.Field(r, 0) == iconId);
                        if (iconRow != null) iconTexture = Str(icons, iconRow, 1);
                    }
                    racialSpells.Add(new Dictionary<string, object> { { "id", id }, { "row", values }, { "icon", iconTexture } });
                }
            }

            var json = new Dictionary<string, object>
            {
                { "key", race.Key },
                { "name", race.Name },
                { "source", new Dictionary<string, object> { { "client", sourceLabel }, { "race", race.Id } } },
                { "flags", cr.Field(row, 1) },
                { "side", cr.Field(row, 13) },
                { "prefix", Str(cr, row, 6) },
                // The real folder of the model. ChrRaces.ClientFilestring can differ (Lightforged: "Lightforged"
                // in the DBC, "lightforgeddraenei" in the archives); keep the DBC value separately for matching.
                { "file", modelFolder.Length > 0 ? modelFolder : Str(cr, row, 11) },
                { "sourceFile", Str(cr, row, 11) },
                { "packer", PackerVersion },
                { "sourceStamp", source.Stamp ?? "" },
                { "maleOnly", race.MaleOnly ? "1" : "0" },
                { "femaleOnly", race.FemaleOnly ? "1" : "0" },
                { "labels", cr.Fields > 67 ? new[] { Str(cr, row, 65), Str(cr, row, 66), Str(cr, row, 67) } : new string[0] },
                { "ui_camera", Rows(Table(source, "UICameraAppearanceChrRaces"), 1, race.Id) },
                { "models", models },
                { "sections", sections },
                { "hd_sections", hdSections },
                { "hair_geosets", HairGeosets(source, Rows(Table(source, "CharHairGeosets"), 1, race.Id), sections, models) },
                { "hd_hair_geosets", Rows(Table(source, "HDCharHairGeosets"), 1, race.Id) },
                { "facial_hair", Rows(Table(source, "CharacterFacialHairStyles"), 0, race.Id) },
                { "hd_facial_hair", Rows(Table(source, "HDCharacterFacialHairStyles"), 1, race.Id) },
                { "hair_textures", Rows(Table(source, "CharHairTextures"), 1, race.Id) },
                { "vocal_ui_sounds", vocal },
                { "emotes_text_sound", emotes },
                { "hd_emotes_text_sound", hdEmotes },
                { "sound_entries", soundRows },
                { "spells", racialSpells },
                { "spells_missing", spellsMissing },
                { "missing", missing },
                { "files", keep },
                { "uses_coa_files", same },
                { "icon", hasIcon ? icon : "" },
            };
            Directory.CreateDirectory(dest);
            File.WriteAllText(Path.Combine(dest, "race.json"), new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(json), new UTF8Encoding(false));
            return L.F("{0}: {1} files, {2} MB", race.Name, keep.Count, bytes / 1000000) + (same.Count > 0 ? L.F(", {0} taken from your client", same.Count) : "") +
                (racialSpells.Count > 0 ? L.F(", {0} racial spells", racialSpells.Count) : "") +
                (helmets > 0 ? L.F(", {0} helmet models", helmets) : "") +
                (missing.Count > 0 ? L.F(", {0} missing", missing.Count) : "") + ".";
        }

        /// <summary>
        /// Darkfallen's body texture was painted from the HD Blood Elf skin, but its model is the classic Blood Elf
        /// model. The red HD eye therefore lies on the classic model's forehead (the face rows have their own red eye
        /// and the eye-glow textures still glow). The red pixels of the skin are replaced by the surrounding skin so the
        /// misplaced dot disappears; the face, the glow textures and everything else stay as they are.
        /// </summary>
        static void RepairDarkfallenEyes(Archives source, string filesDir, List<object> sections, List<object> hdSections,
            List<string> keep, Candidate race)
        {
            string identity = (race.Key + " " + race.Name).ToLowerInvariant();
            if (!identity.Contains("darkfallen")) return;
            var skins = sections.Concat(hdSections).OfType<object[]>()
                .Where(r => Convert.ToInt32(r[3]) == 0 || Convert.ToInt32(r[3]) == 1)
                .SelectMany(r => new[] { r[4] as string, r[5] as string })
                .Where(p => !string.IsNullOrEmpty(p) && p.StartsWith("Character\\Darkfallen\\", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (string path in skins)
            {
                try
                {
                    string file = Os.Resolve(filesDir, path);
                    byte[] data = File.Exists(file) ? File.ReadAllBytes(file) : source.Read(path);
                    byte[] fixedData = data == null ? null : RemoveRedEye(data);
                    if (fixedData == null) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(file));
                    File.WriteAllBytes(file, fixedData);
                    if (!keep.Contains(path, StringComparer.OrdinalIgnoreCase)) keep.Add(path);
                }
                catch { }
            }
        }

        /// <summary>
        /// Darkfallen's male model has the same two explicit eye textures as the female, but both point at the eye
        /// texture; the female's second one is DarkfallenDKGlow. The female therefore glows and the male does not.
        /// The male's second eye-texture slot is pointed at the female's DKGlow, which the pack already carries.
        /// </summary>
        static void RepairDarkfallenMaleGlow(Archives source, string filesDir, Dictionary<string, object> models,
            List<string> keep, Candidate race)
        {
            string identity = (race.Key + " " + race.Name).ToLowerInvariant();
            if (!identity.Contains("darkfallen")) return;
            object maleObject;
            if (!models.TryGetValue("male", out maleObject)) return;
            var male = maleObject as Dictionary<string, object>;
            if (male == null) return;
            string path = M2(Convert.ToString(male["path"]));
            string file = Os.Resolve(filesDir, path);
            if (!File.Exists(file)) return;
            const string glow = "Character\\Darkfallen\\Female\\DarkfallenDKGlow.blp";
            string glowFile = Os.Resolve(filesDir, glow);
            if (!File.Exists(glowFile))
            {
                try
                {
                    byte[] data = source.Read(glow);
                    if (data == null) return;
                    Directory.CreateDirectory(Path.GetDirectoryName(glowFile));
                    File.WriteAllBytes(glowFile, data);
                    if (!keep.Contains(glow, StringComparer.OrdinalIgnoreCase)) keep.Add(glow);
                }
                catch { return; }
            }
            try
            {
                byte[] b = File.ReadAllBytes(file);
                if (b.Length < 0x60 || Encoding.ASCII.GetString(b, 0, 4) != "MD20") return;
                uint count = BitConverter.ToUInt32(b, 0x50), at = BitConverter.ToUInt32(b, 0x54);
                if (count < 4 || at > b.Length || count > (b.Length - at) / 16) return;
                int target = -1;
                for (int i = 0; i < count; i++)
                {
                    uint record = at + (uint)i * 16;
                    if (BitConverter.ToUInt32(b, (int)record) != 0) continue;
                    uint length = BitConverter.ToUInt32(b, (int)record + 8), offset = BitConverter.ToUInt32(b, (int)record + 12);
                    if (length == 0 || offset >= b.Length) continue;
                    int end = (int)Math.Min((long)b.Length, (long)offset + length);
                    string name = Encoding.UTF8.GetString(b, (int)offset, end - (int)offset).TrimEnd('\0').Trim();
                    if (Path.GetFileName(name).IndexOf("EyeGlow", StringComparison.OrdinalIgnoreCase) >= 0) target = (int)record;
                }
                if (target < 0) return;
                byte[] text = Encoding.UTF8.GetBytes(glow + "\0");
                long where = b.Length;
                Array.Resize(ref b, b.Length + text.Length);
                Buffer.BlockCopy(text, 0, b, (int)where, text.Length);
                BitConverter.GetBytes((uint)where).CopyTo(b, target + 12);
                BitConverter.GetBytes((uint)text.Length).CopyTo(b, target + 8);
                File.WriteAllBytes(file, b);
            }
            catch { }
        }

        /// <summary>Replaces the red pixels of a paletted BLP2 skin by their nearest non-red neighbours. Null when
        /// the file is not such a BLP or has no red pixel.</summary>
        static byte[] RemoveRedEye(byte[] source)
        {
            if (source == null || source.Length < 0x494 || source[0] != 'B' || source[1] != 'L' || source[2] != 'P' || source[3] != '2' || source[8] != 1)
                return null;
            int width = BitConverter.ToInt32(source, 12), height = BitConverter.ToInt32(source, 16);
            if (width <= 0 || height <= 0 || width > 2048 || height > 2048) return null;
            var red = new bool[256];
            bool anyRed = false;
            for (int i = 0; i < 256; i++)
            {
                int at = 148 + i * 4;
                byte b = source[at], g = source[at + 1], r = source[at + 2];
                if (r >= 145 && r > g * 1.4 && r > b * 1.4) { red[i] = true; anyRed = true; }
            }
            if (!anyRed) return null;
            byte[] data = (byte[])source.Clone();
            bool changed = false;
            for (int level = 0; level < 16; level++)
            {
                int at = BitConverter.ToInt32(data, 20 + level * 4);
                int size = BitConverter.ToInt32(data, 84 + level * 4);
                int w = Math.Max(1, width >> level), h = Math.Max(1, height >> level), n = w * h;
                if (at <= 0 || size < n || at + n > data.Length) continue;
                var pixels = new byte[n];
                Buffer.BlockCopy(data, at, pixels, 0, n);
                bool hasRed = false;
                for (int i = 0; i < n; i++) if (red[pixels[i]]) { hasRed = true; break; }
                if (!hasRed) continue;
                var filled = new bool[n];
                var queue = new Queue<int>();
                for (int i = 0; i < n; i++)
                    if (!red[pixels[i]]) { filled[i] = true; queue.Enqueue(i); }
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue(), x = current % w, y = current / w;
                    for (int k = 0; k < 4; k++)
                    {
                        int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                        if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                        int next = ny * w + nx;
                        if (filled[next]) continue;
                        pixels[next] = pixels[current];
                        filled[next] = true;
                        queue.Enqueue(next);
                    }
                }
                Buffer.BlockCopy(pixels, 0, data, at, n);
                changed = true;
            }
            return changed ? data : null;
        }

        /// <summary>
        /// Some conversions of a race model leave the first decorative texture slot on the neutral 4x4 placeholder
        /// (the female Highmountain Tauren: her jewelry texture is in the race folder, but her model names neutral.blp
        /// in the slot the male model names the jewelry in). When the race folder holds a jewelry/decor/accessory
        /// texture named after the model, the model is given that path. The path is appended to the model and the
        /// texture record points at it, so no other block of the model moves. Both .m2 and .mdx models are
        /// checked (Dracthyr uses .mdx).
        /// </summary>
        static void RepairDecorationTextures(string filesDir, IEnumerable<string> files)
        {
            var list = files.ToList();
            var decorations = list.Where(f => f.EndsWith(".blp", StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var path in list.Where(f => f.EndsWith(".m2", StringComparison.OrdinalIgnoreCase) ||
                                                   f.EndsWith(".mdx", StringComparison.OrdinalIgnoreCase)))
            {
                string file = Os.Resolve(filesDir, path);
                if (!File.Exists(file)) continue;
                byte[] b;
                try { b = File.ReadAllBytes(file); } catch { continue; }
                if (b.Length < 0x58 || b[0] != (byte)'M' || b[1] != (byte)'D' || b[2] != (byte)'2' || b[3] != (byte)'0') continue;
                uint count = BitConverter.ToUInt32(b, 0x50);
                uint at = BitConverter.ToUInt32(b, 0x54);
                if (count == 0 || at > b.Length || count > (b.Length - at) / 16) continue;

                int slot = -1;
                for (int i = 0; i < count; i++)
                {
                    uint record = at + (uint)i * 16;
                    uint type = BitConverter.ToUInt32(b, (int)record);
                    uint length = BitConverter.ToUInt32(b, (int)record + 8);
                    uint offset = BitConverter.ToUInt32(b, (int)record + 12);
                    if (type != 0 || length == 0 || offset >= b.Length) continue;
                    int end = (int)Math.Min((long)b.Length, (long)offset + length);
                    string texture = Encoding.UTF8.GetString(b, (int)offset, end - (int)offset).TrimEnd('\0').Trim();
                    if (texture.Length == 0) continue;
                    if (slot < 0)
                    {
                        slot = (int)record;
                        if (!Path.GetFileName(texture).Equals("neutral.blp", StringComparison.OrdinalIgnoreCase)) { slot = -1; break; }
                    }
                }
                if (slot < 0) continue;

                string baseName = Path.GetFileNameWithoutExtension(path);
                string decoration = decorations.FirstOrDefault(f =>
                {
                    string name = Path.GetFileName(f);
                    if (!name.StartsWith(baseName + "_", StringComparison.OrdinalIgnoreCase)) return false;
                    string lower = name.ToLowerInvariant();
                    return lower.Contains("jewelry") || lower.Contains("jewellery") ||
                           lower.Contains("decor") || lower.Contains("accessory");
                });
                if (decoration == null) continue;

                byte[] text = Encoding.UTF8.GetBytes(decoration + "\0");
                long where = b.Length;
                Array.Resize(ref b, b.Length + text.Length);
                Buffer.BlockCopy(text, 0, b, (int)where, text.Length);
                BitConverter.GetBytes((uint)where).CopyTo(b, slot + 12);
                BitConverter.GetBytes((uint)text.Length).CopyTo(b, slot + 8);
                try { File.WriteAllBytes(file, b); } catch { }
            }
        }
    }
}
