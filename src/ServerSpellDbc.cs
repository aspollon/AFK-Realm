using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;

namespace CoAInstaller
{
    /// <summary>
    /// The server's Spell.dbc of CoA Custom 1.9.8. The package's server DBC has
    /// 209,708 spells; its client has 225,534, and the working server table has
    /// 225,579 (the client table plus the server-only custom spells such as
    /// 14001006). The server reads its own table before spell_dbc overrides, so
    /// the client table is not a replacement for the 140xxxx racials.
    ///
    /// The 12 MB ZIP is kept out of AFK-Realm.exe and downloaded from the
    /// repository into LocalAppData; a Spell.dbc.zip next to AFK-Realm.exe
    /// works without internet. A server that already has the complete table is
    /// left untouched.
    /// </summary>
    static class ServerSpellDbc
    {
        public const string AssetName = "Spell.dbc.zip";
        const string Sha256 = "04E036F3C8003D3FA116527EF7E50E8196D9AFFAFA6DB31062F1A88E7DE09165";
        const string DbcSha256 = "8F0A054309D6909D253DA4587F9CF2468AB340A49B947A79F513498F9ADED87C";
        static readonly string[] DownloadUrls =
        {
            "https://raw.githubusercontent.com/" + Product.GitHubRepo + "/758dfb7/assets/coa-spell-dbc/Spell.dbc.zip",
            "https://github.com/" + Product.GitHubRepo + "/releases/latest/download/Spell.dbc.zip"
        };

        static string CacheFile()
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AFK Realm");
            return Path.Combine(folder, AssetName);
        }

        static string[] Candidates()
        {
            string baseDir = AppContext.BaseDirectory;
            return new[]
            {
                Path.Combine(baseDir, AssetName),
                Path.Combine(Environment.CurrentDirectory, AssetName),
                CacheFile()
            };
        }

        static bool Matches(byte[] data)
        {
            if (data == null || data.Length == 0) return false;
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").Equals(Sha256, StringComparison.OrdinalIgnoreCase);
        }

        static string FileSha256(string path)
        {
            try
            {
                using (var sha = SHA256.Create())
                using (var stream = File.OpenRead(path))
                    return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            }
            catch { return null; }
        }

        static bool IsDbc(byte[] data)
        {
            if (data == null || data.Length == 0) return false;
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").Equals(DbcSha256, StringComparison.OrdinalIgnoreCase);
        }

        static string Target(Install inst) { return Path.Combine(inst.DataDir, "dbc", "Spell.dbc"); }

        public static bool IsInstalled(Install inst)
        {
            string target = Target(inst);
            if (!File.Exists(target)) return false;
            if (new FileInfo(target).Length != 225077421) return false;
            return string.Equals(FileSha256(target), DbcSha256, StringComparison.OrdinalIgnoreCase);
        }

        static byte[] Read()
        {
            foreach (string path in Candidates())
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    byte[] data = File.ReadAllBytes(path);
                    if (Matches(data)) return data;
                    try { File.Delete(path); } catch { }
                }
                catch { }
            }

            string cached = CacheFile();
            Directory.CreateDirectory(Path.GetDirectoryName(cached));
            string part = cached + ".part";
            string last = null;
            foreach (string url in DownloadUrls)
            {
                try
                {
                    try { if (File.Exists(part)) File.Delete(part); } catch { }
                    try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }
                    var req = (HttpWebRequest)WebRequest.Create(url);
                    req.UserAgent = Product.FileStem;
                    req.Timeout = 30000;
                    req.ReadWriteTimeout = 60000;
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    using (Stream src = resp.GetResponseStream())
                    using (var dst = File.Create(part))
                    {
                        var buffer = new byte[64 * 1024];
                        int n;
                        while ((n = src.Read(buffer, 0, buffer.Length)) > 0) dst.Write(buffer, 0, n);
                    }
                    byte[] data = File.ReadAllBytes(part);
                    if (!Matches(data))
                        throw new InvalidOperationException(L.T("The downloaded server Spell.dbc file has the wrong checksum."));
                    if (File.Exists(cached)) File.Delete(cached);
                    File.Move(part, cached);
                    return data;
                }
                catch (Exception ex)
                {
                    last = ex.Message;
                    try { if (File.Exists(part)) File.Delete(part); } catch { }
                }
            }
            throw new InvalidOperationException(L.F("The server Spell.dbc file could not be downloaded. Download {0} from the AFK Realm repository and put it next to AFK-Realm.exe, then run the setup again. ({1})", AssetName, last));
        }

        static byte[] DbcFromZip(byte[] data)
        {
            using (var zip = new ZipArchive(new MemoryStream(data, false), ZipArchiveMode.Read))
            {
                ZipArchiveEntry file = zip.GetEntry("Spell.dbc");
                if (file == null) throw new InvalidOperationException(L.T("The server Spell.dbc file is incomplete."));
                using (Stream source = file.Open())
                using (var memory = new MemoryStream())
                {
                    source.CopyTo(memory);
                    return memory.ToArray();
                }
            }
        }

        /// <summary>Writes the complete server table and keeps the previous one as .coa-patcher-backup.</summary>
        public static int Install(Install inst)
        {
            if (IsInstalled(inst)) return 0;
            byte[] dbc = DbcFromZip(Read());
            if (!IsDbc(dbc))
                throw new InvalidOperationException(L.T("The server Spell.dbc file is not the expected CoA spell table."));
            string target = Target(inst);
            CoaUninstall.Capture(target);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            if (File.Exists(target))
            {
                string backup = target + ".coa-patcher-backup";
                try { if (!File.Exists(backup)) File.Copy(target, backup, false); } catch { }
            }
            try
            {
                File.WriteAllBytes(target, dbc);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(L.F("The server Spell.dbc could not be written to {0}. Close the server, then run the setup again. ({1})", target, ex.Message), ex);
            }
            return 1;
        }
    }
}
