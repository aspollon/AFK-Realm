using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CoAInstaller
{
    /// <summary>Fixed product data. The engine script carries the repositories and branches.</summary>
    /// <summary>
    /// Product identity. Renaming the tool only needs changes here (plus the output
    /// file name in build.bat / build.sh).
    /// </summary>
    static class Product
    {
        public const string Name = "AFK Realm";                   // window titles, dialogs
        public const string ShortName = "AFK Realm";              // desktop shortcut, firewall rules
        public const string FileStem = "AFK-Realm";                // exe name, settings folder
        public const string Version = "0.1.0-preview";            // pre-release until testers confirm it works
        public const string Tagline = "build, run and tweak your own server the lazy way";
        public const string BuildsFor = "Conquest of AzerothCore";
        public const string WindowTitle = Name + " (preview)";
        public const string EngineResource = "CoAInstaller.engine.ps1";
        public const string EngineFileName = FileStem + "-Engine.ps1";
    }

    /// <summary>All paths of one installation, derived from its root folder.</summary>
    class Install
    {
        public readonly string Root;
        public Install(string root) { Root = Path.GetFullPath(root.Trim().TrimEnd('\\')); }
        public string ServerDir { get { return Path.Combine(Root, "Server"); } }
        public string ConfigDir { get { return Path.Combine(ServerDir, "configs"); } }
        public string DataDir { get { return Path.Combine(ServerDir, "Data"); } }
        public string DbDir { get { return Path.Combine(Root, "DB"); } }
        public string MySqlBin { get { return Path.Combine(DbDir, "mysql", "bin"); } }
        public string MyIni { get { return Path.Combine(DbDir, "my.ini"); } }
        public string LogDir { get { return Path.Combine(Root, "logs"); } }
        public string InstallLog { get { return Path.Combine(LogDir, "install.log"); } }
        public string SourceDir { get { return Path.Combine(Root, "Dependencies", "Source"); } }
        public string PythonExe { get { return Path.Combine(Root, "Dependencies", "Python", "python.exe"); } }
        public string MpqCliExe { get { return Path.Combine(Root, "Dependencies", "Tools", "mpqcli.exe"); } }
        public string DbcScript { get { return Path.Combine(SourceDir, "apps", "coa-dbc", "client_dbc.py"); } }
        /// <summary>True when this core needs the CoA client's own DBC set (extracted with client_dbc.py).</summary>
        public bool NeedsClientDbc { get { return File.Exists(DbcScript); } }
        public string WorldExe { get { return Path.Combine(ServerDir, "worldserver.exe"); } }
        public string AuthExe { get { return Path.Combine(ServerDir, "authserver.exe"); } }
        public string WorldConf { get { return Path.Combine(ConfigDir, "worldserver.conf"); } }
        public string AuthConf { get { return Path.Combine(ConfigDir, "authserver.conf"); } }
        public string CoaConf { get { return Path.Combine(ConfigDir, "modules", "coa.conf"); } }

        public bool IsInstalled
        {
            get { return File.Exists(WorldExe) && File.Exists(AuthExe) && File.Exists(Path.Combine(MySqlBin, "mysqld.exe")) && File.Exists(WorldConf); }
        }
        /// <summary>False when the CoA core would refuse the DBC tables (only the stock extractor's set is there).</summary>
        public bool HasClientDbc
        {
            get
            {
                if (!NeedsClientDbc) return true;
                string dbc = Path.Combine(DataDir, "dbc");
                return Directory.Exists(dbc) && Directory.EnumerateFiles(dbc, "*.dbc").Count() >= 300;
            }
        }
        public bool HasMapData
        {
            get { return Directory.Exists(Path.Combine(DataDir, "maps")) && Directory.EnumerateFiles(Path.Combine(DataDir, "maps")).Any(); }
        }
    }

    /// <summary>Reads and writes "Key = Value" lines of AzerothCore .conf files.</summary>
    static class Conf
    {
        public static string Get(string file, string key)
        {
            if (!File.Exists(file)) return null;
            var m = Regex.Match(File.ReadAllText(file), @"(?m)^\s*" + Regex.Escape(key) + @"\s*=\s*(.*?)\s*$");
            if (!m.Success) return null;
            return m.Groups[1].Value.Trim().Trim('"');
        }
        public static void Set(string file, string key, string rawValue)
        {
            string text = File.ReadAllText(file);
            string line = key + " = " + rawValue;
            var rx = new Regex(@"(?m)^\s*" + Regex.Escape(key) + @"\s*=.*$");
            if (rx.IsMatch(text)) text = rx.Replace(text, line.Replace("$", "$$"), 1);
            else text = text.TrimEnd('\r', '\n') + "\r\n" + line + "\r\n";
            File.WriteAllText(file, text, new UTF8Encoding(false));
        }
        public static int GetInt(string file, string key, int fallback)
        {
            int v; return int.TryParse(Get(file, key), out v) ? v : fallback;
        }
    }

    /// <summary>Connection data taken from worldserver.conf ("host;port;user;password;database").</summary>
    class DbLogin
    {
        public string Host, User, Password; public int Port;
        public static DbLogin FromConfig(Install inst)
        {
            string v = Conf.Get(inst.WorldConf, "LoginDatabaseInfo");
            if (v == null) throw new InvalidOperationException("LoginDatabaseInfo was not found in worldserver.conf.");
            var p = v.Split(';');
            if (p.Length < 5) throw new InvalidOperationException("LoginDatabaseInfo in worldserver.conf is incomplete.");
            return new DbLogin { Host = p[0], Port = int.Parse(p[1]), User = p[2], Password = p[3] };
        }
        /// <summary>The setup script gives root the same password as the acore user.</summary>
        public DbLogin AsRoot() { return new DbLogin { Host = Host, Port = Port, User = "root", Password = Password }; }
    }

    /// <summary>Runs mysql.exe / mysqladmin.exe of the portable database with a temporary option file.</summary>
    static class MySql
    {
        static string WriteOptions(DbLogin login)
        {
            string f = Path.Combine(Path.GetTempPath(), "afk-realm-" + Guid.NewGuid().ToString("N") + ".cnf");
            File.WriteAllText(f, "[client]\r\nuser=" + login.User + "\r\npassword=\"" + login.Password + "\"\r\nhost=" + login.Host +
                "\r\nport=" + login.Port + "\r\nprotocol=tcp\r\ndefault-character-set=utf8mb4\r\n", new UTF8Encoding(false));
            return f;
        }
        /// <summary>Executes SQL, returns tab-separated result lines. Throws with the MySQL message on failure.</summary>
        public static List<string> Query(Install inst, DbLogin login, string sql)
        {
            string opt = WriteOptions(login);
            try
            {
                var psi = new ProcessStartInfo(Path.Combine(inst.MySqlBin, "mysql.exe"),
                    "--defaults-extra-file=\"" + opt + "\" --batch --skip-column-names")
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
                };
                using (var p = Process.Start(psi))
                {
                    var input = new StreamWriter(p.StandardInput.BaseStream, new UTF8Encoding(false));
                    input.Write(sql); input.Close();
                    var errTask = p.StandardError.ReadToEndAsync();
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    string err = errTask.Result;
                    if (p.ExitCode != 0)
                        throw new InvalidOperationException(string.Join(" ", err.Split('\n').Where(l => !l.Contains("Using a password")).Select(l => l.Trim())).Trim());
                    return output.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
                }
            }
            finally { try { File.Delete(opt); } catch { } }
        }
        public static bool Shutdown(Install inst, DbLogin root)
        {
            string opt = WriteOptions(root);
            try
            {
                var psi = new ProcessStartInfo(Path.Combine(inst.MySqlBin, "mysqladmin.exe"), "--defaults-extra-file=\"" + opt + "\" shutdown")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
                using (var p = Process.Start(psi)) { p.WaitForExit(60000); return p.HasExited && p.ExitCode == 0; }
            }
            finally { try { File.Delete(opt); } catch { } }
        }
        public static string Quote(string s) { return "'" + s.Replace("\\", "\\\\").Replace("'", "''") + "'"; }
    }

    /// <summary>SRP6 verifier exactly as AzerothCore's authserver computes it (N, g = 7, SHA1, little-endian).</summary>
    static class Srp6
    {
        static readonly BigInteger N = ParseHexBigEndian("894B645E89E1535BBDAD5B8B290650530801B18EBFBF5E8FAB3C82872A3E9BB7");
        static readonly BigInteger G = new BigInteger(7);

        static BigInteger ParseHexBigEndian(string hex)
        {
            var be = Enumerable.Range(0, hex.Length / 2).Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16)).ToArray();
            return FromLittleEndian(be.Reverse().ToArray());
        }
        static BigInteger FromLittleEndian(byte[] le)
        {
            var b = new byte[le.Length + 1]; Array.Copy(le, b, le.Length); // trailing 0 keeps it positive
            return new BigInteger(b);
        }
        static byte[] ToLittleEndian32(BigInteger v)
        {
            var raw = v.ToByteArray(); var r = new byte[32];
            Array.Copy(raw, r, Math.Min(32, raw.Length));
            return r;
        }
        public static byte[] Verifier(string user, string pass, byte[] salt)
        {
            using (var sha = SHA1.Create())
            {
                byte[] inner = sha.ComputeHash(Encoding.UTF8.GetBytes(user.ToUpperInvariant() + ":" + pass.ToUpperInvariant()));
                byte[] x = sha.ComputeHash(salt.Concat(inner).ToArray());
                return ToLittleEndian32(BigInteger.ModPow(G, FromLittleEndian(x), N));
            }
        }
        public static byte[] NewSalt()
        {
            var s = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(s);
            return s;
        }
        public static string Hex(byte[] b) { return BitConverter.ToString(b).Replace("-", ""); }
    }

    static class Net
    {
        public static bool PortOpen(int port)
        {
            try
            {
                using (var c = new TcpClient())
                {
                    var ar = c.BeginConnect("127.0.0.1", port, null, null);
                    bool ok = ar.AsyncWaitHandle.WaitOne(400) && c.Connected;
                    if (ok) c.EndConnect(ar);
                    return ok;
                }
            }
            catch { return false; }
        }
    }

    /// <summary>Starts, stops and reports the three server processes of one installation.</summary>
    class ServerControl
    {
        readonly Install inst;
        public ServerControl(Install i) { inst = i; }

        public int DbPort { get { return Conf.GetInt(inst.MyIni, "port", 3307); } }
        public int AuthPort { get { return Conf.GetInt(inst.AuthConf, "RealmServerPort", 3724); } }
        public int WorldPort { get { return Conf.GetInt(inst.WorldConf, "WorldServerPort", 8085); } }

        public Process Find(string exeName, string folder)
        {
            foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exeName)))
            {
                try
                {
                    string path = p.MainModule.FileName;
                    if (path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)) return p;
                }
                catch { }
            }
            return null;
        }
        public Process Db { get { return Find("mysqld.exe", Path.Combine(inst.DbDir, "mysql")); } }
        public Process Auth { get { return Find("authserver.exe", inst.ServerDir); } }
        public Process World { get { return Find("worldserver.exe", inst.ServerDir); } }

        public void StartDatabase(Action<string> status)
        {
            if (Db != null) return;
            if (Net.PortOpen(DbPort))
                throw new InvalidOperationException("Port " + DbPort + " is already in use. Is another database (for example another repack) still running? Stop it first.");
            status("Starting the database ...");
            var psi = new ProcessStartInfo(Path.Combine(inst.MySqlBin, "mysqld.exe"), "--defaults-file=\"" + inst.MyIni + "\"")
            { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = inst.DbDir };
            Process.Start(psi);
            WaitPort(DbPort, 90, "The database did not start. Details are in logs\\mysql-error.log.");
        }
        public void StartAuth(Action<string> status)
        {
            if (Auth != null) return;
            if (World == null) ResetRealmFlags();
            status("Starting the authserver ...");
            var p = StartConsole(inst.AuthExe, inst.AuthConf);
            WaitReady(p, AuthPort, 120, "authserver", Path.Combine(inst.ServerDir, "Auth.log"));
        }
        /// <summary>
        /// Starts the worldserver and returns once it is loading. Loading takes minutes; the caller
        /// watches it with <see cref="WatchWorld"/> instead of blocking the window meanwhile.
        /// </summary>
        public Process StartWorld(Action<string> status)
        {
            var running = World;
            if (running != null) return running;
            status("Starting the worldserver ...");
            var p = StartConsole(inst.WorldExe, inst.WorldConf);
            // Configuration and database problems end the process within seconds.
            for (int i = 0; i < 30; i++)
            {
                if (Net.PortOpen(WorldPort)) return p;
                if (p != null && p.WaitForExit(500)) throw WorldStopped();
            }
            return p;
        }
        Exception WorldStopped()
        {
            return new InvalidOperationException("The worldserver stopped while starting. The end of its log:\n\n" + LogTail(Path.Combine(inst.ServerDir, "Server.log"), 8));
        }
        /// <summary>Waits in the background until the worldserver accepts players (true) or closes first (false, with the reason).</summary>
        public void WatchWorld(Process p, Action<bool, string> finished)
        {
            new Thread(() =>
            {
                try
                {
                    while (true)
                    {
                        if (Net.PortOpen(WorldPort)) { finished(true, null); return; }
                        if (p == null || p.WaitForExit(2000)) { finished(false, WorldStopped().Message); return; }
                    }
                }
                catch (Exception ex) { finished(false, ex.Message); }
            }) { IsBackground = true, Name = "world-watch" }.Start();
        }

        /// <summary>
        /// A worldserver marks its realm "not ready" while it loads and clears the mark once it runs.
        /// After a crash during loading the mark stays, and the authserver then refuses to start
        /// ("No valid realms"). With no worldserver running, the mark is always stale.
        /// </summary>
        void ResetRealmFlags()
        {
            try { MySql.Query(inst, DbLogin.FromConfig(inst), "UPDATE acore_auth.realmlist SET flag = flag & ~1;"); } catch { }
        }

        Process StartConsole(string exe, string conf)
        {
            // Own console window, so the log is visible and GM commands can be typed.
            // Started directly (no cmd.exe), so Ctrl+C reaches the server itself.
            var psi = new ProcessStartInfo(exe, "--config \"" + conf + "\"")
            { UseShellExecute = true, WorkingDirectory = inst.ServerDir };
            return Process.Start(psi);
        }

        /// <summary>Waits until the server listens on its port; fails right away if it closes, showing the end of its log.</summary>
        void WaitReady(Process p, int port, int seconds, string name, string log)
        {
            for (int i = 0; i < seconds * 2; i++)
            {
                if (Net.PortOpen(port)) return;
                if (p != null && p.WaitForExit(500))
                    throw new InvalidOperationException("The " + name + " stopped while starting. The end of its log:\n\n" + LogTail(log, 8));
            }
            if (p == null || !p.HasExited) return; // still loading; its window shows the progress
            throw new InvalidOperationException("The " + name + " did not start. See " + log);
        }
        void WaitPort(int port, int seconds, string error)
        {
            for (int i = 0; i < seconds * 2; i++) { if (Net.PortOpen(port)) return; Thread.Sleep(500); }
            throw new InvalidOperationException(error);
        }
        static string LogTail(string file, int lines)
        {
            try
            {
                string[] all;
                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var r = new StreamReader(fs, Encoding.UTF8)) all = r.ReadToEnd().Split('\n');
                var useful = all.Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0 && !l.StartsWith("Closing down DatabasePool")
                    && !l.StartsWith("Asynchronous connections on DatabasePool") && !l.StartsWith("All connections on DatabasePool")).ToList();
                return string.Join("\n", useful.Skip(Math.Max(0, useful.Count - lines)));
            }
            catch { return "(" + file + " could not be read)"; }
        }

        [DllImport("kernel32.dll", SetLastError = true)] static extern bool AttachConsole(uint pid);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool FreeConsole();
        [DllImport("kernel32.dll")] static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add);
        [DllImport("kernel32.dll")] static extern bool GenerateConsoleCtrlEvent(uint ctrlEvent, uint group);

        /// <summary>Sends Ctrl+C to a console process: AzerothCore then saves all players and shuts down cleanly.</summary>
        static bool SendCtrlC(Process p)
        {
            lock (typeof(ServerControl))
            {
                FreeConsole();
                if (!AttachConsole((uint)p.Id)) return false;
                SetConsoleCtrlHandler(IntPtr.Zero, true);
                bool ok = GenerateConsoleCtrlEvent(0, 0);
                Thread.Sleep(300);
                FreeConsole();
                SetConsoleCtrlHandler(IntPtr.Zero, false);
                return ok;
            }
        }
        static bool StopConsoleProcess(Process p, int seconds)
        {
            if (p == null) return true;
            if (!SendCtrlC(p)) return false;
            return p.WaitForExit(seconds * 1000);
        }
        public void StopAll(Action<string> status, bool includeDatabase)
        {
            var w = World;
            if (w != null)
            {
                status("Worldserver is saving and shutting down ...");
                if (!StopConsoleProcess(w, 180))
                    throw new InvalidOperationException("The worldserver did not respond. Type \"server shutdown 1\" in its window, then try again.");
            }
            var a = Auth;
            if (a != null)
            {
                status("Stopping the authserver ...");
                if (!StopConsoleProcess(a, 20)) { try { a.Kill(); } catch { } }
            }
            if (includeDatabase && Db != null)
            {
                status("Stopping the database safely ...");
                if (!MySql.Shutdown(inst, DbLogin.FromConfig(inst).AsRoot()))
                    throw new InvalidOperationException("The database could not be stopped cleanly.");
                for (int i = 0; i < 120 && Db != null; i++) Thread.Sleep(500);
            }
        }
    }

    /// <summary>Accounts and realm address, written directly into acore_auth.</summary>
    static class Accounts
    {
        public static string Validate(string user, string pass, string pass2)
        {
            if (!Regex.IsMatch(user ?? "", "^[A-Za-z0-9]{3,16}$")) return "The account name must be 3 to 16 characters long and may only contain letters and digits.";
            if ((pass ?? "").Length < 4 || pass.Length > 16) return "The password must be 4 to 16 characters long.";
            if (pass.Any(ch => ch > 127)) return "Please use only plain ASCII characters in the password.";
            if (pass != pass2) return "The two passwords do not match.";
            return null;
        }
        public static void Create(Install inst, string user, string pass, int gmLevel)
        {
            var login = DbLogin.FromConfig(inst);
            string name = user.ToUpperInvariant();
            if (MySql.Query(inst, login, "SELECT COUNT(*) FROM acore_auth.account WHERE username=" + MySql.Quote(name) + ";")[0] != "0")
                throw new InvalidOperationException("The account \"" + name + "\" already exists.");
            byte[] salt = Srp6.NewSalt();
            byte[] verifier = Srp6.Verifier(name, pass, salt);
            string sql = "INSERT INTO acore_auth.account (username, salt, verifier, email, reg_mail, expansion) VALUES (" +
                MySql.Quote(name) + ", X'" + Srp6.Hex(salt) + "', X'" + Srp6.Hex(verifier) + "', '', '', 2);\n";
            if (gmLevel > 0)
                sql += "INSERT INTO acore_auth.account_access (id, gmlevel, RealmID, comment) VALUES (LAST_INSERT_ID(), " + gmLevel + ", -1, '" + Product.Name + "');\n";
            MySql.Query(inst, login, sql);
        }
        public static string GetRealmAddress(Install inst)
        {
            var r = MySql.Query(inst, DbLogin.FromConfig(inst), "SELECT address FROM acore_auth.realmlist ORDER BY id LIMIT 1;");
            return r.Count > 0 ? r[0] : "127.0.0.1";
        }
        /// <summary>Other players get <paramref name="address"/>; this PC keeps connecting through 127.0.0.1.</summary>
        public static void SetRealmAddress(Install inst, string address)
        {
            MySql.Query(inst, DbLogin.FromConfig(inst),
                "UPDATE acore_auth.realmlist SET address=" + MySql.Quote(address) + ", localAddress='127.0.0.1', localSubnetMask='255.255.255.0';");
            bool remote = !(address == "127.0.0.1" || address.Equals("localhost", StringComparison.OrdinalIgnoreCase));
            if (File.Exists(inst.CoaConf)) Conf.Set(inst.CoaConf, "CoA.AllowRemoteClients", remote ? "1" : "0");
            foreach (var f in new[] { inst.AuthConf, inst.WorldConf })
                if (Conf.Get(f, "BindIP") != null) Conf.Set(f, "BindIP", "\"0.0.0.0\"");
            if (remote) AddFirewallRules(inst);
        }
        public static void AddFirewallRules(Install inst)
        {
            foreach (var exe in new[] { inst.AuthExe, inst.WorldExe })
            {
                string name = Product.ShortName + " - " + Path.GetFileNameWithoutExtension(exe);
                Run("netsh", "advfirewall firewall delete rule name=\"" + name + "\"");
                Run("netsh", "advfirewall firewall add rule name=\"" + name + "\" dir=in action=allow program=\"" + exe + "\" enable=yes profile=any");
            }
        }
        static void Run(string exe, string args)
        {
            var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var p = Process.Start(psi)) { p.StandardOutput.ReadToEnd(); p.WaitForExit(30000); }
        }
    }

    static class Settings
    {
        static string File_ { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Product.FileStem, "last-install.txt"); } }
        public static string LastInstall
        {
            get { try { return File.Exists(File_) ? File.ReadAllText(File_).Trim() : null; } catch { return null; } }
            set { try { Directory.CreateDirectory(Path.GetDirectoryName(File_)); File.WriteAllText(File_, value ?? ""); } catch { } }
        }
    }
}
