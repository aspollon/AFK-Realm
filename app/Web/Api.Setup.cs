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

            // The game client question of the wizard: a folder that is there (checked against the server kind), or a
            // folder to download the CoA client into.
            Post(app, "/api/setup/client", a =>
            {
                string kind = Install.KindOf(a.Str("kind"));
                string folder = (a.Str("folder") ?? "").Trim();
                if (a.Str("mode") == "download")
                {
                    if (kind != Install.KindCoa) throw new UserError(L.T("AFK Realm can only download the CoA game client."));
                    if (folder.Length == 0 || !Path.IsPathRooted(folder)) throw new UserError(L.T("Choose the folder for the game client. It can be empty; about 43 GB will be downloaded into it."));
                    string target = ClientAddons.GameRoot(folder) ?? folder;
                    if (ClientAddons.IsClientFolder(target))
                    {
                        string mismatch = ClientAddons.ClientMismatch(kind, target);
                        if (mismatch != null) throw new UserError(mismatch);
                    }
                    return new { folder = target };
                }
                string root = ClientAddons.GameRoot(folder);
                if (root == null) throw new UserError(L.T("This is not a game folder: it needs a \"Data\" folder with the game's .MPQ archives.") + " " + L.T("You can also choose the \"Data\" folder itself."));
                string error = ClientAddons.ClientMismatch(kind, root);
                if (error != null) throw new UserError(error);
                return new { folder = root };
            });

            // What can be built in with the first compile: the modules by AFK Realm and the built-in preset (CoA only).
            Post(app, "/api/setup/extras", a =>
            {
                if (Install.KindOf(a.Str("kind")) != Install.KindCoa) return new { modules = new object[0], presets = new object[0] };
                var preset = ServerPresets.BuiltInAfkRealm();
                return new
                {
                    modules = ModuleCatalog.OwnModules().Where(m => !m.Hidden).Select(m => (object)new { name = m.Name, url = m.CloneUrl, text = L.Outside(m.Description) }).ToArray(),
                    presets = new object[] { new { id = preset.Id, name = preset.Name, text = preset.Description, urls = preset.Modules.Select(m => m.CloneUrl).ToArray() } }
                };
            });

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
                // "Try again" through the wizard: a started installation keeps the database password it has.
                if (password.Length == 0) { try { password = DbLogin.FromConfig(inst).Password ?? ""; } catch { } }
                else
                {
                    string pwError = CheckPassword(password, a.Str("password2") ?? password);
                    if (pwError != null) throw new UserError(pwError);
                }
                if (password.Length == 0) throw new UserError(L.T("Enter the database password of the first installation."));
                string mode = a.Str("mode") ?? "Install";
                if (mode != "Install") mode = "Install";
                bool shortcut = a.Bool("shortcut", true);
                // The kind of server is chosen once, for a new server; an installed one keeps its own.
                if (!installed)
                {
                    inst.SetKind(Install.KindOf(a.Str("kind")));
                }
                // The game client and the modules chosen in the wizard: modules go into the first build, the client is
                // downloaded first when asked, and map data and the preset's settings follow when the server is opened.
                string clientMode = a.Str("clientMode") ?? "";
                string clientFolder = (a.Str("clientFolder") ?? "").Trim();
                if (clientMode != "have" && clientMode != "download") clientFolder = "";
                bool coaCustom = inst.IsCoa && a.Bool("coaCustom");
                var chosen = a.List("modules");
                // CoA Custom's races need mod-custom-races (and mod-subclass for the Twins and the Two-Headed Ogre)
                if (coaCustom) chosen.AddRange(ModuleCatalog.OwnModules().Where(m => m.Name == "mod-custom-races" || m.Name == "mod-subclass").Select(m => m.CloneUrl));
                var urls = FirstModules(inst, chosen, a.Str("preset"));
                string engineArgs = urls.Count > 0 ? "-AddModules " + MySql.Arg(string.Join(";", urls)) : null;
                // Voices and the HD pack go into the game client after CoA Custom and the map data.
                bool voices = inst.IsCoa && a.Bool("voices"), hd = inst.IsCoa && a.Bool("hd");
                SaveFirstSteps(inst, clientFolder, installed ? null : a.Str("preset"), clientMode == "download", coaCustom, voices, hd);
                LastArgs = engineArgs;
                return Jobs.RunEngine(inst, mode, password, 3307, engineArgs, name, false, job =>
                {
                    CopySelf(inst, shortcut);
                    Settings.LastInstall = inst.Root;
                    Settings.Remember(inst.Root, name);
                    if (clientMode == "download" && clientFolder.Length > 0) DownloadClientFirst(job, inst, clientFolder);
                    if (coaCustom) DownloadCoaCustomFirst(job);
                }).Snapshot();
            });

            // After an installation (or its retry): the server it was for is opened.
            Post(app, "/api/setup/finished", a =>
            {
                string root = a.Str("root");
                object next = null;
                if (!string.IsNullOrEmpty(root) && new Install(root).IsInstalled)
                {
                    var inst = new Install(root);
                    Manager.Open(inst);
                    next = TakeFirstSteps(inst);
                }
                return new { opened = Manager.Inst != null, next };
            });

            // "Try again" after a failed first install goes through the wizard again: what it needs to fill it in.
            Post(app, "/api/setup/recheck", a =>
            {
                var last = Jobs.Current;
                if (last == null || string.IsNullOrEmpty(last.ServerRoot)) throw new UserError(L.T("There is nothing to try again."));
                var inst = new Install(last.ServerRoot);
                bool hasPassword = false;
                try { hasPassword = !string.IsNullOrEmpty(DbLogin.FromConfig(inst).Password); } catch { }
                var steps = ReadFirstSteps(inst);
                string client, preset, coa, voices, hd;
                steps.TryGetValue("client", out client); steps.TryGetValue("preset", out preset); steps.TryGetValue("coacustom", out coa);
                steps.TryGetValue("voices", out voices); steps.TryGetValue("hd", out hd);
                return new
                {
                    kind = inst.Kind, root = inst.Root, name = last.Runner != null ? last.Runner.RealmName : null, hasPassword,
                    client = client ?? "", preset = preset ?? "", coaCustom = coa == "1", voices = voices == "1", hd = hd == "1"
                };
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
                var steps = last.Mode == "Install" ? ReadFirstSteps(inst) : new Dictionary<string, string>();
                string dlFolder, dl;
                steps.TryGetValue("client", out dlFolder); steps.TryGetValue("download", out dl);
                return Jobs.RunEngine(inst, last.Mode, password, Manager.Ctl != null && Manager.Inst != null && Manager.Inst.Root == inst.Root ? Manager.Ctl.DbPort : 3307,
                    a.Str("args") ?? LastArgs, last.Runner.RealmName, last.Mode != "Install" && last.Mode != "Backup", job =>
                    {
                        // a client download asked for (again) comes first; only missing or changed files are fetched
                        if (dl == "1" && !string.IsNullOrEmpty(dlFolder)) DownloadClientFirst(job, inst, dlFolder);
                    }).Snapshot();
            });
        }

        static string LastArgs;

        static string FirstStepsFile(Install inst) { return Path.Combine(inst.Root, "Dependencies", "afk-first-steps.txt"); }

        /// <summary>What the opened server still has to do after its first install: map data from this client, this preset's settings.</summary>
        static void SaveFirstSteps(Install inst, string client, string preset, bool download = false, bool? coaCustom = null, bool? voices = null, bool? hd = null)
        {
            try
            {
                var old = ReadFirstSteps(inst);
                Func<bool?, string, string> keep = (v, key) => { string c; old.TryGetValue(key, out c); return (v ?? c == "1") ? "1" : ""; };
                Directory.CreateDirectory(Path.GetDirectoryName(FirstStepsFile(inst)));
                File.WriteAllLines(FirstStepsFile(inst), new[] { "client=" + (client ?? ""), "preset=" + (preset ?? ""), "download=" + (download ? "1" : ""),
                    "coacustom=" + keep(coaCustom, "coacustom"), "voices=" + keep(voices, "voices"), "hd=" + keep(hd, "hd") });
            }
            catch { }
        }

        /// <summary>The CoA Custom package, downloaded before the build when it was chosen in the wizard; installed when the server opens.</summary>
        static void DownloadCoaCustomFirst(Job job)
        {
            try
            {
                string target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "CoA-Custom");
                string version = CoaCustom.DownloadAndExtract(target, (t, x, y) =>
                {
                    job.Action = L.T("Downloading CoA Custom ...") + " " + t;
                    if (y > 0) job.Sub = (double)x / y;
                    job.Changed(false);
                });
                Settings.CoaPackage = CoaCustom.PackageRoot(target) ?? target;
                job.Log(L.F("CoA Custom {0} downloaded.", version));
            }
            catch (Exception ex)
            {
                job.Log(L.T("CoA Custom could not be downloaded; the server is built anyway. Install it later under Modules & races.") + " (" + ex.Message + ")");
            }
        }

        static Dictionary<string, string> ReadFirstSteps(Install inst)
        {
            var values = new Dictionary<string, string>();
            try
            {
                foreach (string line in File.ReadAllLines(FirstStepsFile(inst)))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0) values[line.Substring(0, eq)] = line.Substring(eq + 1).Trim();
                }
            }
            catch { }
            return values;
        }

        /// <summary>The first steps left (once): the client folder for the map data when the server has none yet, and the preset.</summary>
        static object TakeFirstSteps(Install inst)
        {
            string file = FirstStepsFile(inst);
            if (!File.Exists(file)) return null;
            var values = ReadFirstSteps(inst);
            try { File.Delete(file); } catch { }
            string client, preset, coa, voices, hd;
            values.TryGetValue("client", out client);
            values.TryGetValue("preset", out preset);
            values.TryGetValue("coacustom", out coa);
            values.TryGetValue("voices", out voices);
            values.TryGetValue("hd", out hd);
            string game = string.IsNullOrEmpty(client) ? null : ClientAddons.GameRoot(client);
            string package = coa == "1" ? Settings.CoaPackage : null;
            if (package != null && !Directory.Exists(package)) package = null;
            if (game == null && string.IsNullOrEmpty(preset) && coa != "1" && voices != "1" && hd != "1") return null;
            return new
            {
                mapFolder = game != null && !inst.HasMapData ? game : null,
                preset = string.IsNullOrEmpty(preset) ? null : preset,
                // CoA Custom goes into the server and the game client: it needs both
                coaCustom = coa == "1", coaPackage = package, coaClient = game, server = inst.Root,
                voices = voices == "1", hd = hd == "1"
            };
        }

        /// <summary>The Git addresses for the first build: the chosen modules by AFK Realm, the preset's, and what they need.</summary>
        static List<string> FirstModules(Install inst, List<string> chosen, string presetId)
        {
            var list = new List<string>();
            if (!inst.IsCoa) return list;
            var own = ModuleCatalog.OwnModules();
            Action<string> add = url =>
            {
                if (string.IsNullOrEmpty(url) || list.Contains(url, StringComparer.OrdinalIgnoreCase)) return;
                if (!own.Any(m => m.CloneUrl.Equals(url, StringComparison.OrdinalIgnoreCase))) return;
                list.Add(url);
            };
            if (!string.IsNullOrEmpty(presetId))
            {
                try { foreach (var m in ServerPresets.Find(inst, presetId).Modules) add(m.CloneUrl); } catch { }
            }
            foreach (string url in chosen ?? new List<string>()) add(url);
            // what the chosen ones need ("requires" in their afk-realm.json), e.g. mod-custom-races brings mod-classic-classes
            for (int i = 0; i < list.Count; i++)
            {
                var entry = own.FirstOrDefault(m => m.CloneUrl.Equals(list[i], StringComparison.OrdinalIgnoreCase));
                if (entry == null) continue;
                try
                {
                    foreach (string need in ModuleCatalog.Analyze(entry).Requires)
                    {
                        var dep = own.FirstOrDefault(m => m.Name.Equals(need, StringComparison.OrdinalIgnoreCase) || ModuleCatalog.ShortName(m.FullName).Equals(need, StringComparison.OrdinalIgnoreCase));
                        if (dep != null) add(dep.CloneUrl);
                    }
                }
                catch { }
            }
            return list;
        }

        /// <summary>The CoA client download asked for in the wizard, before the build. A failed download does not stop the
        /// install: the server is built anyway, and map data is made later from the client.</summary>
        static void DownloadClientFirst(Job job, Install inst, string target)
        {
            try
            {
                job.Action = L.T("Reading the client list ..."); job.Changed();
                var manifest = ClientDownload.FetchManifest();
                Directory.CreateDirectory(target);
                ClientDownload.Update(target, manifest, (p, done, total, current) =>
                {
                    if (total < 1) total = 1;
                    job.Action = L.T("Downloading the game client") + ": " + ClientDownload.FormatBytes(Math.Max(0, done)) + " / " + ClientDownload.FormatBytes(total);
                    job.Sub = Math.Max(0, Math.Min(1, (double)done / total));
                    job.Changed(false);
                }, () => job.Stop);
                ClientAddons.ServerKind = inst.Kind;
                ClientAddons.ClientFolder = target;
                job.Log(L.F("The game client {0} is ready in {1}.", manifest.Version, target));
                string preset;
                ReadFirstSteps(inst).TryGetValue("preset", out preset);
                SaveFirstSteps(inst, target, preset);
            }
            catch (Exception ex)
            {
                job.Log(L.T("The game client could not be downloaded; the server is built anyway. Download it later under Client build.") + " (" + ex.Message + ")");
                string preset;
                ReadFirstSteps(inst).TryGetValue("preset", out preset);
                SaveFirstSteps(inst, "", preset);
            }
        }

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
        /// <summary>
        /// A newer AFK Realm started from somewhere else (a fresh download) replaces the copy in the server folder, which
        /// the desktop shortcut and the application menu start. Without this the menu kept starting the old copy, and a
        /// fix in the new download (its engine) never reached the server.
        /// </summary>
        public static void RefreshSelf(Install inst)
        {
            try
            {
                string self = Paths.ExePath;
                string target = Path.Combine(inst.Root, "Builder", Path.GetFileName(self));
                // an older menu entry without "Quit"
                if (!Os.Windows && File.Exists(target) && File.Exists(DesktopFile) && !File.ReadAllText(DesktopFile).Contains("--quit"))
                    try { WriteDesktopEntry(target, Path.GetDirectoryName(target)); } catch { }
                if (!File.Exists(target) || string.Equals(Path.GetFullPath(self), Path.GetFullPath(target), Os.Windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return;
                if (File.GetLastWriteTimeUtc(self) <= File.GetLastWriteTimeUtc(target)) return;
                CopySelf(inst, false);
            }
            catch { }
        }

        static string DesktopFile { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "applications", "afk-realm.desktop"); } }

        /// <summary>Linux menu entry; its right-click "Quit" ends AFK Realm, which has no tray icon there.</summary>
        static void WriteDesktopEntry(string target, string dir)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DesktopFile));
            string icon = Path.Combine(dir, "afk-realm.png");
            using (var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("web/icon.png"))
                if (s != null) using (var f = File.Create(icon)) s.CopyTo(f);
            File.WriteAllText(DesktopFile,
                "[Desktop Entry]\nType=Application\nName=" + Product.ShortName + "\nComment=" + L.T("Manage the CoA server") +
                "\nExec=\"" + target + "\"\nIcon=" + icon + "\nTerminal=false\nCategories=Game;\nActions=quit;\n\n" +
                "[Desktop Action quit]\nName=" + L.F("Quit {0}", Product.ShortName) + "\nExec=\"" + target + "\" --quit\n");
        }

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
                    // Into a new file first, then moved over: overwriting the file in place would cut short a copy that
                    // is running from it (Linux reads the program's parts from its file while it runs).
                    string temp = target + ".new";
                    File.Copy(self, temp, true);
                    if (!Os.Windows) File.SetUnixFileMode(temp, File.GetUnixFileMode(self));
                    File.Move(temp, target, true);
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
                    WriteDesktopEntry(target, dir);
                }
            }
            catch { }
        }
    }
}
