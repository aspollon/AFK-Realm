using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;

namespace CoAInstaller
{
    /// <summary>
    /// Brings a CoA Custom folder up to a release the way the AFK Client brings a game folder up to its manifest: only
    /// what changed is downloaded. Every ZIP of the release lists its files with size and CRC32 in its central
    /// directory at its end; that directory is read with a few small range requests, each file is compared with the
    /// one in the folder (size, then CRC32, remembered per file so an unchanged folder is not read twice), and only the
    /// files that differ are fetched out of the ZIP by range and unpacked. When most of a ZIP is needed anyway (a first
    /// download), or the server does not answer range requests, the whole ZIP is downloaded as before and only the
    /// files that differ are taken out of it. As before, a file in a later ZIP wins over the same file in an earlier one.
    /// </summary>
    static class CoaDelta
    {
        internal sealed class Entry
        {
            public string Name;
            public int Method, AssetIndex;
            public uint Crc;
            public long Compressed, Size, Offset;
        }

        sealed class RangeNotSupported : Exception { }

        const string StateFile = ".afk-coa-custom-files.txt";
        // a ZIP of which this share or more is needed is downloaded whole; so is one with very many changed files
        const double WholeShare = 0.6;
        const int WholeCount = 300;

        /// <summary>Brings raceFolder up to the release's files. Returns how many files were written.</summary>
        internal static int Update(CoaCustom.Release release, string raceFolder, string downloads, Action<string, int, int> report)
        {
            var state = new FileState(Path.Combine(raceFolder, StateFile));
            try
            {
                // the files of all ZIPs; a later ZIP wins, as extracting them in order did
                var directories = new List<List<Entry>>();
                var final = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
                bool ranges = true;
                for (int i = 0; i < release.Assets.Count; i++)
                {
                    if (report != null) report(L.F("Reading the file list of {0} ({1}/{2}) ...", release.Assets[i].Name, i + 1, release.Assets.Count), 0, 0);
                    List<Entry> list = null;
                    if (ranges)
                    {
                        try { list = ReadDirectory(release.Assets[i], i); }
                        catch (RangeNotSupported) { ranges = false; }
                    }
                    directories.Add(list);
                    if (list != null) foreach (var e in list) final[Rel(e.Name)] = e;
                }
                if (!ranges)
                {
                    // the server sends whole files only: every ZIP downloaded, every file that differs taken out
                    int written = 0;
                    for (int i = 0; i < release.Assets.Count; i++)
                        written += Whole(release, i, null, raceFolder, downloads, state, report);
                    return written;
                }

                // what differs from the folder
                var needed = new List<Entry>();
                int checkedCount = 0;
                foreach (var pair in final.OrderBy(p => p.Value.AssetIndex).ThenBy(p => p.Value.Offset))
                {
                    if (report != null && checkedCount++ % 25 == 0)
                        report(L.F("Comparing the CoA Custom files ({0}/{1}) ...", checkedCount, final.Count), checkedCount, final.Count);
                    if (!state.Same(raceFolder, pair.Key, pair.Value.Size, pair.Value.Crc)) needed.Add(pair.Value);
                }
                int done = 0;
                for (int i = 0; i < release.Assets.Count; i++)
                {
                    var mine = needed.Where(e => e.AssetIndex == i).ToList();
                    if (mine.Count == 0) continue;
                    var asset = release.Assets[i];
                    long bytes = mine.Sum(e => e.Compressed);
                    if (mine.Count > WholeCount || (asset.Size > 0 && bytes >= asset.Size * WholeShare))
                    {
                        done += Whole(release, i, new HashSet<string>(mine.Select(e => e.Name), StringComparer.Ordinal), raceFolder, downloads, state, report);
                        continue;
                    }
                    try
                    {
                        foreach (var e in mine)
                        {
                            Fetch(asset, e, raceFolder, state, report, done + 1, needed.Count);
                            done++;
                        }
                    }
                    catch (RangeNotSupported)
                    {
                        done += Whole(release, i, new HashSet<string>(mine.Select(e => e.Name), StringComparer.Ordinal), raceFolder, downloads, state, report);
                    }
                }
                return done;
            }
            finally
            {
                state.Save();
            }
        }

        // ---------------------------------------------------------------------------------------- whole ZIPs

