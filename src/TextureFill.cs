using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CoAInstaller
{
    /// <summary>
    /// Textures the races ask for under a path no archive has, while the same file lies in the client under another
    /// folder of the same race. CoA Custom's EsteriaSethrak.bin wants its armor colors 2 to 4 under
    /// custom\sethrak\animated\textures\creature\sethrak_melee\, the files are in Creature\sethrak_caster\: without
    /// them the game draws the first color for all four. Only the texture lists of EsteriaAppearance.dll are filled:
    /// files for CharSections rows found that way were HD textures, wrong on the old models (see Run).
    ///
    /// A copy is made only where nothing can be mixed up: the same file name, a folder named after the same race in
    /// both paths, and for a CharSections row a male or female folder that matches the row. Dwarf beards for a Blood
    /// elf, a race that reuses another race's paths on purpose (the Darkfallen scalps are blank), a statue texture
    /// that happens to be called body01.blp: all left as they are.
    /// </summary>
    static class TextureFill
    {
        static readonly HashSet<string> Generic = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "character", "creature", "custom", "male", "female", "textures", "animated", "native", "runtime", "unknown",
            "equipment", "hd", "expanded", "item", "objectcomponents", "world"
        };

        static readonly Regex BlpPath = new Regex(@"[A-Za-z0-9_][A-Za-z0-9_\\/ .\-]*\.blp(?=\0)", RegexOptions.IgnoreCase);

        class Index
        {
            public string Key;
            public List<string> Archives;
            // file name -> full names, from the archives' (listfile)s; the archive that the game reads last wins
            public Dictionary<string, List<string>> ByName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, string> ArchiveOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        static Index cached;

        static Index IndexOf(string client)
        {
            var archives = ClientPatch.ClientArchives(client);
            var key = new StringBuilder();
            foreach (var a in archives) { var fi = new FileInfo(a); key.Append(a).Append('|').Append(fi.Length).Append('|').Append(fi.LastWriteTimeUtc.Ticks).Append(';'); }
            if (cached != null && cached.Key == key.ToString()) return cached;
            var index = new Index { Key = key.ToString(), Archives = archives };
            foreach (var path in archives)
            {
                MpqArchive mpq;
                try { mpq = new MpqArchive(path); } catch { continue; }
                using (mpq)
                    foreach (var name in mpq.Names())
                    {
                        if (!name.EndsWith(".blp", StringComparison.OrdinalIgnoreCase)) continue;
                        string file = name.Substring(name.LastIndexOf('\\') + 1);
                        List<string> list;
                        if (!index.ByName.TryGetValue(file, out list)) index.ByName[file] = list = new List<string>();
                        if (!list.Contains(name, StringComparer.OrdinalIgnoreCase)) list.Add(name);
                        index.ArchiveOf[name] = path;
                    }
            }
            cached = index;
            return index;
        }

        static string[] Folders(string path)
        {
            var parts = path.Split('\\', '/');
            return parts.Take(parts.Length - 1).Where(p => p.Length > 0).ToArray();
        }

        static string DbcString(DbcTable t, byte[] row, int field)
        {
            uint at = t.Field(row, field);
            if (at == 0 || at >= t.Strings.Length) return "";
            int end = (int)at;
            while (end < t.Strings.Length && t.Strings[end] != 0) end++;
            return Encoding.UTF8.GetString(t.Strings, (int)at, end - (int)at);
        }

        class Wanted
        {
            public string Path, From;
            public HashSet<string> Races = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public HashSet<uint> Sexes = new HashSet<uint>();
            public bool RowOfOtherRace;   // a row of a race whose name is not in the path: that race uses the path as it is
            public List<string> Tokens;   // Esteria: the folders of the path that name the race
        }

        /// <summary>Adds the copies to the patch. load reads a file as the patch will have it (or from the client).</summary>
        public static void Apply(Install inst, string client, SortedDictionary<string, byte[]> files, Func<string, byte[]> load, List<string> notes)
        {
            var log = new List<string>();
            try { Run(client, files, load, log); }
            catch (Exception ex) { log.Add("stopped: " + ex.Message); }
            try { EsteriaSkinTint.Apply(client, files, load, log); }
            catch (Exception ex) { log.Add("Sethrak heads stopped: " + ex.Message); }
            if (notes != null) notes.AddRange(log.Where(l => l.StartsWith("+")));
            try
            {
                Directory.CreateDirectory(inst.LogDir);
                File.WriteAllLines(Path.Combine(inst.LogDir, "client-texture-fill.log"),
                    new[] { DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + log.Count(l => l.StartsWith("+")) + " textures copied" }.Concat(log));
            }
            catch { }
        }

        static void Run(string client, SortedDictionary<string, byte[]> files, Func<string, byte[]> load, List<string> log)
        {
            var index = IndexOf(client);
            var open = new Dictionary<string, MpqArchive>(StringComparer.OrdinalIgnoreCase);
            try
            {
                Func<string, bool> exists = name =>
                {
                    if (files.ContainsKey(name)) return true;
                    foreach (var path in index.Archives)
                    {
                        MpqArchive mpq;
                        if (!open.TryGetValue(path, out mpq))
                        {
                            try { mpq = new MpqArchive(path); } catch { mpq = null; }
                            open[path] = mpq;
                        }
                        if (mpq != null && mpq.Contains(name)) return true;
                    }
                    return false;
                };
                var wanted = new Dictionary<string, Wanted>(StringComparer.OrdinalIgnoreCase);

                // CharSections rows are not filled any more (1.2.31): the files found under the race's own folder with a male
                // or female folder in between are the HD textures of the HD models (Character\BloodElf\Female\..., the Tauren
                // and Worgen ones too), and on the old models they gave the High elves wrong faces. A missing CharSections
                // texture stays missing, as in the package.

                // the texture lists of CoA Custom's EsteriaAppearance.dll, next to the game's exe
                if (Directory.Exists(client))
                    foreach (var bin in Directory.GetFiles(client, "Esteria*.bin"))
                    {
                        string text;
                        try { text = Encoding.GetEncoding(28591).GetString(File.ReadAllBytes(bin)); } catch { continue; }
                        foreach (Match m in BlpPath.Matches(text))
                        {
                            string t = m.Value.Replace('/', '\\');
                            if (wanted.ContainsKey(t)) continue;
                            var tokens = Folders(t).Where(p => p.Length >= 4 && !Generic.Contains(p)).ToList();
                            if (tokens.Count > 0) wanted[t] = new Wanted { Path = t, From = Path.GetFileName(bin), Tokens = tokens };
                        }
                    }

                foreach (var w in wanted.Values.OrderBy(w => w.Path, StringComparer.OrdinalIgnoreCase))
                {
                    if (w.Tokens == null && (w.RowOfOtherRace || w.Races.Count == 0)) continue;
                    List<string> candidates;
                    string file = w.Path.Substring(w.Path.LastIndexOf('\\') + 1);
                    if (!index.ByName.TryGetValue(file, out candidates)) continue;
                    if (exists(w.Path)) continue;
                    string pick = null;
                    foreach (var c in candidates.OrderBy(c => c.Length))
                    {
                        var folders = Folders(c);
                        bool female = folders.Any(p => p.Equals("female", StringComparison.OrdinalIgnoreCase));
                        bool male = folders.Any(p => p.Equals("male", StringComparison.OrdinalIgnoreCase));
                        if (w.Tokens != null)
                        {
                            if (!w.Tokens.Any(tok => folders.Any(p => p.IndexOf(tok, StringComparison.OrdinalIgnoreCase) >= 0))) continue;
                        }
                        else
                        {
                            if (!w.Races.All(r => folders.Any(p => p.StartsWith(r, StringComparison.OrdinalIgnoreCase)))) continue;
                            if (female && w.Sexes.Any(s => s != 1)) continue;
                            if (male && w.Sexes.Any(s => s != 0)) continue;
                        }
                        pick = c;
                        break;
                    }
                    if (pick == null) { log.Add("- " + w.Path + " (" + w.From + "): missing, no copy of the same race"); continue; }
                    byte[] bytes = null;
                    string archive;
                    if (index.ArchiveOf.TryGetValue(pick, out archive))
                        try { using (var mpq = new MpqArchive(archive)) bytes = mpq.Read(pick); } catch { bytes = null; }
                    if (bytes == null || bytes.Length == 0) { log.Add("- " + w.Path + ": " + pick + " could not be read"); continue; }
                    files[w.Path] = bytes;
                    log.Add("+ " + w.Path + " <- " + pick + " (" + Path.GetFileName(archive) + ", for " + w.From + ")");
                }
            }
            finally
            {
                foreach (var mpq in open.Values) if (mpq != null) mpq.Dispose();
            }
        }
    }
}
