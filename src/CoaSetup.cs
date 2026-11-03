using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace CoAInstaller
{
    /// <summary>
    /// CoA Custom setup.
    ///
    /// Copies the finished CoA Custom package files instead of rebuilding race packs. Client
    /// files and settings are copied, together with the CoA server DBCs/SQL that carry the
    /// custom racial abilities and race data. The AFK core keeps its own Classic Classes.
    /// </summary>
    static class CoaSetup
    {
        /// <summary>The exact error of the last setup run, for the web page and the next retry.</summary>
        public static string LastError { get; private set; }

        public sealed class Options
        {
            public string Package;
            public string Server;
            public string Client;
            public bool Check;
            public bool ClientPatchOnly;
            public bool ClientPatch = true;
            public bool NoBackup;
            public bool Help;
        }

        public static int Run(string[] args)
        {
            L.Init();
            LastError = null;
            Options o = Parse(args);
            if (o.Help)
            {
                PrintHelp();
                return 0;
            }
            try
            {
                return Run(o);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Console.Error.WriteLine("ERROR: " + ex.Message);
                return 1;
            }
        }

        static int Run(Options o)
        {
            if (string.IsNullOrEmpty(o.Package)) return Fail(L.T("--package is missing (the extracted CoA Custom 1.9.3 folder)."));
            if (string.IsNullOrEmpty(o.Server)) return Fail(L.T("--server is missing (the AFK Realm installation root)."));
            if (string.IsNullOrEmpty(o.Client)) return Fail(L.T("--client is missing (the game folder with Data and Ascension.exe)."));
            if (!Directory.Exists(o.Package)) return Fail(L.F("Package folder not found: {0}", o.Package));
            if (!Directory.Exists(o.Client) || !File.Exists(Path.Combine(o.Client, "Ascension.exe"))) return Fail(L.F("No game client in: {0}", o.Client));
            var inst = new Install(o.Server);
            if (!inst.IsInstalled) return Fail(L.F("No complete AFK Realm server in: {0}", o.Server));

            string packRoot = PackageRoot(o.Package);
            if (packRoot == null) return Fail(L.F("No CoA-Custom\\files tree found in: {0}", o.Package));
            string files = Path.Combine(packRoot, "files");
            string clientSource = Path.Combine(files, "client");
            string clientRoot = Path.Combine(files, "client_root");
            if (!Directory.Exists(clientSource) || !Directory.Exists(clientRoot))
                return Fail(L.F("The package has no files\\client or files\\client_root: {0}", packRoot));

            // Remember what the user chose; the setup page starts with these paths next time.
            Settings.CoaPackage = packRoot;
            Settings.CoaServer = inst.Root;
            ClientAddons.ClientFolder = o.Client;

            if (!o.Check && ClientAddons.ClientRunning(o.Client))
                return Fail(L.T("The game or its launcher is running from this folder. Close it first, then run the setup again."));

            string manifestVersion = ReadVersion(Path.Combine(packRoot, "manifest.json"));
            Console.WriteLine(L.F("CoA Custom package: {0}", packRoot + (manifestVersion.Length > 0 ? " (" + L.F("version {0}", manifestVersion) + ")" : "")));
            Console.WriteLine(L.F("Server: {0}", inst.Root));
            Console.WriteLine(L.F("Client: {0}", o.Client));
            Console.WriteLine(L.F("Mode: {0}", o.ClientPatchOnly ? L.T("client patch only") : L.T("copy package files")));
            Console.WriteLine();

            if (o.Check)
            {
                PrintCheck(o, inst, files, clientSource, clientRoot);
                return 0;
            }

            if (o.ClientPatchOnly)
            {
                Console.WriteLine(L.T("Installing AFK client patch only ..."));
                ClientAddons.ClientFolder = o.Client;
                var plan = ClientPatch.Collect(inst);
                string result = ClientPatch.Install(inst, o.Client, plan,
                    delegate(string text, int done, int total) { if (total > 0) Console.WriteLine("  " + text); });
                Console.WriteLine("  " + result);
                return 0;
            }

            int copied = CopyPackage(o, inst, files, clientSource, clientRoot);
            if (copied == 0) CoaCustom.RecordInstalled(inst, manifestVersion);
            return copied;
        }

        /// <summary>
        /// Packs the complete CoA Custom client as one ZIP that can be unpacked into the game
        /// folder on another computer, like the add-on ZIP of AFK Realm.
        /// </summary>
        public static string ExportClientZip(string file, string package, Install inst, string client, Action<string, int, int> progress)
        {
            if (string.IsNullOrEmpty(file)) throw new ArgumentException("file");
            string packRoot = PackageRoot(package);
            if (packRoot == null) throw new InvalidOperationException(L.T("No CoA-Custom\\files tree found in the package."));
            string files = Path.Combine(packRoot, "files");
            string clientSource = Path.Combine(files, "client");
            string clientRoot = Path.Combine(files, "client_root");
            if (!Directory.Exists(clientSource) || !Directory.Exists(clientRoot))
                throw new InvalidOperationException(L.T("The package has no files\\client or files\\client_root."));
            if (inst == null || !inst.IsInstalled) throw new InvalidOperationException(L.T("No complete AFK Realm server was chosen."));
            if (string.IsNullOrEmpty(client) || !Directory.Exists(client))
                throw new InvalidOperationException(L.T("No game client was chosen."));
            if (File.Exists(file)) File.Delete(file);
            if (progress == null) progress = (t, a, b) => { };
            var rootFiles = Directory.GetFiles(clientRoot, "*", SearchOption.AllDirectories);
            var addonFiles = Directory.Exists(Path.Combine(files, "addons")) ? Directory.GetFiles(Path.Combine(files, "addons"), "*", SearchOption.AllDirectories) : new string[0];
            var mpqs = Directory.GetFiles(clientSource, "*.MPQ", SearchOption.TopDirectoryOnly);
            // Add-ons from installed modules belong in the same ZIP as the CoA package.
            var moduleAddons = ClientAddons.Find(inst, client);
            var moduleAddonFiles = moduleAddons
                .SelectMany(a => Directory.GetFiles(a.Source, "*", SearchOption.AllDirectories)
                    .Select(path => new { Addon = a, Path = path }))
                .ToList();
            bool nativeSupported = NativeHd.SupportsPackage(clientRoot);
            int total = rootFiles.Length + addonFiles.Length + moduleAddonFiles.Count + mpqs.Length + (nativeSupported ? NativeHd.Count : 0) + Dxvk.Count;
            int done = 0;
            using (var zip = ZipFile.Open(file, ZipArchiveMode.Create))
            {
                foreach (string path in mpqs.OrderBy(x => x))
                {
                    progress(L.F("Packing CoA client files ({0}/{1}) ...", done + 1, total), done, total);
                    zip.CreateEntryFromFile(path, "Data/" + Path.GetFileName(path), CompressionLevel.NoCompression);
                    done++;
                }
                string rootPrefix = Path.GetFullPath(clientRoot).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                foreach (string path in rootFiles.OrderBy(x => x))
                {
                    // The package's CoA64.exe is replaced by the native HD build below,
                    // but only for a CoA package version this native build supports.
                    if (nativeSupported && string.Equals(Path.GetFileName(path), "CoA64.exe", StringComparison.OrdinalIgnoreCase)) continue;
                    progress(L.F("Packing CoA client files ({0}/{1}) ...", done + 1, total), done, total);
                    string rel = path.Substring(rootPrefix.Length).Replace('\\', '/');
                    if (string.Equals(Path.GetFileName(path), "CoA64.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        // Any other CoA64 build: HD without DXVK, patched by its code (Coa64HdPatch).
                        byte[] exe = File.ReadAllBytes(path);
                        Coa64HdPatch.Apply(exe);
                        ZipArchiveEntry exeEntry = zip.CreateEntry(rel, CompressionLevel.NoCompression);
                        using (Stream output = exeEntry.Open()) output.Write(exe, 0, exe.Length);
                    }
                    else zip.CreateEntryFromFile(path, rel, CompressionLevel.NoCompression);
                    done++;
                }
                if (nativeSupported)
                {
                    progress(L.T("Adding the native HD CoA64 ..."), done, total);
                    NativeHd.AddToZip(zip);
                    done += NativeHd.Count;
                }
                // The x64 DXVK files: the official CoA64 uses them when they load; HD no longer depends on them.
                progress(L.T("Adding the x64 DXVK files ..."), done, total);
                Dxvk.AddToZip(zip);
                done += Dxvk.Count;
                if (addonFiles.Length > 0)
                {
                    string addonPrefix = Path.GetFullPath(Path.Combine(files, "addons")).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                    foreach (string path in addonFiles.OrderBy(x => x))
                    {
                        progress(L.F("Packing CoA client files ({0}/{1}) ...", done + 1, total), done, total);
                        string rel = path.Substring(addonPrefix.Length).Replace('\\', '/');
                        zip.CreateEntryFromFile(path, "Interface/AddOns/" + rel, CompressionLevel.NoCompression);
                        done++;
                    }
                }
                foreach (var addonFile in moduleAddonFiles.OrderBy(x => x.Addon.Name).ThenBy(x => x.Path))
                {
                    progress(L.F("Packing module add-ons ({0}/{1}) ...", done + 1, total), done, total);
                    string prefix = Path.GetFullPath(addonFile.Addon.Source).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                    string rel = addonFile.Path.Substring(prefix.Length).Replace('\\', '/');
                    zip.CreateEntryFromFile(addonFile.Path, "Interface/AddOns/" + addonFile.Addon.Name + "/" + rel, CompressionLevel.Optimal);
                    done++;
                }
                // The AFK client patch is generated from the chosen server and the installed client.
                var plan = ClientPatch.Collect(inst);
                if (plan != null && !plan.Empty)
                {
                    progress(L.T("Adding the AFK client patch ..."), done, total);
                    ClientPatch.AddPatchToZip(zip, inst, client, plan, progress);
                }
                string realm = GameLaunch.AddToZip(zip, inst);
                var entry = zip.CreateEntry("READ ME FIRST.txt");
                using (var writer = new StreamWriter(entry.Open(), new System.Text.UTF8Encoding(false)))
                    writer.Write(ClientZipReadMe(nativeSupported) + (realm != null ? Environment.NewLine + realm + Environment.NewLine : ""));
            }
            return file;
        }

        static string ClientZipReadMe(bool nativeHd)
        {
            var text = new System.Text.StringBuilder();
            text.AppendLine(L.T("CoA Custom client files for AFK Realm"));
            text.AppendLine();
            text.AppendLine(L.T("1. Close the game and its launcher."));
            text.AppendLine(L.T("2. Unpack this ZIP into your game folder (the folder with the \"Data\" folder). Replace files that are already there."));
            text.AppendLine(L.T("3. Start CoA64.exe for the 64-bit client, or Ascension.exe for the 32-bit client."));
            text.AppendLine(L.T("4. Delete the folder \"Cache\" in your game folder if the game still shows old races or classes."));
            text.AppendLine();
            text.AppendLine(L.T("The ZIP contains the CoA Custom client files, its add-ons, the AFK Realm client patch and the add-ons of the installed modules."));
            if (nativeHd)
            {
                text.AppendLine(L.T("CoA64.exe in this ZIP is the native HD 1.9.6 build. It uses the normal Direct3D9 renderer and does not need the x64 DXVK folder; an existing x64 folder is ignored."));
                text.AppendLine(L.T("Keep the full HD archive set enabled: patch-Z.MPQ, patch-4.MPQ, patch-5.MPQ and patch-ZZZZS/T/W.MPQ. The native build has room for all of them."));
            }
            else
            {
                text.AppendLine(L.T("CoA64.exe in this ZIP is the package's own build, changed so that it loads HD without DXVK. The x64 folder (DXVK) is included; if the game does not start on your PC, rename the x64 folder and try again."));
                text.AppendLine(L.T("Keep Data\\patch-Z.MPQ, patch-4.MPQ and patch-5.MPQ disabled (their .coa-patcher-backup files are fine)."));
            }
            return text.ToString();
        }

        static int CopyPackage(Options o, Install inst, string files, string clientSource, string clientRoot)
        {
            CoaUninstall.Begin(o.Client);
            int copied = 0, skipped = 0;
            Console.WriteLine(L.T("Copying client MPQs ..."));
            CopyFiles(clientSource, Path.Combine(o.Client, "Data"), "*.MPQ", o.NoBackup, ref copied, ref skipped);
            EnsurePatchT(clientSource, o.Client);

            string dinput = Path.Combine(clientSource, "dinput8.dll");
            if (File.Exists(dinput))
            {
                CopyFile(dinput, Path.Combine(o.Client, "dinput8.dll"), o.NoBackup);
                copied++;
            }

            Console.WriteLine(L.T("Copying client_root (CoA64, Xvid, Esteria, Retail) ..."));
            bool nativeSupported = NativeHd.SupportsPackage(clientRoot);
            // Keep an already installed native CoA64.exe only when this native build
            // belongs to the package's official CoA64. A new CoA Custom package must
            // not run an old native exe against new race data.
            CopyTree(clientRoot, o.Client, o.NoBackup, ref copied, ref skipped, nativeSupported ? "CoA64.exe" : null);

            if (nativeSupported)
            {
                Console.WriteLine(L.T("Installing native HD CoA64 (no x64 DXVK needed) ..."));
                int nativeHd = NativeHd.Install(o.Client);
                if (nativeHd > 0) copied++;
            }
            else
            {
                // A CoA64 build the native HD exe was not made for: its own exe, changed so that HD no
                // longer depends on DXVK (Coa64HdPatch finds the two checks by their code).
                var patched = Coa64HdPatch.ApplyToFile(Path.Combine(o.Client, "CoA64.exe"));
                Console.WriteLine(patched == Coa64HdPatch.Result.NotFound
                    ? L.T("CoA64.exe: the HD check was not found in this build; HD needs the x64 DXVK folder to load.")
                    : L.T("CoA64.exe: HD no longer depends on DXVK."));
                if (patched == Coa64HdPatch.Result.Patched) copied++;
            }

            // The x64 DXVK files. The native build ignores them unless started with -dxvk; the official
            // CoA64 uses them when they load (and needs them for HD where the patch above did not fit).
            Console.WriteLine(L.T("Installing x64 DXVK ..."));
            copied += Dxvk.Install(o.Client);

            // CoA64 1.9.6 needs the full HD archive set. Older AFK versions disabled
            // patch-Z/4/5 because the official 127-handle limit overflowed with the new
            // patch-ZZZZS/T/W archives; the native build reserves 512 handles, so put
            // them back. For a package without the native build, keep the old quarantine.
            if (nativeSupported)
            {
                EnableHdWorldArchives(o.Client);
            }
            else
            {
                DisableHdWorldArchive(o.Client);
            }

            string addons = Path.Combine(files, "addons");
            if (Directory.Exists(addons))
            {
                Console.WriteLine(L.T("Copying addons ..."));
                CopyTree(addons, Path.Combine(o.Client, "Interface", "AddOns"), o.NoBackup, ref copied, ref skipped);
            }

            // The server-side race data lives in these DBCs (Spell, SkillLineAbility,
            // SkillRaceClassInfo, ChrRaces, ...), so they are part of the CoA install, not an
            // optional extra. SQL files are copied for the matching server data setup.
            Console.WriteLine(L.T("Copying server DBCs ..."));
            CopyFiles(Path.Combine(files, "dbc"), Path.Combine(inst.DataDir, "dbc"), "*.dbc", o.NoBackup, ref copied, ref skipped);
            string clientDbc = Path.Combine(files, "dbc_clientset", "CreatureDisplayInfo.dbc");
            if (File.Exists(clientDbc))
            {
                CopyFile(clientDbc, Path.Combine(inst.DataDir, "dbc_clientset", "CreatureDisplayInfo.dbc"), o.NoBackup);
                copied++;
            }
            string moduleConf = Path.Combine(files, "modules", "coa_custom_races.conf.dist");
            if (File.Exists(moduleConf))
            {
                CopyFile(moduleConf, Path.Combine(inst.ConfigDir, "modules", "coa_custom_races.conf.dist"), o.NoBackup);
                copied++;
            }
            string sqlSource = Path.Combine(files, "sql");
            if (Directory.Exists(sqlSource))
            {
                string sqlTarget = Path.Combine(inst.Root, "CoA-Custom", "sql");
                Console.WriteLine(L.F("Copying SQL to {0} ...", sqlTarget));
                CopyFiles(sqlSource, sqlTarget, "*.sql", o.NoBackup, ref copied, ref skipped);
                Console.WriteLine(L.T("SQL files copied. Automatic execution is disabled; the included module data is used."));
            }

            VerifyCopied(clientSource, Path.Combine(o.Client, "Data"), "*.MPQ");
            if (File.Exists(Path.Combine(clientSource, "dinput8.dll")))
                VerifyFile(Path.Combine(clientSource, "dinput8.dll"), Path.Combine(o.Client, "dinput8.dll"));
            // CoA64.exe is deliberately replaced by the native build for a supported package;
            // do not compare it with the package's official size afterwards.
            VerifyTree(clientRoot, o.Client, nativeSupported ? "CoA64.exe" : null);
            if (Directory.Exists(addons)) VerifyTree(addons, Path.Combine(o.Client, "Interface", "AddOns"));
            VerifyCopied(Path.Combine(files, "dbc"), Path.Combine(inst.DataDir, "dbc"), "*.dbc");
            if (File.Exists(clientDbc)) VerifyFile(clientDbc, Path.Combine(inst.DataDir, "dbc_clientset", "CreatureDisplayInfo.dbc"));
            if (File.Exists(moduleConf)) VerifyFile(moduleConf, Path.Combine(inst.ConfigDir, "modules", "coa_custom_races.conf.dist"));
            if (Directory.Exists(sqlSource)) VerifyCopied(sqlSource, Path.Combine(inst.Root, "CoA-Custom", "sql"), "*.sql");

            Console.WriteLine(L.T("Writing client settings ..."));
            var carried = ResetRaceConfig(inst);
            WriteRaceDefaults(inst, files);
            ApplyCarried(inst, carried);
            WriteClientSettings(inst, clientRoot, clientSource);
            // the racials, the racial rows and the paths of this computer, from the new settings (AFK Realm's races page)
            if (AfterRaceSettings != null) try { AfterRaceSettings(inst); } catch { }
            // every playable race of the package must be on after the install, whatever changed the settings on the way
            try
            {
                var restored = AddMissingServerRaces(inst);
                if (restored.Count > 0)
                    Console.WriteLine(L.F("Race settings: {0} race(s) of the package were missing and are switched on again: {1}", restored.Count,
                        string.Join(", ", restored.Select(r => r.Value + " (" + r.Key + ")"))));
            }
            catch (Exception ex) { Console.WriteLine(L.T("The race settings could not be checked:") + " " + ex.Message); }
            EnsureHardwareCursor(o.Client);

            // The module add-ons are part of the client build too. Installing them here means
            // the user no longer has to run Modules > Game client after the CoA setup.
            Console.WriteLine(L.T("Installing module add-ons ..."));
            var moduleAddons = ClientAddons.Find(inst, o.Client);
            foreach (var addon in moduleAddons)
                CoaUninstall.CaptureDirectory(Path.Combine(o.Client, "Interface", "AddOns", addon.Name));
            var leftovers = ClientAddons.Leftovers(inst, moduleAddons, o.Client);
            string addonResult = ClientAddons.Install(inst, o.Client, moduleAddons, leftovers, false);
            if (addonResult.Length > 0) Console.WriteLine(addonResult);

            try
            {
                if (o.ClientPatch)
                {
                    Console.WriteLine(L.T("Installing AFK client patch ..."));
                    CoaUninstall.CaptureGenerated(Path.Combine(o.Client, "Data", ClientPatch.ArchiveName));
                    var plan = ClientPatch.Collect(inst);
                    string result = ClientPatch.Install(inst, o.Client, plan,
                        delegate(string text, int done, int total) { if (total > 0) Console.WriteLine("  " + text); });
                    Console.WriteLine("  " + result);
                }
            }
            finally
            {
                CoaUninstall.Flush();
            }

            Console.WriteLine();
            Console.WriteLine(L.F("Done. Copied: {0}, skipped unchanged: {1}.", copied, skipped));
            Console.WriteLine(L.T("Start CoA64.exe for the 64-bit client, or Ascension.exe for the 32-bit client."));
            return 0;
        }

        /// <summary>
        /// The package's patch-T carries the custom-race models and the race portraits. A stale
        /// 19 MB client patch-T with only four DBCs makes them disappear, so verify the copy.
        /// </summary>
        static void EnsurePatchT(string clientSource, string client)
        {
            // The old client patch-T is about 19 MB. The full package file is about 346 MB.
            // A size check catches both a stale package and a client where the copy was skipped.
            const long MinimumPatchTBytes = 300000000;
            string target = Path.Combine(client, "Data", "patch-T.MPQ");
            if (File.Exists(target) && new FileInfo(target).Length < MinimumPatchTBytes)
                throw new InvalidOperationException(L.F("The game client still has the old small patch-T.MPQ ({0} bytes). The CoA package must supply the full patch-T.MPQ (about 346 MB); update the CoA package and run the setup again.", new FileInfo(target).Length));

            string source = Path.Combine(clientSource, "patch-T.MPQ");
            if (!File.Exists(source)) return;
            if (new FileInfo(source).Length < MinimumPatchTBytes)
                throw new InvalidOperationException(L.F("The selected CoA package has the old small patch-T.MPQ ({0} bytes). Use a current CoA Custom package with the full patch-T.MPQ (about 346 MB).", new FileInfo(source).Length));
            if (!File.Exists(target) || new FileInfo(target).Length != new FileInfo(source).Length)
                throw new InvalidOperationException(L.F("The CoA patch-T.MPQ was not copied completely. Expected {0} bytes in {1}; close the game and run the setup again.", new FileInfo(source).Length, target));
        }

        /// <summary>
        /// Puts the HD world archives back after an older AFK install quarantined them.
        /// The native 1.9.6 build has enough archive-handle room for the full chain.
        /// </summary>
        static void EnableHdWorldArchives(string client)
        {
            EnableArchive(client, "patch-Z.MPQ");
            EnableArchive(client, "patch-4.MPQ");
            EnableArchive(client, "patch-5.MPQ");
        }

        static void EnableArchive(string client, string name)
        {
            string archive = Path.Combine(client, "Data", name);
            if (File.Exists(archive)) return;

            string[] suffixes = { ".coa-patcher-backup", ".off", ".disabled" };
            foreach (string suffix in suffixes)
            {
                string backup = archive + suffix;
                if (!File.Exists(backup)) continue;
                try
                {
                    File.Move(backup, archive);
                    Console.WriteLine(L.F("Enabled Data\\{0} for the native CoA64 HD build.", name));
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(L.F("Data\\{0} could not be enabled. Close the game and run the setup again. ({1})", name, ex.Message), ex);
                }
                return;
            }
        }

        /// <summary>
        /// CoA64 packages without the native 1.9.6 build keep the old quarantine. The
        /// player's files stay next to the client as CoA backups so uninstall restores them.
        /// </summary>
        static void DisableHdWorldArchive(string client)
        {
            DisableArchive(client, "patch-Z.MPQ");
            DisableArchive(client, "patch-4.MPQ");
            DisableArchive(client, "patch-5.MPQ");
        }

        static void DisableArchive(string client, string name)
        {
            string archive = Path.Combine(client, "Data", name);
            if (!File.Exists(archive)) return;

            CoaUninstall.Capture(archive);
            string backup = archive + ".coa-patcher-backup";
            try
            {
                if (File.Exists(backup)) File.Delete(archive);
                else File.Move(archive, backup);
                Console.WriteLine(L.F("Disabled Data\\{0} for CoA64 compatibility; the original stays as .coa-patcher-backup.", name));
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(L.F("Data\\{0} could not be disabled. Close the game and run the setup again. ({1})", name, ex.Message), ex);
            }
        }

        /// <summary>
        /// DXVK + the software cursor can make CoA64 fail on the first start. Hardware cursor
        /// avoids that path; keep the old Config.wtf as a CoA backup.
        /// </summary>
        static void EnsureHardwareCursor(string client)
        {
            string path = Path.Combine(client, "WTF", "Config.wtf");
            if (!File.Exists(path)) return;
            try
            {
                string[] lines = File.ReadAllLines(path);
                bool found = false, changed = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    if (!lines[i].TrimStart().StartsWith("SET gxCursor", StringComparison.OrdinalIgnoreCase)) continue;
                    found = true;
                    if (lines[i].Trim() == "SET gxCursor \"1\"") continue;
                    lines[i] = "SET gxCursor \"1\"";
                    changed = true;
                }
                if (!found)
                {
                    Array.Resize(ref lines, lines.Length + 1);
                    lines[lines.Length - 1] = "SET gxCursor \"1\"";
                    changed = true;
                }
                if (!changed) return;

                CoaUninstall.Capture(path);
                string backup = path + ".coa-patcher-backup";
                if (!File.Exists(backup)) File.Copy(path, backup, false);
                File.WriteAllLines(path, lines, new UTF8Encoding(false));
                Console.WriteLine(L.T("Enabled the hardware cursor for CoA64 (DXVK software-cursor fix)."));
            }
            catch (Exception ex)
            {
                Console.WriteLine(L.T("The hardware cursor could not be set:") + " " + ex.Message);
            }
        }

        static void PrintCheck(Options o, Install inst, string files, string clientSource, string clientRoot)
        {
            Console.WriteLine(L.T("Check only. Would copy:"));
            Console.WriteLine(L.F("  - {0} MPQs from {1} to {2}", Count(clientSource, "*.MPQ"), clientSource, Path.Combine(o.Client, "Data")));
            Console.WriteLine(L.F("  - client_root files from {0} to {1}", clientRoot, o.Client));
            Console.WriteLine(L.F("  - addons from {0}", Path.Combine(files, "addons")));
            Console.WriteLine("  - native HD CoA64.exe, or the package's CoA64.exe changed to load HD without DXVK");
            Console.WriteLine("  - x64 DXVK files (d3d9, dxgi)");
            Console.WriteLine("  - full Data\\patch-T.MPQ (about 346 MB)");
            Console.WriteLine(NativeHd.SupportsPackage(clientRoot)
                ? "  - keep Data\\patch-Z.MPQ, patch-4.MPQ, patch-5.MPQ and patch-ZZZZS/T/W.MPQ enabled (native 1.9.6 HD)"
                : "  - disable Data\\patch-Z.MPQ, patch-4.MPQ and patch-5.MPQ for CoA64 compatibility");
            Console.WriteLine(L.F("  - server DBCs to {0}", Path.Combine(inst.DataDir, "dbc")));
            Console.WriteLine(L.F("  - server SQL to {0}", Path.Combine(inst.Root, "CoA-Custom", "sql")));
            Console.WriteLine(L.F("  - settings in {0}", Path.Combine(inst.ConfigDir, "modules", "mod_custom_races.conf")));
            if (o.ClientPatch) Console.WriteLine("  - AFK client patch");
        }

        static readonly HashSet<int> HiddenCoaRaces = new HashSet<int> { 19, 27, 65, 72, 77, 80, 81 };

        // The base race CoA Custom gives its races: its RACIAL table for the first 31 races and a
        // same-faction stock race for the Esteria races above them.
        static readonly Dictionary<int, string> CoaBaseByRace = new Dictionary<int, string>
        {
            { 9, "Orc" }, { 12, "Troll" }, { 13, "NightElf" }, { 14, "Human" }, { 15, "Dwarf" },
            { 16, "Human" }, { 17, "Tauren" }, { 18, "Human" }, { 20, "Tauren" }, { 21, "Undead" },
            { 22, "Draenei" }, { 23, "Orc" }, { 24, "Troll" }, { 25, "Troll" }, { 26, "Undead" },
            { 28, "Troll" }, { 29, "Draenei" }, { 30, "Human" }, { 31, "Undead" },
            { 47, "NightElf" }, { 48, "Dwarf" }, { 49, "Orc" }, { 50, "NightElf" }, { 51, "Undead" },
            { 53, "Orc" }, { 54, "Human" }, { 55, "Orc" }, { 56, "BloodElf" }, { 57, "Orc" },
            { 58, "Orc" }, { 59, "NightElf" }, { 60, "BloodElf" }, { 61, "NightElf" }, { 62, "Draenei" },
            { 63, "BloodElf" }, { 66, "Tauren" }, { 67, "Gnome" }, { 68, "Dwarf" }, { 69, "Tauren" },
            { 70, "Orc" }, { 71, "Human" }, { 74, "Troll" }, { 75, "Human" }, { 78, "Human" },
            { 79, "Human" }, { 82, "Human" }, { 83, "Orc" }, { 87, "Orc" }, { 90, "Undead" },
            { 97, "Human" }, { 98, "Orc" }, { 99, "Human" }, { 100, "Human" }, { 101, "Undead" },
            { 102, "Tauren" }, { 103, "Human" }, { 104, "Orc" }, { 106, "Orc" }, { 107, "Draenei" },
            { 109, "Human" }, { 110, "Human" }, { 112, "Orc" },
        };

        /// <summary>
        /// Makes the playable races of the CoA package active and adds their base-race entries.
        /// Existing base-race choices are left alone. The CoA UI owns race visibility; there are
        /// no on/off switches here, and stale keys of older client versions are removed from
        /// CustomRaces.Races.
        /// </summary>
        /// <summary>
        /// The active race config is normally created by the module when the server first starts.
        /// A fresh or reset server can reach the CoA setup before that has happened; without the
        /// file the setup cannot write UseCoAUI=1 and the old AFK race selector is built into the
        /// client patch. Create it from the module's .dist in that case.
        /// </summary>
        /// <summary>Called after the setup wrote the race settings, before the client patch is built: the web app brings
        /// the racials, their rows and the paths of this computer into them (its races page).</summary>
        public static Action<Install> AfterRaceSettings;

        sealed class CarriedRaces
        {
            public readonly Dictionary<string, string> Globals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, string> Imported = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public readonly List<string> ImportedKeys = new List<string>();
            public readonly Dictionary<string, string> Bases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The race settings are made anew from the module's .dist at every CoA Custom install: only adding to an old
        /// file kept stale numbers, paths of another computer (a file copied from someone else: its RacialsFolder and
        /// client paths) and old entries, and the racials did not work until something wrote them again. The old file
        /// is kept as mod_custom_races.conf.before-coa-&lt;time&gt;. Carried over: the user's own races (imported from a race
        /// folder, or copies of a client race under a number of their own) and whether they were on, the base race chosen
        /// for each race, the race folder and whether bots use custom races.
        /// </summary>
        static CarriedRaces ResetRaceConfig(Install inst)
        {
            var carried = new CarriedRaces();
            string confPath = Path.Combine(inst.ConfigDir, "modules", "mod_custom_races.conf");
            if (!File.Exists(confPath)) return carried;
            ConfigFile old;
            try { old = ConfigFile.Load(confPath, "modules\\mod_custom_races.conf"); }
            catch { return carried; }
            Func<string, string> raw = key => { ConfigOption o; return old.ByKey.TryGetValue(key, out o) ? o.CurrentRaw : null; };
            foreach (string key in new[] { "CustomRaces.ImportFolder", "CoACustomRaces.BotsEnable" })
            {
                string v = raw(key);
                if (!string.IsNullOrEmpty(v)) carried.Globals[key] = v;
            }
            var listed = new HashSet<string>(ConfigOption.Unquote(raw("CustomRaces.Races") ?? "").Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
            foreach (var pair in old.ByKey)
            {
                if (!pair.Key.StartsWith("CustomRaces.", StringComparison.OrdinalIgnoreCase) || !pair.Key.EndsWith(".Source", StringComparison.OrdinalIgnoreCase)) continue;
                string race = pair.Key.Substring("CustomRaces.".Length, pair.Key.Length - "CustomRaces.".Length - ".Source".Length);
                if (race.Length == 0 || race.IndexOf('.') >= 0) continue;
                string prefix = "CustomRaces." + race + ".";
                // the user's own races: imported from a race folder, or a copy of a client race under a number of its own
                // (the High elf of older setups: a copy of the Blood elf as race 15)
                if (ConfigOption.Unquote(raw(prefix + "Import") ?? "") == "1" || ConfigOption.Unquote(raw(prefix + "Copy") ?? "") == "1")
                {
                    if (listed.Contains(race)) carried.ImportedKeys.Add(race);
                    foreach (var option in old.ByKey)
                        if (option.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && option.Value.CurrentRaw.Length > 0)
                            carried.Imported[option.Key] = option.Value.CurrentRaw;
                }
                else
                {
                    string b = raw(prefix + "Base");
                    if (!string.IsNullOrEmpty(b)) carried.Bases[race] = b;
                }
            }
            try
            {
                File.Copy(confPath, confPath + ".before-coa-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true);
                File.Delete(confPath);
            }
            catch { return new CarriedRaces(); }
            Console.WriteLine(L.T("Race settings made anew (the old file is kept as mod_custom_races.conf.before-coa-...)."));
            return carried;
        }

        static void ApplyCarried(Install inst, CarriedRaces carried)
        {
            string confPath = Path.Combine(inst.ConfigDir, "modules", "mod_custom_races.conf");
            EnsureRaceConfig(inst);
            if (!File.Exists(confPath) || (carried.Globals.Count == 0 && carried.Imported.Count == 0 && carried.Bases.Count == 0)) return;
            ConfigFile conf = ConfigFile.Load(confPath, "modules\\mod_custom_races.conf");
            foreach (var pair in carried.Globals) Set(conf, pair.Key, pair.Value);
            // a race of the user's whose number a race of the package has now is not carried over: two races with one
            // number leave one of them off, maybe the package's
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in conf.ByKey)
                if (pair.Key.StartsWith("CustomRaces.", StringComparison.OrdinalIgnoreCase) && pair.Key.EndsWith(".Id", StringComparison.OrdinalIgnoreCase))
                    taken.Add(ConfigOption.Unquote(pair.Value.CurrentRaw));
            var dropped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in carried.Imported)
                if (pair.Key.EndsWith(".Id", StringComparison.OrdinalIgnoreCase) && taken.Contains(ConfigOption.Unquote(pair.Value)))
                {
                    string race = pair.Key.Substring("CustomRaces.".Length, pair.Key.Length - "CustomRaces.".Length - ".Id".Length);
                    dropped.Add(race);
                    Console.WriteLine(L.F("Your race {0} has number {1}, which a race of the package has now; it is not carried over.", race, ConfigOption.Unquote(pair.Value)));
                }
            foreach (var pair in carried.Imported)
            {
                string rest = pair.Key.Substring("CustomRaces.".Length);
                if (!dropped.Contains(rest.Substring(0, rest.IndexOf('.')))) Set(conf, pair.Key, pair.Value);
            }
            carried.ImportedKeys.RemoveAll(k => dropped.Contains(k));
            foreach (var pair in carried.Bases)
                if (Has(conf, "CustomRaces." + pair.Key + ".Source")) Set(conf, "CustomRaces." + pair.Key + ".Base", pair.Value);
            if (carried.ImportedKeys.Count > 0)
            {
                ConfigOption list;
                string current = conf.ByKey.TryGetValue("CustomRaces.Races", out list) ? ConfigOption.Unquote(list.CurrentRaw) : "";
                string pending;
                if (conf.Pending.TryGetValue("CustomRaces.Races", out pending)) current = ConfigOption.Unquote(pending);
                var keys = current.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                foreach (string key in carried.ImportedKeys)
                    if (!keys.Contains(key, StringComparer.OrdinalIgnoreCase)) keys.Add(key);
                Set(conf, "CustomRaces.Races", Quote(string.Join(" ", keys.ToArray())));
            }
            conf.Save();
        }

        static void EnsureRaceConfig(Install inst)
        {
            string confPath = Path.Combine(inst.ConfigDir, "modules", "mod_custom_races.conf");
            if (File.Exists(confPath)) return;
            string dist = confPath + ".dist";
            if (!File.Exists(dist))
            {
                string moduleDist = Path.Combine(inst.SourceDir, "modules", "mod-custom-races", "conf", "mod_custom_races.conf.dist");
                if (File.Exists(moduleDist)) dist = moduleDist;
            }
            if (File.Exists(dist)) File.Copy(dist, confPath, false);
        }

        static void WriteRaceDefaults(Install inst, string files)
        {
            EnsureRaceConfig(inst);
            WriteRaceRows(inst, Path.Combine(files, "dbc", "ChrRaces.dbc"), null);
        }

        /// <summary>The playable races of CoA Custom by the server's race table (the package's, copied at the install):
        /// number and name. These are the races the game client offers in the character creation.</summary>
        public static List<KeyValuePair<int, string>> PackageRaces(Install inst)
        {
            var list = new List<KeyValuePair<int, string>>();
            string dbc = Path.Combine(inst.DataDir, "dbc", "ChrRaces.dbc");
            if (!File.Exists(dbc)) return list;
            DbcTable table;
            try { table = DbcTable.Parse(File.ReadAllBytes(dbc), "ChrRaces.dbc"); }
            catch { return list; }
            foreach (var row in table.Records)
            {
                int id = (int)table.Field(row, 0);
                uint flags = table.Field(row, 1);
                if (id <= 11 || id > 127 || (flags & 0xC) != 0xC || HiddenCoaRaces.Contains(id)) continue;
                string name = ClientRaces.Str(table, row, 14);
                if (name.Length == 0) name = ClientRaces.Str(table, row, 11);
                list.Add(new KeyValuePair<int, string>(id, name));
            }
            return list;
        }

        /// <summary>
        /// The server modules a CoA Custom package needs on an AFK Realm server. CoA Custom ships a worldserver built from
        /// its own core, which AFK Realm does not use: it builds the current Conquest of Azeroth core with modules instead,
        /// and the code parts of a package come as those modules. mod-custom-races always (the races); mod-subclass when
        /// the package brings Stefan's subclassing (1.9.8 and newer: a "subclass" folder or Apply-Subclass.bat), for the
        /// Two-Headed Ogre and the Thin Twins.
        /// </summary>
        public static List<string> ModulesForPackage(Install inst)
        {
            var modules = new List<string> { "mod-custom-races" };
            try
            {
                string picked = Settings.CoaPackage ?? "";
                string root = picked.Length > 0 && Directory.Exists(picked) ? PackageRoot(picked) : null;
                if (root != null && (Directory.Exists(Path.Combine(root, "subclass")) || File.Exists(Path.Combine(root, "Apply-Subclass.bat"))))
                    modules.Add("mod-subclass");
            }
            catch { }
            return modules;
        }

        static string LeftOffFile(Install inst) { return Path.Combine(inst.Root, "Dependencies", "races-left-off.txt"); }

        static HashSet<int> LeftOff(Install inst)
        {
            var set = new HashSet<int>();
            try
            {
                if (File.Exists(LeftOffFile(inst)))
                    foreach (string line in File.ReadAllLines(LeftOffFile(inst)))
                    {
                        int n;
                        if (int.TryParse(line.Trim(), out n)) set.Add(n);
                    }
            }
            catch { }
            return set;
        }

        /// <summary>The races the game client offers (CoA Custom) that the server cannot create, because the race settings
        /// do not switch them on (CustomRaces.Races): a player gets "Error creating character" for them, the server log says
        /// "invalid race/class pair". Seen after race settings that were written by hand, sent along by another player, or
        /// made by an AFK Realm before 1.2.30. Races the user chose to leave off are not listed.</summary>
        public static List<KeyValuePair<int, string>> MissingServerRaces(Install inst)
        {
            var missing = new List<KeyValuePair<int, string>>();
            string confPath = Path.Combine(inst.ConfigDir, "modules", "mod_custom_races.conf");
            if (!File.Exists(confPath)) return missing;
            var offered = PackageRaces(inst);
            if (offered.Count == 0) return missing;
            ConfigFile conf;
            try { conf = ConfigFile.Load(confPath, "modules\\mod_custom_races.conf"); }
            catch { return missing; }
            ConfigOption option;
            string list = conf.ByKey.TryGetValue("CustomRaces.Races", out option) ? ConfigOption.Unquote(option.CurrentRaw) : "";
            var on = new HashSet<int>();
            foreach (string key in list.Split(new[] { ' ', ',', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                // as mod-custom-races reads it: the Id, or without one the Source if it is below 32; 0 is off
                int n, source = 0;
                if (!conf.ByKey.TryGetValue("CustomRaces." + key + ".Source", out option) || !int.TryParse(ConfigOption.Unquote(option.CurrentRaw), out source)) continue;
                if (conf.ByKey.TryGetValue("CustomRaces." + key + ".Id", out option) && ConfigOption.Unquote(option.CurrentRaw).Trim().Length > 0)
                    n = int.TryParse(ConfigOption.Unquote(option.CurrentRaw), out n) ? n : 0;
                else n = source < 32 ? source : 0;
                if (n > 0) { on.Add(n); on.Add(source); }   // a race put at another number (Copy) is on as well
            }
            if (on.Count == 0) return missing;   // no races at all: the notice for empty race settings covers it
            var leftOff = LeftOff(inst);
            foreach (var race in offered)
                if (!on.Contains(race.Key) && !leftOff.Contains(race.Key)) missing.Add(race);
            return missing;
        }

        /// <summary>Switches the missing races on (MissingServerRaces): each gets its section as the CoA Custom install
        /// writes it, if it has none, and joins CustomRaces.Races; the races already on stay as they are. The racials and
        /// race files are written again after. The server reads it at its next start. Returns the races switched on.</summary>
        public static List<KeyValuePair<int, string>> AddMissingServerRaces(Install inst)
        {
            var missing = MissingServerRaces(inst);
            if (missing.Count == 0) return missing;
            WriteRaceRows(inst, Path.Combine(inst.DataDir, "dbc", "ChrRaces.dbc"), new HashSet<int>(missing.Select(m => m.Key)));
            if (AfterRaceSettings != null) try { AfterRaceSettings(inst); } catch { }
            return missing;
        }

        /// <summary>The user keeps these races off: no notice about them any more (until the CoA Custom install, which
        /// switches every race of the package on again).</summary>
        public static void LeaveRacesOff(Install inst, IEnumerable<int> ids)
        {
            var set = LeftOff(inst);
            foreach (int id in ids) set.Add(id);
            Directory.CreateDirectory(Path.GetDirectoryName(LeftOffFile(inst)));
            File.WriteAllLines(LeftOffFile(inst), set.OrderBy(n => n).Select(n => n.ToString()).ToArray());
        }

        /// <summary>Writes the sections of the package's races and the race list. only == null: every playable race of
        /// the package, and the list becomes exactly these (the install). Otherwise only these races, added to the list.</summary>
        static void WriteRaceRows(Install inst, string dbc, HashSet<int> only)
        {
            if (!File.Exists(dbc)) return;
            string confPath = Path.Combine(inst.ConfigDir, "modules", "mod_custom_races.conf");
            if (!File.Exists(confPath)) return;
            DbcTable table;
            try { table = DbcTable.Parse(File.ReadAllBytes(dbc), "ChrRaces.dbc"); }
            catch { return; }
            ConfigFile conf = ConfigFile.Load(confPath, "modules\\mod_custom_races.conf");
            var keys = new List<string>();
            HashSet<string> known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in table.Records)
            {
                int id = (int)table.Field(row, 0);
                uint flags = table.Field(row, 1);
                if (id <= 11 || id > 127 || (flags & 0xC) != 0xC || HiddenCoaRaces.Contains(id)) continue;
                if (only != null && !only.Contains(id)) continue;
                string file = ClientRaces.Str(table, row, 11);
                string name = ClientRaces.Str(table, row, 14);
                if (name.Length == 0) name = file;
                string key = RaceKey(conf, id, file);
                if (!known.Add(key)) key = RaceKey(conf, id, file + id);
                string prefix = "CustomRaces." + key + ".";
                if (!Has(conf, prefix + "Source"))
                {
                    Set(conf, prefix + "Source", id.ToString());
                    Set(conf, prefix + "Id", id.ToString());
                    Set(conf, prefix + "Copy", "0");
                    Set(conf, prefix + "Import", "0");
                    Set(conf, prefix + "Name", Quote(name));
                    Set(conf, prefix + "Prefix", Quote(ClientRaces.Str(table, row, 6)));
                    Set(conf, prefix + "File", Quote(file));
                    Set(conf, prefix + "MaleDisplay", table.Field(row, 4).ToString());
                    Set(conf, prefix + "FemaleDisplay", table.Field(row, 5).ToString());
                    Set(conf, prefix + "MaleOnly", "0");
                    Set(conf, prefix + "FemaleOnly", "0");
                    Set(conf, prefix + "OwnHelmets", "1");
                    Set(conf, prefix + "HelmetPrefix", "\"\"");
                    Set(conf, prefix + "IconFile", "\"\"");
                    Set(conf, prefix + "Icon", "\"\"");
                    Set(conf, prefix + "Racials", "\"\"");
                    Set(conf, prefix + "Base", Quote(BaseFor(id, (int)table.Field(row, 13))));
                    if (id == 66 || id == 68 || id == 69) Set(conf, prefix + "Appearance", "1");
                    else if (id == 70 || id == 71) Set(conf, prefix + "Appearance", "2");
                }
                else
                {
                    // A package race that used to have no number must get one, or the module
                    // leaves it switched off even though the CoA UI expects it.
                    string currentId = Has(conf, prefix + "Id") ? ConfigOption.Unquote(conf.ByKey[prefix + "Id"].CurrentRaw) : "";
                    if (currentId.Length == 0 || currentId == "0") Set(conf, prefix + "Id", id.ToString());
                    // Repair old generated entries whose base race belongs to the other faction
                    // (for example Forest Troll 40 was set to the Horde's Orc).
                    int side = (int)table.Field(row, 13);
                    string current = Has(conf, prefix + "Base") ? ConfigOption.Unquote(conf.ByKey[prefix + "Base"].CurrentRaw) : "";
                    if (!BaseFitsSide(current, side)) Set(conf, prefix + "Base", Quote(BaseFor(id, side)));
                }
                keys.Add(key);
            }
            if (keys.Count > 0 && only != null)
            {
                ConfigOption current;
                var list = (conf.ByKey.TryGetValue("CustomRaces.Races", out current) ? ConfigOption.Unquote(current.CurrentRaw) : "")
                    .Split(new[] { ' ', ',', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                foreach (string k in keys)
                    if (!list.Contains(k, StringComparer.OrdinalIgnoreCase)) list.Add(k);
                Set(conf, "CustomRaces.Races", Quote(string.Join(" ", list.ToArray())));
            }
            else if (keys.Count > 0)
            {
                // The CoA UI owns race selection: every playable race of the CoA package is on,
                // and no stale key or client-only race from an older configuration survives in
                // CustomRaces.Races. Base races stay as the user chose them.
                Set(conf, "CustomRaces.Races", Quote(string.Join(" ", keys.ToArray())));
                try { if (File.Exists(LeftOffFile(inst))) File.Delete(LeftOffFile(inst)); } catch { }
            }
            conf.Save();
        }

        static bool Has(ConfigFile conf, string key)
        {
            ConfigOption option;
            return conf.ByKey.TryGetValue(key, out option) && option.CurrentRaw.Length > 0;
        }

        static string RaceKey(ConfigFile conf, int id, string file)
        {
            // Keep the section that already names this exact client row, whatever key it uses.
            foreach (var pair in conf.ByKey)
            {
                if (!pair.Key.StartsWith("CustomRaces.", StringComparison.OrdinalIgnoreCase) ||
                    !pair.Key.EndsWith(".Source", StringComparison.OrdinalIgnoreCase)) continue;
                if (ConfigOption.Unquote(pair.Value.CurrentRaw) == id.ToString())
                {
                    string prefix = pair.Key.Substring("CustomRaces.".Length);
                    return prefix.Substring(0, prefix.Length - ".Source".Length);
                }
            }
            string key = "";
            foreach (char c in file ?? "")
                if (char.IsLetterOrDigit(c)) key += c;
            if (key.Length == 0) key = "Race" + id;
            // The file name may belong to an old row of another number (ThinHuman 33 beside
            // Forgotten 32). Do not let the new row take that section over.
            string candidate = key;
            for (int n = 2; KeyExists(conf, candidate); n++)
                candidate = key + id + (n > 2 ? n.ToString() : "");
            return candidate;
        }

        static bool KeyExists(ConfigFile conf, string key)
        {
            string prefix = "CustomRaces." + key + ".";
            foreach (var pair in conf.ByKey)
                if (pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static string BaseFor(int id, int side)
        {
            string b;
            if (CoaBaseByRace.TryGetValue(id, out b)) return b;
            return side == 1 ? "Orc" : "Human";
        }

        static bool BaseFitsSide(string baseRace, int side)
        {
            if (string.IsNullOrEmpty(baseRace)) return false;
            bool horde = string.Equals(baseRace, "Orc", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(baseRace, "Undead", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(baseRace, "Tauren", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(baseRace, "Troll", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(baseRace, "BloodElf", StringComparison.OrdinalIgnoreCase);
            return side == 1 ? horde : !horde;
        }

        static void WriteClientSettings(Install inst, string clientRoot, string clientSource)
        {
            string confPath = Path.Combine(inst.ConfigDir, "modules", "mod_custom_races.conf");
            if (!File.Exists(confPath)) return;
            ConfigFile conf = ConfigFile.Load(confPath, "modules\\mod_custom_races.conf");
            Set(conf, "CustomRaces.Enable", "1");
            Set(conf, "CustomRaces.ClientRootFolder", Quote(clientRoot));
            string exe = Path.Combine(clientRoot, "Ascension.exe");
            if (File.Exists(exe)) Set(conf, "CustomRaces.ClientExe", Quote(exe));
            Set(conf, "CustomRaces.MoreRaces", "1");
            Set(conf, "CustomRaces.HighRaces", "1");
            Set(conf, "CustomRaces.UseCoAUI", "1");
            string patch = Path.Combine(clientSource, "dinput8.dll");
            if (File.Exists(patch)) Set(conf, "CustomRaces.MoreRacesFile", Quote(patch));
            // off on a first install only: the user's choice (or the one carried into new settings) stays
            if (!Has(conf, "CoACustomRaces.BotsEnable") && !conf.Pending.ContainsKey("CoACustomRaces.BotsEnable")) Set(conf, "CoACustomRaces.BotsEnable", "0");
            conf.Save();
        }

        static void Set(ConfigFile conf, string key, string value)
        {
            ConfigOption option;
            bool quoted = conf.ByKey.TryGetValue(key, out option) && option.Quoted;
            conf.Pending[key] = quoted && !ConfigOption.IsQuoted(value) ? Quote(value) : value;
        }

        static string Quote(string value)
        {
            return "\"" + (value ?? "").Replace("\"", "") + "\"";
        }

        static int Count(string folder, string pattern)
        {
            try { return Directory.Exists(folder) ? Directory.GetFiles(folder, pattern).Length : 0; }
            catch { return 0; }
        }

        static void CopyFiles(string source, string target, string pattern, bool noBackup, ref int copied, ref int skipped)
        {
            if (!Directory.Exists(source)) return;
            Directory.CreateDirectory(target);
            // Windows matches *.MPQ case-insensitively; Linux does not. Match the
            // suffix explicitly so CoA's upper-case client MPQs are copied there too.
            string suffix = pattern.StartsWith("*", StringComparison.Ordinal) ? pattern.Substring(1) : null;
            foreach (string file in Directory.GetFiles(source)
                .Where(f => suffix == null || Path.GetFileName(f).EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f))
            {
                if (CopyFile(file, Path.Combine(target, Path.GetFileName(file)), noBackup)) copied++;
                else skipped++;
            }
        }

        static void CopyTree(string source, string target, bool noBackup, ref int copied, ref int skipped, string skipFile = null)
        {
            if (!Directory.Exists(source)) return;
            string root = Path.GetFullPath(source).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories).OrderBy(x => x))
            {
                if (skipFile != null && string.Equals(Path.GetFileName(file), skipFile, StringComparison.OrdinalIgnoreCase)) continue;
                string rel = file.Substring(root.Length);
                string dest = Path.Combine(target, rel);
                if (CopyFile(file, dest, noBackup)) copied++;
                else skipped++;
            }
        }

        /// <summary>Checks after a copy that every package file is present and complete.</summary>
        static void VerifyCopied(string source, string target, string pattern)
        {
            if (!Directory.Exists(source)) return;
            string suffix = pattern.StartsWith("*", StringComparison.Ordinal) ? pattern.Substring(1) : null;
            foreach (string file in Directory.GetFiles(source)
                .Where(f => suffix == null || Path.GetFileName(f).EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
                VerifyFile(file, Path.Combine(target, Path.GetFileName(file)));
        }

        static void VerifyTree(string source, string target, string skipFile = null)
        {
            if (!Directory.Exists(source)) return;
            string root = Path.GetFullPath(source).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                if (skipFile != null && string.Equals(Path.GetFileName(file), skipFile, StringComparison.OrdinalIgnoreCase)) continue;
                string rel = file.Substring(root.Length);
                VerifyFile(file, Path.Combine(target, rel));
            }
        }

        static void VerifyFile(string source, string target)
        {
            if (!File.Exists(target))
                throw new InvalidOperationException(L.F("The CoA package file is missing after the copy: {0}", target));
            if (new FileInfo(source).Length != new FileInfo(target).Length)
                throw new InvalidOperationException(L.F("The CoA package file is incomplete after the copy: {0}", target));
        }

        static bool SameFile(string a, string b)
        {
            try
            {
                FileInfo fa = new FileInfo(a), fb = new FileInfo(b);
                if (fa.Length != fb.Length) return false;
                using (FileStream sa = File.OpenRead(a))
                using (FileStream sb = File.OpenRead(b))
                {
                    byte[] ba = new byte[1024 * 1024], bb = new byte[1024 * 1024];
                    while (true)
                    {
                        int na = sa.Read(ba, 0, ba.Length), nb = sb.Read(bb, 0, bb.Length);
                        if (na != nb) return false;
                        if (na == 0) return true;
                        for (int i = 0; i < na; i++) if (ba[i] != bb[i]) return false;
                    }
                }
            }
            catch { return false; }
        }

        static bool CopyFile(string source, string target, bool noBackup)
        {
            CoaUninstall.Capture(target);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            if (File.Exists(target))
            {
                if (SameFile(source, target)) return false;
                if (!noBackup)
                {
                    string backup = target + ".coa-patcher-backup";
                    if (!File.Exists(backup)) File.Copy(target, backup, false);
                }
            }
            try
            {
                File.Copy(source, target, true);
            }
            catch (Exception ex)
            {
                // Never report success while a package file was left behind. The former
                // silent skip hid locked files (game, launcher, antivirus) and produced
                // clients with missing MPQs, DLLs and Esteria appearance files.
                throw new InvalidOperationException(L.F("The CoA file could not be copied to {0}. Close the game, the launcher and the server, then run the setup again. ({1})", target, ex.Message), ex);
            }
            Console.WriteLine("  " + target);
            return true;
        }

        static string PackageRoot(string picked)
        {
            string direct = Path.Combine(picked, "files");
            if (Directory.Exists(direct)) return picked;
            string root = CoaCustom.PackageRoot(picked);
            if (root != null) return root;
            foreach (string dir in Directory.GetDirectories(picked))
            {
                if (Directory.Exists(Path.Combine(dir, "files"))) return dir;
                foreach (string nested in Directory.GetDirectories(dir))
                    if (Directory.Exists(Path.Combine(nested, "files"))) return nested;
            }
            return null;
        }

        internal static string ReadVersion(string manifest)
        {
            try
            {
                string text = File.ReadAllText(manifest);
                int at = text.IndexOf("\"version\"", StringComparison.OrdinalIgnoreCase);
                if (at < 0) return "";
                at = text.IndexOf(':', at);
                if (at < 0) return "";
                int start = text.IndexOf('"', at + 1);
                int end = start < 0 ? -1 : text.IndexOf('"', start + 1);
                return start >= 0 && end > start ? text.Substring(start + 1, end - start - 1) : "";
            }
            catch { return ""; }
        }

        static int Fail(string text)
        {
            Console.Error.WriteLine("ERROR: " + text);
            return 2;
        }

        static Options Parse(string[] args)
        {
            var o = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "--package" && i + 1 < args.Length) o.Package = args[++i];
                else if (a == "--server" && i + 1 < args.Length) o.Server = args[++i];
                else if (a == "--client" && i + 1 < args.Length) o.Client = args[++i];
                else if (a == "--check") o.Check = true;
                else if (a == "--client-patch-only") o.ClientPatchOnly = true;
                else if (a == "--client-patch") o.ClientPatch = true;
                else if (a == "--no-client-patch") o.ClientPatch = false;
                else if (a == "--no-backup") o.NoBackup = true;
                else if (a == "--help" || a == "-h") o.Help = true;
            }
            return o;
        }

        static void PrintHelp()
        {
            Console.WriteLine("CoA Custom setup (integrated in AFK Realm)");
            Console.WriteLine();
            Console.WriteLine("  --package <CoA-Custom> --server <AFK server> --client <game folder> [--check]");
            Console.WriteLine();
            Console.WriteLine("  default          copy CoA MPQs, client_root, addons and settings; install AFK client patch");
            Console.WriteLine("  --check          only report what would be done");
            Console.WriteLine("  --client-patch   install AFK Realm's generated client patch (default)");
            Console.WriteLine("  --no-client-patch  leave the AFK client patch alone");
            Console.WriteLine("  --client-patch-only  rebuild/reinstall just the AFK client patch");
            Console.WriteLine("  --no-backup      do not keep .coa-patcher-backup files");
        }
    }
}
