using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CoAInstaller
{
    /// <summary>
    /// Module manager: the AzerothCore module catalog with the installed modules ticked.
    /// Ticking a module installs it, unticking removes it; "Apply changes" hands the choice
    /// to the engine (backup, download, rebuild, record or undo the database changes).
    /// </summary>
    class ModulesDialog : AfkForm
    {
        /// <summary>Git addresses to install and folder names to remove, set when the user applies.</summary>
        public readonly List<string> Add = new List<string>(), Remove = new List<string>();

        readonly Install inst;
        readonly TextBox search = new TextBox { Font = Ui.Base, Width = 260, Margin = new Padding(0, 3, 10, 3) };
        readonly CheckBox hideOld = new CheckBox { Text = "Hide modules not changed for 2 years", AutoSize = true, Checked = true, Font = Ui.Base, Margin = new Padding(0, 6, 10, 0) };
        readonly CheckBox onlyInstalled = new CheckBox { Text = "Installed only", AutoSize = true, Font = Ui.Base, Margin = new Padding(0, 6, 10, 0) };
        readonly ListView list = new ListView { View = View.Details, CheckBoxes = true, FullRowSelect = true, HideSelection = false, MultiSelect = false, Dock = DockStyle.Fill, Font = Ui.Base, BorderStyle = BorderStyle.FixedSingle };
        readonly Label status = new Label { AutoSize = true, Font = Ui.Small, ForeColor = Ui.Muted, Margin = new Padding(0, 8, 0, 0) };
        readonly Label detailName = new Label { AutoSize = true, Font = Ui.H2, ForeColor = Ui.Text, MaximumSize = new Size(330, 0), Margin = new Padding(0, 0, 0, 2) };
        readonly Label detailText = new Label { AutoSize = true, Font = Ui.Base, ForeColor = Ui.Text, MaximumSize = new Size(330, 0), Margin = new Padding(0, 2, 0, 6) };
        readonly LinkLabel detailLink = new LinkLabel { AutoSize = true, Font = Ui.Small, Text = "Open on GitHub", Margin = new Padding(0, 0, 0, 8) };
        readonly FlowLayoutPanel checks = Ui.Column();
        readonly Button readme = Ui.Secondary("Show README");
        readonly Button apply = Ui.Primary("Apply changes …");
        readonly TextBox customUrl = new TextBox { Font = Ui.Base, Width = 380, Margin = new Padding(0, 3, 8, 3) };

        // The modules made for AFK Realm stand above the catalog, in a section of their own.
        readonly FlowLayoutPanel ownPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(0, 0, 0, 2) };
        readonly Dictionary<ModuleEntry, Label> ownStatus = new Dictionary<ModuleEntry, Label>();
        ModuleEntry ownSelected;
        List<ModuleEntry> all = new List<ModuleEntry>();
        readonly HashSet<ModuleEntry> ticked = new HashSet<ModuleEntry>();
        bool filling;

        public ModulesDialog(Install i)
        {
            inst = i;
            Text = Product.Name + " – Modules"; Font = Ui.Base; BackColor = Color.White; ShowIcon = false;
            StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(1080, 660); MinimumSize = new Size(900, 520);

            var top = Ui.Column(); top.Dock = DockStyle.Top; top.AutoSize = true; top.Padding = new Padding(16, 12, 16, 4);
            top.Controls.Add(Ui.Para("Tick a module to install it, untick it to remove it, then choose \"Apply changes\". " + Product.Name +
                " backs up the server, downloads the modules and rebuilds the server. Database changes of modules installed here are recorded, " +
                "so removing a module also undoes them. \"Modules by " + Product.Name + "\" are made for this server; the rest of the list comes from the AzerothCore module catalog, and not every module there works with CoA.", 1040));
            var filter = Ui.Row();
            filter.Controls.Add(new Label { Text = "Search", AutoSize = true, Font = Ui.Base, Margin = new Padding(0, 6, 6, 0) });
            filter.Controls.Add(search); filter.Controls.Add(hideOld); filter.Controls.Add(onlyInstalled);
            var reload = Ui.Secondary("Reload list"); reload.Click += (s, e) => LoadList(true);
            filter.Controls.Add(reload);
            top.Controls.Add(filter);

            list.Columns.Add("Module", 205);
            list.Columns.Add("Description", 240);
            list.Columns.Add("★", 40, HorizontalAlignment.Right);
            list.Columns.Add("Last change", 90);
            list.Columns.Add("Status", 115);
            var listPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 4, 8, 4) };
            listPanel.Controls.Add(list);
            listPanel.Controls.Add(ownPanel);

            var details = Ui.Column(); details.Padding = new Padding(4, 4, 8, 4);
            details.Controls.Add(detailName); details.Controls.Add(detailText); details.Controls.Add(detailLink);
            details.Controls.Add(checks); details.Controls.Add(readme);
            var detailPanel = new Panel { Dock = DockStyle.Right, Width = 360, AutoScroll = true, Padding = new Padding(4, 4, 8, 4) };
            detailPanel.Controls.Add(details);
            readme.Visible = false; detailLink.Visible = false;

            var urlRow = Ui.Row(); urlRow.Dock = DockStyle.Bottom; urlRow.Padding = new Padding(16, 2, 16, 4); urlRow.AutoSize = true;
            urlRow.Controls.Add(new Label { Text = "Not in the list? Git address:", AutoSize = true, Font = Ui.Base, Margin = new Padding(0, 6, 6, 0) });
            urlRow.Controls.Add(customUrl);
            var addUrl = Ui.Secondary("Add"); addUrl.Click += (s, e) => AddCustom();
            urlRow.Controls.Add(addUrl);
            urlRow.Controls.Add(status);

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58, Padding = new Padding(14, 10, 14, 10), FlowDirection = FlowDirection.RightToLeft, BackColor = Ui.Panel, WrapContents = false };
            var close = Ui.Secondary("Close"); close.Click += (s, e) => Close();
            bottom.Controls.Add(close); bottom.Controls.Add(apply);

            Controls.Add(listPanel);
            Controls.Add(detailPanel);
            Controls.Add(urlRow);
            Controls.Add(bottom);
            Controls.Add(top);

            search.TextChanged += (s, e) => Fill();
            hideOld.CheckedChanged += (s, e) => Fill();
            onlyInstalled.CheckedChanged += (s, e) => Fill();
            list.ItemCheck += OnItemCheck;
            list.SelectedIndexChanged += (s, e) => { if (list.SelectedItems.Count > 0) SelectOwn(null); ShowDetails(); };
            detailLink.LinkClicked += (s, e) => { var m = Selected; if (m != null && m.Url.StartsWith("http")) Process.Start(m.Url); };
            readme.Click += (s, e) => ShowReadme();
            apply.Click += (s, e) => Apply();
            apply.Enabled = false;
            Shown += (s, e) => LoadList(false);
        }

        ModuleEntry Selected { get { return ownSelected ?? (list.SelectedItems.Count == 1 ? list.SelectedItems[0].Tag as ModuleEntry : null); } }

        /// <summary>Marks one of the modules by AFK Realm as the one the details are shown for (null: none).</summary>
        void SelectOwn(ModuleEntry m)
        {
            ownSelected = m;
            foreach (Control row in ownPanel.Controls) if (row.Tag is ModuleEntry) row.BackColor = row.Tag == m ? Ui.Panel : Color.White;
        }

        /// <summary>The section "Modules by AFK Realm": one row per module, with a tick like the list below.</summary>
        void FillOwn()
        {
            ownPanel.SuspendLayout();
            foreach (var old in ownPanel.Controls.Cast<Control>().ToList()) { ownPanel.Controls.Remove(old); old.Dispose(); }
            ownStatus.Clear();
            var mine = all.Where(m => m.Own).OrderBy(m => m.Name).ToList();
            if (ownSelected != null && !mine.Contains(ownSelected)) ownSelected = null;
            if (mine.Count > 0)
            {
                ownPanel.Controls.Add(new Label { Text = "Modules by " + Product.Name, AutoSize = true, Font = Ui.H2, ForeColor = Ui.Accent, Margin = new Padding(0, 0, 0, 2) });
                foreach (var m in mine)
                {
                    var entry = m;
                    var row = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Tag = entry, Margin = new Padding(0, 0, 0, 2), Padding = new Padding(2, 2, 6, 2),
                        BackColor = entry == ownSelected ? Ui.Panel : Color.White };
                    var tick = new CheckBox { Text = entry.Name, AutoSize = true, Font = Ui.Bold, Checked = ticked.Contains(entry), Enabled = entry.State != ModuleState.PartOfCoA, Margin = new Padding(2, 1, 10, 0) };
                    var text = new Label { Text = entry.Description, AutoSize = true, Font = Ui.Base, ForeColor = Ui.Text, Margin = new Padding(0, 4, 10, 0), MaximumSize = new Size(360, 0) };
                    var state = new Label { Text = StatusText(entry), AutoSize = true, Font = Ui.Small, ForeColor = Ui.Muted, Margin = new Padding(0, 5, 0, 0) };
                    ownStatus[entry] = state;
                    tick.CheckedChanged += (s, e) =>
                    {
                        if (filling) return;
                        var clash = tick.Checked && !entry.Installed ? Clash(entry) : null;
                        if (clash != null)
                        {
                            filling = true; tick.Checked = false; filling = false;
                            Ui.Error(this, ClashText(entry, clash));
                            return;
                        }
                        if (tick.Checked) ticked.Add(entry); else ticked.Remove(entry);
                        state.Text = StatusText(entry);
                        Pick(entry);
                        UpdateApply();
                    };
                    EventHandler pick = (s, e) => Pick(entry);
                    row.Click += pick; text.Click += pick; state.Click += pick;
                    row.Controls.Add(tick); row.Controls.Add(text); row.Controls.Add(state);
                    ownPanel.Controls.Add(row);
                }
                ownPanel.Controls.Add(new Label { Text = "AzerothCore module catalog", AutoSize = true, Font = Ui.H2, ForeColor = Ui.Text, Margin = new Padding(0, 8, 0, 2) });
            }
            ownPanel.ResumeLayout();
        }

        void Pick(ModuleEntry m)
        {
            list.SelectedItems.Clear();
            SelectOwn(m);
            ShowDetails();
        }

        void LoadList(bool refresh)
        {
            status.Text = "Loading the module list from GitHub …";
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                List<ModuleEntry> catalog = null, merged = new List<ModuleEntry>(); string warning = null, error = null;
                try { catalog = ModuleCatalog.LoadCatalog(inst, refresh, out warning); }
                catch (Exception ex) { error = ex.Message; }
                try { merged = ModuleCatalog.Merge(inst, catalog); }
                catch (Exception ex) { error = (error != null ? error + " " : "") + "The installed modules could not be read: " + ex.Message; }
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        all = merged;
                        ticked.Clear();
                        foreach (var m in all.Where(m => m.Installed || m.State == ModuleState.PartOfCoA)) ticked.Add(m);
                        ownSelected = null;
                        FillOwn();
                        Fill();
                        ShowDetails();
                        status.Text = error ?? warning ?? (all.Count(m => m.InCatalog) + " modules in the catalog.");
                        status.ForeColor = error != null || warning != null ? Ui.Warn : Ui.Muted;
                    }));
                }
                catch { }
            });
        }

        void Fill()
        {
            filling = true;
            list.BeginUpdate();
            list.Items.Clear();
            string q = search.Text.Trim();
            var shown = all.Where(m => !m.Own &&
                    (!hideOld.Checked || m.Installed || ticked.Contains(m) || m.Pushed == DateTime.MinValue || m.Pushed > DateTime.UtcNow.AddYears(-2)) &&
                    (!onlyInstalled.Checked || m.Installed) &&
                    (q.Length == 0 || m.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || m.Description.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                     m.FullName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0))
                .OrderByDescending(m => m.Installed).ThenByDescending(m => ticked.Contains(m)).ThenByDescending(m => m.Stars).ThenBy(m => m.Name);
            foreach (var m in shown)
            {
                var it = new ListViewItem(m.Name) { Tag = m, Checked = ticked.Contains(m) };
                it.SubItems.Add(m.Description);
                it.SubItems.Add(m.InCatalog && m.Stars > 0 ? m.Stars.ToString() : "");
                it.SubItems.Add(m.Pushed > DateTime.MinValue ? m.Pushed.ToLocalTime().ToString("yyyy-MM-dd") : "");
                it.SubItems.Add(StatusText(m));
                if (m.State == ModuleState.PartOfCoA) it.ForeColor = Ui.Muted;
                else if (m.Status == "sql-failed" || m.Status == "missing") it.ForeColor = Ui.Bad;
                list.Items.Add(it);
            }
            list.EndUpdate();
            filling = false;
            UpdateApply();
        }

        string StatusText(ModuleEntry m)
        {
            bool tick = ticked.Contains(m);
            switch (m.State)
            {
                case ModuleState.PartOfCoA: return m.IsPlayerbots ? "included" : "part of CoA";
                case ModuleState.Managed:
                    if (!tick) return "will be removed";
                    return m.Status == "sql-failed" ? "database step failed" : m.Status == "missing" ? "folder missing" : "installed";
                case ModuleState.ByHand: return tick ? "added by hand" : "will be removed";
                default: return tick ? "will be installed" : "";
            }
        }

        void OnItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (filling) return;
            var m = list.Items[e.Index].Tag as ModuleEntry;
            if (m == null) return;
            if (m.State == ModuleState.PartOfCoA) { e.NewValue = CheckState.Checked; return; }
            bool on = e.NewValue == CheckState.Checked;
            if (on && !m.Installed)
            {
                var clash = Clash(m);
                if (clash != null)
                {
                    e.NewValue = CheckState.Unchecked;
                    BeginInvoke((Action)(() => Ui.Error(this, ClashText(m, clash))));
                    return;
                }
            }
            if (on) ticked.Add(m); else ticked.Remove(m);
            list.Items[e.Index].SubItems[4].Text = StatusText(m);
            BeginInvoke((Action)UpdateApply);
        }

        /// <summary>Another chosen or installed module with the same folder name, or null.</summary>
        ModuleEntry Clash(ModuleEntry m)
        {
            return all.FirstOrDefault(o => o != m && ticked.Contains(o) && o.Name.Equals(m.Name, StringComparison.OrdinalIgnoreCase));
        }

        static string ClashText(ModuleEntry m, ModuleEntry clash)
        {
            return "Another module named \"" + m.Name + "\" (" + (clash.FullName.Length > 0 ? clash.FullName : clash.CloneUrl) + ") is already chosen. Only one module of the same name can be installed.";
        }

        void UpdateApply()
        {
            int n = all.Count(m => m.State != ModuleState.PartOfCoA && m.Installed != ticked.Contains(m));
            apply.Enabled = n > 0;
            apply.Text = n > 0 ? "Apply " + n + (n == 1 ? " change …" : " changes …") : "Apply changes …";
        }

        void ShowDetails()
        {
            var m = Selected;
            checks.Controls.Clear();
            if (m == null) { detailName.Text = detailText.Text = ""; readme.Visible = detailLink.Visible = false; return; }
            detailName.Text = m.Name;
            detailText.Text = (m.Description.Length > 0 ? m.Description : "") + (m.FullName.Length > 0 ? "\n" + m.FullName : "");
            detailLink.Visible = m.Url.StartsWith("http");
            readme.Visible = false;
            if (m.IsPlayerbots)
            {
                AddCheck(0, "Playerbots is already included: " + Product.Name + " always builds the CoA version (Zyth45/mod-playerbots, branch coa) and keeps it up to date with every server update.");
                if (m.FullName.Length > 0 && !m.FullName.Equals("Zyth45/mod-playerbots", StringComparison.OrdinalIgnoreCase))
                    AddCheck(-1, "This entry is a different version (" + m.FullName + ") that does not fit CoA. Nothing needs to be installed here.");
                return;
            }
            if (m.State == ModuleState.PartOfCoA) { AddCheck(0, "This module is already part of the CoA core."); return; }
            if (m.Own) AddCheck(0, "Made for " + Product.Name + " and the CoA server it builds.");
            if (m.State == ModuleState.Managed)
            {
                AddCheck(0, "Installed through " + Product.Name + (m.InstalledOn.Length > 0 ? " on " + m.InstalledOn : "") + ". Removing it undoes its recorded database changes.");
                if (m.CreatedTables.Length > 0) AddCheck(1, "Removing it deletes its own tables and the data in them: " + string.Join(", ", m.CreatedTables));
                if (m.ManualTables.Length > 0) AddCheck(1, "Changes to " + string.Join(", ", m.ManualTables) + " cannot be undone automatically; a backup from before the module restores them.");
                if (m.Status == "sql-failed") AddCheck(2, "One of its database files failed. Removing the module undoes what was applied.");
                if (m.Status == "missing") AddCheck(2, "Its folder is missing. Removing it undoes its database changes.");
            }
            if (m.State == ModuleState.ByHand) AddCheck(1, "Added by hand, not through " + Product.Name + ". Removing it takes the module out, but its database changes stay.");
            if (m.Installed) { readme.Visible = m.FullName.Length > 0; return; }
            var loading = new Label { Text = "Checking the module …", AutoSize = true, Font = Ui.Small, ForeColor = Ui.Muted };
            checks.Controls.Add(loading);
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                ModuleAnalysis a = null; string error = null;
                try { a = ModuleCatalog.Analyze(m); } catch (Exception ex) { error = ex.Message; }
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        if (Selected != m) return;
                        checks.Controls.Clear();
                        if (m.Own) AddCheck(0, "Made for " + Product.Name + " and the CoA server it builds.");
                        if (error != null) { AddCheck(m.Own ? -1 : 1, "The module could not be checked" + (m.Own ? " online right now" : "") + ": " + error); return; }
                        foreach (var c in a.Checks.OrderByDescending(c => c.Key)) AddCheck(c.Key, c.Value);
                        AddCheck(-1, "Check the README for anything you need to set up yourself, for example in the game.");
                        readme.Visible = a.ReadmeText.Length > 0;
                        detailText.Text = (m.Description.Length > 0 ? m.Description : "") + (m.FullName.Length > 0 ? "\n" + m.FullName : "");
                    }));
                }
                catch { }
            });
        }

        void AddCheck(int level, string text)
        {
            string mark = level < 0 ? "ⓘ" : level == 0 ? "✔" : level == 1 ? "⚠" : "✖";
            var color = level < 0 ? Ui.Muted : level == 0 ? Ui.Ok : level == 1 ? Ui.Warn : Ui.Bad;
            var row = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 4) };
            row.Controls.Add(new Label { Text = mark, AutoSize = true, Font = Ui.Bold, ForeColor = color, Margin = new Padding(0, 0, 4, 0) });
            row.Controls.Add(new Label { Text = text, AutoSize = true, Font = Ui.Small, ForeColor = level == 2 ? Ui.Bad : Ui.Text, MaximumSize = new Size(300, 0) });
            checks.Controls.Add(row);
        }

        void ShowReadme()
        {
            var m = Selected;
            if (m == null) return;
            Cursor = Cursors.WaitCursor;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                string text; try { text = ModuleCatalog.Analyze(m).ReadmeText; } catch (Exception ex) { text = "The README could not be loaded: " + ex.Message; }
                try { BeginInvoke((Action)(() => { Cursor = Cursors.Default; using (var d = new TextDialog(m.Name + " – README", text)) d.ShowDialog(this); })); } catch { }
            });
        }

        void AddCustom()
        {
            string url = customUrl.Text.Trim();
            if (!Regex.IsMatch(url, @"^https://[^\s""';]+$"))
            { Ui.Error(this, "Please enter the https address of the module's Git repository, for example https://github.com/azerothcore/mod-transmog"); return; }
            string name = ModuleCatalog.FolderName(url);
            if (!Regex.IsMatch(name, @"^[\w.\-]+$")) { Ui.Error(this, "This address does not end in a module name."); return; }
            string full = ModuleCatalog.GitHubName(url);
            var known = all.FirstOrDefault(m => (full != null && m.FullName.Equals(full, StringComparison.OrdinalIgnoreCase)) || m.CloneUrl.Equals(url, StringComparison.OrdinalIgnoreCase));
            if (known == null)
            {
                known = new ModuleEntry { Name = name, FullName = full ?? "", Url = full != null ? "https://github.com/" + full : url, CloneUrl = full != null ? "https://github.com/" + full + ".git" : url, Description = "Added from an address" };
                all.Add(known);
            }
            customUrl.Text = "";
            search.Text = known.Name;
            Fill();
            if (known.Own)
            {
                if (!known.Installed && known.State != ModuleState.PartOfCoA && Clash(known) == null) ticked.Add(known);
                search.Text = ""; FillOwn(); Fill(); Pick(known); return;
            }
            foreach (ListViewItem it in list.Items) if (it.Tag == known) { it.Selected = true; if (!known.Installed) it.Checked = true; it.EnsureVisible(); }
        }

        void Apply()
        {
            var add = all.Where(m => m.State == ModuleState.Available && ticked.Contains(m)).ToList();
            var remove = all.Where(m => m.Installed && !ticked.Contains(m)).ToList();
            if (add.Count + remove.Count == 0) return;
            UseWaitCursor = true; apply.Enabled = false;
            status.Text = "Checking the chosen modules …"; status.ForeColor = Ui.Muted;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                var found = new Dictionary<ModuleEntry, ModuleAnalysis>();
                var failed = new Dictionary<ModuleEntry, string>();
                foreach (var m in add) { try { found[m] = ModuleCatalog.Analyze(m); } catch (Exception ex) { failed[m] = ex.Message; } }
                try { BeginInvoke((Action)(() => { UseWaitCursor = false; UpdateApply(); status.Text = ""; Confirm(add, remove, found, failed); })); } catch { }
            });
        }

        void Confirm(List<ModuleEntry> add, List<ModuleEntry> remove, Dictionary<ModuleEntry, ModuleAnalysis> found, Dictionary<ModuleEntry, string> failed)
        {
            var b = new StringBuilder();
            bool blocking = false;
            if (add.Count > 0)
            {
                b.AppendLine("INSTALL");
                b.AppendLine();
                foreach (var m in add)
                {
                    b.AppendLine("  " + m.Name + (m.FullName.Length > 0 ? "  (" + m.FullName + ")" : "  (" + m.CloneUrl + ")"));
                    ModuleAnalysis a;
                    if (found.TryGetValue(m, out a))
                        foreach (var c in a.Checks.Where(c => c.Key > 0).OrderByDescending(c => c.Key)) { b.AppendLine("     " + (c.Key == 2 ? "✖ " : "⚠ ") + c.Value); if (c.Key == 2) blocking = true; }
                    else if (failed.ContainsKey(m)) b.AppendLine("     ⚠ Could not be checked: " + failed[m]);
                    b.AppendLine();
                }
            }
            if (remove.Count > 0)
            {
                b.AppendLine("REMOVE");
                b.AppendLine();
                foreach (var m in remove)
                {
                    b.AppendLine("  " + m.Name);
                    if (m.State == ModuleState.ByHand) b.AppendLine("     ⚠ Added by hand: its database changes (if any) stay in place.");
                    else
                    {
                        b.AppendLine("     ✔ Its recorded database changes are undone.");
                        if (m.CreatedTables.Length > 0) b.AppendLine("     ⚠ Its tables are deleted with their data: " + string.Join(", ", m.CreatedTables));
                        if (m.ManualTables.Length > 0) b.AppendLine("     ⚠ Not undoable automatically: " + string.Join(", ", m.ManualTables) + " (a backup restores them)");
                    }
                    b.AppendLine("     Items or spells that players got from the module may disappear or stop working.");
                    b.AppendLine();
                }
            }
            b.AppendLine("WHAT HAPPENS");
            b.AppendLine();
            b.AppendLine(Product.Name + " stops the server, backs it up, " + (add.Count > 0 ? "downloads the modules, " : "") +
                "rebuilds it" + (remove.Count > 0 ? ", undoes the database changes of the removed modules" : "") +
                (add.Count > 0 ? " and applies the new modules' database changes with a record of them" : "") + ". This can take a while.");
            if (add.Count > 0) b.AppendLine("If the server cannot be built with a new module, the module is taken out again and the server stays as it is.");
            if (blocking) b.AppendLine("\n✖ At least one module will most likely not work. Install it anyway only if you know what you are doing.");

            using (var d = new TextDialog("Apply module changes", b.ToString(), blocking ? "Install anyway" : "Apply changes"))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
            }
            Add.Clear(); Remove.Clear();
            Add.AddRange(add.Select(m => m.CloneUrl.Length > 0 ? m.CloneUrl : "https://github.com/" + m.FullName + ".git"));
            Remove.AddRange(remove.Select(m => m.Name));
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    /// <summary>Read-only text in a window, optionally with a confirm button.</summary>
    class TextDialog : AfkForm
    {
        public TextDialog(string title, string text, string confirm = null)
        {
            Text = title; Font = Ui.Base; BackColor = Color.White; ShowIcon = false;
            StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(780, 560); MinimumSize = new Size(500, 300);
            var box = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true, Dock = DockStyle.Fill,
                Font = new Font("Consolas", 10f), BackColor = Color.White, BorderStyle = BorderStyle.None, Text = (text ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n") };
            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16) };
            panel.Controls.Add(box);
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58, Padding = new Padding(14, 10, 14, 10), FlowDirection = FlowDirection.RightToLeft, BackColor = Ui.Panel, WrapContents = false };
            var close = Ui.Secondary(confirm == null ? "Close" : "Cancel"); close.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            bottom.Controls.Add(close);
            if (confirm != null)
            {
                var ok = Ui.Primary(confirm); ok.Click += (s, e) => { DialogResult = DialogResult.OK; Close(); };
                bottom.Controls.Add(ok);
            }
            Controls.Add(panel); Controls.Add(bottom);
            Shown += (s, e) => { box.SelectionStart = 0; box.SelectionLength = 0; };
        }
    }
}