        /// <summary>Downloads a ZIP whole (with resume and checksum, as before) and writes the files of it that are
        /// wanted (all files when wanted is null) and differ from the folder.</summary>
        static int Whole(CoaCustom.Release release, int index, HashSet<string> wanted, string raceFolder, string downloads,
            FileState state, Action<string, int, int> report)
        {
            var asset = release.Assets[index];
            string zipFile = Path.Combine(downloads, asset.Name);
            if (report != null) report(L.F("Downloading CoA Custom {0} ({1}/{2}) ...", asset.Name, index + 1, release.Assets.Count), 0, 0);
            CoaCustom.DownloadAssetTo(asset, zipFile, report, index, release.Assets.Count);
            int written = 0;
            using (var zip = ZipFile.OpenRead(zipFile))
            {
                var entries = zip.Entries.Where(z => z.Name.Length > 0 && (wanted == null || wanted.Contains(z.FullName))).ToList();
                for (int i = 0; i < entries.Count; i++)
                {
                    var z = entries[i];
                    string rel = Rel(z.FullName);
                    if (wanted == null && state.Same(raceFolder, rel, z.Length, z.Crc32)) continue;
                    if (report != null) report(L.F("Extracting {0} ({1}/{2}) ...", Path.GetFileName(zipFile), i + 1, entries.Count), i + 1, entries.Count);
                    using (var stream = z.Open())
                        Write(stream, z.Length, z.Crc32, raceFolder, rel, state, null);
                    written++;
                }
            }
            try { File.Delete(zipFile); } catch { }
            return written;
        }

        // ---------------------------------------------------------------------------------------- range requests

        static byte[] Range(CoaCustom.Asset asset, long from, long to)
        {
            using (var resp = Request(asset, from, to))
            using (var stream = resp.GetResponseStream())
            {
                var data = new byte[to - from + 1];
                int at = 0, n;
                while (at < data.Length && (n = stream.Read(data, at, data.Length - at)) > 0) at += n;
                if (at != data.Length) throw new IOException(L.F("The download of {0} was cut off.", asset.Name));
                return data;
            }
        }

