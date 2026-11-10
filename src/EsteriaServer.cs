using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CoAInstaller
{
    /// <summary>
    /// What an EsteriaWoW server needs beyond the stock map data. The stock tables come from map_extractor; the
    /// races, looks and spells are in seven tables of the Esteria client, spread over patch archives the stock
    /// map_extractor never opens. Exactly those seven are taken as the game reads them (from the archive it loads
    /// last) - the list EsteriaWoW's own Docker setup lays over the stock set. Not every table: other tables in the
    /// client's patches (from CoA and other projects) have layouts the EsteriaWoW core does not read, and the
    /// worldserver stopped on them (Vehicle.dbc, "stringOffset &lt; file.stringSize"). Then the server-only files of
    /// the source (AreaTable of mod-fly-anywhere, SkillRaceClassInfo of mod-custom-server) and the DBC continuations
    /// of mod-wxl-dbc, as in that Docker setup.
    /// </summary>
    static class EsteriaServer
    {
        const string Folder = "DBFilesClient\\";

        /// <summary>The tables EsteriaWoW's docker-compose.override.yml mounts from its race work over the stock set; the
        /// client carries the same content (RaceGuide/08-OUTPUTS-AND-DEPLOYMENT.md compares them).</summary>
        public static readonly string[] ClientTables =
            { "ChrRaces.dbc", "CharStartOutfit.dbc", "CharSections.dbc", "BarberShopStyle.dbc", "CreatureDisplayInfo.dbc", "CreatureModelData.dbc", "Spell.dbc" };

        /// <summary>Writes the <see cref="ClientTables"/> into <paramref name="target"/> as the game reads them, over what
        /// map_extractor put there. Returns how many were written.</summary>
        public static int ExtractClientDbc(string client, string target, Action<string> note)
        {
            Directory.CreateDirectory(target);
            var order = ClientPatch.ClientArchives(client, true);
            // table -> the archive that has it and is read last (the archives are in the order the game reads them)
            var newest = new Dictionary<string, KeyValuePair<string, string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in order)
            {
                try
                {
                    using (var mpq = new MpqArchive(path))
                        foreach (var table in ClientTables)
                            if (mpq.Contains(Folder + table)) newest[table] = new KeyValuePair<string, string>(path, Folder + table);
                }
                catch (Exception ex) { note(L.F("{0} could not be read: {1}", Path.GetFileName(path), ex.Message)); }
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

        /// <summary>The game client program the engine built from wowdecomp (Dependencies\ClientExe\WowClient64.exe), or null.</summary>
        public static string BuiltClientExe(Install inst)
        {
            if (!inst.IsEsteria) return null;
            string exe = Path.Combine(inst.Root, "Dependencies", "ClientExe", "WowClient64.exe");
            return File.Exists(exe) ? exe : null;
        }

        /// <summary>Puts the built WowClient64.exe (with its .pdb for the crash log, and XvidDecoder.dll) into an EsteriaWoW
        /// game folder. The program that was there first is kept once as WowClient64.exe.afk-backup. Returns what was done,
        /// "" when nothing had to be done; throws when the game is running.</summary>
        public static string InstallClientExe(Install inst, string client)
        {
            string built = BuiltClientExe(inst);
            if (built == null || string.IsNullOrEmpty(client) || !ClientAddons.IsEsteriaClient(client)) return "";
            string target = Path.Combine(client, "WowClient64.exe");
            if (File.Exists(target) && SameFile(built, target)) return "";
            if (ClientAddons.ClientRunning(client))
                throw new InvalidOperationException(L.T("The game is running from this folder. Close it so that the new WowClient64.exe can be put in."));
            string backup = target + ".afk-backup";
            if (File.Exists(target) && !File.Exists(backup)) File.Copy(target, backup);
            string from = Path.GetDirectoryName(built);
            foreach (string name in new[] { "WowClient64.exe", "WowClient64.pdb", "XvidDecoder.dll" })
                if (File.Exists(Path.Combine(from, name))) File.Copy(Path.Combine(from, name), Path.Combine(client, name), true);
            return L.T("WowClient64.exe built from wowdecomp put into the game");
        }

        static bool SameFile(string a, string b)
        {
            var fa = new FileInfo(a); var fb = new FileInfo(b);
            if (fa.Length != fb.Length) return false;
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var sa = File.OpenRead(a)) using (var sb = File.OpenRead(b))
                return sha.ComputeHash(sa).SequenceEqual(sha.ComputeHash(sb));
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
