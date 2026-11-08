using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CoAInstaller
{
    /// <summary>
    /// The skin tables CoA Custom's game exe paints creature-model races with (Dreadlord, Sureki, Qiraji ...): for every
    /// race it reads Esteria&lt;Race&gt;.bin next to the exe, an "EHM1" table with one row per sex, texture slot and colour
    /// (slot 11 = the model's monster skin, 12 = its second skin, 13 closes the sex), and Esteria&lt;Race&gt;Textures.bin
    /// ("EHT1"). Without the file the exe gets an empty table and the body stays bare: CoA Custom 1.9.9 shipped no
    /// EsteriaQiraji.bin. A table the exe asks for that no folder or module brings is made here from the race's own skin
    /// rows in CharSections.dbc (texture 1 into slot 11, texture 2 into slot 12), so a race added later without its
    /// table is painted as well. A table that CoA Custom's folder or a module brings always wins.
    /// </summary>
    static class EsteriaTables
    {
        const int RowSize = 156, PathSize = 128;

        class Source { public string Key; public DbcTable Races, Sections; public List<string> Asked; }
        static Source cached;

        /// <summary>
        /// Puts the tables into the plan's files next to the exe: a table no folder, module or client has is made from the
        /// race's skin rows; a table that is there (CoA Custom's, a module's or the one in the client) keeps every row and
        /// only gets the colours of CharSections.dbc it lacks. The colours are in CharSections already, so the creator
        /// offers them; without their row in the table the exe left them bare (Mo'arg, Mogu, Nerubian ... in CoA Custom 1.9.9).
        /// A table in another format is left as it is.
        /// </summary>
        internal static void AddMissing(ClientPatch.Plan plan, string client)
        {
            if (plan == null || string.IsNullOrEmpty(client)) return;
            try { Fill(plan, client); } catch { }      // never stop the client patch for this
        }

        static void Fill(ClientPatch.Plan plan, string client)
        {
            var src = Load(client);
            if (src == null || src.Asked.Count == 0) return;
            string dir = Path.Combine(Path.GetTempPath(), "afk-realm-esteria");
            foreach (string name in src.Asked)
            {
                string file = "Esteria" + name + ".bin", textures = "Esteria" + name + "Textures.bin";
                int race = RaceOf(src.Races, name);
                if (race <= 0) continue;
                var skins = Skins(src.Sections, race);
                if (skins.Count == 0) continue;
                var planned = plan.RootFiles.FirstOrDefault(r => r.To.Equals(file, StringComparison.OrdinalIgnoreCase) && File.Exists(r.From ?? ""));
                string inClient = Path.Combine(client, file);
                byte[] have = planned != null ? File.ReadAllBytes(planned.From) : File.Exists(inClient) ? File.ReadAllBytes(inClient) : null;
                byte[] table = have == null ? Table(src.Sections, skins) : Merge(have, src.Sections, skins);
                if (table == null) continue;                                   // another format: left alone
                if (planned != null)
                {
                    if (!table.SequenceEqual(have)) { Directory.CreateDirectory(dir); planned.From = Keep(dir, file, table); }
                }
                else
                {
                    Directory.CreateDirectory(dir);
                    plan.RootFiles.Add(new ClientPatch.RootFile { Module = "afk-realm", To = file, From = Keep(dir, file, table) });
                }
                if (!plan.RootFiles.Any(r => r.To.Equals(textures, StringComparison.OrdinalIgnoreCase)) && !File.Exists(Path.Combine(client, textures)))
                {
                    Directory.CreateDirectory(dir);
                    plan.RootFiles.Add(new ClientPatch.RootFile { Module = "afk-realm", To = textures, From = Keep(dir, textures, EmptyTextures()) });
                }
            }
        }

        /// <summary>The client's race and skin tables and the race names its exe asks for (Esteria&lt;Name&gt;.bin together
        /// with Esteria&lt;Name&gt;Textures.bin), read again only when the exe or an archive changed.</summary>
        static Source Load(string client)
        {
            var exes = new[] { "CoA64.exe", "EsteriaAppearance.dll" }.Select(n => Path.Combine(client, n)).Where(File.Exists).ToList();
            if (exes.Count == 0) return null;
            var key = new StringBuilder(client).Append('|');
            foreach (var p in exes.Concat(ClientPatch.ClientArchives(client)))
            {
                var fi = new FileInfo(p);
                key.Append(p).Append('|').Append(fi.Length).Append('|').Append(fi.LastWriteTimeUtc.Ticks).Append(';');
            }
            if (cached != null && cached.Key == key.ToString()) return cached;
            var asked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var exe in exes)
            {
                byte[] bytes = File.ReadAllBytes(exe);
                // the names are UTF-16 strings; read at both byte alignments
                string text = Encoding.Unicode.GetString(bytes) + "\n" + (bytes.Length > 1 ? Encoding.Unicode.GetString(bytes, 1, bytes.Length - 1) : "");
                var names = new HashSet<string>(Regex.Matches(text, @"Esteria([A-Za-z]+)\.bin").Cast<Match>().Select(m => m.Groups[1].Value), StringComparer.OrdinalIgnoreCase);
                foreach (var n in names)
                    if (names.Contains(n + "Textures")) asked.Add(n);
            }
            string source;
            byte[] raceBytes = ClientPatch.FromClient(client, "DBFilesClient\\ChrRaces.dbc", out source);
            byte[] sectionBytes = ClientPatch.FromClient(client, "DBFilesClient\\CharSections.dbc", out source);
            if (raceBytes == null || sectionBytes == null) return null;
            var sections = DbcTable.Parse(sectionBytes, "CharSections.dbc");
            if (sections.Fields < 10) return null;
            cached = new Source { Key = key.ToString(), Races = DbcTable.Parse(raceBytes, "ChrRaces.dbc"), Sections = sections,
                Asked = asked.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList() };
            return cached;
        }

        /// <summary>The skin rows of a race (section 0, style 0), one per sex and colour.</summary>
        static List<byte[]> Skins(DbcTable sections, int race)
        {
            return sections.Records.Where(r => sections.Field(r, 1) == (uint)race && sections.Field(r, 3) == 0 && sections.Field(r, 8) == 0)
                .GroupBy(r => sections.Field(r, 2) + ":" + sections.Field(r, 9)).Select(g => g.OrderBy(r => sections.Field(r, 0)).First())
                .Where(r => sections.Field(r, 2) < 2 && sections.Field(r, 9) < 0xFFFF && Str(sections, r, 4).Length > 0)
                .OrderBy(r => sections.Field(r, 2)).ThenBy(r => sections.Field(r, 9)).ToList();
        }

        /// <summary>A table that is there with the colours it lacks added before the closing row of their sex; null when it
        /// is not an EHM1 table.</summary>
        static byte[] Merge(byte[] have, DbcTable sections, List<byte[]> skins)
        {
            if (have.Length < 12 || Encoding.ASCII.GetString(have, 0, 4) != "EHM1") return null;
            int count = (int)BitConverter.ToUInt32(have, 8);
            if (count < 0 || 12 + (long)count * RowSize != have.Length) return null;
            var rows = new List<byte[]>();
            for (int i = 0; i < count; i++) { var r = new byte[RowSize]; Buffer.BlockCopy(have, 12 + i * RowSize, r, 0, RowSize); rows.Add(r); }
            var painted = new HashSet<string>(rows.Where(r => BitConverter.ToUInt32(r, 8) == 11)
                .Select(r => BitConverter.ToUInt32(r, 0) + ":" + BitConverter.ToUInt16(r, 18)));
            var lacking = skins.Where(s => !painted.Contains(sections.Field(s, 2) + ":" + sections.Field(s, 9))).ToList();
            if (lacking.Count == 0) return have;
            var result = new List<byte[]>();
            var done = new HashSet<uint>();
            foreach (var r in rows)
            {
                uint sex = BitConverter.ToUInt32(r, 0);
                if (BitConverter.ToUInt32(r, 8) == 13 && done.Add(sex)) result.AddRange(RowsOf(sections, lacking.Where(s => sections.Field(s, 2) == sex)));
                result.Add(r);
            }
            foreach (var sex in lacking.Select(s => sections.Field(s, 2)).Distinct().Where(x => !done.Contains(x)).OrderBy(x => x))
            {
                result.AddRange(RowsOf(sections, lacking.Where(s => sections.Field(s, 2) == sex)));
                result.Add(Row(sex, 13, -1, ""));
            }
            return Pack(result);
        }

        /// <summary>A new EHM1 table from the skin rows, the way CoA Custom's own tables are laid out.</summary>
        static byte[] Table(DbcTable sections, List<byte[]> skins)
        {
            var rows = new List<byte[]>();
            foreach (var sex in skins.Select(r => sections.Field(r, 2)).Distinct().OrderBy(s => s))
            {
                rows.AddRange(RowsOf(sections, skins.Where(r => sections.Field(r, 2) == sex)));
                rows.Add(Row(sex, 13, -1, ""));
            }
            return Pack(rows);
        }

        static IEnumerable<byte[]> RowsOf(DbcTable sections, IEnumerable<byte[]> skins)
        {
            foreach (var r in skins.OrderBy(r => sections.Field(r, 9)))
            {
                uint sex = sections.Field(r, 2);
                int colour = (int)sections.Field(r, 9);
                yield return Row(sex, 11, colour, Str(sections, r, 4));
                yield return Row(sex, 12, colour, Str(sections, r, 5));
            }
        }

        static byte[] Pack(List<byte[]> rows)
        {
            var bytes = new byte[12 + rows.Count * RowSize];
            Encoding.ASCII.GetBytes("EHM1").CopyTo(bytes, 0);
            BitConverter.GetBytes(1u).CopyTo(bytes, 4);
            BitConverter.GetBytes((uint)rows.Count).CopyTo(bytes, 8);
            for (int i = 0; i < rows.Count; i++) rows[i].CopyTo(bytes, 12 + i * RowSize);
            return bytes;
        }

        /// <summary>The race whose name (ChrRaces field 11 or 14) is the name in the file, letters and digits only.</summary>
        static int RaceOf(DbcTable races, string name)
        {
            Func<string, string> plain = s => new string((s ?? "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            string want = plain(name);
            foreach (var row in races.Records)
                foreach (int field in new[] { 11, 14 })
                    if (field < races.Fields && plain(Str(races, row, field)) == want) return (int)races.Field(row, 0);
            return 0;
        }

        static byte[] Row(uint sex, uint slot, int colour, string path)
        {
            var row = new byte[RowSize];
            BitConverter.GetBytes(sex).CopyTo(row, 0);
            BitConverter.GetBytes(1u).CopyTo(row, 4);
            BitConverter.GetBytes(slot).CopyTo(row, 8);
            // bytes 12..15 stay 0; 16..17 the style (0), 18..19 the colour, or 0xFFFFFFFF on the closing row
            if (colour < 0) for (int i = 16; i < 20; i++) row[i] = 0xFF;
            else BitConverter.GetBytes((ushort)colour).CopyTo(row, 18);
            for (int i = 20; i < 28; i++) row[i] = 0xFF;
            byte[] text = Encoding.ASCII.GetBytes(path ?? "");
            if (text.Length >= PathSize) text = new byte[0];
            text.CopyTo(row, 28);
            return row;
        }

        static byte[] EmptyTextures()
        {
            var bytes = new byte[16];
            Encoding.ASCII.GetBytes("EHT1").CopyTo(bytes, 0);
            BitConverter.GetBytes(1u).CopyTo(bytes, 4);
            return bytes;
        }

        static string Keep(string dir, string name, byte[] data)
        {
            string file = Path.Combine(dir, Hash(data).Substring(0, 16) + "-" + name);
            if (!File.Exists(file)) File.WriteAllBytes(file, data);
            return file;
        }

        static string Hash(byte[] data)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "");
        }

        static string Str(DbcTable t, byte[] row, int field)
        {
            uint at = t.Field(row, field);
            var s = t.Strings;
            if (at == 0 || at >= s.Length) return "";
            int end = (int)at;
            while (end < s.Length && s[end] != 0) end++;
            return Encoding.UTF8.GetString(s, (int)at, end - (int)at);
        }
    }
}