        static HttpWebResponse Request(CoaCustom.Asset asset, long from, long to)
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }
            var req = (HttpWebRequest)WebRequest.Create(asset.Url);
            req.UserAgent = Product.FileStem;
            string token = Settings.CoaGitHubToken;
            if (!string.IsNullOrEmpty(token)) req.Headers["Authorization"] = "token " + token;
            req.Accept = "application/octet-stream";
            req.Timeout = 30000;
            req.ReadWriteTimeout = 60000;
            req.AddRange(from, to);
            var resp = (HttpWebResponse)req.GetResponse();
            if (resp.StatusCode != HttpStatusCode.PartialContent)
            {
                resp.Dispose();
                throw new RangeNotSupported();
            }
            return resp;
        }

        /// <summary>The files of a ZIP from its central directory (ZIP64 included).</summary>
        static List<Entry> ReadDirectory(CoaCustom.Asset asset, int index)
        {
            if (asset.Size < 22) throw new InvalidOperationException(L.F("{0} is not a ZIP file.", asset.Name));
            int tailSize = (int)Math.Min(asset.Size, 65557 + 20);
            byte[] tail = Range(asset, asset.Size - tailSize, asset.Size - 1);
            int eocd = -1;
            for (int p = tail.Length - 22; p >= 0; p--)
                if (U32(tail, p) == 0x06054b50) { eocd = p; break; }
            if (eocd < 0) throw new InvalidOperationException(L.F("{0} is not a ZIP file.", asset.Name));
            long count = U16(tail, eocd + 10), size = U32(tail, eocd + 12), offset = U32(tail, eocd + 16);
            if (count == 0xFFFF || size == 0xFFFFFFFF || offset == 0xFFFFFFFF)
            {
                int locator = eocd - 20;
                if (locator < 0 || U32(tail, locator) != 0x07064b50) throw new InvalidOperationException(L.F("{0} is not a ZIP file.", asset.Name));
                long at = (long)U64(tail, locator + 8);
                byte[] z64 = Range(asset, at, at + 55);
                if (U32(z64, 0) != 0x06064b50) throw new InvalidOperationException(L.F("{0} is not a ZIP file.", asset.Name));
                count = (long)U64(z64, 32); size = (long)U64(z64, 40); offset = (long)U64(z64, 48);
            }
            var list = new List<Entry>();
            if (count == 0) return list;
            byte[] cd = Range(asset, offset, offset + size - 1);
            int q = 0;
            for (long i = 0; i < count; i++)
            {
                if (q + 46 > cd.Length || U32(cd, q) != 0x02014b50) throw new InvalidOperationException(L.F("{0} is not a ZIP file.", asset.Name));
                int nameLen = U16(cd, q + 28), extraLen = U16(cd, q + 30), commentLen = U16(cd, q + 32);
                var e = new Entry
                {
                    Method = U16(cd, q + 10), Crc = U32(cd, q + 16), Compressed = U32(cd, q + 20), Size = U32(cd, q + 24),
                    Offset = U32(cd, q + 42), AssetIndex = index,
                    Name = Encoding.UTF8.GetString(cd, q + 46, nameLen)
                };
                // ZIP64: the values that did not fit come from the extra field, in this order
                int x = q + 46 + nameLen, xEnd = x + extraLen;
                while (x + 4 <= xEnd)
                {
                    int id = U16(cd, x), len = U16(cd, x + 2), v = x + 4;
                    if (id == 1)
                    {
                        if (e.Size == 0xFFFFFFFF && v + 8 <= x + 4 + len) { e.Size = (long)U64(cd, v); v += 8; }
                        if (e.Compressed == 0xFFFFFFFF && v + 8 <= x + 4 + len) { e.Compressed = (long)U64(cd, v); v += 8; }
                        if (e.Offset == 0xFFFFFFFF && v + 8 <= x + 4 + len) { e.Offset = (long)U64(cd, v); }
                    }
                    x += 4 + len;
                }
                q += 46 + nameLen + extraLen + commentLen;
                if (e.Name.EndsWith("/", StringComparison.Ordinal) || e.Name.EndsWith("\\", StringComparison.Ordinal)) continue;
                Rel(e.Name);                                                        // an unsafe path stops here
                list.Add(e);
            }
            return list;
        }

        /// <summary>Fetches one file out of a ZIP by range and writes it into the folder.</summary>
        static void Fetch(CoaCustom.Asset asset, Entry e, string raceFolder, FileState state, Action<string, int, int> report, int number, int total)
        {
            if (e.Method != 0 && e.Method != 8) throw new RangeNotSupported();      // an unusual ZIP: whole download instead
            string rel = Rel(e.Name);
            if (report != null) report(L.F("Updating {0} ({1}/{2}) ...", Path.GetFileName(rel), number, total), 0, 0);
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    byte[] local = Range(asset, e.Offset, e.Offset + 29);
                    if (U32(local, 0) != 0x04034b50) throw new InvalidOperationException(L.F("{0} is not a ZIP file.", asset.Name));
                    long data = e.Offset + 30 + U16(local, 26) + U16(local, 28);
                    if (e.Compressed == 0)
                    {
                        Write(new MemoryStream(new byte[0]), e.Size, e.Crc, raceFolder, rel, state, null);
                        return;
                    }
                    using (var resp = Request(asset, data, data + e.Compressed - 1))
                    using (var raw = resp.GetResponseStream())
                    using (Stream plain = e.Method == 8 ? (Stream)new DeflateStream(raw, CompressionMode.Decompress) : raw)
                    {
                        Action<long> progress = report == null ? null : new Action<long>(n =>
                            report(L.F("Updating {0} ({1}/{2}) ...", Path.GetFileName(rel), number, total),
                                (int)Math.Min(int.MaxValue, n), e.Size > int.MaxValue ? 0 : (int)e.Size));
                        Write(plain, e.Size, e.Crc, raceFolder, rel, state, progress);
                    }
                    return;
                }
                catch (RangeNotSupported) { throw; }
                catch (Exception)
                {
                    if (attempt >= 2) throw;
                }
            }
        }

        // ---------------------------------------------------------------------------------------- writing

        /// <summary>Writes a file next to its place, checks its size and CRC32, and only then puts it in place.</summary>
        static void Write(Stream source, long size, uint crc, string raceFolder, string rel, FileState state, Action<long> progress)
        {
            string dest = Os.Resolve(raceFolder, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            string temp = dest + ".afk-part";
            uint actual = Crc32.Start;
            long count = 0;
            try
            {
                using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
                {
                    var buffer = new byte[1 << 20];
                    int n;
                    long reported = 0;
                    while ((n = source.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        output.Write(buffer, 0, n);
                        actual = Crc32.Add(actual, buffer, 0, n);
                        count += n;
                        if (progress != null && count - reported >= 8 << 20) { progress(count); reported = count; }
                    }
                }
                actual = Crc32.End(actual);
                if (count != size || actual != crc)
                    throw new InvalidOperationException(L.F("The checksum of {0} does not match.", Path.GetFileName(rel)));
                File.Move(temp, dest, true);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
            state.Remember(raceFolder, rel, crc);
        }

        /// <summary>The path of a ZIP entry inside the folder; an unsafe one is refused.</summary>
        static string Rel(string name)
        {
            string rel = name.Replace('\\', '/').TrimStart('/');
            if (rel.Length == 0 || rel.Split('/').Any(p => p == ".." || p == "." || p.IndexOf(':') >= 0))
                throw new InvalidOperationException(L.F("{0} contains an unsafe path.", name));
            return rel;
        }

        static int U16(byte[] b, int at) { return b[at] | (b[at + 1] << 8); }
        static uint U32(byte[] b, int at) { return BitConverter.ToUInt32(b, at); }
        static ulong U64(byte[] b, int at) { return BitConverter.ToUInt64(b, at); }

        // ---------------------------------------------------------------------------------------- the folder

        /// <summary>The CRC32 of the folder's files, remembered with their size and time so an unchanged file is read once.</summary>
        sealed class FileState
        {
            readonly string path;
            readonly Dictionary<string, string[]> known = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            bool changed;

            public FileState(string path)
            {
                this.path = path;
                try
                {
                    if (File.Exists(path))
                        foreach (var line in File.ReadAllLines(path))
                        {
                            var parts = line.Split('\t');
                            if (parts.Length == 4) known[parts[0]] = parts;
                        }
                }
                catch { known.Clear(); }
            }

            public bool Same(string folder, string rel, long size, uint crc)
            {
                string file = Os.Resolve(folder, rel);
                var info = new FileInfo(file);
                if (!info.Exists || info.Length != size) return false;
                string[] k;
                string stamp = info.LastWriteTimeUtc.Ticks.ToString();
                uint have;
                if (known.TryGetValue(rel, out k) && k[1] == size.ToString() && k[2] == stamp && uint.TryParse(k[3], System.Globalization.NumberStyles.HexNumber, null, out have))
                    return have == crc;
                have = Crc32.OfFile(file);
                known[rel] = new[] { rel, size.ToString(), stamp, have.ToString("x8") };
                changed = true;
                return have == crc;
            }

            public void Remember(string folder, string rel, uint crc)
            {
                var info = new FileInfo(Os.Resolve(folder, rel));
                if (!info.Exists) return;
                known[rel] = new[] { rel, info.Length.ToString(), info.LastWriteTimeUtc.Ticks.ToString(), crc.ToString("x8") };
                changed = true;
            }

            public void Save()
            {
                if (!changed) return;
                try { File.WriteAllLines(path, known.Values.Select(k => string.Join("\t", k)).OrderBy(x => x, StringComparer.OrdinalIgnoreCase)); }
                catch { }
            }
        }
    }

    /// <summary>CRC32 as ZIP uses it (polynomial 0xEDB88320), eight bytes at a time.</summary>
    static class Crc32
    {
        public const uint Start = 0xFFFFFFFF;
        static readonly uint[,] table = Build();

        static uint[,] Build()
        {
            var t = new uint[8, 256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                t[0, i] = c;
            }
            for (int i = 0; i < 256; i++)
                for (int s = 1; s < 8; s++) t[s, i] = (t[s - 1, i] >> 8) ^ t[0, t[s - 1, i] & 0xFF];
            return t;
        }

        public static uint Add(uint crc, byte[] data, int offset, int count)
        {
            int i = offset, end = offset + count;
            while (end - i >= 8)
            {
                uint a = crc ^ (uint)(data[i] | data[i + 1] << 8 | data[i + 2] << 16 | data[i + 3] << 24);
                uint b = (uint)(data[i + 4] | data[i + 5] << 8 | data[i + 6] << 16 | data[i + 7] << 24);
                crc = table[7, a & 0xFF] ^ table[6, (a >> 8) & 0xFF] ^ table[5, (a >> 16) & 0xFF] ^ table[4, a >> 24] ^
                      table[3, b & 0xFF] ^ table[2, (b >> 8) & 0xFF] ^ table[1, (b >> 16) & 0xFF] ^ table[0, b >> 24];
                i += 8;
            }
            while (i < end) crc = table[0, (crc ^ data[i++]) & 0xFF] ^ (crc >> 8);
            return crc;
        }

        public static uint End(uint crc) { return crc ^ 0xFFFFFFFF; }

        public static uint OfFile(string path)
        {
            uint crc = Start;
            var buffer = new byte[1 << 20];
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
            {
                int n;
                while ((n = stream.Read(buffer, 0, buffer.Length)) > 0) crc = Add(crc, buffer, 0, n);
            }
            return End(crc);
        }
    }
}
