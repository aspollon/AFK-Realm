using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Builder;

namespace CoAInstaller.Web
{
    /// <summary>The first steps (welcome, folder, name, password, install) and the list of servers.</summary>
    static partial class Api
    {
        static void MapSetup(WebApplication app)
        {
            Get(app, "/api/servers", c => new
            {
                current = Manager.Inst == null ? null : Manager.Inst.Root,
                list = Settings.KnownServers.Select(k => new { root = k.Root, name = string.IsNullOrEmpty(k.Name) ? Path.GetFileName(k.Root.TrimEnd('\\', '/')) : k.Name, installed = new Install(k.Root).IsInstalled }).ToArray()
            });

            Post(app, "/api/servers/open", a =>
            {
                string root = a.Str("root") ?? "";
                var target = new Install(root);
                if (!target.IsInstalled) throw new UserError(L.F("No finished server was found in {0}.", root));
                if (Jobs.Busy) throw new UserError(L.T("A task is still running."));
                Settings.Remember(target.Root, null);
                Manager.Open(target);
                return new { root = target.Root };
            });

            Post(app, "/api/servers/forget", a => { Settings.Forget(a.Str("root") ?? ""); return Ok; });
            Post(app, "/api/servers/close", a => { Manager.Close(); return Ok; });

            Get(app, "/api/setup/suggest", c => new { path = SuggestPath() });

            Post(app, "/api/setup/check", a =>
            {
                var notes = new List<string>();
                bool installed;
                string error = CheckFolder(a.Str("path") ?? "", notes, out installed);
                return new { error, notes, installed };
            });

            Post(app, "/api/setup/name", a => new { error = Accounts.CheckRealmName((a.Str("name") ?? "").Trim()) });

            Post(app, "/api/setup/install", a =>
            {
                string path = a.Str("path") ?? "";
                var notes = new List<string>(); bool installed;
                string error = CheckFolder(path, notes, out installed);
                if (error != null) throw new UserError(error);
                var inst = new Install(path);
                string name = (a.Str("name") ?? "").Trim();
                string password = a.Str("password") ?? "";
                if (!installed)
                {
                    string nameError = Accounts.CheckRealmName(name);
                    if (nameError != null) throw new UserError(nameError);
                }
                else name = null;
                string pwError = CheckPassword(password, a.Str("password2") ?? password);
                if (pwError != null) throw new UserError(pwError);
                string mode = a.Str("mode") ?? "Install";
                if (mode != "Install") mode = "Install";
                bool shortcut = a.Bool("shortcut", true);
                return Jobs.RunEngine(inst, mode, password, 3307, null, name, false, job =>
                {
                    CopySelf(inst, shortcut);
                    Settings.LastInstall = inst.Root;
                    Settings.Remember(inst.Root, name);
                }).Snapshot();
            });

            // After an installation (or its retry): the server it was for is opened.
            Post(app, "/api/setup/finished", a =>
            {
                string root = a.Str("root");
                if (!string.IsNullOrEmpty(root) && new Install(root).IsInstalled) Manager.Open(new Install(root));
                return new { opened = Manager.Inst != null };
            });

            // Trying a failed task again: the same mode for the same server, with the password of the first try or of its configuration.
            Post(app, "/api/setup/retry", a =>
            {
                var last = Jobs.Current;
                if (last == null || last.Running || last.Runner == null) throw new UserError(L.T("There is nothing to try again."));
                var inst = new Install(last.ServerRoot);
                string password = a.Str("password");
                if (string.IsNullOrEmpty(password)) { try { password = DbLogin.FromConfig(inst).Password; } catch { } }
                if (string.IsNullOrEmpty(password)) throw new UserError(L.T("Enter the database password of the first installation."));
                return Jobs.RunEngine(inst, last.Mode, password, Manager.Ctl != null && Manager.Inst != null && Manager.Inst.Root == inst.Root ? Manager.Ctl.DbPort : 3307,
                    a.Str("args") ?? LastArgs, last.Runner.RealmName, last.Mode != "Install" && last.Mode != "Backup").Snapshot();
            });
        }

        static string LastArgs;

