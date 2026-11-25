using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace CoAInstaller
{
    /// <summary>
    /// AFK64.exe draws on Vulkan only with a SPIR-V module for every DX9 shader the game loads (looked up by md5). The
    /// voices release brings modules for a CoA Custom client; a shader the player's own archives add (the HD pack's,
    /// a newer CoA patch) has none, and its terrain, buildings or models flicker or turn black. This translates exactly
    /// those, on the player's machine, with CoA64's translator (Tools/VulkanShaders), and adds them to VulkanShaders.
    /// Python (embeddable) and glslang are downloaded once into LocalAppData. docs/2026-11-21-vulkan-shaders.md
    /// </summary>
    static class ClientShaders
    {
        const string Folder = "VulkanShaders";
        // Azeroth Remastered's set (CoA64 on Mado's engine): its manifest.txt stays as shipped, ours go to manifest-coa.txt.
        static readonly string MadoFolder = Path.Combine("Vulkan", "shaders", "translated");
        const string Stamp = ".afk-translated";
        // Bump with the translator in assets/vulkan-shaders: a new one translates again.
        const string TranslatorVersion = "coa64-2026-11-25";
        const string PythonVersion = "3.12.10";
        const string GlslangVersion = "16.6.0";
        static readonly string PythonUrl = "https://www.python.org/ftp/python/" + PythonVersion + "/python-" + PythonVersion + "-embed-amd64.zip";
        static readonly string GlslangUrl = "https://github.com/KhronosGroup/glslang/releases/download/" + GlslangVersion + "/glslang-" + GlslangVersion + "-windows-x86_64-release.zip";

        static string Tools() { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AFK Realm", "shader-tools"); }

        /// <summary>The release's manifest as installed, kept beside the merged one (ClientVoices compares with it).</summary>
        public static string ReleaseManifest(string client) { return Path.Combine(client, Folder, "release-manifest.txt"); }

        /// <summary>After the release's set was (re)installed: the next Ensure translates again.</summary>
        public static void Reset(string client)
        {
            foreach (string rel in new[] { Folder, MadoFolder })
                try { File.Delete(Path.Combine(client, rel, Stamp)); } catch { }
        }

        /// <summary>The shader sets in this game folder: the old VulkanShaders and Azeroth Remastered's, whichever exist.</summary>
        static IEnumerable<string> Sets(string client)
        {
            foreach (string rel in new[] { Folder, MadoFolder })
            {
                string set = Path.Combine(client, rel);
                if (File.Exists(Path.Combine(set, "manifest.txt"))) yield return set;
            }
        }

        /// <summary>The archives the game reads shaders from: names, sizes and times, so a changed patch translates again.</summary>
        static string Fingerprint(string client)
        {
            var sb = new StringBuilder(TranslatorVersion).Append('\n');
            string data = Path.Combine(client, "Data");
            foreach (string f in Directory.GetFiles(data, "*.mpq", new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive })
                         .OrderBy(x => Path.GetFileName(x), StringComparer.OrdinalIgnoreCase))
            {
                var fi = new FileInfo(f);
                sb.Append(fi.Name.ToLowerInvariant()).Append('|').Append(fi.Length).Append('|').Append(fi.LastWriteTimeUtc.Ticks).Append('\n');
            }
            // The exe dumps its own HD shaders here (snow and the like); new ones translate again.
            string blobs = Path.Combine(client, "Vulkan", "hd-blobs");
            if (Directory.Exists(blobs))
                foreach (string f in Directory.GetFiles(blobs, "*.bin").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    sb.Append("blob|").Append(Path.GetFileName(f).ToLowerInvariant()).Append('|').Append(new FileInfo(f).Length).Append('\n');
            if (File.Exists(ReleaseManifest(client))) sb.Append("release|").Append(new FileInfo(ReleaseManifest(client)).Length).Append('\n');
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()))).Replace("-", "");
        }

        /// <summary>This folder has a VulkanShaders set that the last translation did not cover (it failed or never ran).</summary>
        public static bool Missing(string client)
        {
            try
            {
                string print = null;
                foreach (string set in Sets(client))
                {
                    string stamp = Path.Combine(set, Stamp);
                    print ??= Fingerprint(client);
                    if (!File.Exists(stamp) || File.ReadAllText(stamp).Trim() != print) return true;
                }
                return false;
            }
            catch { return true; }
        }

        /// <summary>
        /// Translates the shaders of this game folder that VulkanShaders has no module for. Null when there was nothing to
        /// do; otherwise one line for the log. Never throws: a game without the new modules still starts.
        /// </summary>
        public static string Ensure(string client, Action<string, long, long> report)
        {
            try
            {
                if (!File.Exists(Path.Combine(client, "Data", "common.MPQ")) && !File.Exists(Path.Combine(client, "Data", "common.mpq"))) return null;
                string print = Fingerprint(client);
                var todo = Sets(client).Where(set => !File.Exists(Path.Combine(set, Stamp)) || File.ReadAllText(Path.Combine(set, Stamp)).Trim() != print).ToList();
                if (todo.Count == 0) return null;

                string python, glslang;
                if (!FindTools(report, out python, out glslang))
                    return L.T("Vulkan shaders: Python or glslang is missing, the game's own shaders were not translated (install python3 and glslang).");
                string translator = Translator();
                report(L.T("Translating the game's shaders for Vulkan (a minute or two) ..."), 0, 0);
                int n = 0, code = 0;
                string output = "";
                foreach (string set in todo)
                {
                    int one = Run(python, new[] { Path.Combine(translator, "translate_shaders.py"), "--client", client, "--glslang", glslang, "--merge-into", set }, out output);
                    if (one != 0 && one != 1)
                        return L.F("Vulkan shaders: the translation stopped ({0}). {1}", one, LastLine(output));
                    code = Math.Max(code, one);
                    File.WriteAllText(Path.Combine(set, Stamp), print);
                    string merged = output.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("merged ", StringComparison.Ordinal));
                    int m = 0;
                    if (merged != null) int.TryParse(merged.Split(' ')[1], out m);
                    n += m;
                }
                if (n == 0) return null;
                return code == 0
                    ? L.F("Vulkan shaders: {0} of your game's shaders translated (HD pack, newer patches)", n)
                    : L.F("Vulkan shaders: {0} of your game's shaders translated, some could not be ({1})", n, LastLine(output));
            }
            catch (Exception e) { return L.F("Vulkan shaders: not translated ({0})", e.Message); }
        }

        static string LastLine(string text)
        {
            return (text ?? "").Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0) ?? "";
        }

        static int Run(string exe, string[] args, out string output)
        {
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (string a in args) psi.ArgumentList.Add(a);
            psi.Environment["PYTHONIOENCODING"] = "utf-8";
            var sb = new StringBuilder();
            using (var p = new Process { StartInfo = psi })
            {
                p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                if (!p.WaitForExit(30 * 60 * 1000)) { try { p.Kill(true); } catch { } output = sb.ToString(); return -1; }
                p.WaitForExit();
                output = sb.ToString();
                return p.ExitCode;
            }
        }

        /// <summary>CoA64's translator, from AFK-Realm.exe, unpacked once per version.</summary>
        static string Translator()
        {
            string dir = Path.Combine(Tools(), "translator-" + TranslatorVersion);
            if (File.Exists(Path.Combine(dir, "translate_shaders.py"))) return dir;
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("CoAInstaller.vulkanshaders.zip"))
            using (var zip = new ZipArchive(s))
            {
                string tmp = dir + ".part";
                if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
                zip.ExtractToDirectory(tmp);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.Move(tmp, dir);
            }
            return dir;
        }

        static bool FindTools(Action<string, long, long> report, out string python, out string glslang)
        {
            if (!Os.Windows)
            {
                python = OnPath("python3");
                glslang = OnPath("glslangValidator") ?? OnPath("glslang");
                return python != null && glslang != null;
            }
            string tools = Tools();
            python = Path.Combine(tools, "python-" + PythonVersion, "python.exe");
            if (!File.Exists(python)) Unpack(PythonUrl, Path.Combine(tools, "python-" + PythonVersion), report);
            string gdir = Path.Combine(tools, "glslang-" + GlslangVersion);
            glslang = Directory.Exists(gdir) ? Directory.GetFiles(gdir, "glslang.exe", SearchOption.AllDirectories).FirstOrDefault() : null;
            if (glslang == null)
            {
                Unpack(GlslangUrl, gdir, report);
                glslang = Directory.GetFiles(gdir, "glslang.exe", SearchOption.AllDirectories).FirstOrDefault();
            }
            return File.Exists(python) && glslang != null;
        }

        static string OnPath(string name)
        {
            foreach (string d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                try { string f = Path.Combine(d, name); if (File.Exists(f)) return f; } catch { }
            }
            return null;
        }

        static void Unpack(string url, string target, Action<string, long, long> report)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            string zip = target + ".zip";
            string name = Path.GetFileName(new Uri(url).AbsolutePath);
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = Product.FileStem;
            req.Timeout = 30000;
            req.ReadWriteTimeout = 60000;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var src = resp.GetResponseStream())
            using (var dst = new FileStream(zip, FileMode.Create, FileAccess.Write))
            {
                var buffer = new byte[256 * 1024];
                long done = 0, total = resp.ContentLength;
                int n;
                while ((n = src.Read(buffer, 0, buffer.Length)) > 0)
                {
                    dst.Write(buffer, 0, n);
                    done += n;
                    report(L.F("Downloading {0} ...", name), done, total);
                }
            }
            string tmp = target + ".part";
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            ZipFile.ExtractToDirectory(zip, tmp);
            if (Directory.Exists(target)) Directory.Delete(target, true);
            Directory.Move(tmp, target);
            File.Delete(zip);
        }
    }
}
