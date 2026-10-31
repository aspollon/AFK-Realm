using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;

namespace CoAInstaller
{
    /// <summary>
    /// CoA64 1.9.5 with HD on the native 64-bit Direct3D9 renderer. The normal CoA64
    /// only opens the HD archives when its Vulkan (DXVK) renderer is active; machines
    /// where the x64 DXVK preload crashes therefore fall back to the old models. This
    /// build loads HD with system D3D9 and ignores an existing x64 DXVK folder unless it
    /// is started with -dxvk, so a bad x64 folder can no longer stop the client.
    ///
    /// The 8 MB ZIP is kept out of AFK-Realm.exe: the Windows release has to stay below
    /// GitHub's 50 MiB update limit. It is downloaded from the repository once and kept
    /// in LocalAppData; a CoA64.zip next to AFK-Realm.exe works without internet.
    /// </summary>
    static class NativeHd
    {
        public const string AssetName = "CoA64.zip";
        const string Sha256 = "6B175B8BFE4C4405D37DC89DD7B04E63F0BE0D2DBFB3866618A1DCAA49B5424B";
        const string DownloadUrl = "https://raw.githubusercontent.com/" + Product.GitHubRepo + "/main/assets/coa64-native/CoA64.zip";

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
            try { if (File.Exists(part)) File.Delete(part); } catch { }
            try
            {
                try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }
                var req = (HttpWebRequest)WebRequest.Create(DownloadUrl);
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
                try { if (File.Exists(part)) File.Delete(part); } catch { }
                throw new InvalidOperationException(L.F("The native HD CoA64 file could not be downloaded from {0}. Download {1} from the AFK Realm repository and put it next to AFK-Realm.exe, then run the setup again. ({2})", DownloadUrl, AssetName, ex.Message), ex);
            }
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
            byte[] expected;
            using (ZipArchive zip = OpenPacked(Read()))
            using (Stream source = ExeEntry(zip).Open())
            using (var memory = new MemoryStream())
            {
                source.CopyTo(memory);
                expected = memory.ToArray();
            }

            string target = Path.Combine(client, "CoA64.exe");
            if (File.Exists(target))
            {
                byte[] present = File.ReadAllBytes(target);
                if (present.Length == expected.Length)
                {
                    bool same = true;
                    for (int i = 0; i < present.Length; i++) if (present[i] != expected[i]) { same = false; break; }
                    if (same) return 0;
                }
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
