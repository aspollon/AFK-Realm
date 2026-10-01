using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace CoAInstaller
{
    /// <summary>One visible step of the installation.</summary>
    class Phase
    {
        public string Id, Title; public int Weight;
        public Phase(string id, string title, int weight) { Id = id; Title = title; Weight = weight; }
    }

    /// <summary>
    /// Runs the embedded PowerShell engine without a console and turns its output into progress:
    /// "##AC|PHASE|id|text", "##AC|DONE|ok", "##AC|FAIL|message", download percentages,
    /// compiled source files and imported world tables.
    /// </summary>
    class EngineRunner
    {
        static readonly Phase[] InstallPhases =
        {
            new Phase("tools",     "Check tools (Git, CMake)", 3),
            new Phase("vs",        "Visual Studio C++ Build Tools", 15),
            new Phase("libs",      "Libraries (OpenSSL, Boost)", 7),
            new Phase("mysql",     "Portable MySQL database", 4),
            new Phase("source",    "Download the server source code", 5),
            new Phase("configure", "Prepare the build (CMake)", 3),
            new Phase("compile",   "Compile the server", 45),
            new Phase("database",  "Set up the database", 3),
            new Phase("world",     "Import the CoA world data", 12),
            new Phase("finish",    "Configuration and final checks", 3),
        };

        /// <summary>The steps shown for a mode. Updates back up the server right after the update check.</summary>
        public static Phase[] For(string mode)
        {
            if (mode == "Backup") return new[] { new Phase("backup", "Back up the server", 1) };
            if (mode == "Restore") return new[] { new Phase("restore", "Restore the databases", 4), new Phase("files", "Restore the server programs and versions", 1) };
            if (mode == "Update" || mode == "Rebuild")
            {
                var list = InstallPhases.ToList();
                list.Insert(1, new Phase("backup", "Back up the current server", 4));
                return list.ToArray();
            }
            return InstallPhases;
        }

        public readonly Phase[] Phases;
        public EngineRunner(string mode) { Phases = For(mode); }

        // Events are raised on the reader thread; the UI marshals them.
        public event Action<int> PhaseStarted;            // index into Phases
        public event Action<double, string> SubProgress;  // 0..1 (or <0 = unknown), description
        public event Action<string> LogLine;
        public event Action<string> Change;               // update: what changed
        public event Action<bool, string> Finished;       // success, "ok" / "uptodate" / error text

        Process proc;
        int phaseIndex = -1;
        int totalSources = 0, compiledSources = 0;
        bool finished;
        readonly HashSet<string> seenCompiled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static readonly Regex DownloadRx = new Regex(@"^Downloading .+?: (\d+)%", RegexOptions.Compiled);
        static readonly Regex CompiledRx = new Regex(@"^\s{2}([\w\-.+]+\.(cpp|cc|c))\s*$", RegexOptions.Compiled);
        static readonly Regex WorldRx = new Regex(@"Imported (\d+)/(\d+) world tables", RegexOptions.Compiled);

        public int CurrentPhase { get { return phaseIndex; } }

        public static string ExtractEngine(Install inst)
        {
            string dir = Path.Combine(inst.Root, "Builder");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, Product.EngineFileName);
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(Product.EngineResource))
            using (var r = new StreamReader(s, Encoding.UTF8))
            {
                // With BOM: Windows PowerShell 5.1 reads BOM-less scripts as ANSI.
                File.WriteAllText(path, r.ReadToEnd(), new UTF8Encoding(true));
            }
            return path;
        }

        public void Start(Install inst, string mode, string dbPassword, int dbPort, string extraArgs = null)
        {
            Directory.CreateDirectory(inst.Root);
            string engine = ExtractEngine(inst);
            var psi = new ProcessStartInfo("powershell.exe",
                "-NoLogo -NoProfile -ExecutionPolicy Bypass -File \"" + engine + "\" -InstallRoot \"" + inst.Root + "\" -DatabasePort " + dbPort +
                " -NonInteractive -Mode " + mode + (string.IsNullOrEmpty(extraArgs) ? "" : " " + extraArgs))
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = inst.Root
            };
            psi.EnvironmentVariables["AC_DB_PASSWORD"] = dbPassword ?? "";
            proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            proc.OutputDataReceived += (s, e) => { if (e.Data != null) Handle(e.Data); };
            proc.ErrorDataReceived += (s, e) => { if (e.Data != null) Handle(e.Data); };
            proc.Exited += (s, e) =>
            {
                // Give the asynchronous readers a moment to deliver the last lines.
                System.Threading.Thread.Sleep(500);
                if (!finished) Finish(proc.ExitCode == 0, proc.ExitCode == 0 ? "ok" : "The installation process ended unexpectedly (code " + proc.ExitCode + ").");
            };
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
        }

        void Finish(bool ok, string text)
        {
            lock (this) { if (finished) return; finished = true; }
            if (Finished != null) Finished(ok, text);
        }

        /// <summary>Parses one output line. Public for tests.</summary>
        public void Handle(string line)
        {
            if (line.StartsWith("##AC|"))
            {
                var p = line.Split(new[] { '|' }, 4);
                string kind = p.Length > 1 ? p[1] : "";
                string a = p.Length > 2 ? p[2] : "", b = p.Length > 3 ? p[3] : "";
                switch (kind)
                {
                    case "PHASE":
                        int idx = Array.FindIndex(Phases, x => x.Id == a);
                        if (idx >= 0) { phaseIndex = idx; if (PhaseStarted != null) PhaseStarted(idx); if (SubProgress != null) SubProgress(-1, b); }
                        break;
                    case "SOURCES": int.TryParse(a, out totalSources); break;
                    case "CHANGE": if (Change != null) Change(a); break;
                    case "DONE": Finish(true, a); break;
                    case "FAIL": Finish(false, a); break;
                }
                return;
            }
            if (LogLine != null) LogLine(line);
            if (SubProgress == null) return;
            var m = DownloadRx.Match(line);
            if (m.Success) { SubProgress(int.Parse(m.Groups[1].Value) / 100.0, "Download: " + m.Groups[1].Value + " %"); return; }
            m = WorldRx.Match(line);
            if (m.Success)
            {
                int n = int.Parse(m.Groups[1].Value), t = Math.Max(1, int.Parse(m.Groups[2].Value));
                SubProgress((double)n / t, "World data: " + n + " of " + t + " tables");
                return;
            }
            if (phaseIndex >= 0 && Phases[phaseIndex].Id == "compile")
            {
                m = CompiledRx.Match(line);
                if (m.Success && seenCompiled.Add(m.Groups[1].Value))
                {
                    compiledSources++;
                    double f = totalSources > 0 ? Math.Min(0.99, (double)compiledSources / totalSources) : -1;
                    SubProgress(f, compiledSources + " files compiled" + (totalSources > 0 ? " (of about " + totalSources + ")" : ""));
                }
            }
        }

        /// <summary>Overall progress 0..1 from the current phase and its sub progress.</summary>
        public double Overall(int phase, double sub)
        {
            if (phase < 0) return 0;
            double total = Phases.Sum(p => p.Weight), done = Phases.Take(phase).Sum(p => p.Weight);
            return Math.Min(1, (done + Phases[phase].Weight * Math.Max(0, sub)) / total);
        }

        public void Cancel()
        {
            try
            {
                if (proc != null && !proc.HasExited)
                {
                    // Ends the engine together with the tools it started (git, cmake, compiler, installers).
                    var k = Process.Start(new ProcessStartInfo("taskkill", "/PID " + proc.Id + " /T /F") { UseShellExecute = false, CreateNoWindow = true });
                    if (k != null) k.WaitForExit(15000);
                }
            }
            catch { }
            Finish(false, "Cancelled.");
        }
    }
}
