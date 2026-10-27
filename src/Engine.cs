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
            new Phase("tools",     L.T("Check tools (Git, CMake)"), 3),
            Os.Windows ? new Phase("vs", L.T("Visual Studio C++ Build Tools"), 15) : new Phase("vs", L.T("Compiler and libraries"), 2),
            new Phase("libs",      L.T("Libraries (OpenSSL, Boost)"), Os.Windows ? 7 : 0),
            new Phase("mysql",     L.T("Portable MySQL database"), 4),
            new Phase("source",    L.T("Download the server source code"), 5),
            new Phase("configure", L.T("Prepare the build (CMake)"), 3),
            new Phase("compile",   L.T("Compile the server"), 45),
            new Phase("database",  L.T("Set up the database"), 3),
            new Phase("world",     L.T("Import the CoA world data"), 12),
            new Phase("finish",    L.T("Configuration and final checks"), 3),
        };

        /// <summary>The steps shown for a mode. Updates back up the server right after the update check.</summary>
        public static Phase[] For(string mode)
        {
            if (mode == "Backup") return new[] { new Phase("backup", L.T("Back up the server"), 1) };
            if (mode == "RestoreParts") return new[] { new Phase("restore", L.T("Restore the databases"), 3), new Phase("files", L.T("Restore the settings"), 1), new Phase("finish", L.T("Bring the databases up to date"), 3) };
            if (mode == "Restore") return new[] { new Phase("restore", L.T("Restore the databases"), 4), new Phase("files", L.T("Restore the server programs and versions"), 1) };
            if (mode == "Modules")
            {
                return new[]
                {
                    new Phase("tools",     L.T("Check tools (Git, CMake)"), 2),
                    new Phase("backup",    L.T("Back up the current server"), 6),
                    Os.Windows ? new Phase("vs", L.T("Visual Studio C++ Build Tools"), 3) : new Phase("vs", L.T("Compiler and libraries"), 1),
                    new Phase("libs",      L.T("Libraries (OpenSSL, Boost)"), Os.Windows ? 2 : 0),
                    new Phase("mysql",     L.T("Portable MySQL database"), 1),
                    new Phase("modules",   L.T("Download and remove modules"), 4),
                    new Phase("configure", L.T("Prepare the build (CMake)"), 4),
                    new Phase("compile",   L.T("Compile the server"), 60),
                    new Phase("database",  L.T("Database: undo removed modules"), 4),
                    new Phase("world",     L.T("World data check"), 1),
                    new Phase("finish",    L.T("Database: apply and record new modules, final checks"), 8),
                };
            }
            if (mode == "Update" || mode == "Rebuild")
            {
                var list = InstallPhases.ToList();
                list.Insert(1, new Phase("backup", L.T("Back up the current server"), 4));
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
        public event Action<string> Note;                 // module results for the result page
        public event Action<bool, string> Finished;       // success, "ok" / "uptodate" / error text

        Process proc;
        int phaseIndex = -1;
        int totalSources = 0, compiledSources = 0;
        bool finished;
        readonly HashSet<string> seenCompiled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static readonly Regex DownloadRx = new Regex(@"^Downloading .+?: (\d+)%", RegexOptions.Compiled);
        static readonly Regex CompiledRx = new Regex(@"^\s{2}([\w\-.+]+\.(cpp|cc|c))\s*$", RegexOptions.Compiled);
        static readonly Regex WorldRx = new Regex(@"Imported (\d+)/(\d+) world tables", RegexOptions.Compiled);
        // Linux builds with Ninja ("[12/1534] Building CXX object ...") or make ("[ 12%] Building CXX object ...").
        static readonly Regex NinjaRx = new Regex(@"^\[(\d+)/(\d+)\] ", RegexOptions.Compiled);
        static readonly Regex MakeRx = new Regex(@"^\[\s*(\d+)%\] ", RegexOptions.Compiled);

        /// <summary>The PowerShell that runs the engine: Windows PowerShell on Windows, PowerShell 7 (pwsh) elsewhere.</summary>
        public static string Shell;

        public int CurrentPhase { get { return phaseIndex; } }
        /// <summary>A module was installed in this run (the result page points to the READMEs).</summary>
        public bool ModuleInstalled;
        // What the engine says before a backup ("The backup before the update failed ..."), for the language files.
        static readonly string[] BackupReasons = { L.N("update"), L.N("rebuild"), L.N("setup"), L.N("module change") };

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
            // The SQL procedures that record and undo module database changes.
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("CoAInstaller.module-journal.sql"))
            using (var r = new StreamReader(s, Encoding.UTF8))
                File.WriteAllText(Path.Combine(dir, "module-journal.sql"), r.ReadToEnd(), new UTF8Encoding(false));
            return path;
        }

        /// <summary>Name for the realm on a new installation; empty keeps the name it has.</summary>
        public string RealmName;

        public void Start(Install inst, string mode, string dbPassword, int dbPort, string extraArgs = null)
        {
            Directory.CreateDirectory(inst.Root);
            string engine = ExtractEngine(inst);
            var psi = new ProcessStartInfo(Shell ?? (Os.Windows ? "powershell.exe" : "pwsh"),
                "-NoLogo -NoProfile -ExecutionPolicy Bypass -File \"" + engine + "\" -InstallRoot \"" + inst.Root + "\" -DatabasePort " + dbPort +
                " -NonInteractive -Mode " + mode + (string.IsNullOrEmpty(extraArgs) ? "" : " " + extraArgs))
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = inst.Root
            };
            psi.EnvironmentVariables["AC_DB_PASSWORD"] = dbPassword ?? "";
            psi.EnvironmentVariables["AC_REALM_NAME"] = RealmName ?? "";
            proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            proc.OutputDataReceived += (s, e) => { if (e.Data != null) Handle(e.Data); };
            proc.ErrorDataReceived += (s, e) => { if (e.Data != null) Handle(e.Data); };
            proc.Exited += (s, e) =>
            {
                // Give the asynchronous readers a moment to deliver the last lines.
                System.Threading.Thread.Sleep(500);
                if (!finished) Finish(proc.ExitCode == 0, proc.ExitCode == 0 ? "ok" : L.F("The installation process ended unexpectedly (code {0}).", proc.ExitCode));
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
                // Everything after the kind, for texts that may hold a '|' themselves.
                string all = line.Length > 6 + kind.Length ? line.Substring(6 + kind.Length) : "";
                // Texts of the engine come marked for translation (Tr in engine.ps1).
                if (kind == "PHASE") b = L.Engine(b);
                else if (kind == "NOTE" && L.Plain(all).Contains(" was installed")) ModuleInstalled = true;
                switch (kind)
                {
                    case "PHASE":
                        int idx = Array.FindIndex(Phases, x => x.Id == a);
                        if (idx >= 0) { phaseIndex = idx; if (PhaseStarted != null) PhaseStarted(idx); if (SubProgress != null) SubProgress(-1, b); }
                        break;
                    case "SOURCES": int.TryParse(a, out totalSources); break;
                    case "CHANGE": if (Change != null) Change(L.Engine(all)); break;
                    case "NOTE": if (Note != null) Note(L.Engine(all)); break;
                    case "DONE": Finish(true, a); break;
                    case "FAIL": Finish(false, L.Engine(all)); break;
                }
                return;
            }
            line = L.Plain(line);
            if (LogLine != null) LogLine(line);
            if (SubProgress == null) return;
            var m = DownloadRx.Match(line);
            if (m.Success) { SubProgress(int.Parse(m.Groups[1].Value) / 100.0, L.F("Download: {0} %", m.Groups[1].Value)); return; }
            m = WorldRx.Match(line);
            if (m.Success)
            {
                int n = int.Parse(m.Groups[1].Value), t = Math.Max(1, int.Parse(m.Groups[2].Value));
                SubProgress((double)n / t, L.F("World data: {0} of {1} tables", n, t));
                return;
            }
            if (phaseIndex >= 0 && Phases[phaseIndex].Id == "compile")
            {
                m = NinjaRx.Match(line);
                if (m.Success)
                {
                    int done = int.Parse(m.Groups[1].Value), all = Math.Max(1, int.Parse(m.Groups[2].Value));
                    SubProgress(Math.Min(0.99, (double)done / all), L.F("{0} files compiled (of about {1})", done, all));
                    return;
                }
                m = MakeRx.Match(line);
                if (m.Success)
                {
                    SubProgress(Math.Min(0.99, int.Parse(m.Groups[1].Value) / 100.0), L.F("Compiling: {0} %", m.Groups[1].Value));
                    return;
                }
                m = CompiledRx.Match(line);
                if (m.Success && seenCompiled.Add(m.Groups[1].Value))
                {
                    compiledSources++;
                    double f = totalSources > 0 ? Math.Min(0.99, (double)compiledSources / totalSources) : -1;
                    SubProgress(f, (totalSources > 0 ? L.F("{0} files compiled (of about {1})", compiledSources, totalSources) : L.F("{0} files compiled", compiledSources)));
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
                if (proc != null && !proc.HasExited && !Os.Windows)
                {
#if AFK_WEB
                    proc.Kill(true);                // with git, cmake and the compilers it started
#else
                    proc.Kill();
#endif
                }
                else if (proc != null && !proc.HasExited)
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
