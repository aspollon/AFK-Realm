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
        readonly CheckBox hideOld = new CheckBox { Text = L.T("Hide modules not changed for 2 years"), AutoSize = true, Checked = true, Font = Ui.Base, Margin = new Padding(0, 6, 10, 0) };
        readonly CheckBox onlyInstalled = new CheckBox { Text = L.T("Installed only"), AutoSize = true, Font = Ui.Base, Margin = new Padding(0, 6, 10, 0) };
        readonly ComboBox sort = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160, Font = Ui.Base, Margin = new Padding(0, 3, 10, 3) };
        readonly ListView list = new ListView { View = View.Details, CheckBoxes = true, FullRowSelect = true, HideSelection = false, MultiSelect = false, Dock = DockStyle.Fill, Font = Ui.Base, BorderStyle = BorderStyle.FixedSingle };
        readonly Label status = new Label { AutoSize = true, Font = Ui.Small, ForeColor = Ui.Muted, Margin = new Padding(0, 8, 0, 0) };
        readonly Label detailName = new Label { AutoSize = true, Font = Ui.H2, ForeColor = Ui.Text, MaximumSize = new Size(330, 0), Margin = new Padding(0, 0, 0, 2) };
        readonly Label detailText = new Label { AutoSize = true, Font = Ui.Base, ForeColor = Ui.Text, MaximumSize = new Size(330, 0), Margin = new Padding(0, 2, 0, 6) };
        readonly LinkLabel detailLink = new LinkLabel { AutoSize = true, Font = Ui.Small, Text = L.T("Open on GitHub"), Margin = new Padding(0, 0, 0, 8) };
        readonly FlowLayoutPanel checks = Ui.Column();
        readonly Button readme = Ui.Secondary(L.T("Show README"));
        readonly Button apply = Ui.Primary(L.T("Apply changes …"));
        readonly TextBox customUrl = new TextBox { Font = Ui.Base, Width = 380, Margin = new Padding(0, 3, 8, 3) };
        readonly TextBox token = new TextBox { Font = Ui.Base, Width = 320, Margin = new Padding(0, 3, 8, 3) };

        // The modules made for AFK Realm stand above the catalog, in a section of their own.
        readonly FlowLayoutPanel ownPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(0, 0, 0, 2) };
        // The own section grows with its rows, but the catalog below always keeps room. With five or six
        // modules of our own the fixed-height version pushed the catalog out of the window.
        readonly SplitContainer ownSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 6, Panel1MinSize = 70, Panel2MinSize = 120 };
        readonly Label catalogTitle = new Label { Text = L.T("AzerothCore module catalog"), Dock = DockStyle.Top, AutoSize = true, Font = Ui.H2, ForeColor = Ui.Text, Padding = new Padding(0, 4, 0, 2) };
        readonly Dictionary<ModuleEntry, Label> ownStatus = new Dictionary<ModuleEntry, Label>();
        ModuleEntry ownSelected;
        List<ModuleEntry> all = new List<ModuleEntry>();
        readonly HashSet<ModuleEntry> ticked = new HashSet<ModuleEntry>();
        bool filling, fitting;

        public ModulesDialog(Install i)
        {
            inst = i;
            Text = Product.Name + " – " + L.T("Modules"); Font = Ui.Base; BackColor = Ui.Surface; ShowIcon = false;
            StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(1080, 660); MinimumSize = new Size(900, 520);

            var top = Ui.Column(); top.Dock = DockStyle.Top; top.AutoSize = true; top.Padding = new Padding(16, 12, 16, 4);
            top.Controls.Add(Ui.Para(L.F("Tick a module to install it, untick it to remove it, then choose \"Apply changes\". {0}" +
                " backs up the server, downloads the modules and rebuilds the server. Database changes of modules installed here are recorded, " +
                "so removing a module also undoes them. \"Modules by {0}\" are made for this server; the rest of the list comes from the AzerothCore module catalog, and not every module there works with CoA.", Product.Name), 1040));
            var filter = Ui.Row();
            filter.Controls.Add(new Label { Text = L.T("Search"), AutoSize = true, Font = Ui.Base, Margin = new Padding(0, 6, 6, 0) });
            filter.Controls.Add(search); filter.Controls.Add(hideOld); filter.Controls.Add(onlyInstalled);
            filter.Controls.Add(new Label { Text = L.T("Sort by"), AutoSize = true, Font = Ui.Base, Margin = new Padding(0, 6, 6, 0) });
            sort.Items.AddRange(new object[] { L.T("Recommended"), L.T("Name A–Z"), L.T("Name Z–A"), L.T("Newest first"), L.T("Oldest first"), L.T("Most stars") });
            sort.SelectedIndex = 0;
            sort.SelectedIndexChanged += (s, e) => Fill();
            filter.Controls.Add(sort);
            var reload = Ui.Secondary(L.T("Reload list")); reload.Click += (s, e) => { KeepToken(); LoadList(true); };
            filter.Controls.Add(reload);
            top.Controls.Add(filter);

            list.Columns.Add(L.T("Module"), 205);
            list.Columns.Add(L.T("Description"), 240);
            list.Columns.Add("★", 40, HorizontalAlignment.Right);
            list.Columns.Add(L.T("Last change"), 90);
            list.Columns.Add(L.T("Status"), 115);
            var listPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 4, 8, 4) };
            ownSplit.Panel1.AutoScroll = true;
            ownSplit.Panel1.Controls.Add(ownPanel);
            ownSplit.Panel2.Controls.Add(list);
            ownSplit.Panel2.Controls.Add(catalogTitle);
            ownSplit.SizeChanged += (s, e) => FitOwn();
            listPanel.Controls.Add(ownSplit);

            var details = Ui.Column(); details.Padding = new Padding(4, 4, 8, 4);
            details.Controls.Add(detailName); details.Controls.Add(detailText); details.Controls.Add(detailLink);
            details.Controls.Add(checks); details.Controls.Add(readme);
            var detailPanel = new Panel { Dock = DockStyle.Right, Width = 360, AutoScroll = true, Padding = new Padding(4, 4, 8, 4) };
            detailPanel.Controls.Add(details);
            readme.Visible = false; detailLink.Visible = false;

            // The GitHub token: both the module list and the update check use it, and both burn GitHub's hourly limit without one.
            var tokenRow = Ui.Row(); tokenRow.Dock = DockStyle.Bottom; tokenRow.Padding = new Padding(16, 2, 16, 2); tokenRow.AutoSize = true;
            var tokenLabel = new Label { Text = L.T("GitHub token (optional):"), AutoSize = true, Font = Ui.Base, Margin = new Padding(0, 6, 6, 0) };
            tokenRow.Controls.Add(tokenLabel); tokenRow.Controls.Add(token);
            tokenRow.Controls.Add(new Label { Text = L.T("GitHub allows 60 requests an hour without a token, 5000 with one."), AutoSize = true, Font = Ui.Small, ForeColor = Ui.Muted, Margin = new Padding(10, 8, 0, 0) });
            token.Text = Settings.GitHubToken ?? "";
            token.Leave += (s, e) => KeepToken();
            FormClosing += (s, e) => KeepToken();

            var urlRow = Ui.Row(); urlRow.Dock = DockStyle.Bottom; urlRow.Padding = new Padding(16, 2, 16, 4); urlRow.AutoSize = true;
            urlRow.Controls.Add(new Label { Text = L.T("Not in the list? Git address:"), AutoSize = true, Font = Ui.Base, Margin = new Padding(0, 6, 6, 0) });
            urlRow.Controls.Add(customUrl);
            var addUrl = Ui.Secondary(L.T("Add")); addUrl.Click += (s, e) => AddCustom();
            urlRow.Controls.Add(addUrl);
            urlRow.Controls.Add(status);

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58, Padding = new Padding(14, 10, 14, 10), FlowDirection = FlowDirection.RightToLeft, BackColor = Ui.Panel, WrapContents = false };
            var close = Ui.Secondary(L.T("Close")); close.Click += (s, e) => Close();
            bottom.Controls.Add(close); bottom.Controls.Add(apply);

            Controls.Add(listPanel);
            Controls.Add(detailPanel);
            Controls.Add(tokenRow);
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

        /// <summary>Keeps the GitHub token the user typed (empty: none). The next request uses it.</summary>
        void KeepToken()
        {
            string want = token.Text.Trim();
            if (want.Length == 0) want = null;
            if (want != Settings.GitHubToken) Settings.GitHubToken = want;
        }

        /// <summary>Marks one of the modules by AFK Realm as the one the details are shown for (null: none).</summary>
        void SelectOwn(ModuleEntry m)
        {
            ownSelected = m;
            foreach (Control row in ownPanel.Controls) if (row.Tag is ModuleEntry) row.BackColor = row.Tag == m ? Ui.Panel : Ui.Surface;
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
                ownPanel.Controls.Add(new Label { Text = L.F("Modules by {0}", Product.Name), AutoSize = true, Font = Ui.H2, ForeColor = Ui.AccentText, Margin = new Padding(0, 0, 0, 2) });
                foreach (var m in mine)
                {
                    var entry = m;
                    var row = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Tag = entry, Margin = new Padding(0, 0, 0, 2), Padding = new Padding(2, 2, 6, 2),
                        BackColor = entry == ownSelected ? Ui.Panel : Ui.Surface };
                    var tick = new CheckBox { Text = entry.Name, AutoSize = true, Font = Ui.Bold, Checked = ticked.Contains(entry), Enabled = entry.State != ModuleState.PartOfCoA, Margin = new Padding(2, 1, 10, 0) };
                    var text = new Label { Text = L.Outside(entry.Description), AutoSize = true, Font = Ui.Base, ForeColor = Ui.Text, Margin = new Padding(0, 4, 10, 0), MaximumSize = new Size(360, 0) };
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
            }
            ownPanel.ResumeLayout();
            ownSplit.Panel1Collapsed = mine.Count == 0;
            ownPanel.PerformLayout();
            FitOwn();
        }

        /// <summary>Gives the own-module section its content height, but never more than half the list area.</summary>
        void FitOwn()
        {
            if (fitting || ownSplit.Height <= 0 || ownSplit.Panel1Collapsed)
                return;

            fitting = true;
            try
            {
                int wanted = Math.Max(ownSplit.Panel1MinSize, ownPanel.PreferredSize.Height + 4);
                int room = ownSplit.Height - ownSplit.Panel2MinSize - ownSplit.SplitterWidth;
                int max = Math.Max(ownSplit.Panel1MinSize, Math.Min(room, ownSplit.Height / 2));
                ownSplit.SplitterDistance = Math.Min(wanted, max);
            }
            catch (InvalidOperationException) { /* the window is still too small for the splitter; the next layout fixes it */ }
            finally { fitting = false; }
        }

        void Pick(ModuleEntry m)
        {
            list.SelectedItems.Clear();
            SelectOwn(m);
            ShowDetails();
        }

        void LoadList(bool refresh)
        {
            status.Text = L.T("Loading the module list from GitHub …");
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                List<ModuleEntry> catalog = null, merged = new List<ModuleEntry>(); string warning = null, error = null;
                try { catalog = ModuleCatalog.LoadCatalog(inst, refresh, out warning); }
                catch (Exception ex) { error = ex.Message; }
                try { merged = ModuleCatalog.Merge(inst, catalog); }
                catch (Exception ex) { error = (error != null ? error + " " : "") + L.T("The installed modules could not be read:") + " " + ex.Message; }
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
                        status.Text = error ?? warning ?? L.F("{0} modules in the catalog.", all.Count(m => m.InCatalog));
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
                    (q.Length == 0 || m.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || m.Description.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || L.Outside(m.Description).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                     m.FullName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0));
            IEnumerable<ModuleEntry> ordered;
            switch (sort.SelectedIndex)
            {
                case 1: ordered = shown.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase); break;
                case 2: ordered = shown.OrderByDescending(m => m.Name, StringComparer.OrdinalIgnoreCase); break;
                case 3: ordered = shown.OrderByDescending(m => m.Pushed); break;
                case 4: ordered = shown.OrderBy(m => m.Pushed == DateTime.MinValue ? DateTime.MaxValue : m.Pushed); break;
                case 5: ordered = shown.OrderByDescending(m => m.Stars).ThenBy(m => m.Name); break;
                default: ordered = shown.OrderByDescending(m => m.Installed).ThenByDescending(m => ticked.Contains(m)).ThenByDescending(m => m.Stars).ThenBy(m => m.Name); break;
            }
            foreach (var m in ordered)
            {
                var it = new ListViewItem(m.Name) { Tag = m, Checked = ticked.Contains(m) };
                it.SubItems.Add(L.Outside(m.Description));
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
                case ModuleState.PartOfCoA: return m.IsPlayerbots ? L.T("included") : L.T("part of CoA");
                case ModuleState.Managed:
                    if (!tick) return L.T("will be removed");
                    return m.Status == "sql-failed" ? L.T("database step failed") : m.Status == "missing" ? L.T("folder missing") : L.T("installed");
                case ModuleState.ByHand: return tick ? L.T("added by hand") : L.T("will be removed");
                default: return tick ? L.T("will be installed") : "";
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
            return L.F("Another module named \"{0}\" ({1}) is already chosen. Only one module of the same name can be installed.", m.Name, clash.FullName.Length > 0 ? clash.FullName : clash.CloneUrl);
        }

        void UpdateApply()
        {
            int n = all.Count(m => m.State != ModuleState.PartOfCoA && m.Installed != ticked.Contains(m));
            apply.Enabled = n > 0;
            apply.Text = n == 1 ? L.T("Apply 1 change …") : n > 1 ? L.F("Apply {0} changes …", n) : L.T("Apply changes …");
        }

        void ShowDetails()
        {
            var m = Selected;
            checks.Controls.Clear();
            if (m == null) { detailName.Text = detailText.Text = ""; readme.Visible = detailLink.Visible = false; return; }
            detailName.Text = m.Name;
            detailText.Text = (m.Description.Length > 0 ? L.Outside(m.Description) : "") + (m.FullName.Length > 0 ? "\n" + m.FullName : "");
            detailLink.Visible = m.Url.StartsWith("http");
            readme.Visible = false;
            if (m.IsPlayerbots)
            {
                AddCheck(0, L.F("Playerbots is already included: {0} always builds the CoA version (Zyth45/mod-playerbots, branch coa) and keeps it up to date with every server update.", Product.Name));
                if (m.FullName.Length > 0 && !m.FullName.Equals("Zyth45/mod-playerbots", StringComparison.OrdinalIgnoreCase))
                    AddCheck(-1, L.F("This entry is a different version ({0}) that does not fit CoA. Nothing needs to be installed here.", m.FullName));
                return;
            }
            if (m.State == ModuleState.PartOfCoA) { AddCheck(0, L.T("This module is already part of the CoA core.")); return; }
            if (m.Own) AddCheck(0, L.F("Made for {0} and the CoA server it builds.", Product.Name));
            if (m.State == ModuleState.Managed)
            {
                AddCheck(0, (m.InstalledOn.Length > 0 ? L.F("Installed through {0} on {1}.", Product.Name, m.InstalledOn) : L.F("Installed through {0}.", Product.Name)) + " " + L.T("Removing it undoes its recorded database changes."));
                if (m.CreatedTables.Length > 0) AddCheck(1, L.T("Removing it deletes its own tables and the data in them:") + " " + string.Join(", ", m.CreatedTables));
                if (m.ManualTables.Length > 0) AddCheck(1, L.F("Changes to {0} cannot be undone automatically; a backup from before the module restores them.", string.Join(", ", m.ManualTables)));
                if (m.Status == "sql-failed") AddCheck(2, L.T("One of its database files failed. Removing the module undoes what was applied."));
                if (m.Status == "missing") AddCheck(2, L.T("Its folder is missing. Removing it undoes its database changes."));
            }
            if (m.State == ModuleState.ByHand) AddCheck(1, L.F("Added by hand, not through {0}. Removing it takes the module out, but its database changes stay.", Product.Name));
            if (m.Installed) { readme.Visible = m.FullName.Length > 0; return; }
            var loading = new Label { Text = L.T("Checking the module …"), AutoSize = true, Font = Ui.Small, ForeColor = Ui.Muted };
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
                        if (m.Own) AddCheck(0, L.F("Made for {0} and the CoA server it builds.", Product.Name));
                        if (error != null) { AddCheck(m.Own ? -1 : 1, (m.Own ? L.T("The module could not be checked online right now:") : L.T("The module could not be checked:")) + " " + error); return; }
                        foreach (var c in a.Checks.OrderByDescending(c => c.Key)) AddCheck(c.Key, c.Value);
                        AddCheck(-1, L.T("Check the README for anything you need to set up yourself, for example in the game."));
                        readme.Visible = a.ReadmeText.Length > 0;
                        detailText.Text = (m.Description.Length > 0 ? L.Outside(m.Description) : "") + (m.FullName.Length > 0 ? "\n" + m.FullName : "");
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
                string text; try { text = ModuleCatalog.Analyze(m).ReadmeText; } catch (Exception ex) { text = L.T("The README could not be loaded:") + " " + ex.Message; }
                try { BeginInvoke((Action)(() => { Cursor = Cursors.Default; using (var d = new TextDialog(m.Name + " – README", text)) d.ShowDialog(this); })); } catch { }
            });
        }

        void AddCustom()
        {
            string url = customUrl.Text.Trim();
            if (!Regex.IsMatch(url, @"^https://[^\s""';]+$"))
            { Ui.Error(this, L.T("Please enter the https address of the module's Git repository, for example") + " https://github.com/azerothcore/mod-transmog"); return; }
            string name = ModuleCatalog.FolderName(url);
            if (!Regex.IsMatch(name, @"^[\w.\-]+$")) { Ui.Error(this, L.T("This address does not end in a module name.")); return; }
            string full = ModuleCatalog.GitHubName(url);
            var known = all.FirstOrDefault(m => (full != null && m.FullName.Equals(full, StringComparison.OrdinalIgnoreCase)) || m.CloneUrl.Equals(url, StringComparison.OrdinalIgnoreCase));
            if (known == null)
            {
                known = new ModuleEntry { Name = name, FullName = full ?? "", Url = full != null ? "https://github.com/" + full : url, CloneUrl = full != null ? "https://github.com/" + full + ".git" : url, Description = L.N("Added from an address") };
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
            status.Text = L.T("Checking the chosen modules …"); status.ForeColor = Ui.Muted;
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
                b.AppendLine(L.T("INSTALL"));
                b.AppendLine();
                foreach (var m in add)
                {
                    b.AppendLine("  " + m.Name + (m.FullName.Length > 0 ? "  (" + m.FullName + ")" : "  (" + m.CloneUrl + ")"));
                    ModuleAnalysis a;
                    if (found.TryGetValue(m, out a))
                        foreach (var c in a.Checks.Where(c => c.Key > 0).OrderByDescending(c => c.Key)) { b.AppendLine("     " + (c.Key == 2 ? "✖ " : "⚠ ") + c.Value); if (c.Key == 2) blocking = true; }
                    else if (failed.ContainsKey(m)) b.AppendLine("     ⚠ " + L.T("Could not be checked:") + " " + failed[m]);
                    b.AppendLine();
                }
            }
            if (remove.Count > 0)
            {
                b.AppendLine(L.T("REMOVE"));
                b.AppendLine();
                foreach (var m in remove)
                {
                    b.AppendLine("  " + m.Name);
                    if (m.State == ModuleState.ByHand) b.AppendLine("     ⚠ " + L.T("Added by hand: its database changes (if any) stay in place."));
                    else
                    {
                        b.AppendLine("     ✔ " + L.T("Its recorded database changes are undone."));
                        if (m.CreatedTables.Length > 0) b.AppendLine("     ⚠ " + L.T("Its tables are deleted with their data:") + " " + string.Join(", ", m.CreatedTables));
                        if (m.ManualTables.Length > 0) b.AppendLine("     ⚠ " + L.F("Not undoable automatically: {0} (a backup restores them)", string.Join(", ", m.ManualTables)));
                    }
                    b.AppendLine("     " + L.T("Items or spells that players got from the module may disappear or stop working."));
                    b.AppendLine();
                }
            }
            b.AppendLine(L.T("WHAT HAPPENS"));
            b.AppendLine();
            var steps = new List<string> { L.T("stops the server"), L.T("backs it up") };
            if (add.Count > 0) steps.Add(L.T("downloads the modules"));
            steps.Add(L.T("rebuilds it"));
            if (remove.Count > 0) steps.Add(L.T("undoes the database changes of the removed modules"));
            if (add.Count > 0) steps.Add(L.T("applies the new modules' database changes with a record of them"));
            b.AppendLine(L.F("{0}: {1}. This can take a while.", Product.Name, string.Join(", ", steps)));
            if (add.Count > 0) b.AppendLine(L.T("If the server cannot be built with a new module, the module is taken out again and the server stays as it is."));
            if (blocking) b.AppendLine("\n✖ " + L.T("At least one module will most likely not work. Install it anyway only if you know what you are doing."));

            using (var d = new TextDialog(L.T("Apply module changes"), b.ToString(), blocking ? L.T("Install anyway") : L.T("Apply changes")))
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
            Text = title; Font = Ui.Base; BackColor = Ui.Surface; ShowIcon = false;
            StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(780, 560); MinimumSize = new Size(500, 300);
            var box = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true, Dock = DockStyle.Fill,
                Font = L.Font != null ? Ui.Face(10f) : new Font("Consolas", 10f), BackColor = Ui.Surface, BorderStyle = BorderStyle.None, Text = (text ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n") };
            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16) };
            panel.Controls.Add(box);
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58, Padding = new Padding(14, 10, 14, 10), FlowDirection = FlowDirection.RightToLeft, BackColor = Ui.Panel, WrapContents = false };
            var close = Ui.Secondary(confirm == null ? L.T("Close") : L.T("Cancel")); close.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
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
