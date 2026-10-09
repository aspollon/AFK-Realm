using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CoAInstaller
{
    /// <summary>
    /// Server settings: a hand-picked list of popular options plus every option of every
    /// config file, each with the description from its template.
    /// </summary>
    class SettingsDialog : AfkForm
    {
        const string PopularName = "★  Popular settings";

        readonly Install inst;
        readonly ConfigSet set;
        readonly ComboBox fileBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260, Font = Ui.Base };
        readonly TextBox search = Ui.Input(240);
        readonly CheckBox changedOnly = new CheckBox { Text = "Only changed", AutoSize = true, Font = Ui.Base, Margin = new Padding(12, 6, 0, 0) };
        readonly ListView list = new ListView { View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, Dock = DockStyle.Fill, Font = Ui.Base, BorderStyle = BorderStyle.FixedSingle };
        readonly Label keyLabel = new Label { AutoSize = true, Font = Ui.H2, ForeColor = Ui.Text, Margin = new Padding(0, 0, 0, 2) };
        readonly Label whereLabel = new Label { AutoSize = true, Font = Ui.Small, ForeColor = Ui.Muted, Margin = new Padding(0, 0, 0, 8) };
        readonly TextBox description = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.White, Font = Ui.Small, BorderStyle = BorderStyle.None, WordWrap = true };
        readonly Label defaultLabel = new Label { AutoSize = true, Font = Ui.Small, ForeColor = Ui.Muted, Margin = new Padding(0, 8, 0, 4) };
        readonly FlowLayoutPanel editorRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 0) };
        readonly Label lockedLabel = new Label { AutoSize = true, Font = Ui.Small, ForeColor = Ui.Warn, MaximumSize = new Size(420, 0) };
        readonly Label pendingLabel = new Label { AutoSize = true, Font = Ui.Base, ForeColor = Ui.Muted, Margin = new Padding(0, 10, 16, 0) };
        readonly Button save = Ui.Primary("Save changes");
        readonly Font boldList;
        readonly bool worldRunning;
        ConfigOption current;
        bool loading;

        public SettingsDialog(Install i, bool serverRunning)
        {
            inst = i; worldRunning = serverRunning;
            set = ConfigSet.Load(inst);
            boldList = new Font(Ui.Base, FontStyle.Bold);
            Text = Product.Name + " – Server settings"; Font = Ui.Base; BackColor = Color.White;
            StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(1080, 700); MinimumSize = new Size(900, 560);
            ShowIcon = false;

            // --- top bar: file, search, filter
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(14, 10, 14, 4), WrapContents = false, BackColor = Ui.Panel };
            top.Controls.Add(new Label { Text = "Show", AutoSize = true, Margin = new Padding(0, 6, 6, 0) });
            fileBox.Items.Add(PopularName);
            foreach (var f in set.Files) fileBox.Items.Add(f.Name);
            top.Controls.Add(fileBox);
            top.Controls.Add(new Label { Text = "Search", AutoSize = true, Margin = new Padding(18, 6, 6, 0) });
            top.Controls.Add(search);
            top.Controls.Add(changedOnly);

            // --- list on the left
            list.Columns.Add("Setting", 330);
            list.Columns.Add("Value", 140);
            list.ShowGroups = true;
            var listPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 8, 10) };
            listPanel.Controls.Add(list);

            // --- details on the right
            var detail = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8, 12, 16, 10), ColumnCount = 1, RowCount = 6 };
            detail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            detail.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            detail.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            detail.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            detail.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            detail.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            detail.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            // Long names and paths wrap inside the panel instead of widening it.
            detail.SizeChanged += (s, e) =>
            {
                int w = Math.Max(200, detail.ClientSize.Width - detail.Padding.Horizontal - 8);
                keyLabel.MaximumSize = new Size(w, 0); whereLabel.MaximumSize = new Size(w, 0); lockedLabel.MaximumSize = new Size(w, 0);
            };
            description.Dock = DockStyle.Fill;
            detail.Controls.Add(keyLabel, 0, 0);
            detail.Controls.Add(whereLabel, 0, 1);
            detail.Controls.Add(description, 0, 2);
            detail.Controls.Add(defaultLabel, 0, 3);
            detail.Controls.Add(editorRow, 0, 4);
            detail.Controls.Add(lockedLabel, 0, 5);

            var split = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 6, BackColor = Color.White, FixedPanel = FixedPanel.None };
            split.Panel1.Controls.Add(listPanel);
            split.Panel2.Controls.Add(detail);

            // --- bottom bar
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58, Padding = new Padding(14, 10, 14, 10), FlowDirection = FlowDirection.RightToLeft, BackColor = Ui.Panel, WrapContents = false };
            var close = Ui.Secondary("Close");
            close.Click += (s, e) => Close();
            save.Click += (s, e) => Save();
            bottom.Controls.Add(close);
            bottom.Controls.Add(save);
            bottom.Controls.Add(pendingLabel);

            Controls.Add(split);
            Controls.Add(bottom);
            Controls.Add(top);
            Load += (s, e) => { split.SplitterDistance = 540; };

            fileBox.SelectedIndexChanged += (s, e) => Fill();
            search.TextChanged += (s, e) => Fill();
            changedOnly.CheckedChanged += (s, e) => Fill();
            list.SelectedIndexChanged += (s, e) => { if (list.SelectedItems.Count > 0) ShowOption((ConfigOption)list.SelectedItems[0].Tag); };
            FormClosing += OnClosing;

            fileBox.SelectedIndex = 0;
            UpdatePending();
        }

        IEnumerable<ConfigOption> Matching()
        {
            bool popular = fileBox.SelectedIndex <= 0;
            IEnumerable<ConfigOption> items = popular ? set.Popular : set.Files[fileBox.SelectedIndex - 1].Options;
            string q = search.Text.Trim();
            if (q.Length > 0)
            {
                // Searching looks through every file, so an option is found without knowing where it lives.
                items = set.Files.SelectMany(f => f.Options).Where(o =>
                    o.Key.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (o.Label != null && o.Label.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    o.Description.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
            }
            if (changedOnly.Checked) items = items.Where(o => o.DiffersFromDefault || o.IsPending);
            return items;
        }

        void Fill()
        {
            var keep = current;
            list.BeginUpdate();
            list.Items.Clear(); list.Groups.Clear();
            var groups = new Dictionary<string, ListViewGroup>();
            bool searching = search.Text.Trim().Length > 0;
            bool popular = fileBox.SelectedIndex <= 0 && !searching;
            ListViewItem select = null;
            foreach (var o in Matching())
            {
                string g;
                if (popular && set.PopularSection.TryGetValue(o, out g)) { }
                else g = searching ? o.File.Name + "  ›  " + o.Section : o.Section;
                ListViewGroup group;
                if (!groups.TryGetValue(g, out group)) { group = new ListViewGroup(g, g); groups[g] = group; list.Groups.Add(group); }
                var item = new ListViewItem(new[] { popular && o.Label != null ? o.Label : o.Key, o.Display(o.EditedRaw) }, group) { Tag = o };
                Style(item);
                list.Items.Add(item);
                if (o == keep) select = item;
            }
            list.EndUpdate();
            if (select != null) { select.Selected = true; select.EnsureVisible(); }
            else if (list.Items.Count > 0) list.Items[0].Selected = true;
            else ShowOption(null);
        }

        void Style(ListViewItem item)
        {
            var o = (ConfigOption)item.Tag;
            item.SubItems[1].Text = o.Display(o.EditedRaw);
            item.Font = o.DiffersFromDefault || o.IsPending ? boldList : Ui.Base;
            item.ForeColor = o.IsPending ? Ui.Accent : (o.Locked ? Ui.Muted : Ui.Text);
            item.ToolTipText = o.Key;
        }

        void ShowOption(ConfigOption o)
        {
            current = o;
            loading = true;
            editorRow.Controls.Clear();
            lockedLabel.Text = "";
            if (o == null)
            {
                keyLabel.Text = "No setting found"; whereLabel.Text = ""; description.Text = ""; defaultLabel.Text = "";
                loading = false; return;
            }
            keyLabel.Text = o.Label ?? o.Key;
            whereLabel.Text = (o.Label != null ? o.Key + "   ·   " : "") + o.File.Name + "   ·   " + o.Section;
            description.Text = (o.Description.Length > 0 ? o.Description : "No description in the template.").Replace("\r\n", "\n").Replace("\n", "\r\n");
            defaultLabel.Text = o.DefaultRaw != null ? "Default: " + o.Display(o.DefaultRaw) : "";

            Control editor;
            string value = ConfigOption.Unquote(o.EditedRaw);
            if (o.Kind == OptionKind.Bool)
            {
                var cb = new CheckBox { Text = "On", Checked = value == "1", AutoSize = true, Font = Ui.Bold, Margin = new Padding(0, 4, 16, 0) };
                cb.CheckedChanged += (s, e) => { cb.Text = cb.Checked ? "On" : "Off"; Edit(o, cb.Checked ? "1" : "0"); };
                cb.Text = cb.Checked ? "On" : "Off";
                editor = cb;
            }
            else if (o.Kind == OptionKind.Choice)
            {
                // Editable, so values that are not in the list (e.g. other numbers) stay possible.
                var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 360, Font = Ui.Base };
                foreach (var c in o.Choices) combo.Items.Add(c.Key + " – " + c.Value);
                var match = o.Choices.FirstOrDefault(c => c.Key == value);
                combo.Text = match.Key != null ? match.Key + " – " + match.Value : value;
                EventHandler apply = (s, e) =>
                {
                    string t = combo.Text.Trim();
                    int dash = t.IndexOf(" – ");
                    Edit(o, dash > 0 ? t.Substring(0, dash) : t);
                };
                combo.SelectedIndexChanged += apply;
                combo.TextChanged += apply;
                combo.Leave += apply;
                combo.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) apply(s, e); };
                editor = combo;
            }
            else
            {
                var tb = Ui.Input(o.Kind == OptionKind.Number ? 160 : 420);
                tb.Text = value;
                EventHandler apply = (s, e) => Edit(o, tb.Text);
                // While typing already, not only when the field is left: "Save changes" is disabled until
                // there is a change, and a disabled button cannot take the focus away from the field.
                tb.TextChanged += apply;
                tb.Leave += apply;
                tb.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; apply(s, e); } };
                editor = tb;
            }
            editor.Enabled = !o.Locked;
            editorRow.Controls.Add(editor);
            if (!o.Locked && o.DefaultRaw != null)
            {
                var reset = Ui.Secondary("Reset to default");
                reset.Margin = new Padding(10, 0, 0, 0);
                reset.Click += (s, e) => { Edit(o, ConfigOption.Unquote(o.DefaultRaw)); ShowOption(o); };
                editorRow.Controls.Add(reset);
            }
            if (o.Locked) lockedLabel.Text = Product.Name + " manages this setting itself, so it cannot be changed here.";
            loading = false;
        }

        void Edit(ConfigOption o, string input)
        {
            if (loading || o == null || o.Locked) return;
            string raw;
            try { raw = o.ToRaw(input); }
            catch (FormatException ex) { lockedLabel.Text = ex.Message; lockedLabel.ForeColor = Ui.Bad; return; }
            lockedLabel.ForeColor = Ui.Warn; lockedLabel.Text = "";
            if (ConfigOption.Unquote(raw) == ConfigOption.Unquote(o.CurrentRaw) && o.File.Values.ContainsKey(o.Key)) o.File.Pending.Remove(o.Key);
            else o.File.Pending[o.Key] = raw;
            foreach (ListViewItem item in list.Items) if (item.Tag == o) Style(item);
            UpdatePending();
        }

        void UpdatePending()
        {
            int n = set.PendingCount;
            save.Enabled = n > 0;
            pendingLabel.Text = n == 0 ? "Changes take effect after restarting the worldserver." : n + (n == 1 ? " unsaved change" : " unsaved changes");
            pendingLabel.ForeColor = n == 0 ? Ui.Muted : Ui.Accent;
        }

        void Save()
        {
            // Commit a value that is still being typed.
            if (ActiveControl is TextBox || ActiveControl is ComboBox) list.Focus();
            try { set.SaveAll(); }
            catch (Exception ex) { Ui.Error(this, "The settings could not be saved:\n" + ex.Message); return; }
            Fill(); UpdatePending();
            MessageBox.Show(this, worldRunning
                ? "Saved. Restart the server (Stop server, then Start server) so the worldserver uses the new settings."
                : "Saved. The new settings are used the next time the server starts.",
                Product.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (set.PendingCount == 0) return;
            var r = MessageBox.Show(this, "Save your " + set.PendingCount + " unsaved change(s)?", Product.Name, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) { e.Cancel = true; return; }
            if (r == DialogResult.Yes)
            {
                try { set.SaveAll(); }
                catch (Exception ex) { Ui.Error(this, "The settings could not be saved:\n" + ex.Message); e.Cancel = true; }
            }
            else set.DiscardAll();
        }
    }
}
