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
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace CoAInstaller
{
    enum AddonState { Current, Missing, Outdated }

    /// <summary>An add-on for the game client that a module brings (the "client" part of its afk-realm.json).</summary>
    class ClientAddon
    {
        public string Module = "", Name = "", Source = "", Why = "";
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
            try
            {
                string data = Path.Combine(dir ?? "", "Data");
                return !string.IsNullOrEmpty(dir) && Directory.Exists(data) && Directory.EnumerateFiles(data, "*.mpq").Any();
            }
            catch { return false; }
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
                string why = clientPart.TryGetValue("why", out v) ? Convert.ToString(v) : "";
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
                    var addon = new ClientAddon { Module = Path.GetFileName(dir), Name = name, Source = source, ClearCache = clear, Why = why };
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
                done.Add(addon.Name + " " + (addon.State == AddonState.Outdated ? "updated" : addon.State == AddonState.Current ? "copied again" : "installed"));
            }
            foreach (var name in leftovers)
            {
                string folder = Path.Combine(target, name);
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
                record.Remove(name);
                done.Add(name + " removed (its module is no longer installed)");
            }
            SaveRecord(inst, record);
            if (clearCache)
            {
                string cache = Path.Combine(client, "Cache");
                if (Directory.Exists(cache)) { Directory.Delete(cache, true); done.Add("the client's cache cleared"); }
                else done.Add("the client had no cache to clear");
            }
            return string.Join("\n", done);
        }

        /// <summary>A ZIP for other players: the add-ons in Interface\AddOns, to be unpacked into the game folder, and a note on what to do.</summary>
        public static void Export(string file, List<ClientAddon> addons)
        {
            if (File.Exists(file)) File.Delete(file);
            using (var zip = ZipFile.Open(file, ZipArchiveMode.Create))
            {
                foreach (var addon in addons)
                    foreach (var path in Directory.GetFiles(addon.Source, "*", SearchOption.AllDirectories))
                        zip.CreateEntryFromFile(path, "Interface/AddOns/" + addon.Name + "/" + path.Substring(addon.Source.Length).TrimStart('\\', '/').Replace('\\', '/'), CompressionLevel.Optimal);
                var note = new StringBuilder();
                note.AppendLine("Add-ons for the game client, from the modules of this server (" + Product.Name + ").");
                note.AppendLine();
                note.AppendLine("1. Close the game and its launcher.");
                note.AppendLine("2. Unpack this ZIP into your game folder (the folder with the \"Data\" folder),");
                note.AppendLine("   so that the add-ons land in Interface\\AddOns. Replace files that are already there.");
                if (addons.Any(a => a.ClearCache))
                    note.AppendLine("3. Delete the folder \"Cache\" in your game folder, so the game forgets old values of items, creatures and quests.");
                note.AppendLine();
                note.AppendLine("Add-ons:");
                foreach (var addon in addons) note.AppendLine("  " + addon.Name + "  (module " + addon.Module + ")" + (addon.Why.Length > 0 ? " - " + addon.Why : ""));
                note.AppendLine();
                note.AppendLine("Do the same again after the server owner updates these modules.");
                var entry = zip.CreateEntry("READ ME FIRST.txt");
                using (var w = new StreamWriter(entry.Open(), new UTF8Encoding(false))) w.Write(note.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n"));
            }
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
        readonly Toggle clearCache = new Toggle("Clear the game's cache (recommended: the game then forgets the old levels and values of items, creatures and quests)");
        readonly Label state = new Label { AutoSize = true, Font = Ui.Small, ForeColor = Ui.Muted, MaximumSize = new Size(780, 0), Margin = new Padding(0, 4, 0, 0) };
        readonly Button install = Ui.Primary("Install add-ons");
        readonly Button export = Ui.Secondary("Export as ZIP …");
        List<ClientAddon> addons = new List<ClientAddon>();
        List<string> leftovers = new List<string>();

        public ClientDialog(Install i, string reason = null)
        {
            inst = i;
            Text = Product.Name + " – Game client"; Font = Ui.Base; BackColor = Color.White; ShowIcon = false;
            StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(840, 520); MinimumSize = new Size(720, 440);

            var top = Ui.Column(); top.Dock = DockStyle.Top; top.AutoSize = true; top.Padding = new Padding(16, 12, 16, 4);
            if (reason != null) top.Controls.Add(new Label { Text = reason, AutoSize = true, Font = Ui.Bold, ForeColor = Ui.Accent, MaximumSize = new Size(800, 0), Margin = new Padding(0, 0, 0, 6) });
            top.Controls.Add(Ui.Para("Some modules bring an add-on for the game client. " + Product.Name + " copies them into Interface\\AddOns of your game, keeps them up to date " +
                "and takes them out again when their module is removed. Other players can get them as a ZIP.", 800));
            var row = Ui.Row();
            row.Controls.Add(Ui.FieldLabel("Game folder", 110));
            row.Controls.Add(folder);
            var browse = Ui.Secondary("Browse …");
            browse.Click += (s, e) =>
            {
                using (var d = new FolderBrowserDialog { Description = "Choose your game folder (it contains the \"Data\" folder)" })
                {
                    if (IsFolder(folder.Text)) d.SelectedPath = folder.Text.Trim();
                    if (d.ShowDialog(this) == DialogResult.OK) { folder.Text = d.SelectedPath; Refresh_(); }
                }
            };
            row.Controls.Add(browse);
            top.Controls.Add(row);

            list.Columns.Add("Add-on", 170);
            list.Columns.Add("Module", 190);
            list.Columns.Add("In your game", 150);
            list.Columns.Add("What it does", 300);
            var listPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 6, 16, 4) };
            listPanel.Controls.Add(list);

            var under = Ui.Column(); under.Dock = DockStyle.Bottom; under.AutoSize = true; under.Padding = new Padding(16, 0, 16, 6);
            clearCache.Wrap(790);
            under.Controls.Add(clearCache); under.Controls.Add(state);

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58, Padding = new Padding(14, 10, 14, 10), FlowDirection = FlowDirection.RightToLeft, BackColor = Ui.Panel, WrapContents = false };
            var close = Ui.Secondary("Close"); close.Click += (s, e) => Close();
            bottom.Controls.Add(close); bottom.Controls.Add(export); bottom.Controls.Add(install);

            Controls.Add(listPanel); Controls.Add(under); Controls.Add(bottom); Controls.Add(top);
            folder.Text = ClientAddons.ClientFolder ?? "";
            folder.Leave += (s, e) => Refresh_();
            install.Click += (s, e) => Install_();
            export.Click += (s, e) => Export_();
            Shown += (s, e) => Refresh_();
        }

        static bool IsFolder(string dir) { return ClientAddons.IsClientFolder((dir ?? "").Trim().TrimEnd('\\')); }
        string Client { get { var d = folder.Text.Trim().TrimEnd('\\'); return ClientAddons.IsClientFolder(d) ? d : null; } }

        void Refresh_()
        {
            string client = Client;
            try { addons = ClientAddons.Find(inst, client); leftovers = ClientAddons.Leftovers(inst, addons, client); }
            catch (Exception ex) { addons = new List<ClientAddon>(); leftovers = new List<string>(); state.Text = "The modules could not be read: " + ex.Message; }
            list.BeginUpdate(); list.Items.Clear();
            foreach (var a in addons)
            {
                var it = new ListViewItem(a.Name) { Tag = a };
                it.SubItems.Add(a.Module);
                it.SubItems.Add(client == null ? "-" : a.State == AddonState.Current ? "up to date" : a.State == AddonState.Outdated ? "older version" : "not installed");
                it.SubItems.Add(a.Why);
                it.ForeColor = client == null || a.State == AddonState.Current ? Ui.Text : Ui.Warn;
                list.Items.Add(it);
            }
            foreach (var name in leftovers)
            {
                var it = new ListViewItem(name) { ForeColor = Ui.Warn };
                it.SubItems.Add("(removed)"); it.SubItems.Add("will be taken out"); it.SubItems.Add("Its module is no longer installed.");
                list.Items.Add(it);
            }
            list.EndUpdate();
            bool due = addons.Any(a => a.State != AddonState.Current) || leftovers.Count > 0;
            clearCache.Visible = addons.Any(a => a.ClearCache);
            clearCache.Checked = clearCache.Visible && addons.Any(a => a.ClearCache && a.State != AddonState.Current);
            export.Enabled = addons.Count > 0;
            install.Enabled = client != null && (addons.Count > 0 || leftovers.Count > 0);
            install.Text = due || addons.Count == 0 ? "Install add-ons" : "Install again";
            if (addons.Count == 0 && leftovers.Count == 0) state.Text = "None of the installed modules brings an add-on for the game client.";
            else if (folder.Text.Trim().Length == 0) state.Text = "Choose your game folder, the one with the \"Data\" folder.";
            else if (client == null) state.Text = "This is not a game folder: it needs a \"Data\" folder with the game's .MPQ archives.";
            else state.Text = due ? "Not everything is in your game yet. Close the game and its launcher, then choose \"Install add-ons\"." : "Your game has every add-on of the installed modules.";
            state.ForeColor = client != null && due ? Ui.Warn : Ui.Muted;
        }

        void Install_()
        {
            string client = Client;
            if (client == null) { Refresh_(); return; }
            if (ClientAddons.ClientRunning(client))
            {
                Ui.Error(this, "The game or its launcher is running from this folder. Please close it first: add-ons are only loaded at the start, " +
                    "and a running game writes its old cache back when it closes.");
                return;
            }
            try
            {
                string done = ClientAddons.Install(inst, client, addons, leftovers, clearCache.Visible && clearCache.Checked);
                ClientAddons.ClientFolder = client;
                Refresh_();
                MessageBox.Show(this, "Done:\n\n" + done, Product.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { Ui.Error(this, "The add-ons could not be installed:\n" + ex.Message); Refresh_(); }
        }

        void Export_()
        {
            if (addons.Count == 0) return;
            using (var d = new SaveFileDialog { Filter = "ZIP archive (*.zip)|*.zip", FileName = "Add-ons for " + Product.Name + " server.zip", Title = "Save the add-ons as ZIP" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    ClientAddons.Export(d.FileName, addons);
                    MessageBox.Show(this, "Saved: " + d.FileName + "\n\nGive it to the other players: they unpack it into their game folder (it says how inside).",
                        Product.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) { Ui.Error(this, "The ZIP could not be saved:\n" + ex.Message); }
            }
        }
    }
}
