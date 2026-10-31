using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace CoAInstaller
{
    /// <summary>
    /// The x64 Direct3D wrapper files that CoA64 needs for its HD path. They are embedded into
    /// AFK Realm so a CoA setup does not depend on the player having installed DXVK by hand.
    /// Only missing files are written; a player's own wrapper files are left alone.
    /// </summary>
    static class Dxvk
    {
        // CoA64 uses the D3D9Ex device; d3d9.dll and dxgi.dll are the two files that must
        // come with it. The other DXVK wrappers are not loaded by this client.
        static readonly string[] Files = { "d3d9.dll", "dxgi.dll" };
        const string Resource = "CoAInstaller.dxvk.zip";

        public static int Count { get { return Files.Length; } }

        static ZipArchive OpenPacked()
        {
            Stream packed = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource);
            if (packed == null) throw new InvalidOperationException(L.T("The x64 DXVK files are missing from AFK Realm."));
            return new ZipArchive(packed, ZipArchiveMode.Read);
        }

        /// <summary>Adds the embedded wrappers to a complete-client ZIP.</summary>
        public static void AddToZip(ZipArchive zip)
        {
            using (ZipArchive packed = OpenPacked())
            {
                foreach (ZipArchiveEntry file in packed.Entries)
                {
                    ZipArchiveEntry entry = zip.CreateEntry("x64/" + file.FullName, CompressionLevel.NoCompression);
                    using (Stream source = file.Open())
                    using (Stream output = entry.Open()) source.CopyTo(output);
                }
            }
        }

        /// <summary>Writes the missing x64 DLLs to &lt;client&gt;\x64 and returns how many were written.</summary>
        public static int Install(string client)
        {
            string folder = Path.Combine(client, "x64");
            Directory.CreateDirectory(folder);
            int written = 0;
            using (ZipArchive packed = OpenPacked())
            {
                foreach (ZipArchiveEntry file in packed.Entries)
                {
                    string name = Path.GetFileName(file.FullName);
                    if (Array.IndexOf(Files, name) < 0) continue;
                    string target = Path.Combine(folder, name);
                    if (File.Exists(target) && new FileInfo(target).Length > 0) continue;
                    CoaUninstall.Capture(target);
                    using (Stream source = file.Open())
                    using (FileStream output = File.Create(target))
                        source.CopyTo(output);
                    written++;
                }
            }
            return written;
        }

        /// <summary>True when every wrapper file is present in the client.</summary>
        public static bool IsComplete(string client)
        {
            string folder = Path.Combine(client, "x64");
            foreach (string name in Files)
                if (!File.Exists(Path.Combine(folder, name)) || new FileInfo(Path.Combine(folder, name)).Length == 0) return false;
            return true;
        }
    }
}
