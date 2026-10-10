using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace CoAInstaller
{
    enum PatchState { None, Current, Missing, Outdated, Leftover }

    /// <summary>
    /// The client patch of the modules: an archive (Data\patch-Y.MPQ) that changes files of the game client - the
    /// "client": {"patch": ...} part of a module's afk-realm.json. A module brings files of its own ("files") and asks
    /// for changes to files of the client ("edits"); AFK Realm takes those files from the player's own game client,
    /// so it never hands out files of the game, and builds the archive anew whenever the client, a module or a setting
    /// it reads has changed. The archive goes into the game folder, and into the ZIP for the other players.
    /// </summary>
    static class ClientPatch
    {
        public const string ArchiveName = "patch-Y.MPQ";
        const string StampName = "AFK-Realm\\client-patch.txt";

        static string RecordFile(Install inst) { return Path.Combine(inst.Root, "Dependencies", "client-patch.txt"); }

        internal class FileSpec { public string Module, From, To; public Dictionary<string, object> Fill; }
        internal class EditSpec { public string Module, File, InsertAfter, Line; public Dictionary<string, object> DropRows, AddRows, CloneRows, FillRows; }

        public class Plan
        {
            public List<string> Modules = new List<string>();
            public List<string> Why = new List<string>();
            public bool ClearCache;
            internal List<FileSpec> Files = new List<FileSpec>();
            internal List<EditSpec> Edits = new List<EditSpec>();
            public bool Empty { get { return Files.Count == 0 && Edits.Count == 0; } }
        }

        /// <summary>What the installed modules want changed in the client.</summary>
        public static Plan Collect(Install inst)
        {
            var plan = new Plan();
            string modules = ModuleCatalog.ModulesDir(inst);
            if (!Directory.Exists(modules)) return plan;
            var json = new JavaScriptSerializer();
            foreach (var dir in Directory.GetDirectories(modules).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                string file = Path.Combine(dir, ModuleCatalog.ManifestName);
                if (!File.Exists(file)) continue;
                Dictionary<string, object> clientPart, patch;
                object v;
                try
                {
                    var data = json.DeserializeObject(File.ReadAllText(file, Encoding.UTF8)) as Dictionary<string, object>;
                    if (data == null || !data.TryGetValue("client", out v) || (clientPart = v as Dictionary<string, object>) == null) continue;
                    if (!clientPart.TryGetValue("patch", out v) || (patch = v as Dictionary<string, object>) == null) continue;
                }
                catch { continue; }
                string module = Path.GetFileName(dir);
                string root = Path.GetFullPath(dir).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                int before = plan.Files.Count + plan.Edits.Count;
                foreach (var f in List(patch, "files"))
                {
                    string from = Text(f, "from"), to = Text(f, "to");
                    string source;
                    try { source = Path.GetFullPath(Path.Combine(dir, from.Replace('/', Path.DirectorySeparatorChar))); } catch { continue; }
                    if (!source.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(source) || !SafeClientPath(to)) continue;
                    plan.Files.Add(new FileSpec { Module = module, From = source, To = to.Replace('/', '\\'), Fill = f.TryGetValue("fill", out v) ? v as Dictionary<string, object> : null });
                }
                foreach (var e in List(patch, "edits"))
                {
                    string target = Text(e, "file");
                    if (!SafeClientPath(target)) continue;
                    var edit = new EditSpec { Module = module, File = target.Replace('/', '\\'), InsertAfter = Text(e, "insertAfter"), Line = Text(e, "line"),
                        DropRows = e.TryGetValue("dropRows", out v) ? v as Dictionary<string, object> : null,
                        AddRows = e.TryGetValue("addRows", out v) ? v as Dictionary<string, object> : null,
                        CloneRows = e.TryGetValue("cloneRows", out v) ? v as Dictionary<string, object> : null,
                        FillRows = e.TryGetValue("fillRows", out v) ? v as Dictionary<string, object> : null };
                    if ((edit.Line.Length > 0 && edit.InsertAfter.Length > 0) || edit.DropRows != null || edit.AddRows != null || edit.CloneRows != null ||
                        edit.FillRows != null)
                        plan.Edits.Add(edit);
                }
                if (plan.Files.Count + plan.Edits.Count > before)
                {
                    plan.Modules.Add(module);
                    string why = clientPart.TryGetValue("why", out v) ? Convert.ToString(v) : "";
                    if (why.Length > 0) plan.Why.Add(module + ": " + why);
                    plan.ClearCache |= clientPart.TryGetValue("clearCache", out v) && v is bool && (bool)v;
                }
            }
            return plan;
        }

        static IEnumerable<Dictionary<string, object>> List(Dictionary<string, object> d, string key)
        {
            object v;
            var list = d.TryGetValue(key, out v) ? v as object[] : null;
            return list == null ? Enumerable.Empty<Dictionary<string, object>>() : list.OfType<Dictionary<string, object>>();
        }

        static string Text(Dictionary<string, object> d, string key) { object v; return d.TryGetValue(key, out v) && v != null ? Convert.ToString(v) : ""; }

        /// <summary>Only files of the interface and the data tables, by a plain relative name.</summary>
        static bool SafeClientPath(string path)
        {
            return Regex.IsMatch(path ?? "", @"^(Interface|DBFilesClient)[\\/][\w .\-\\/]+$", RegexOptions.IgnoreCase) && !path.Contains("..");
        }

        /// <summary>The archives of the client, the one that wins last: the game reads the patches after its base archives.</summary>
        static List<string> ClientArchives(string client)
        {
            string data = Path.Combine(client, "Data");
            Func<string, int> rank = path =>
            {
                string name = Path.GetFileName(path).ToLowerInvariant();
                bool locale = !Path.GetDirectoryName(path).Equals(data, StringComparison.OrdinalIgnoreCase);
                if (!name.StartsWith("patch")) return locale ? 1 : 0;
                if (locale) return 2;
                // patch.MPQ, then patch-2 to patch-9, then the lettered ones
                return name == "patch.mpq" ? 3 : Regex.IsMatch(name, @"^patch-\d\.mpq$") ? 4 : 5;
            };
            var all = new List<string>();
            if (!Directory.Exists(data)) return all;
            Func<string, IEnumerable<string>> archives = dir => Directory.GetFiles(dir).Where(f => f.EndsWith(".mpq", StringComparison.OrdinalIgnoreCase));
            all.AddRange(archives(data));
            foreach (var sub in Directory.GetDirectories(data))
                if (Regex.IsMatch(Path.GetFileName(sub), @"^[a-z]{2}[A-Z]{2}$")) all.AddRange(archives(sub));
            return all.Where(p => !Path.GetFileName(p).Equals(ArchiveName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(rank).ThenBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>The newest copy of a file of the client: from the archive the game reads last.</summary>
        static byte[] FromClient(string client, string name, out string source)
        {
            foreach (var path in Enumerable.Reverse(ClientArchives(client)))
            {
                MpqArchive mpq;
                try { mpq = new MpqArchive(path); } catch { continue; }
                using (mpq)
                    if (mpq.Contains(name)) { source = Path.GetFileName(path); return mpq.Read(name); }
            }
            source = null;
            return null;
        }

        /// <summary>The value of an option of a server config file (the .conf, or its .dist), or null.</summary>
        static string ConfValue(Install inst, string conf, string key)
        {
            foreach (var path in new[] { Path.Combine(inst.ConfigDir, "modules", conf), Path.Combine(inst.ConfigDir, conf),
                                         Path.Combine(inst.ConfigDir, "modules", conf + ".dist"), Path.Combine(inst.ConfigDir, conf + ".dist") })
            {
                if (!File.Exists(path)) continue;
                var m = Regex.Match(File.ReadAllText(path), @"(?m)^[ \t]*" + Regex.Escape(key) + @"[ \t]*=[ \t]*(.*?)[ \t]*\r?$");
                if (m.Success) return m.Groups[1].Value.Trim().Trim('"');
            }
            return null;
        }

        /// <summary>The contents of the archive as it should be now: name -> bytes. Throws when the client lacks a file a module edits.</summary>
        public static SortedDictionary<string, byte[]> Build(Install inst, string client, Plan plan, List<string> notes)
        {
            var files = new SortedDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in plan.Files)
            {
                var bytes = File.ReadAllBytes(f.From);
                if (f.Fill != null) bytes = Fill(inst, bytes, f.Fill);
                files[f.To] = bytes;
            }
            foreach (var group in plan.Edits.GroupBy(e => e.File, StringComparer.OrdinalIgnoreCase))
            {
                byte[] bytes;
                string source = "this server's modules";
                if (!files.TryGetValue(group.Key, out bytes))
                {
                    bytes = FromClient(client, group.Key, out source);
                    if (bytes == null) throw new InvalidOperationException("The game client has no " + group.Key + ", which module " + group.First().Module + " changes. Is this the game folder of Conquest of Azeroth?");
                }
                foreach (var edit in group)
                    bytes = edit.DropRows != null ? DropRows(bytes, edit, group.Key) : edit.AddRows != null ? AddRows(bytes, edit, group.Key) :
                        edit.CloneRows != null ? CloneRows(bytes, edit, group.Key) :
                        edit.FillRows != null ? FillRows(bytes, edit, group.Key) : InsertAfter(bytes, edit, group.Key);
                files[group.Key] = bytes;
                if (notes != null) notes.Add(group.Key + " (from " + source + ")");
            }
            if (files.Count > 0) files[StampName] = Encoding.UTF8.GetBytes(Stamp(files));
            return files;
        }

        static byte[] Fill(Install inst, byte[] bytes, Dictionary<string, object> fill)
        {
            string marker = Text(fill, "marker"), template = Text(fill, "line");
            if (marker.Length == 0 || template.Length == 0) return bytes;
            string value = ConfValue(inst, Text(fill, "conf"), Text(fill, "key")) ?? Text(fill, "default");
            var numbers = Regex.Matches(value, @"\d+").Cast<Match>().Select(m => int.Parse(m.Value)).Distinct().OrderBy(n => n);
            string set = "{ " + string.Join(", ", numbers.Select(n => "[" + n + "] = true")) + " }";
            string line = template.Replace("{set}", set);
            string text = Encoding.UTF8.GetString(bytes);
            var lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
                if (lines[i].Contains(marker)) lines[i] = line + (lines[i].EndsWith("\r") ? "\r" : "");
            return Encoding.UTF8.GetBytes(string.Join("\n", lines));
        }

        static byte[] InsertAfter(byte[] bytes, EditSpec edit, string file)
        {
            string text = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
            string nl = text.Contains("\r\n") ? "\r\n" : "\n";
            var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
            if (lines.Any(l => l.Trim().Equals(edit.Line.Trim(), StringComparison.OrdinalIgnoreCase))) return bytes;
            int at = lines.FindIndex(l => l.Trim().Equals(edit.InsertAfter.Trim(), StringComparison.OrdinalIgnoreCase));
            if (at < 0) throw new InvalidOperationException(file + " of the game client has no line \"" + edit.InsertAfter + "\", after which module " + edit.Module + " adds its own. The client may have changed; update the module.");
            lines.Insert(at + 1, edit.Line);
            bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            var result = Encoding.UTF8.GetBytes(string.Join(nl, lines));
            return bom ? new byte[] { 0xEF, 0xBB, 0xBF }.Concat(result).ToArray() : result;
        }

        static byte[] DropRows(byte[] bytes, EditSpec edit, string file)
        {
            var table = DbcTable.Parse(bytes, file);
            object v;
            long idBelow = edit.DropRows.TryGetValue("idBelow", out v) ? Convert.ToInt64(v) : long.MaxValue;
            int field = edit.DropRows.TryGetValue("field", out v) ? Convert.ToInt32(v) : 0;
            var listed = (edit.DropRows.TryGetValue("values", out v) ? v as object[] : null) ?? new object[0];
            var values = new HashSet<uint>(listed.Select(x => Convert.ToUInt32(x)));
            if (field < 0 || field >= table.Fields) throw new InvalidOperationException(file + " has no field " + field + " (module " + edit.Module + ").");
            table.Records.RemoveAll(r => table.Field(r, 0) < idBelow && values.Contains(table.Field(r, field)));
            return table.ToBytes();
        }

        /// <summary>
        /// Rows added to a DBC table: every combination of the value lists in "cross" (one list per field, in field order),
        /// unless the table has that row already (a row the table holds twice keeps only its first). The fields keep the
        /// width the table has (CharBaseInfo.dbc of the CoA client stores a byte per field), and the rows go at the end,
        /// so the rows of the client keep their order.
        /// </summary>
        static byte[] AddRows(byte[] bytes, EditSpec edit, string file)
        {
            var table = DbcTable.Parse(bytes, file);
            object v;
            var lists = ((edit.AddRows.TryGetValue("cross", out v) ? v as object[] : null) ?? new object[0])
                .Select(l => ((l as object[]) ?? new object[0]).Select(x => Convert.ToUInt32(x)).ToArray()).ToList();
            if (lists.Count != table.Fields || lists.Any(l => l.Length == 0))
                throw new InvalidOperationException(file + " has " + table.Fields + " fields; module " + edit.Module + " gives values for " + lists.Count + ".");
            int width = table.Fields > 0 ? table.RecordSize / table.Fields : 0;
            if ((width != 1 && width != 2 && width != 4) || width * table.Fields != table.RecordSize)
                throw new InvalidOperationException(file + " has a row layout module " + edit.Module + " cannot add rows to.");
            if (width < 4 && lists.Any(l => l.Any(x => x >= (1u << (8 * width)))))
                throw new InvalidOperationException(file + " stores " + width + " byte(s) per field; a value of module " + edit.Module + " does not fit.");
            var have = new HashSet<string>();
            // A row the table holds twice takes a place in the client's list for nothing: the second one goes.
            table.Records.RemoveAll(r => !have.Add(Convert.ToBase64String(r)));
            var combo = new uint[lists.Count];
            Action<int> walk = null;
            walk = i =>
            {
                if (i == lists.Count)
                {
                    var rec = new byte[table.RecordSize];
                    for (int f = 0; f < combo.Length; f++)
                        for (int b = 0; b < width; b++) rec[f * width + b] = (byte)(combo[f] >> (8 * b));
                    if (have.Add(Convert.ToBase64String(rec))) table.Records.Add(rec);
                    return;
                }
                foreach (var value in lists[i]) { combo[i] = value; walk(i + 1); }
            };
            walk(0);
            // Some tables the client holds in a list of fixed size (CharBaseInfo.dbc of the CoA client: about 300 rows);
            // more rows than that crash the game, so the module names its limit.
            int most = edit.AddRows.TryGetValue("maxRows", out v) ? Convert.ToInt32(v) : int.MaxValue;
            if (table.Records.Count > most)
                throw new InvalidOperationException(file + " would have " + table.Records.Count + " rows, more than the " + most +
                    " the game takes (module " + edit.Module + "). Another module may have added rows to it too.");
            return table.ToBytes();
        }

        /// <summary>
        /// Rows copied within a DBC table: every row whose bytes at the offsets in "match" hold those values is copied with
        /// the bytes in "set" changed and a new id (the first field, after the highest id), unless a row with the copy's
        /// bytes at the "key" offsets exists already. For tables with byte fields, such as CharStartOutfit.dbc (the start
        /// outfit of a race, class and sex).
        /// </summary>
        static byte[] CloneRows(byte[] bytes, EditSpec edit, string file)
        {
            var table = DbcTable.Parse(bytes, file);
            object v;
            Func<string, Dictionary<int, byte>> offsets = name =>
            {
                var d = edit.CloneRows.TryGetValue(name, out v) ? v as Dictionary<string, object> : null;
                var result = new Dictionary<int, byte>();
                if (d != null) foreach (var kv in d) result[int.Parse(kv.Key)] = Convert.ToByte(kv.Value);
                return result;
            };
            var match = offsets("match"); var set = offsets("set");
            var key = ((edit.CloneRows.TryGetValue("key", out v) ? v as object[] : null) ?? new object[0]).Select(x => Convert.ToInt32(x)).ToArray();
            if (match.Count == 0 || set.Count == 0 || table.RecordSize < 4 || match.Keys.Concat(set.Keys).Concat(key).Any(o => o < 4 || o >= table.RecordSize))
                throw new InvalidOperationException("The rows module " + edit.Module + " copies in " + file + " are not described in a way it can use.");
            Func<byte[], string> keyOf = r => string.Join(",", key.Select(o => r[o]));
            var have = new HashSet<string>(table.Records.Select(keyOf));
            uint next = table.Records.Count == 0 ? 1 : table.Records.Max(r => BitConverter.ToUInt32(r, 0)) + 1;
            foreach (var row in table.Records.Where(r => match.All(m => r[m.Key] == m.Value)).ToList())
            {
                var copy = (byte[])row.Clone();
                foreach (var s in set) copy[s.Key] = s.Value;
                if (!have.Add(keyOf(copy))) continue;
                BitConverter.GetBytes(next++).CopyTo(copy, 0);
                table.Records.Add(copy);
            }
            return table.ToBytes();
        }

        /// <summary>
        /// Rows of a DBC table filled from a sibling: a row whose bytes at a "match" offset hold one of its values and whose
        /// 32-bit numbers in "empty" are all 0 or below gets the bytes in "copy" from a row that agrees with it at the "same"
        /// offsets and is not empty - first from a row whose byte at "prefer.offset" is in the same list of "prefer.groups".
        /// CharStartOutfit.dbc: an outfit of the CoA client that only shows armour, without items, takes the items of the
        /// same class and sex of another race of the same faction.
        /// </summary>
        static byte[] FillRows(byte[] bytes, EditSpec edit, string file)
        {
            var table = DbcTable.Parse(bytes, file);
            var spec = edit.FillRows;
            object v;
            Func<string, int[]> ints = name => ((spec.TryGetValue(name, out v) ? v as object[] : null) ?? new object[0]).Select(x => Convert.ToInt32(x)).ToArray();
            var match = new Dictionary<int, HashSet<byte>>();
            if (spec.TryGetValue("match", out v) && v is Dictionary<string, object>)
                foreach (var kv in (Dictionary<string, object>)v)
                    match[int.Parse(kv.Key)] = new HashSet<byte>(((kv.Value as object[]) ?? new[] { kv.Value }).Select(x => Convert.ToByte(x)));
            int[] empty = ints("empty"), copy = ints("copy"), same = ints("same");
            int preferAt = -1; var groups = new List<HashSet<byte>>();
            if (spec.TryGetValue("prefer", out v) && v is Dictionary<string, object>)
            {
                var p = (Dictionary<string, object>)v;
                preferAt = p.TryGetValue("offset", out v) ? Convert.ToInt32(v) : -1;
                if (p.TryGetValue("groups", out v) && v is object[])
                    foreach (var g in (object[])v) groups.Add(new HashSet<byte>(((g as object[]) ?? new object[0]).Select(x => Convert.ToByte(x))));
            }
            int size = table.RecordSize;
            if (empty.Length != 2 || copy.Length != 2 || empty[0] < 4 || empty[1] > size || (empty[1] - empty[0]) % 4 != 0 || copy[0] < 4 || copy[1] > size ||
                copy[0] >= copy[1] || same.Any(o => o < 4 || o >= size) || match.Keys.Any(o => o < 4 || o >= size) || preferAt >= size)
                throw new InvalidOperationException("The rows module " + edit.Module + " fills in " + file + " are not described in a way it can use.");
            Func<byte[], bool> isEmpty = r => { for (int o = empty[0]; o < empty[1]; o += 4) if (BitConverter.ToInt32(r, o) > 0) return false; return true; };
            var donors = table.Records.Where(r => !isEmpty(r)).ToList();
            foreach (var row in table.Records)
            {
                if (!match.All(m => m.Value.Contains(row[m.Key])) || !isEmpty(row)) continue;
                var fitting = donors.Where(d => same.All(o => d[o] == row[o])).ToList();
                if (preferAt >= 4)
                {
                    var group = groups.FirstOrDefault(g => g.Contains(row[preferAt]));
                    var near = group == null ? null : fitting.Where(d => group.Contains(d[preferAt])).OrderBy(d => d[preferAt]).FirstOrDefault();
                    if (near != null) fitting.Insert(0, near);
                }
                var donor = fitting.FirstOrDefault();
                if (donor != null) Buffer.BlockCopy(donor, copy[0], row, copy[0], copy[1] - copy[0]);
            }
            return table.ToBytes();
        }

        static string Stamp(SortedDictionary<string, byte[]> files)
        {
            using (var sha = SHA256.Create())
            {
                foreach (var f in files)
                {
                    if (f.Key.Equals(StampName, StringComparison.OrdinalIgnoreCase)) continue;
                    var name = Encoding.UTF8.GetBytes(f.Key.ToLowerInvariant() + "\n");
                    sha.TransformBlock(name, 0, name.Length, null, 0);
                    sha.TransformBlock(f.Value, 0, f.Value.Length, null, 0);
                }
                sha.TransformFinalBlock(new byte[0], 0, 0);
                return "Made by " + Product.Name + " for the game client it was built from.\r\nContents " + BitConverter.ToString(sha.Hash).Replace("-", "").Substring(0, 16) + "\r\n";
            }
        }

        /// <summary>The stamp of the patch in the game folder: null without one, "" when the archive there is not ours.</summary>
        static string InstalledStamp(string client)
        {
            string path = Path.Combine(client, "Data", ArchiveName);
            if (!File.Exists(path)) return null;
            try
            {
                using (var mpq = new MpqArchive(path))
                {
                    var b = mpq.Read(StampName);
                    string stamp = b == null ? "" : Encoding.UTF8.GetString(b);
                    return stamp.StartsWith("Made by " + Product.Name) ? stamp : "";
                }
            }
            catch { return ""; }
        }

        const string Foreign = "Data\\" + ArchiveName + " in the game folder was not made by " + Product.Name + ". Rename or remove it, then install again.";

        /// <summary>Whether the client has the patch the modules want. Building it reads the client, which takes a moment.</summary>
        public static PatchState StateOf(Install inst, string client, Plan plan, out string problem)
        {
            problem = null;
            string installed = client == null ? null : InstalledStamp(client);
            if (plan.Empty) return !string.IsNullOrEmpty(installed) && WasOurs(inst) ? PatchState.Leftover : PatchState.None;
            if (client == null) return PatchState.Missing;
            if (installed == "") { problem = Foreign; return PatchState.Outdated; }
            try
            {
                var files = Build(inst, client, plan, null);
                string wanted = Encoding.UTF8.GetString(files[StampName]);
                return installed == null ? PatchState.Missing : installed == wanted ? PatchState.Current : PatchState.Outdated;
            }
            catch (Exception ex) { problem = ex.Message; return PatchState.Outdated; }
        }

        static bool WasOurs(Install inst) { return File.Exists(RecordFile(inst)); }

        /// <summary>Builds the patch and puts it into the game folder, or takes it out when no module needs it any more.</summary>
        public static string Install(Install inst, string client, Plan plan)
        {
            string path = Path.Combine(client, "Data", ArchiveName);
            string installed = InstalledStamp(client);
            if (plan.Empty)
            {
                if (!string.IsNullOrEmpty(installed) && WasOurs(inst))
                {
                    try { File.Delete(path); }
                    catch (IOException) { throw new InvalidOperationException("Data\\" + ArchiveName + " is in use. Close the game and its launcher first."); }
                    File.Delete(RecordFile(inst));
                    return "the client patch removed (no module needs it any more)";
                }
                return "";
            }
            if (installed == "") throw new InvalidOperationException(Foreign);
            var notes = new List<string>();
            var files = Build(inst, client, plan, notes);
            string temp = path + ".new";
            MpqArchive.Write(temp, files);
            try
            {
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            catch (IOException)
            {
                try { File.Delete(temp); } catch { }
                throw new InvalidOperationException("Data\\" + ArchiveName + " is in use. Close the game and its launcher first.");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(RecordFile(inst)));
            File.WriteAllLines(RecordFile(inst), new[] { ArchiveName }.Concat(plan.Modules), new UTF8Encoding(false));
            return "the client patch Data\\" + ArchiveName + " built for " + string.Join(", ", plan.Modules) + (notes.Count > 0 ? " (changes " + string.Join(", ", notes) + ")" : "");
        }

        /// <summary>Puts the patch into a ZIP for other players, as Data\patch-Y.MPQ.</summary>
        public static void AddToZip(ZipArchive zip, Install inst, string client, Plan plan)
        {
            var files = Build(inst, client, plan, null);
            string temp = Path.GetTempFileName();
            try
            {
                MpqArchive.Write(temp, files);
                zip.CreateEntryFromFile(temp, "Data/" + ArchiveName, CompressionLevel.Optimal);
            }
            finally { try { File.Delete(temp); } catch { } }
        }
    }
}