        public static string CheckPassword(string a, string b)
        {
            if (a.Length < 10) return L.T("The password needs at least 10 characters.");
            if (a.IndexOfAny(new[] { ';', '"', '\\', '\r', '\n' }) >= 0) return L.T("Please do not use a semicolon, quote or backslash.");
            if (a != b) return L.T("The two passwords do not match.");
            return null;
        }

        static string SuggestPath()
        {
            if (!Os.Windows) return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "CoA-Server");
            var fixedDrives = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).OrderByDescending(d => d.AvailableFreeSpace).ToList();
            string drive = fixedDrives.Count > 0 ? fixedDrives[0].RootDirectory.FullName : "C:\\";
            return Path.Combine(drive, "CoA-Server");
        }

        /// <summary>Linux: why a folder cannot hold a server because its drive ignores file permissions, or null.</summary>
        static string PermissionProblem(string folder)
        {
            string test = Path.Combine(folder, ".afk-mode-test");
            try
            {
                File.WriteAllText(test, "#!/bin/sh\nexit 0\n");
                File.SetUnixFileMode(test, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                var mode = File.GetUnixFileMode(test);
                bool exec = (mode & UnixFileMode.UserExecute) != 0;
                bool otherWrite = (mode & UnixFileMode.OtherWrite) != 0;
                if (!exec || otherWrite)
                    return L.F("{0} is on a drive without Linux file permissions (for example an NTFS or exFAT drive of Windows, or a network share). The database and the server cannot run from there. Choose a folder on a Linux drive, for example ~/CoA-Server.", folder);
                // a drive mounted with "noexec" keeps the permissions but runs no program
                try
                {
                    using (var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(test) { UseShellExecute = false }))
                        p.WaitForExit(5000);
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    return L.F("Programs cannot run from {0}: its drive is mounted with \"noexec\". Mount it with \"exec\" (for example: sudo mount -o remount,exec <mount point>, and the same in /etc/fstab), or choose another folder.", folder);
                }
                return null;
            }
            catch { return null; }
            finally { try { File.Delete(test); } catch { } }
        }

        /// <summary>An error that keeps the folder from being used, or null; notes (free space, warnings) go to the list.</summary>
        static string CheckFolder(string p, List<string> notes, out bool installed)
        {
            installed = false;
            p = (p ?? "").Trim();
            try
            {
                if (p.Length < (Os.Windows ? 4 : 2) || !Path.IsPathRooted(p))
                    return Os.Windows ? L.T("Please enter a full path, e.g. C:\\CoA-Server.") : L.T("Please enter a full path, e.g. /home/you/CoA-Server.");
                string full = Path.GetFullPath(p);
                if (full.Any(ch => ch > 127)) return L.T("The path contains non-English or special characters, which some build tools cannot handle.");
                // quotes, apostrophes and the like: the installer of the Visual Studio build tools refuses such a folder
                // (error 8004 for C:\Vol'jin), and scripts and the database stumble over them too
                if (full.IndexOfAny(new[] { '\'', '"', '`', '&', '%', '!', '^', '$', ';', '(', ')', '[', ']', '{', '}', '#', '@', '=', '+', ',' }) >= 0)
                    return L.T("Please use only letters, digits, - and _ in the folder names (no apostrophe or other signs): the build tools cannot handle them.");
                if (Os.Windows && full.Length > 120) return L.T("The path is too long (120 characters at most).");
                if (full.IndexOf("OneDrive", StringComparison.OrdinalIgnoreCase) >= 0) return L.T("Please do not use a OneDrive folder: syncing interferes with the database and the build.");
                if (Os.Windows)
                {
                    if (full.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), StringComparison.OrdinalIgnoreCase)
                        || full.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase))
                        return L.T("Please do not install under \"Program Files\" or \"Windows\".");
                }
                else
                {
                    foreach (var system in new[] { "/usr", "/bin", "/sbin", "/etc", "/lib", "/proc", "/sys", "/dev", "/boot", "/var/lib/mysql" })
                        if (full == system || full.StartsWith(system + "/")) return L.T("Please choose a folder of your own, for example in your home folder.");
                    if (full.Contains(' ')) return L.T("Please use a path without spaces: some build tools cannot handle them.");
                }
                if (full.Contains(" ")) notes.Add(L.T("Note: spaces in the path work, but a path without them is more robust."));
                string existing = full;
                while (!Directory.Exists(existing) && Path.GetDirectoryName(existing) != null) existing = Path.GetDirectoryName(existing);
                var drive = new DriveInfo(Os.Windows ? Path.GetPathRoot(full) : existing);
                double freeGb = drive.AvailableFreeSpace / 1024.0 / 1024 / 1024;
                notes.Add(L.F("Free space on {0}: {1:N0} GB (needed: about 40 GB).", Os.Windows ? drive.Name : existing, freeGb));
                double needed = 40; double.TryParse(Environment.GetEnvironmentVariable("AFK_MIN_FREE_GB") ?? "", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out needed); if (needed <= 0) needed = 40;
                if (freeGb < needed) return L.T("There is not enough free space on this drive.");
                if (Os.Windows && drive.DriveType != DriveType.Fixed) return L.T("Please use an internal drive.");
                var inst = new Install(full);
                installed = inst.IsInstalled;
                if (installed) notes.Add(L.T("A server is already installed in this folder. You can open it via \"Manage an existing server\"."));
                else if (Directory.Exists(full) && Directory.EnumerateFileSystemEntries(full).Any() && !Directory.Exists(Path.Combine(full, "Dependencies")))
                    notes.Add(L.T("The folder is not empty. Existing files are kept, but an empty folder is cleaner."));
                if (!Os.Windows)
                {
                    // Can this user write there?
                    string probe = existing;
                    try { string test = Path.Combine(probe, ".afk-write-test"); File.WriteAllText(test, ""); File.Delete(test); }
                    catch { return L.F("You cannot write to {0}. Choose a folder of your own.", probe); }
                    // A drive without Linux file permissions (NTFS or exFAT of Windows, a network share): every file is
                    // world-writable and nothing can be made executable - the database ignores its settings file there
                    // ("World-writable config file ... is ignored") and its start script cannot run.
                    string why = PermissionProblem(probe);
                    if (why != null) return why;
                }
                return null;
            }
            catch (Exception ex) { return L.T("Invalid path:") + " " + ex.Message; }
        }

        /// <summary>
        /// A copy of AFK Realm goes into the server folder (Builder) and serves as the management tool later on, with a
        /// shortcut on the desktop (Windows) or in the application menu (Linux).
        /// </summary>
        static void CopySelf(Install inst, bool shortcut)
        {
            string dir = Path.Combine(inst.Root, "Builder");
            Directory.CreateDirectory(dir);
            string self = Paths.ExePath;
            string target = Path.Combine(dir, Path.GetFileName(self));
            try
            {
                if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(target), Os.Windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                {
                    File.Copy(self, target, true);
                    if (!Os.Windows) File.SetUnixFileMode(target, File.GetUnixFileMode(self));
                }
            }
            catch { return; }
            if (!shortcut) return;
            try
            {
                if (Os.Windows)
                {
                    var shellType = Type.GetTypeFromProgID("WScript.Shell");
                    object shell = Activator.CreateInstance(shellType);
                    string lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Product.ShortName + ".lnk");
                    object sc = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                    var t = sc.GetType();
                    t.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { target });
                    t.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { dir });
                    t.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { L.T("Manage the CoA server") });
                    t.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, sc, null);
                }
                else
                {
                    string apps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "applications");
                    Directory.CreateDirectory(apps);
                    string icon = Path.Combine(dir, "afk-realm.png");
                    using (var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("web/icon.png"))
                        if (s != null) using (var f = File.Create(icon)) s.CopyTo(f);
                    File.WriteAllText(Path.Combine(apps, "afk-realm.desktop"),
                        "[Desktop Entry]\nType=Application\nName=" + Product.ShortName + "\nComment=" + L.T("Manage the CoA server") +
                        "\nExec=\"" + target + "\"\nIcon=" + icon + "\nTerminal=false\nCategories=Game;\n");
                }
            }
            catch { }
        }
    }
}
