using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace CoAInstaller
{
    /// <summary>
    /// The x64 Direct3D wrapper files that CoA64 needs for its HD path. They are embedded into
    /// AFK Realm so a CoA setup does not depend on the player having installed DXVK by hand.
    /// The known DXVK files are checked and replaced when they differ; the old copy is kept.
    /// </summary>
    static class Dxvk
    {
        // CoA64 uses the D3D9Ex device; d3d9.dll and dxgi.dll are the two files that must
        // come with it. The other DXVK wrappers are not loaded by this client.
        static readonly string[] Files = { "d3d9.dll", "dxgi.dll" };
        const string Resource = "CoAInstaller.dxvk.zip";
        const string StableConfig =
            "dxvk.enableGraphicsPipelineLibrary = False\r\n" +
            "dxvk.trackPipelineLifetime = False\r\n" +
            "d3d9.floatEmulation = False\r\n" +
            "d3d9.textureMemory = 0\r\n";

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
            ZipArchiveEntry config = zip.CreateEntry("dxvk.conf", CompressionLevel.Optimal);
            using (Stream output = config.Open())
            using (StreamWriter writer = new StreamWriter(output, new UTF8Encoding(false)))
                writer.Write(StableConfig);
        }

        /// <summary>
        /// Writes the bundled x64 DLLs to &lt;client&gt;\x64 and returns how many were written.
        /// An old DXVK copy is replaced and kept as .coa-patcher-backup; an identical copy is
        /// left alone.
        /// </summary>
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
                    byte[] wanted;
                    using (Stream source = file.Open())
                    using (MemoryStream memory = new MemoryStream())
                    {
                        source.CopyTo(memory);
                        wanted = memory.ToArray();
                    }
                    if (File.Exists(target))
                    {
                        byte[] present = File.ReadAllBytes(target);
                        if (Same(present, wanted)) continue;
                        CoaUninstall.Capture(target);
                        string backup = target + ".coa-patcher-backup";
                        try { if (!File.Exists(backup)) File.Copy(target, backup, false); } catch { }
                    }
                    else
                    {
                        CoaUninstall.Capture(target);
                    }
                    File.WriteAllBytes(target, wanted);
                    written++;
                }
            }
            Configure(client);
            return written;
        }

        static bool Same(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>
        /// CoA64 can crash on a cold start with DXVK's experimental graphics-pipeline
        /// library. Keep the stable settings while preserving every other line.
        /// </summary>
        static void Configure(string client)
        {
            string path = Path.Combine(client, "dxvk.conf");
            string text = File.Exists(path) ? File.ReadAllText(path) : "";
            bool risky = Regex.IsMatch(text, @"(?im)^\s*dxvk\.(enableGraphicsPipelineLibrary|trackPipelineLifetime)\s*=\s*True\s*$");
            if (File.Exists(path) && !risky) return;

            if (File.Exists(path))
            {
                CoaUninstall.Capture(path);
                string backup = path + ".coa-patcher-backup";
                try { if (!File.Exists(backup)) File.Copy(path, backup, false); } catch { }
                text = Regex.Replace(text, @"(?im)^\s*dxvk\.enableGraphicsPipelineLibrary\s*=.*$", "dxvk.enableGraphicsPipelineLibrary = False");
                text = Regex.Replace(text, @"(?im)^\s*dxvk\.trackPipelineLifetime\s*=.*$", "dxvk.trackPipelineLifetime = False");
                if (!Regex.IsMatch(text, @"(?im)^\s*dxvk\.enableGraphicsPipelineLibrary\s*="))
                    text += (text.EndsWith("\n") ? "" : "\r\n") + "dxvk.enableGraphicsPipelineLibrary = False\r\n";
                if (!Regex.IsMatch(text, @"(?im)^\s*dxvk\.trackPipelineLifetime\s*="))
                    text += "dxvk.trackPipelineLifetime = False\r\n";
                File.WriteAllText(path, text, new UTF8Encoding(false));
            }
            else
            {
                CoaUninstall.Capture(path);
                File.WriteAllText(path, StableConfig, new UTF8Encoding(false));
            }
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
