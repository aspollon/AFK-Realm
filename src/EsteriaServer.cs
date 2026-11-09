using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CoAInstaller
{
    /// <summary>
    /// What an EsteriaWoW server needs beyond the stock map data. Its races, looks and spells are in the DBC tables of
    /// the Esteria client, spread over dozens of patch archives the stock map_extractor never opens (it reads the
    /// base archives and patch-2 to patch-5 only). The server gets the table the game itself reads - from the
    /// archive it loads last - for every table. The Docker setup of EsteriaWoW then lays a few server-only files
    /// over them (AreaTable of mod-fly-anywhere, SkillRaceClassInfo of mod-custom-server) and puts the DBC
    /// continuations of mod-wxl-dbc next to them; AFK Realm does the same from the server's source.
    /// </summary>
    static class EsteriaServer
    {
        const string Folder = "DBFilesClient\\";

        /// <summary>Writes every DBC table of the client into <paramref name="target"/>, as the game reads it.
        /// Tables already in the folder (from map_extractor) are replaced. Returns how many were written.</summary>
        public static int ExtractClientDbc(string client, string target, Action<string> note)
        {
            Directory.CreateDirectory(target);
            var order = ClientPatch.ClientArchives(client, true);
            // table file name -> the archive that has it and is read last
            var newest = new Dictionary<string, KeyValuePair<string, string>>(StringComparer.OrdinalIgnoreCase);
            var unlisted = new List<string>();
            foreach (var path in order)
            {
                try
                {
                    using (var mpq = new MpqArchive(path))
                    {
                        var names = mpq.Names();
                        if (names == null || names.Count == 0) { unlisted.Add(path); continue; }
                        foreach (var name in names)
                        {
                            string n = name.Replace('/', '\\');
                            if (!n.StartsWith(Folder, StringComparison.OrdinalIgnoreCase) || !n.EndsWith(".dbc", StringComparison.OrdinalIgnoreCase)) continue;
                            if (!mpq.Contains(n)) continue;
                            newest[n.Substring(Folder.Length)] = new KeyValuePair<string, string>(path, n);
                        }
                    }
                }
                catch (Exception ex) { note(L.F("{0} could not be read: {1}", Path.GetFileName(path), ex.Message)); }
            }
            // Archives without a file list can still hold tables: the names map_extractor wrote are looked for there too.
            if (unlisted.Count > 0 && Directory.Exists(target))
                foreach (var file in Directory.GetFiles(target, "*.dbc"))
                {
                    string n = Folder + Path.GetFileName(file);
                    foreach (var path in unlisted)
                        try { using (var mpq = new MpqArchive(path)) if (mpq.Contains(n)) newest[Path.GetFileName(file)] = new KeyValuePair<string, string>(path, n); }
                        catch { }
                }
            if (newest.Count == 0) throw new InvalidOperationException(L.T("The game client has no DBC tables. Is this the EsteriaWoW client folder, and is the game closed?"));

            int written = 0;
            foreach (var group in newest.GroupBy(kv => kv.Value.Key, StringComparer.OrdinalIgnoreCase))
            {
                using (var mpq = new MpqArchive(group.Key))
                    foreach (var kv in group)
                    {
                        byte[] bytes;
                        try { bytes = mpq.Read(kv.Value.Value); } catch { bytes = null; }
                        if (bytes == null || bytes.Length < 20) continue;
                        File.WriteAllBytes(Path.Combine(target, kv.Key), bytes);
                        written++;
                    }
            }
            note(L.F("{0} DBC tables taken from the client archives ({1} archives).", written, order.Count));
            return written;
        }

        /// <summary>The server-only DBC files of the EsteriaWoW source, laid over the tables in Server/Data/dbc, and the
        /// DBC continuations of mod-wxl-dbc in Server/Data/dbc-continuations. Nothing happens before the map data exists.</summary>
        public static void ApplyServerFiles(Install inst, Action<string> note)
        {
            if (!inst.IsEsteria) return;
            string dbc = Path.Combine(inst.DataDir, "dbc");
            if (!Directory.Exists(dbc)) return;
            string modules = Path.Combine(inst.SourceDir, "modules");
            var overlays = new[]
            {
                Path.Combine(modules, "mod-fly-anywhere", "data", "patch", "server", "AreaTable.dbc"),
                Path.Combine(modules, "mod-custom-server", "data", "dbc", "SkillRaceClassInfo.dbc"),
            };
            foreach (var file in overlays)
            {
                if (!File.Exists(file)) continue;
                File.Copy(file, Path.Combine(dbc, Path.GetFileName(file)), true);
                note(L.F("Server table {0} from {1}.", Path.GetFileName(file), file.Substring(inst.SourceDir.Length).TrimStart('\\', '/')));
            }
            string continuations = Path.Combine(modules, "mod-custom-server", "data", "dbc-continuations");
            if (Directory.Exists(continuations))
            {
                string to = Path.Combine(inst.DataDir, "dbc-continuations");
                Directory.CreateDirectory(to);
                foreach (var file in Directory.GetFiles(continuations))
                {
                    File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
                    note(L.F("DBC continuation {0}.", Path.GetFileName(file)));
                }
            }
        }
    }
}
