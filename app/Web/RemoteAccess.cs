using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;

namespace CoAInstaller.Web
{
    /// <summary>
    /// Access from other computers (a laptop in the same network or the VPN): switched on in the page, with a password
    /// of its own. A second web server then answers on all addresses of this computer; the window of this computer keeps
    /// its own, local one. The setting is kept (remote-access.json) and comes back with the next start.
    /// </summary>
    static class RemoteAccess
    {
        public const int DefaultPort = 47800;
        class Saved { public bool Enabled { get; set; } public int Port { get; set; } public string Salt { get; set; } public string Hash { get; set; } }

        static string FilePath { get { return Path.Combine(Paths.SettingsDir, "remote-access.json"); } }
        static readonly object gate = new object();
        static WebApplication app;
        public static string Error;

        static Saved Load()
        {
            try { if (File.Exists(FilePath)) return JsonSerializer.Deserialize<Saved>(File.ReadAllText(FilePath)) ?? new Saved(); } catch { }
            return new Saved();
        }

        static void Save(Saved s)
        {
            Directory.CreateDirectory(Paths.SettingsDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(s));
            Os.Private(FilePath);
        }

        public static bool Enabled { get { return Load().Enabled; } }
        public static int Port { get { int p = Load().Port; return p > 0 ? p : DefaultPort; } }
        public static bool HasPassword { get { return !string.IsNullOrEmpty(Load().Hash); } }
        public static bool Running { get { return app != null; } }

        static string HashOf(string password, byte[] salt)
        {
            return Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, 200000, HashAlgorithmName.SHA256, 32));
        }

        public static bool Verify(string password)
        {
            var s = Load();
            if (string.IsNullOrEmpty(s.Hash) || string.IsNullOrEmpty(password)) return false;
            try
            {
                var expected = Convert.FromBase64String(s.Hash);
                var actual = Convert.FromBase64String(HashOf(password, Convert.FromBase64String(s.Salt)));
                return CryptographicOperations.FixedTimeEquals(expected, actual);
            }
            catch { return false; }
        }

        /// <summary>Switches access from elsewhere on or off. A password is needed the first time; an empty one keeps the old.</summary>
        public static void Set(bool enabled, string password, int port)
        {
            lock (gate)
            {
                var s = Load();
                if (port <= 0) port = s.Port > 0 ? s.Port : DefaultPort;
                if (port < 1024 || port > 65535) throw new UserError(L.T("Please choose a port between 1024 and 65535."));
                if (!string.IsNullOrEmpty(password))
                {
                    if (password.Length < 8) throw new UserError(L.T("The password needs at least 8 characters."));
                    var salt = RandomNumberGenerator.GetBytes(16);
                    s.Salt = Convert.ToBase64String(salt); s.Hash = HashOf(password, salt);
                }
                if (enabled && string.IsNullOrEmpty(s.Hash)) throw new UserError(L.T("Choose a password for the access from other computers."));
                bool portChanged = s.Port != port;
                s.Enabled = enabled; s.Port = port;
                Save(s);
                if (!enabled || portChanged) Stop();
                if (enabled && app == null) Start();
            }
        }

        /// <summary>At the start of the program: on when it was on last time (not with --listen, which is remote already).</summary>
        public static void StartIfEnabled()
        {
            if (Program.Opts.Listen != null) return;
            try { if (Enabled && HasPassword) lock (gate) Start(); } catch { }
        }

        static void Start()
        {
            Error = null;
            int port = Port;
            try
            {
                OpenFirewall(port);
                var a = WebHost.Build(k => k.Listen(IPAddress.Any, port), true);
                a.StartAsync().Wait();
                app = a;
            }
            catch (Exception ex)
            {
                while (ex.InnerException != null) ex = ex.InnerException;
                Error = ex is SocketException || ex is IOException ? L.F("Port {0} is used by another program. Choose another port.", port) : ex.Message;
                throw new UserError(Error);
            }
        }

        public static void Stop()
        {
            var a = app;
            app = null;
            if (a == null) return;
            try { a.StopAsync().Wait(3000); } catch { }
            try { ((IDisposable)a).Dispose(); } catch { }
        }

        /// <summary>Windows: lets other computers of the own networks (the home network, the VPN) reach the port.</summary>
        static void OpenFirewall(int port)
        {
            if (!Os.Windows) return;
            try
            {
                Run("netsh", "advfirewall firewall delete rule name=\"AFK Realm remote access\"");
                Run("netsh", "advfirewall firewall add rule name=\"AFK Realm remote access\" dir=in action=allow protocol=TCP localport=" + port + " remoteip=localsubnet profile=any");
            }
            catch { }
        }

        static void Run(string file, string args)
        {
            using (var p = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }))
            {
                p.StandardOutput.ReadToEnd(); p.WaitForExit(15000);
            }
        }

        /// <summary>The addresses other computers can use: one per network of this computer.</summary>
        public static List<object> Addresses()
        {
            var list = new List<object>();
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var u in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (u.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(u.Address)) continue;
                        string ip = u.Address.ToString();
                        if (ip.StartsWith("169.254.")) continue;
                        list.Add(new { address = "http://" + ip + ":" + Port + "/", network = nic.Name + (string.IsNullOrEmpty(nic.Description) || nic.Description == nic.Name ? "" : " (" + nic.Description + ")") });
                    }
                }
            }
            catch { }
            return list;
        }
    }
}
