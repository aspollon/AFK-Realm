using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;

namespace CoAInstaller.Web
{
    /// <summary>
    /// AFK Realm for Windows and Linux. The program is a small web server on this computer; its page opens in an app
    /// window (Edge or Chromium without the browser's bars) or, failing that, in the browser. Closing the window ends the
    /// program once nothing runs any more; the game servers keep running as before.
    ///
    ///   AFK-Realm                        opens the window
    ///   AFK-Realm --no-window            only the web server; prints the address
    ///   AFK-Realm --listen 0.0.0.0 --password &lt;pw&gt;   reachable from other computers (a server without a screen)
    ///   AFK-Realm --port 47800           a port of its own choice
    ///   AFK-Realm cli ...                the command line (see Cli)
    /// </summary>
    static class Program
    {
        public static Options Opts = new Options();

        public class Options
        {
            public int Port;
            public string Listen;                  // null: this computer only
            public string Password;                // for --listen
            public bool NoWindow;
            public string Server;                  // a server folder to open
            public bool Restarted;                 // started by a restart of its own (language change, update)
            public string WebRoot;                 // development: serve the page from this folder instead of the exe
        }

        [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);
        [DllImport("kernel32.dll")] static extern bool AllocConsole();

        /// <summary>
        /// On Windows AFK Realm is a window program (no empty console next to its window). Started from a command line
        /// to print something (cli, --help, --no-window, --listen), it writes into the console it was started from; the
        /// command line (cli) started without one gets a console of its own, since it asks and answers there.
        /// </summary>
        static void ConsoleIfNeeded(string[] args)
        {
            if (!Os.Windows) return;
            bool cli = args.Length > 0 && args[0] == "cli";
            bool prints = cli || args.Any(a => a == "--help" || a == "-h" || a == "--no-window" || a == "--headless" || a == "--listen");
            if (!prints) return;
            try { if (!AttachConsole(-1) && cli) AllocConsole(); } catch { }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);

        /// <summary>The file a failed start is written to (the settings folder: %APPDATA%\AFK-Realm).</summary>
        static string StartErrorFile { get { return Path.Combine(Paths.SettingsDir, "startup-error.txt"); } }

        /// <summary>
        /// A start that fails says so: without a console window (Windows) the program used to vanish after a few seconds
        /// without a word. The error goes to startup-error.txt and, on Windows, into a message box.
        /// </summary>
        static void Fatal(Exception ex)
        {
            string text = Product.Name + " " + Product.Version + " - " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine +
                Environment.OSVersion + ", " + System.Runtime.InteropServices.RuntimeInformation.OSArchitecture + Environment.NewLine +
                "Program: " + Environment.ProcessPath + Environment.NewLine + Environment.NewLine + ex;
            try { Directory.CreateDirectory(Paths.SettingsDir); File.WriteAllText(StartErrorFile, text); } catch { }
            try { Console.Error.WriteLine(text); } catch { }
            if (Os.Windows && !Opts.NoWindow)
                try
                {
                    MessageBoxW(IntPtr.Zero, Product.Name + " could not start:" + Environment.NewLine + Environment.NewLine + ex.Message + Environment.NewLine + Environment.NewLine +
                        "The details are in " + StartErrorFile + " - please send this file on Discord.", Product.Name, 0x10);
                }
                catch { }
        }

        static int Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) => { if (e.ExceptionObject is Exception ex) Fatal(ex); };
            try { return Start(args); }
            catch (Exception ex) { Fatal(ex); return 1; }
        }

        static int Start(string[] args)
        {
            ConsoleIfNeeded(args);
            // The release file this build updates itself from.
            UpdateCheck.ToolAsset = Os.Windows ? "AFK-Realm.exe"
                : System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "AFK-Realm-linux-arm64" : "AFK-Realm-linux-x64";
            if (args.Length > 0 && args[0] == "cli") { L.Init(); return Cli.Run(args.Skip(1).ToArray()); }
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                Func<string> next = () => i + 1 < args.Length ? args[++i] : "";
                if (a == "--port") { int p; if (int.TryParse(next(), out p)) Opts.Port = p; }
                else if (a == "--listen") Opts.Listen = next();
                else if (a == "--password") Opts.Password = next();
                else if (a == "--no-window" || a == "--headless") Opts.NoWindow = true;
                else if (a == "--server") Opts.Server = next();
                else if (a == "--restart" || a == "--updated" || a == "--after-update") { Opts.Restarted = true; if (a != "--restart") SelfUpdateWeb.JustUpdated = true; }
                else if (a == "--web-root") Opts.WebRoot = next();
                else if (a == "--help" || a == "-h") { PrintHelp(); return 0; }
            }
            if (Opts.WebRoot == null) Opts.WebRoot = Environment.GetEnvironmentVariable("AFK_WEB_ROOT");
            if (Opts.Password == null) Opts.Password = Environment.GetEnvironmentVariable("AFK_REALM_PASSWORD");
            if (Opts.Listen != null && string.IsNullOrEmpty(Opts.Password))
            {
                Console.Error.WriteLine("--listen needs --password (or AFK_REALM_PASSWORD): anyone who can reach this computer could control the server otherwise.");
                return 2;
            }

            L.Init();

            // Windows: installing Visual Studio, the firewall rules and the database need administrator rights.
            if (Os.Windows && !IsAdministrator())
            {
                try
                {
                    Process.Start(new ProcessStartInfo(Environment.ProcessPath, string.Join(" ", args.Select(MySql.Arg))) { UseShellExecute = true, Verb = "runas" });
                }
                catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
                {
                    // the question for administrator rights was answered with No
                    if (!Opts.NoWindow)
                        try { MessageBoxW(IntPtr.Zero, Product.Name + " needs administrator rights (to install the build tools, the database and the firewall rules). Start it again and answer Yes.", Product.Name, 0x30); } catch { }
                }
                return 0;
            }

            // One AFK Realm at a time: a second start shows the window of the first.
            var running = Instance.Running();
            if (running != null && !Opts.Restarted)
            {
                if (Opts.NoWindow || Opts.Listen != null) { Console.WriteLine(L.F("{0} is already running: {1}", Product.Name, running.Url)); return 0; }
                AppWindow.Open(running.LoginUrl, false);
                return 0;
            }
            if (running != null && Opts.Restarted)
            {
                for (int i = 0; i < 60 && Instance.Running() != null; i++) Thread.Sleep(250);
            }

            try { SelfUpdateWeb.CleanUp(); } catch { }
            return WebHost.Run();
        }

        public static bool IsAdministrator()
        {
            if (!Os.Windows) return false;
            try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
            catch { return false; }
        }

        static void PrintHelp()
        {
            Console.WriteLine(Product.Name + " " + Product.Version + " - " + Product.Tagline);
            Console.WriteLine();
            Console.WriteLine("  AFK-Realm                         open the window");
            Console.WriteLine("  AFK-Realm --no-window             run without a window and print the address");
            Console.WriteLine("  AFK-Realm --listen 0.0.0.0 --password <pw> [--port 47800]");
            Console.WriteLine("                                    reachable from other computers (log in with the password)");
            Console.WriteLine("  AFK-Realm --server <folder>       open this server");
            Console.WriteLine("  AFK-Realm cli help                the command line: install, start, stop, backup ...");
        }
    }
}
