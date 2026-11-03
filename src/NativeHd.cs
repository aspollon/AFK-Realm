using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace CoAInstaller
{
    /// <summary>
    /// The patched CoA64 client for CoA Custom 1.9.8. It carries the two model-sequence
    /// bounds checks (an unsigned underflow in CM2Model::FindKey and a retail centaur
    /// sequence chunk whose count exceeded its keys) and the HD archive handle reserve
    /// of the native build. The normal CoA64 only opens the HD archives when its Vulkan
    /// (DXVK) renderer is active; this build keeps that choice and also ignores a broken
    /// x64 DXVK folder unless started with -dxvk.
    ///
    /// The 8 MB ZIP is kept out of AFK-Realm.exe: the Windows release has to stay below
    /// GitHub's 50 MiB update limit. It is downloaded from the repository once and kept
    /// in LocalAppData; a CoA64.zip next to AFK-Realm.exe works without internet.
    /// </summary>
    static class NativeHd
    {
        public const string AssetName = "CoA64.zip";
        const string Sha256 = "85FC14320DC072673066B90161032D581D53F8D76D7926DEFB9ADBB3C11C6A90";
        const string ExeSha256 = "09D3DD17D7FEC1FB3EA88595AA38CFC3068DF29970BF51BFFCB3BE779B97AAE0";
        // The official CoA64.exe build of CoA Custom 1.9.8 this patched client was made
        // for. A newer package must not keep it next to new race and spell data.
        static readonly string[] SupportedOfficialSha256 =
        {
            "5055A35DA3B1638B75402B992B9CA3D3DC6094A99940796A5CEBBD82920917D9"
        };
        static readonly string[] DownloadUrls =
        {
            "https://raw.githubusercontent.com/" + Product.GitHubRepo + "/main/assets/coa64-native/CoA64.zip",
            "https://github.com/" + Product.GitHubRepo + "/releases/latest/download/CoA64.zip"
        };

        public const int Count = 1;

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
                Path.Combine(baseDir, "CoA64-native.zip"),
                Path.Combine(Environment.CurrentDirectory, "CoA64-native.zip"),
                CacheFile()
            };
        }

        static bool Matches(byte[] data)
        {
            if (data == null || data.Length == 0) return false;
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").Equals(Sha256, StringComparison.OrdinalIgnoreCase);
        }

        static bool IsNativeExe(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                using (var sha = SHA256.Create())
                using (var stream = File.OpenRead(path))
                    return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").Equals(ExeSha256, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
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

        /// <summary>
        /// Whether this native build belongs to the official CoA64.exe in the chosen
        /// package. A CoA Custom update with a different CoA64.exe must use the official
        /// exe instead of keeping an old native build next to new race data.
        /// </summary>
        public static bool SupportsPackage(string clientRoot)
        {
            string official = Path.Combine(clientRoot ?? "", "CoA64.exe");
            if (!File.Exists(official)) return true; // the package does not replace the client exe

            // The package version is the stable rule. This patched client is built for
            // the 1.9.8 client; a later package must use its own official exe until a
            // matching patched build exists.
            try
            {
                string manifest = Path.GetFullPath(Path.Combine(clientRoot, "..", "..", "manifest.json"));
                if (File.Exists(manifest))
                {
                    string text = File.ReadAllText(manifest);
                    if (Regex.IsMatch(text, "\"version\"\\s*:\\s*\"1\\.9\\.8\"")) return true;
                }
            }
            catch { }

            string hash = FileSha256(official);
            if (string.IsNullOrEmpty(hash)) return false;
            if (hash.Equals(ExeSha256, StringComparison.OrdinalIgnoreCase)) return true;
            foreach (string supported in SupportedOfficialSha256)
                if (hash.Equals(supported, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
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
                        throw new InvalidOperationException(L.T("The downloaded native HD CoA64 file has the wrong checksum."));
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
            throw new InvalidOperationException(L.F("The native HD CoA64 file could not be downloaded. Download {0} from the AFK Realm repository and put it next to AFK-Realm.exe, then run the setup again. ({1})", AssetName, last));
        }

        static ZipArchive OpenPacked(byte[] data)
        {
            return new ZipArchive(new MemoryStream(data, false), ZipArchiveMode.Read);
        }

        static ZipArchiveEntry ExeEntry(ZipArchive zip)
        {
            ZipArchiveEntry file = zip.GetEntry("CoA64.exe");
            if (file == null) throw new InvalidOperationException(L.T("The native HD CoA64 file is incomplete."));
            return file;
        }

        public static bool IsInstalled(string client)
        {
            try
            {
                string target = Path.Combine(client ?? "", "CoA64.exe");
                if (!File.Exists(target)) return false;
                using (ZipArchive zip = OpenPacked(Read()))
                using (Stream source = ExeEntry(zip).Open())
                {
                    var wanted = new MemoryStream();
                    source.CopyTo(wanted);
                    byte[] present = File.ReadAllBytes(target);
                    byte[] expected = wanted.ToArray();
                    if (present.Length != expected.Length) return false;
                    for (int i = 0; i < present.Length; i++) if (present[i] != expected[i]) return false;
                    return true;
                }
            }
            catch { return false; }
        }

        /// <summary>Writes the native HD CoA64.exe and keeps the previous one as .coa-patcher-backup.</summary>
        public static int Install(string client)
        {
            string target = Path.Combine(client, "CoA64.exe");
            // A user who tested a native build already has the correct exe. Do not
            // depend on a download just to compare it with itself.
            if (IsNativeExe(target)) return 0;

            byte[] expected;
            using (ZipArchive zip = OpenPacked(Read()))
            using (Stream source = ExeEntry(zip).Open())
            using (var memory = new MemoryStream())
            {
                source.CopyTo(memory);
                expected = memory.ToArray();
            }

            CoaUninstall.Capture(target);
            if (File.Exists(target))
            {
                string backup = target + ".coa-patcher-backup";
                try { if (!File.Exists(backup)) File.Copy(target, backup, false); } catch { }
            }
            try
            {
                File.WriteAllBytes(target, expected);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(L.F("The native HD CoA64.exe could not be written to {0}. Close the game, the launcher and the server, then run the setup again. ({1})", target, ex.Message), ex);
            }
            return 1;
        }

        /// <summary>Adds the native HD CoA64.exe to a complete-client ZIP.</summary>
        public static void AddToZip(ZipArchive zip)
        {
            using (ZipArchive packed = OpenPacked(Read()))
            using (Stream source = ExeEntry(packed).Open())
            {
                ZipArchiveEntry entry = zip.CreateEntry("CoA64.exe", CompressionLevel.NoCompression);
                using (Stream output = entry.Open()) source.CopyTo(output);
            }
        }
    }
}
