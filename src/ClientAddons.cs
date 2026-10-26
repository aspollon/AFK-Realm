using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace CoAInstaller
{
    enum AddonState { Current, Missing, Outdated }

    /// <summary>An add-on for the game client that a module brings (the "client" part of its afk-realm.json).</summary>
    class ClientAddon
    {
        public string Module = "", Name = "", Source = "", Why = "", WhyEnglish = "";
        public bool ClearCache;
        public AddonState State;
    }

    /// <summary>
    /// The game client half of the modules: a module can name add-ons in its afk-realm.json
    /// ("client": {"addons": ["client/AddOns/Name"], "clearCache": true}). AFK Realm copies them
    /// into Interface\AddOns of the game client, clears the client's cache when the module asks for
    /// it (so the client forgets old levels and values of items, creatures and quests), takes an
    /// add-on out again when its module is removed, and packs them into a ZIP for other players.
    /// What it installed is recorded in Dependencies\client-addons.txt.
    /// </summary>
    static class ClientAddons
    {
        static string RecordFile(Install inst) { return Path.Combine(inst.Root, "Dependencies", "client-addons.txt"); }
        static string FolderFile { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Product.FileStem, "client-folder.txt"); } }

        /// <summary>The game client folder chosen last time, or null.</summary>
        public static string ClientFolder
        {
            get { try { var d = File.Exists(FolderFile) ? File.ReadAllText(FolderFile).Trim() : ""; return d.Length > 0 ? d : null; } catch { return null; } }
            set { try { Directory.CreateDirectory(Path.GetDirectoryName(FolderFile)); File.WriteAllText(FolderFile, value ?? ""); } catch { } }
        }

        /// <summary>A game folder has a "Data" folder with the game's .MPQ archives.</summary>
        public static bool IsClientFolder(string dir)
        {
            try { return !string.IsNullOrEmpty(dir) && HasArchives(dir.TrimEnd('\\', '/')); }
            catch { return false; }
        }

        /// <summary>The game folder (the one with "Data") behind what the user picked: the game folder itself, the
        /// "Data" folder or a folder inside it, or a folder of a repack with the game somewhere below it. Null when
        /// there is no game folder (see also RacePacker.ArchivesOf, which reads the archives the same way).</summary>
        public static string GameRoot(string dir)
        {
            try
            {
                dir = (dir ?? "").Trim().TrimEnd('\\', '/');
                if (dir.Length == 0 || !Directory.Exists(dir)) return null;
                if (HasArchives(dir)) return dir;                                     // the game folder itself
                string parent = Path.GetDirectoryName(dir);
                string up = Named(Path.GetFileName(dir), "Data") ? parent             // the "Data" folder itself
                    : Named(Path.GetFileName(parent ?? ""), "Data") ? Path.GetDirectoryName(parent) : null;  // a folder in "Data"
                if (up != null && HasArchives(up)) return up;
                foreach (var d in Below(dir, 3)) if (HasArchives(d)) return d;        // a repack unpacked into a folder of its own
                return null;
            }
            catch { return null; }
        }

        static bool HasArchives(string dir)
        {
            string data = Path.Combine(dir ?? "", "Data");
            return Directory.Exists(data) && Directory.EnumerateFiles(data, "*.mpq").Any();
        }

        static bool Named(string name, string want) { return string.Equals(name, want, StringComparison.OrdinalIgnoreCase); }

        /// <summary>The folders below a folder, up to the given depth.</summary>
        static IEnumerable<string> Below(string folder, int depth)
        {
            if (depth == 0) yield break;
            string[] dirs;
            try { dirs = Directory.GetDirectories(folder); } catch { yield break; }
            foreach (var d in dirs.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                yield return d;
                foreach (var e in Below(d, depth - 1)) yield return e;
            }
        }

        static string AddonsDir(string client) { return Path.Combine(client, "Interface", "AddOns"); }

        /// <summary>The add-ons the installed modules bring, with their state in the given client folder (null: all count as missing).</summary>
        public static List<ClientAddon> Find(Install inst, string client)
        {
            var result = new List<ClientAddon>();
            string modules = ModuleCatalog.ModulesDir(inst);
            if (!Directory.Exists(modules)) return result;
            var json = new JavaScriptSerializer();
            foreach (var dir in Directory.GetDirectories(modules).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                string file = Path.Combine(dir, ModuleCatalog.ManifestName);
                if (!File.Exists(file)) continue;
                Dictionary<string, object> clientPart = null;
                object v;
                try
                {
                    var data = json.DeserializeObject(File.ReadAllText(file, Encoding.UTF8)) as Dictionary<string, object>;
                    if (data != null && data.TryGetValue("client", out v)) clientPart = v as Dictionary<string, object>;
                }
                catch { continue; }
                if (clientPart == null) continue;
                bool clear = clientPart.TryGetValue("clearCache", out v) && v is bool && (bool)v;
                string whyEnglish = clientPart.TryGetValue("why", out v) ? Convert.ToString(v) : "", why = L.Outside(whyEnglish);
                var list = clientPart.TryGetValue("addons", out v) ? v as object[] : null;
                if (list == null) continue;
                string root = Path.GetFullPath(dir).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                foreach (var entry in list.OfType<string>())
                {
                    string source;
                    try { source = Path.GetFullPath(Path.Combine(dir, entry.Replace('/', Path.DirectorySeparatorChar))); } catch { continue; }
                    string name = Path.GetFileName(source.TrimEnd('\\', '/'));
                    // Only a folder inside the module, with a plain name and a .toc of that name, counts as an add-on.
                    if (!source.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !Regex.IsMatch(name, @"^[\w.\-]+$") ||
                        !File.Exists(Path.Combine(source, name + ".toc")))
                        continue;
                    var addon = new ClientAddon { Module = Path.GetFileName(dir), Name = name, Source = source, ClearCache = clear, Why = why, WhyEnglish = whyEnglish };
                    addon.State = client == null ? AddonState.Missing : StateOf(addon, client);
                    result.Add(addon);
                }
            }
            return result;
        }

        static AddonState StateOf(ClientAddon addon, string client)
        {
            string target = Path.Combine(AddonsDir(client), addon.Name);
            if (!Directory.Exists(target)) return AddonState.Missing;
            return Fingerprint(addon.Source) == Fingerprint(target) ? AddonState.Current : AddonState.Outdated;
        }

        /// <summary>One hash over the names and contents of every file in a folder.</summary>
        static string Fingerprint(string dir)
        {
            using (var sha = SHA256.Create())
            {
                foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    var name = Encoding.UTF8.GetBytes(file.Substring(dir.Length).Replace('\\', '/').ToLowerInvariant() + "\n");
                    sha.TransformBlock(name, 0, name.Length, null, 0);
                    var data = File.ReadAllBytes(file);
                    sha.TransformBlock(data, 0, data.Length, null, 0);
                }
                sha.TransformFinalBlock(new byte[0], 0, 0);
                return BitConverter.ToString(sha.Hash);
            }
        }

        /// <summary>What AFK Realm put into the client: add-on name -> module.</summary>
        static Dictionary<string, string> Record(Install inst)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(RecordFile(inst)))
                    foreach (var line in File.ReadAllLines(RecordFile(inst)))
                    {
                        var f = line.Split('|');
                        if (f.Length >= 2 && Regex.IsMatch(f[1], @"^[\w.\-]+$")) d[f[1]] = f[0];
                    }
            }
            catch { }
            return d;
        }

        static void SaveRecord(Install inst, Dictionary<string, string> record)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RecordFile(inst)));
            File.WriteAllLines(RecordFile(inst), record.OrderBy(r => r.Key, StringComparer.OrdinalIgnoreCase).Select(r => r.Value + "|" + r.Key), new UTF8Encoding(false));
        }

        /// <summary>Add-ons AFK Realm installed whose module is gone: they are taken out of the client.</summary>
        public static List<string> Leftovers(Install inst, List<ClientAddon> current, string client)
        {
            return Record(inst).Keys.Where(name => !current.Any(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) &&
                (client == null || Directory.Exists(Path.Combine(AddonsDir(client), name)))).ToList();
        }

        /// <summary>True while a program from the client folder runs (the game or its launcher).</summary>
        public static bool ClientRunning(string client)
        {
            string root = Path.GetFullPath(client).TrimEnd('\\') + "\\";
            foreach (var p in Process.GetProcesses())
            {
                try { if (p.MainModule.FileName.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return true; }
                catch { }
                finally { p.Dispose(); }
            }
            return false;
        }

        static void CopyFolder(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
            foreach (var dir in Directory.GetDirectories(from)) CopyFolder(dir, Path.Combine(to, Path.GetFileName(dir)));
        }

        /// <summary>Copies the add-ons into the client, takes out the leftovers and clears the cache when asked. Returns what was done.</summary>
        public static string Install(Install inst, string client, List<ClientAddon> addons, List<string> leftovers, bool clearCache)
        {
            var done = new List<string>();
            var record = Record(inst);
            string target = AddonsDir(client);
            Directory.CreateDirectory(target);
            foreach (var addon in addons)
            {
                string to = Path.Combine(target, addon.Name);
                if (Directory.Exists(to)) Directory.Delete(to, true);
                CopyFolder(addon.Source, to);
                record[addon.Name] = addon.Module;
                done.Add(addon.State == AddonState.Outdated ? L.F("{0} updated", addon.Name) : addon.State == AddonState.Current ? L.F("{0} copied again", addon.Name) : L.F("{0} installed", addon.Name));
            }
            foreach (var name in leftovers)
            {
                string folder = Path.Combine(target, name);
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
                record.Remove(name);
                done.Add(L.F("{0} removed (its module is no longer installed)", name));
            }
            SaveRecord(inst, record);
            if (clearCache)
            {
                string cache = Path.Combine(client, "Cache");
                if (Directory.Exists(cache)) { Directory.Delete(cache, true); done.Add(L.T("the client's cache cleared")); }
                else done.Add(L.T("the client had no cache to clear"));
            }
            return string.Join("\n", done);
        }

        /// <summary>A ZIP for other players: the add-ons in Interface\AddOns, to be unpacked into the game folder, and a note on what to do.</summary>
        public static void Export(string file, List<ClientAddon> addons, Install inst, string client, ClientPatch.Plan patch) { Export(file, addons, inst, client, patch, null); }

        public static void Export(string file, List<ClientAddon> addons, Install inst, string client, ClientPatch.Plan patch, Action<string, int, int> progress)
        {
            if (File.Exists(file)) File.Delete(file);
            bool withPatch = patch != null && !patch.Empty && client != null;
            using (var zip = ZipFile.Open(file, ZipArchiveMode.Create))
            {
                if (withPatch) ClientPatch.AddToZip(zip, inst, client, patch, progress);
                if (progress != null) progress(L.T("Adding the add-ons …"), 0, 0);
                foreach (var addon in addons)
                    foreach (var path in Directory.GetFiles(addon.Source, "*", SearchOption.AllDirectories))
                        zip.CreateEntryFromFile(path, "Interface/AddOns/" + addon.Name + "/" + path.Substring(addon.Source.Length).TrimStart('\\', '/').Replace('\\', '/'), CompressionLevel.Optimal);
                // In the language of the server owner, and in English below it for players who speak another one.
                var note = new StringBuilder(ReadMe(addons, patch, withPatch, false));
                if (L.Code != "en") note.AppendLine().AppendLine("----------------------------------------------------------------").AppendLine().Append(ReadMe(addons, patch, withPatch, true));
                var entry = zip.CreateEntry("READ ME FIRST.txt");
                using (var w = new StreamWriter(entry.Open(), new UTF8Encoding(false))) w.Write(note.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n"));
            }
        }

        /// <summary>The note in the ZIP: in the language shown, or in English.</summary>
        static string ReadMe(List<ClientAddon> addons, ClientPatch.Plan patch, bool withPatch, bool english)
        {
            Func<string, string> t = s => english ? s : L.T(s);
            Func<string, object[], string> f = (s, a) => english ? string.Format(s, a) : L.F(s, a);
            var note = new StringBuilder();
            note.AppendLine(f(L.N("Files for the game client, from the modules of this server ({0})."), new object[] { Product.Name }));
            note.AppendLine();
            note.AppendLine(t(L.N("1. Close the game and its launcher.")));
            note.AppendLine(t(L.N("2. Unpack this ZIP into your game folder (the folder with the \"Data\" folder).")));
            note.AppendLine("   " + t(L.N("Replace files that are already there.")) + (addons.Count > 0 ? " " + t(L.N("The add-ons land in Interface\\AddOns.")) : ""));
            if (withPatch) note.AppendLine("   " + f(L.N("Data\\{0} is the client patch of the modules."), new object[] { ClientPatch.ArchiveName }));
            var root = withPatch ? patch.RootFiles.Where(r => r.Zip).Select(r => r.To).ToList() : new List<string>();
            if (root.Count > 0)
            {
                note.AppendLine("   " + f(L.N("Next to Ascension.exe: {0}."), new object[] { string.Join(", ", root) }));
                if (root.Any(r => r.Equals("Ascension-b.exe", StringComparison.OrdinalIgnoreCase)))
                    note.AppendLine("   " + t(L.N("Start the game with Ascension-b.exe (the exe of CoA Custom), not with Ascension.exe: only it shows the races of CoA Custom and all their looks.")));
                if (root.Any(r => r.Equals("Ascension.exe", StringComparison.OrdinalIgnoreCase)))
                    note.AppendLine("   " + t(L.N("The ZIP brings its own Ascension.exe (the one of CoA Custom). Keep a copy of yours first, as Ascension.exe.old for example, if you want to go back.")));
                if (root.Any(r => r.Equals("EsteriaAppearance.dll", StringComparison.OrdinalIgnoreCase)))
                    note.AppendLine("   " + t(L.N("The game folder must be writable: EsteriaAppearance.dll keeps the looks of the races in Interface\\AddOns\\EsteriaAppearanceCache. A game under C:\\Program Files is better moved to another folder.")));
                if (root.Any(r => r.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
                    note.AppendLine("   " + t(L.N("If your antivirus program removes a .dll of the ZIP, allow it there.")));
            }
            if (addons.Any(a => a.ClearCache) || (withPatch && patch.ClearCache))
                note.AppendLine(t(L.N("3. Delete the folder \"Cache\" in your game folder, so the game forgets old values of items, creatures and quests.")));
            note.AppendLine();
            if (withPatch)
            {
                note.AppendLine(t(L.N("The client patch was built from the game client of the server owner. It fits the same version of the " +
                    "Conquest of Azeroth client; after the server owner updates it, get a new ZIP. It contains changed files " +
                    "of the game client: it is meant for the players of this server, not for passing on.")));
                foreach (var why in english ? patch.WhyEnglish : patch.Why) note.AppendLine("  " + why);
                note.AppendLine();
            }
            if (addons.Count > 0) note.AppendLine(t(L.N("Add-ons:")));
            foreach (var addon in addons)
            {
                string why = english ? addon.WhyEnglish : addon.Why;
                note.AppendLine("  " + addon.Name + "  " + f(L.N("(module {0})"), new object[] { addon.Module }) + (why.Length > 0 ? " - " + why : ""));
            }
            note.AppendLine();
            note.AppendLine(t(L.N("Do the same again after the server owner updates these modules.")));
            return note.ToString();
        }

        /// <summary>What is still to be done in the client, as one key (empty: nothing).</summary>
        public static string Pending(Install inst)
        {
            try
            {
                string client = ClientFolder;
                if (client != null && !IsClientFolder(client)) client = null;
                var addons = Find(inst, client);
                var left = Leftovers(inst, addons, client);
                var open = addons.Where(a => a.State != AddonState.Current).Select(a => a.Name + ":" + a.State).Concat(left.Select(l => l + ":left")).ToList();
                string problem;
                var patch = ClientPatch.StateOf(inst, client, ClientPatch.Collect(inst), out problem);
                if (patch != PatchState.None && patch != PatchState.Current) open.Add("client-patch:" + patch);
                return string.Join(",", open);
            }
            catch { return ""; }
        }
    }

    /// <summary>The add-ons of the modules and the game client they go into.</summary>
    class ClientDialog : AfkForm
    {
        readonly Install inst;
        readonly TextBox folder = Ui.Input(440);
        readonly ListView list = new ListView { View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, Dock = DockStyle.Fill, Font = Ui.Base, BorderStyle = BorderStyle.FixedSingle };
        readonly Toggle clearCache = new Toggle(L.T("Clear the game's cache (recommended: the game then forgets the old levels and values of items, creatures and quests)"));
        readonly Label state = new Label { AutoSize = true, Font = Ui.Small, ForeColor = Ui.Muted, MaximumSize = new Size(780, 0), Margin = new Padding(0, 4, 0, 0) };
        readonly Button install = Ui.Primary(L.T("Install add-ons"));
        readonly Button export = Ui.Secondary(L.T("Export as ZIP …"));
        readonly Button download = Ui.Secondary(L.T("Download / update client …"));
        List<ClientAddon> addons = new List<ClientAddon>();
        List<string> leftovers = new List<string>();
        ClientPatch.Plan patch = new ClientPatch.Plan();
        PatchState patchState = PatchState.None;
        string patchProblem;

        public ClientDialog(Install i, string reason = null)
        {
            inst = i;
            Text = Product.Name + " – " + L.T("Game client"); Font = Ui.Base; BackColor = Ui.Surface; ShowIcon = false;
            StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(840, 520); MinimumSize = new Size(720, 440);

            var top = Ui.Column(); top.Dock = DockStyle.Top; top.AutoSize = true; top.Padding = new Padding(16, 12, 16, 4);
            if (reason != null) top.Controls.Add(new Label { Text = reason, AutoSize = true, Font = Ui.Bold, ForeColor = Ui.AccentText, MaximumSize = new Size(800, 0), Margin = new Padding(0, 0, 0, 6) });
            top.Controls.Add(Ui.Para(L.F("Some modules bring an add-on for the game client, or change files of the client. {0} copies the add-ons into Interface\\AddOns " +
                "of your game and builds the client patch (Data\\{1}) from your own game client, keeps both up to date and takes them out again " +
                "when their module is removed. Other players can get everything as a ZIP.", Product.Name, ClientPatch.ArchiveName), 800));
            var row = Ui.Row();
            row.Controls.Add(Ui.FieldLabel(L.T("Game folder"), 110));
            row.Controls.Add(folder);
            var browse = Ui.Secondary(L.T("Browse …"));
            browse.Click += (s, e) =>
            {
                using (var d = new FolderBrowserDialog { Description = L.T("Choose your game folder (it contains the \"Data\" folder)") })
                {
                    string root = ClientAddons.GameRoot(folder.Text);
                    if (root != null) d.SelectedPath = root;
                    if (d.ShowDialog(this) == DialogResult.OK) { folder.Text = ClientAddons.GameRoot(d.SelectedPath) ?? d.SelectedPath; Refresh_(); }
                }
            };
            row.Controls.Add(browse);
            download.Margin = new Padding(6, 0, 0, 0);
            row.Controls.Add(download);
            top.Controls.Add(row);

            list.Columns.Add(L.T("Add-on or patch"), 190);
            list.Columns.Add(L.T("Module"), 170);
            list.Columns.Add(L.T("In your game"), 150);
            list.Columns.Add(L.T("What it does"), 300);
            var listPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 6, 16, 4) };
            listPanel.Controls.Add(list);

            var under = Ui.Column(); under.Dock = DockStyle.Bottom; under.AutoSize = true; under.Padding = new Padding(16, 0, 16, 6);
            clearCache.Wrap(790);
            under.Controls.Add(clearCache); under.Controls.Add(state);

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58, Padding = new Padding(14, 10, 14, 10), FlowDirection = FlowDirection.RightToLeft, BackColor = Ui.Panel, WrapContents = false };
            var close = Ui.Secondary(L.T("Close")); close.Click += (s, e) => Close();
            bottom.Controls.Add(close); bottom.Controls.Add(export); bottom.Controls.Add(install);

            Controls.Add(listPanel); Controls.Add(under); Controls.Add(bottom); Controls.Add(top);
            folder.Text = ClientAddons.ClientFolder ?? "";
            folder.Leave += (s, e) => { if (folder.Text.Trim() != checkedFolder && Visible) Refresh_(); };
            install.Click += (s, e) => Install_();
            export.Click += (s, e) => Export_();
            download.Click += (s, e) => Download_();
            Shown += (s, e) => { Refresh_(); CheckDownloadUpdate(); };
        }

        /// <summary>The button says when a new client manifest version is available. The client itself is not
        /// changed here; Download_ plans and copies only the files that differ.</summary>
        void CheckDownloadUpdate()
        {
            string client = Client;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var manifest = ClientDownload.Latest();
                    string installed = client == null ? null : ClientDownload.InstalledVersion(client);
                    BeginInvoke((Action)(() =>
                    {
                        if (IsDisposed) return;
                        download.Text = installed != null && installed != manifest.Version
                            ? L.F("Update client to {0} …", manifest.Version)
                            : client != null ? L.T("Update / repair client …") : L.F("Download client {0} …", manifest.Version);
                    }));
                }
                catch { }
            });
        }

        string checkedFolder;

        string Client { get { return ClientAddons.GameRoot(folder.Text); } }

        void Refresh_()
        {
            string client = Client;
            checkedFolder = folder.Text.Trim();
            string readError = null;
            // reading the client and building the patch to compare takes a while with races of the race folder
            Busy.Run(this, L.T("Checking what your game needs …"), report =>
            {
                try { addons = ClientAddons.Find(inst, client); leftovers = ClientAddons.Leftovers(inst, addons, client); }
                catch (Exception ex) { addons = new List<ClientAddon>(); leftovers = new List<string>(); readError = L.T("The modules could not be read:") + " " + ex.Message; }
                patchProblem = null;
                try { patch = ClientPatch.Collect(inst); patchState = ClientPatch.StateOf(inst, client, patch, out patchProblem); }
                catch (Exception ex) { patch = new ClientPatch.Plan(); patchState = PatchState.None; patchProblem = ex.Message; }
            });
            if (readError != null) state.Text = readError;
            list.BeginUpdate(); list.Items.Clear();
            foreach (var a in addons)
            {
                var it = new ListViewItem(a.Name) { Tag = a };
                it.SubItems.Add(a.Module);
                it.SubItems.Add(client == null ? "-" : a.State == AddonState.Current ? L.T("up to date") : a.State == AddonState.Outdated ? L.T("older version") : L.T("not installed"));
                it.SubItems.Add(a.Why);
                it.ForeColor = client == null || a.State == AddonState.Current ? Ui.Text : Ui.Warn;
                list.Items.Add(it);
            }
            if (patchState != PatchState.None)
            {
                var it = new ListViewItem(L.F("Client patch ({0})", ClientPatch.ArchiveName));
                it.SubItems.Add(patch.Empty ? L.T("(removed)") : string.Join(", ", patch.Modules));
                it.SubItems.Add(client == null ? "-" : patchState == PatchState.Current ? L.T("up to date") : patchState == PatchState.Outdated ? L.T("to be built again") :
                    patchState == PatchState.Leftover ? L.T("will be taken out") : L.T("not installed"));
                it.SubItems.Add(patchProblem ?? (patch.Empty ? L.T("No module needs it any more.") : string.Join(" ", patch.Why)));
                it.ForeColor = client == null || patchState == PatchState.Current ? Ui.Text : Ui.Warn;
                list.Items.Add(it);
            }
            foreach (var name in leftovers)
            {
                var it = new ListViewItem(name) { ForeColor = Ui.Warn };
                it.SubItems.Add(L.T("(removed)")); it.SubItems.Add(L.T("will be taken out")); it.SubItems.Add(L.T("Its module is no longer installed."));
                list.Items.Add(it);
            }
            list.EndUpdate();
            bool patchDue = patchState == PatchState.Missing || patchState == PatchState.Outdated || patchState == PatchState.Leftover;
            bool due = addons.Any(a => a.State != AddonState.Current) || leftovers.Count > 0 || patchDue;
            bool anything = addons.Count > 0 || leftovers.Count > 0 || patchState != PatchState.None;
            clearCache.Visible = addons.Any(a => a.ClearCache) || patch.ClearCache;
            clearCache.Checked = clearCache.Visible && (addons.Any(a => a.ClearCache && a.State != AddonState.Current) || (patch.ClearCache && patchDue));
            export.Enabled = addons.Count > 0 || !patch.Empty;
            install.Enabled = client != null && anything;
            install.Text = due || !anything ? L.T("Install") : L.T("Install again");
            if (!anything) state.Text = L.T("None of the installed modules brings an add-on or a client patch for the game client.");
            else if (folder.Text.Trim().Length == 0) state.Text = L.T("Choose your game folder, the one with the \"Data\" folder.");
            else if (client == null) state.Text = L.T("This is not a game folder: it needs a \"Data\" folder with the game's .MPQ archives.") + " " + L.T("You can also choose the \"Data\" folder itself.");
            else if (patchProblem != null) state.Text = L.T("The client patch cannot be built:") + " " + patchProblem;
            else state.Text = due ? L.T("Not everything is in your game yet. Close the game and its launcher, then choose \"Install\".") : L.T("Your game has everything the installed modules bring.");
            // Haranir and Esteria Vulpera load many files from paths below the client; Windows' 260-character
            // limit starts to bite when the game folder itself is deep. A short path is the reliable fix.
            string longPath = client != null && client.Length > 100
                ? L.F("This game folder path is long ({0} characters). Ascension.exe is not long-path aware, so custom textures may be missing; move the client to a short path such as C:\\CoA.", client.Length)
                : null;
            if (longPath != null) state.Text = longPath + " " + state.Text;
            state.ForeColor = client != null && (due || longPath != null) ? Ui.Warn : Ui.Muted;
        }

        void Download_()
        {
            string target = Client;
            if (target == null && folder.Text.Trim().Length > 0 && Directory.Exists(folder.Text.Trim()))
                target = folder.Text.Trim();
            if (target == null)
            {
                string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "CoA Client");
                try { Directory.CreateDirectory(fallback); } catch { fallback = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); }
                using (var d = new FolderBrowserDialog { Description = L.T("Choose the folder for the game client. It can be empty; about 43 GB will be downloaded into it."), SelectedPath = fallback })
                {
                    if (d.ShowDialog(this) != DialogResult.OK) return;
                    target = ClientAddons.GameRoot(d.SelectedPath) ?? d.SelectedPath;
                }
                bool hasFiles = Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any();
                if (hasFiles && !ClientAddons.IsClientFolder(target) &&
                    !Ui.Confirm(this, L.F("The folder {0} already contains files and is not a game folder. Download the client into it anyway?", target))) return;
            }

            if (ClientAddons.ClientRunning(target))
            {
                Ui.Error(this, L.T("The game or its launcher is running from this folder. Close it before downloading or updating the client."));
                return;
            }

            ClientDownload.Manifest manifest;
            try { manifest = Busy.Run(this, L.T("Reading the client list ..."), report => ClientDownload.FetchManifest()); }
            catch (Exception ex) { Ui.Error(this, L.T("The client list could not be read:") + "\n" + ex.Message); return; }

            long free = ClientDownload.FreeSpace(target);
            if (free > 0 && free < manifest.TotalBytes + 1024L * 1024 * 1024 && !ClientAddons.IsClientFolder(target))
            {
                Ui.Error(this, L.F("The game client needs {0}. The drive only has {1} free.", ClientDownload.FormatBytes(manifest.TotalBytes), ClientDownload.FormatBytes(free)));
                return;
            }

            string text = L.F("Download or update the game client?\n\nVersion: {0}\nFiles: {1}\nFull size: {2}\n\n" +
                "The files come from the public CoA development launcher. The client data belongs to its owners; only use it as allowed. " +
                "Your WTF, Cache, add-ons and realmlist are kept, and game files you changed are backed up as .afk-backup.",
                manifest.Version, manifest.Files.Count, ClientDownload.FormatBytes(manifest.TotalBytes));
            if (!Ui.Confirm(this, text)) return;

            using (var d = new ClientDownloadDialog(inst, target, manifest))
            {
                var answer = d.ShowDialog(this);
                if (answer == DialogResult.OK && d.Result != null)
                {
                    ClientAddons.ClientFolder = target;
                    folder.Text = target;
                    Refresh_();
                    MessageBox.Show(this, L.F("The game client {0} is ready.\n\n{1} files downloaded, {2} already present, {3} kept as yours.\n\nInstall the add-ons and the client patch next.",
                        d.Result.Version, d.Result.Downloaded, d.Result.AlreadyPresent, d.Result.Kept),
                        Product.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else if (answer == DialogResult.Cancel)
                {
                    Ui.Error(this, L.T("The download was stopped. Everything already downloaded is kept; run it again to continue."));
                }
                else if (d.Failure != null) Ui.Error(this, L.T("The client download failed:") + "\n" + d.Failure);
            }
        }

        void Install_()
        {
            string client = Client;
            if (client == null) { Refresh_(); return; }
            if (ClientAddons.ClientRunning(client))
            {
                Ui.Error(this, L.T("The game or its launcher is running from this folder. Please close it first: add-ons are only loaded at the start, " +
                    "and a running game writes its old cache back when it closes."));
                return;
            }
            try
            {
                bool clear = clearCache.Visible && clearCache.Checked;
                string done = Busy.Run(this, L.T("Installing into your game …"), report =>
                {
                    report(L.T("Copying the add-ons …"));
                    string result = ClientAddons.Install(inst, client, addons, leftovers, false);
                    try
                    {
                        string patched = ClientPatch.Install(inst, client, patch, (t, a, b) => report(t, a, b));
                        if (patched.Length > 0) result = (result + "\n" + patched).Trim();
                    }
                    finally
                    {
                        string cache = System.IO.Path.Combine(client, "Cache");
                        if (clear && Directory.Exists(cache)) { report(L.T("Clearing the game's cache …")); Directory.Delete(cache, true); result += "\n" + L.T("the client's cache cleared"); }
                    }
                    return result;
                });
                ClientAddons.ClientFolder = client;
                Refresh_();
                MessageBox.Show(this, L.T("Done:") + "\n\n" + done, Product.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { Ui.Error(this, L.T("Not everything could be installed:") + "\n" + ex.Message); Refresh_(); }
        }

        void Export_()
        {
            if (addons.Count == 0 && patch.Empty) return;
            if (!patch.Empty && Client == null)
            {
                Ui.Error(this, L.T("Choose your game folder first: the client patch is built from your own game client."));
                return;
            }
            using (var d = new SaveFileDialog { Filter = L.T("ZIP archive") + " (*.zip)|*.zip", FileName = "Game client files for " + Product.Name + " server.zip", Title = L.T("Save the files for the game client as ZIP") })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    string file = d.FileName, client = Client;
                    Busy.Run(this, L.T("Making the ZIP for the other players …"), report => ClientAddons.Export(file, addons, inst, client, patch, (t, a, b) => report(t, a, b)));
                    MessageBox.Show(this, L.T("Saved:") + " " + d.FileName + "\n\n" + L.T("Give it to the other players: they unpack it into their game folder (it says how inside)."),
                        Product.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) { Ui.Error(this, L.T("The ZIP could not be saved:") + "\n" + ex.Message); }
            }
        }
    }
}
