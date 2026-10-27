using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace CoAInstaller.Web
{
    /// <summary>
    /// The command line, for servers without a screen and for scripts:
    ///   AFK-Realm cli install --path /srv/coa --name "My Realm" --password ...    (or AC_DB_PASSWORD)
    ///   AFK-Realm cli status|start|stop|restart|update|repair|backup [--server folder]
    /// </summary>
    static class Cli
    {
        public static int Run(string[] args)
        {
            if (args.Length == 0 || args[0] == "help" || args[0] == "--help")
            {
                Console.WriteLine(Product.Name + " " + Product.Version + " - command line");
                Console.WriteLine();
                Console.WriteLine("  cli install --path <folder> --name <server name> [--password <db password>]");
                Console.WriteLine("  cli status   [--server <folder>]   database, authserver and worldserver");
                Console.WriteLine("  cli start    [--server <folder>]");
                Console.WriteLine("  cli stop     [--server <folder>]   saves every player and shuts down cleanly");
                Console.WriteLine("  cli restart  [--server <folder>]");
                Console.WriteLine("  cli update   [--server <folder>]   newest CoA core, Playerbots and modules (backed up first)");
                Console.WriteLine("  cli repair   [--server <folder>]   database and configuration again, without compiling");
                Console.WriteLine("  cli backup   [--server <folder>]");
                Console.WriteLine("  cli account  --name <name> --password <pw> [--level 0-3] [--server <folder>]");
                Console.WriteLine("  cli command  <GM command ...>      to the running worldserver, e.g.: cli command server info");
                Console.WriteLine();
                Console.WriteLine("Without --server the server last opened is used.");
                return 0;
            }
            string verb = args[0];
            Func<string, string> opt = name =>
            {
                int i = Array.IndexOf(args, "--" + name);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            };
            try
            {
                if (verb == "install")
                {
                    string path = opt("path"), name = opt("name") ?? "AzerothCore", pw = opt("password") ?? Environment.GetEnvironmentVariable("AC_DB_PASSWORD");
                    if (path == null) { Console.Error.WriteLine("--path is missing."); return 2; }
                    if (pw == null) { pw = ReadPassword(); }
                    string err = Api.CheckPassword(pw, pw) ?? Accounts.CheckRealmName(name);
                    if (err != null) { Console.Error.WriteLine(err); return 2; }
                    var inst = new Install(path);
                    Settings.LastInstall = inst.Root; Settings.Remember(inst.Root, name);
                    return Engine(inst, "Install", pw, name);
                }
                string root = opt("server") ?? Settings.LastInstall;
                if (string.IsNullOrEmpty(root) || !new Install(root).IsInstalled) { Console.Error.WriteLine(L.T("No finished server was found in this folder.") + " (" + root + ")"); return 2; }
                var server = new Install(root);
                var ctl = new ServerControl(server);
                Action<string> say = t => Console.WriteLine(t);
                switch (verb)
                {
                    case "status":
                        Console.WriteLine(server.Root);
                        Console.WriteLine("  " + L.T("Database") + ": " + (ctl.Db != null ? L.T("running") : L.T("stopped")));
                        Console.WriteLine("  " + L.T("Login (authserver)") + ": " + (ctl.Auth != null ? L.T("running") : L.T("stopped")));
                        Console.WriteLine("  " + L.T("Game world (worldserver)") + ": " + (ctl.World == null ? L.T("stopped") : Net.PortOpen(ctl.WorldPort) ? L.T("running") : L.T("loading …")));
                        return 0;
                    case "start":
                        ctl.RestoreAfterBotReset(); ctl.StartDatabase(say); ctl.StartAuth(say);
                        if (ctl.World == null) { try { AdminLink.Prepare(server); } catch { } }
                        var world = ctl.StartWorld(say);
                        say(L.F("The worldserver is loading. You can keep using {0} meanwhile.", Product.Name));
                        var done = new ManualResetEvent(false); bool ok = false; string why = null;
                        ctl.WatchWorld(world, (ready, problem) => { ok = ready; why = problem; done.Set(); });
                        done.WaitOne();
                        say(ok ? L.T("The server is running. You can log in now.") : why);
                        return ok ? 0 : 1;
                    case "stop":
                        ctl.StopAll(say, true); say(L.T("Server is stopped.")); return 0;
                    case "restart":
                        ctl.StopAll(say, false); return Run(new[] { "start", "--server", server.Root });
                    case "update": ctl.StopAll(say, true); return Engine(server, "Update", DbLogin.FromConfig(server).Password, null);
                    case "repair": ctl.StopAll(say, true); return Engine(server, "Setup", DbLogin.FromConfig(server).Password, null);
                    case "backup": return Engine(server, "Backup", DbLogin.FromConfig(server).Password, null);
                    case "account":
                        {
                            string name = opt("name"), pw = opt("password"); int level; int.TryParse(opt("level") ?? "0", out level);
                            string err = Accounts.Validate(name, pw, pw);
                            if (err != null) { Console.Error.WriteLine(err); return 2; }
                            if (ctl.Db == null) ctl.StartDatabase(say);
                            Accounts.Create(server, name, pw, Math.Max(0, Math.Min(3, level)));
                            say(L.F("Account \"{0}\" was created.", name.ToUpperInvariant()));
                            return 0;
                        }
                    case "command":
                        {
                            string problem = AdminLink.Problem(server, ctl);
                            if (problem != null) { Console.Error.WriteLine(problem); return 1; }
                            Console.WriteLine(AdminLink.Run(server, string.Join(" ", args.Skip(1).Where(x => x != "--server" && x != root))));
                            return 0;
                        }
                }
                Console.Error.WriteLine("Unknown command: " + verb);
                return 2;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
        }

        static string ReadPassword()
        {
            Console.Write(L.T("Database password") + ": ");
            var sb = new System.Text.StringBuilder();
            while (true)
            {
                var k = Console.ReadKey(true);
                if (k.Key == ConsoleKey.Enter) break;
                if (k.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; continue; }
                sb.Append(k.KeyChar);
            }
            Console.WriteLine();
            return sb.ToString();
        }

        static int Engine(Install inst, string mode, string password, string realm)
        {
            if (!Os.Windows) EngineRunner.Shell = Pwsh.Ensure(t => Console.WriteLine(t));
            var runner = new EngineRunner(mode) { RealmName = realm };
            var done = new ManualResetEvent(false); bool ok = false; string text = null;
            runner.PhaseStarted += i => Console.WriteLine("== " + runner.Phases[i].Title);
            runner.LogLine += l => Console.WriteLine(l);
            runner.Note += n => Console.WriteLine("* " + n);
            runner.Finished += (o, t) => { ok = o; text = t; done.Set(); };
            Console.CancelKeyPress += (s, e) => { e.Cancel = true; runner.Cancel(); };
            runner.Start(inst, mode, password, 3307);
            done.WaitOne();
            Console.WriteLine(ok ? (text == "uptodate" ? L.T("Everything is up to date") : "OK") : "ERROR: " + text);
            return ok ? 0 : 1;
        }
    }
}
