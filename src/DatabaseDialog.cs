using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CoAInstaller
{
    /// <summary>
    /// The server's databases: browse and edit tables, and run any SQL - what one otherwise opens
    /// HeidiSQL for. Read-only until "Allow changes" is ticked; ticking it warns and offers a backup.
    /// </summary>
    class DatabaseDialog : AfkForm
    {
        const int PageSize = 500, QueryRows = 5000;
        static readonly Color Changed = Color.FromArgb(255, 244, 200), Added = Color.FromArgb(222, 244, 226), NullColor = Color.FromArgb(150, 150, 165);

        /// <summary>The user asked for a backup of the server before changing anything.</summary>
        public bool BackUpNow { get; private set; }

        readonly Install inst;
        readonly ServerControl ctl;
        readonly TextBox tableFilter = new TextBox { Dock = DockStyle.Top, Font = Ui.Base };
        readonly TreeView tree = new TreeView { Dock = DockStyle.Fill, Font = Ui.Base, HideSelection = false, BorderStyle = BorderStyle.FixedSingle, ShowLines = false, FullRowSelect = true };
        readonly CheckBox allow = new CheckBox { Text = "Allow changes", AutoSize = true, Font = Ui.Bold, Margin = new Padding(0, 6, 14, 0) };
        readonly Label tableName = new Label { AutoSize = true, Font = Ui.Bold, Margin = new Padding(0, 7, 10, 0), Text = "Choose a table on the left." };
        readonly TextBox where = new TextBox { Font = Ui.Mono, Width = 330, Margin = new Padding(0, 5, 6, 0) };
        readonly Label pageInfo = new Label { AutoSize = true, Font = Ui.Small, ForeColor = Ui.Muted, Margin = new Padding(8, 8, 8, 0) };
        readonly Label tableHint = new Label { Dock = DockStyle.Bottom, Height = 22, Font = Ui.Small, ForeColor = Ui.Muted };
        readonly DataGridView grid = NewGrid();
        readonly DataGridView result = NewGrid();
        readonly TextBox sql = new TextBox { Multiline = true, AcceptsReturn = true, AcceptsTab = true, ScrollBars = ScrollBars.Vertical, Font = Ui.Mono, Dock = DockStyle.Top, Height = 150, WordWrap = true };
        readonly Label sqlHint = new Label { Dock = DockStyle.Bottom, Height = 22, Font = Ui.Small, ForeColor = Ui.Muted };
        readonly ComboBox resultPick = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 560, Font = Ui.Small, Margin = new Padding(10, 6, 0, 0), Visible = false };
        readonly Button save = Ui.Primary("Save changes"), discard = Ui.Secondary("Discard"), addRow = Ui.Secondary("Add row"), delRow = Ui.Secondary("Delete row"),
            prev = Ui.Secondary("< Previous"), next = Ui.Secondary("Next >"), apply = Ui.Secondary("Filter"), run = Ui.Primary("Run (F5)");

        readonly Button export = Ui.Secondary("Export …"), import = Ui.Secondary("Import SQL file …"), undo = Ui.Secondary("Undo last change …");
        string db, table;                               // the table shown in the grid
        List<ColumnInfo> columns = new List<ColumnInfo>();
        int page; bool more, loading, busy;
        readonly List<string[]> deleted = new List<string[]>();
        List<SqlTable> results = new List<SqlTable>();
        Dictionary<string, List<string>> tablesOf = new Dictionary<string, List<string>>();

        class ColumnInfo { public string Name, Type; public bool Nullable, Key, Binary, Auto; }
        /// <summary>What a row held when it was read; null for a row the user added.</summary>
        class RowState { public string[] Original; }

        static DataGridView NewGrid()
        {
            var g = new DataGridView
            {
                Dock = DockStyle.Fill, Font = Ui.Small, BackgroundColor = Color.White, BorderStyle = BorderStyle.FixedSingle, AllowUserToAddRows = false,
                AllowUserToDeleteRows = false, AllowUserToResizeRows = false, RowHeadersWidth = 26, ReadOnly = true, SelectionMode = DataGridViewSelectionMode.CellSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                EnableHeadersVisualStyles = false, ShowCellToolTips = true
            };
            g.ColumnHeadersDefaultCellStyle.Font = Ui.Small; g.ColumnHeadersDefaultCellStyle.BackColor = Ui.Panel;
            g.RowTemplate.Height = 21;
            return g;
        }

        public DatabaseDialog(Install i, ServerControl c)
        {
            inst = i; ctl = c;
            Text = Product.Name + " – Database"; Font = Ui.Base; BackColor = Color.White; ShowIcon = false; KeyPreview = true;
            StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(1280, 760); MinimumSize = new Size(1000, 600);

            // ---- top: the warning and the switch
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(16, 10, 16, 6), WrapContents = false };
            top.Controls.Add(allow);
            var backup = Ui.Secondary("Back up the server first …"); backup.Font = Ui.Small; backup.Padding = new Padding(8, 2, 8, 2); backup.Margin = new Padding(0, 2, 14, 0);
            top.Controls.Add(backup);
            top.Controls.Add(new Label
            {
                AutoSize = true, MaximumSize = new Size(820, 0), Font = Ui.Small, ForeColor = Ui.Warn, Margin = new Padding(0, 0, 0, 0),
                Text = "This is the server's own data. A wrong value or a statement without WHERE can break the world, characters or accounts - if you do not know what a table is for, leave it alone. " +
                       "Looking is safe: nothing can be changed until \"Allow changes\" is ticked."
            });

            // ---- left: databases and their tables
            var left = new Panel { Dock = DockStyle.Left, Width = 270, Padding = new Padding(16, 0, 8, 12) };
            var transfer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = false, Padding = new Padding(0, 6, 0, 0) };
            transfer.WrapContents = true;
            foreach (var x in new[] { export, import, undo }) { x.Font = Ui.Small; x.Padding = new Padding(8, 2, 8, 2); x.Margin = new Padding(0, 0, 6, 6); transfer.Controls.Add(x); }
            left.Controls.Add(tree); left.Controls.Add(tableFilter); left.Controls.Add(transfer);
            left.Controls.Add(new Label { Text = "Find a table", Dock = DockStyle.Top, Height = 22, Font = Ui.Small, ForeColor = Ui.Muted });

            // ---- the table
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Margin = new Padding(0), Padding = new Padding(0, 0, 0, 4) };
            foreach (var b in new[] { apply, prev, next, addRow, delRow, discard }) { b.Font = Ui.Small; b.Padding = new Padding(8, 2, 8, 2); b.Margin = new Padding(0, 3, 6, 0); }
            save.Font = Ui.Small; save.Padding = new Padding(10, 3, 10, 3); save.Margin = new Padding(8, 3, 6, 0);
            bar.Controls.Add(tableName);
            bar.Controls.Add(new Label { Text = "WHERE", AutoSize = true, Font = Ui.Small, ForeColor = Ui.Muted, Margin = new Padding(6, 9, 4, 0) });
            bar.Controls.Add(where); bar.Controls.Add(apply);
            bar.SetFlowBreak(apply, true);
            bar.Controls.Add(prev); bar.Controls.Add(next); bar.Controls.Add(addRow); bar.Controls.Add(delRow); bar.Controls.Add(save); bar.Controls.Add(discard); bar.Controls.Add(pageInfo);
            var tableTab = new TabPage("Table") { BackColor = Color.White, Padding = new Padding(10) };
            tableTab.Controls.Add(grid); tableTab.Controls.Add(tableHint); tableTab.Controls.Add(bar);

            // ---- free SQL
            var sqlBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Padding = new Padding(0, 6, 0, 6) };
            run.Font = Ui.Small; run.Padding = new Padding(10, 3, 10, 3);
            sqlBar.Controls.Add(run); sqlBar.Controls.Add(resultPick);
            var sqlTab = new TabPage("SQL") { BackColor = Color.White, Padding = new Padding(10) };
            sqlTab.Controls.Add(result); sqlTab.Controls.Add(sqlHint); sqlTab.Controls.Add(sqlBar); sqlTab.Controls.Add(sql);
            sqlHint.Text = "Type any SQL; several statements are separated by ';'. Name the database with the table: acore_world.creature_template.";

            var tabs = new TabControl { Dock = DockStyle.Fill, Font = Ui.Base };
            tabs.TabPages.Add(tableTab); tabs.TabPages.Add(sqlTab);
            var note = new Label
            {
                Dock = DockStyle.Bottom, Height = 36, Font = Ui.Small, ForeColor = Ui.Muted, Padding = new Padding(2, 4, 0, 0),
                Text = "The worldserver reads most world tables only when it starts. A change shows in the game after a restart of the server, or after the matching " +
                       "\".reload …\" command in Game master tools (for example \"reload creature_template 1234\"). Characters that are online are written back by the server: change those while they are logged out."
            };
            var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 16, 10) };
            right.Controls.Add(tabs); right.Controls.Add(note);

            Controls.Add(right); Controls.Add(left); Controls.Add(top);

            grid.CellValueChanged += (s, e) => { if (!loading && e.RowIndex >= 0) Mark(e.RowIndex, e.ColumnIndex); };
            grid.CellFormatting += (s, e) => Paint(grid, e);
            result.CellFormatting += (s, e) => Paint(result, e);
            grid.DataError += (s, e) => { e.ThrowException = false; };
            tree.AfterSelect += (s, e) => { if (e.Node != null && e.Node.Parent != null) Open(e.Node.Parent.Text, e.Node.Text); };
            tableFilter.TextChanged += (s, e) => FillTree();
            apply.Click += (s, e) => { page = 0; LoadPage(); };
            where.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; page = 0; LoadPage(); } };
            prev.Click += (s, e) => { if (page > 0) { --page; LoadPage(); } };
            next.Click += (s, e) => { if (more) { ++page; LoadPage(); } };
            discard.Click += (s, e) => LoadPage(true);
            addRow.Click += (s, e) => AddRow();
            delRow.Click += (s, e) => DeleteRows();
            save.Click += (s, e) => Save();
            run.Click += (s, e) => RunSql();
            export.Click += (s, e) => Export();
            import.Click += (s, e) => Import();
            undo.Click += (s, e) => UndoLast();
            resultPick.SelectedIndexChanged += (s, e) => ShowResult(resultPick.SelectedIndex);
            allow.CheckedChanged += (s, e) => Allow();
            backup.Click += (s, e) => { if (Leave("Back up the server now? This window closes; the backup shows its progress and can be restored under \"Backups …\".")) { BackUpNow = true; Close(); } };
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.F5 || (e.Control && e.KeyCode == Keys.Enter)) { e.SuppressKeyPress = true; if (tabs.SelectedTab == sqlTab) RunSql(); else LoadPage(); }
            };
            FormClosing += (s, e) => { if (!BackUpNow && Dirty && !Ui.Confirm(this, "There are changes that were not saved. Close anyway?")) e.Cancel = true; };
            Shown += (s, e) => LoadTree();
            Buttons();
        }

        // ---------------------------------------------------------------- helpers

        void UI(Action a) { try { if (IsHandleCreated && !IsDisposed) BeginInvoke(a); } catch { } }
        void Background(Action work, Action<string> failed) { Background(work, null, failed); }

        /// <summary>Runs work off the window's thread; "then" follows on the window's thread once the window is free again.</summary>
        void Background(Action work, Action then, Action<string> failed)
        {
            busy = true; Buttons();
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                string error = null;
                try { work(); } catch (Exception ex) { error = ex.Message; }
                UI(() => { busy = false; Buttons(); if (error != null) failed(error); else if (then != null) then(); });
            });
        }

        static string Id(string name) { return "`" + name.Replace("`", "``") + "`"; }
        DbLogin Login { get { return DbLogin.FromConfig(inst); } }
        bool Dirty { get { return deleted.Count > 0 || grid.Rows.Cast<DataGridViewRow>().Any(r => IsDirty(r)); } }
        bool HasKey { get { return columns.Any(c => c.Key); } }
        static bool IsDirty(DataGridViewRow r) { return r.Tag is RowState && (((RowState)r.Tag).Original == null || r.Cells.Cast<DataGridViewCell>().Any(c => c.Tag as string == "changed")); }

        void Buttons()
        {
            bool edit = allow.Checked && table != null && HasKey && !busy;
            grid.ReadOnly = !edit;
            if (edit) foreach (DataGridViewColumn c in grid.Columns) c.ReadOnly = columns[c.Index].Binary;
            addRow.Enabled = delRow.Enabled = edit;
            save.Enabled = discard.Enabled = edit && Dirty;
            prev.Enabled = !busy && table != null && page > 0;
            next.Enabled = !busy && table != null && more;
            apply.Enabled = !busy && table != null;
            run.Enabled = !busy;
            export.Enabled = import.Enabled = !busy && tablesOf.Count > 0;
            undo.Enabled = !busy && tablesOf.Count > 0 && UndoFiles().Count > 0;
        }

        /// <summary>Asks before unsaved changes are thrown away.</summary>
        bool Leave(string question)
        {
            return Ui.Confirm(this, Dirty ? "There are changes that were not saved; they are lost. " + question : question);
        }

        static void Paint(DataGridView g, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            if (e.Value == null) { e.Value = "NULL"; e.CellStyle.ForeColor = NullColor; e.CellStyle.Font = new Font(g.Font, FontStyle.Italic); e.FormattingApplied = true; }
        }

        // ---------------------------------------------------------------- databases and tables

        void LoadTree()
        {
            tableHint.Text = "Reading the databases …";
            Dictionary<string, List<string>> found = null;
            Background(() =>
            {
                found = new Dictionary<string, List<string>>();
                var t = MySql.Tables(inst, Login, "SELECT TABLE_SCHEMA, TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA NOT IN " +
                    "('information_schema', 'performance_schema', 'mysql', 'sys') ORDER BY TABLE_SCHEMA, TABLE_NAME;", 100000);
                foreach (var row in t.Count > 0 ? t[0].Rows : new List<string[]>())
                {
                    if (!found.ContainsKey(row[0])) found[row[0]] = new List<string>();
                    found[row[0]].Add(row[1]);
                }
                UI(() => { tablesOf = found; FillTree(); tableHint.Text = found.Count + " database(s). Choose a table on the left."; });
            }, error => tableHint.Text = "The databases could not be read: " + error);
        }

        void FillTree()
        {
            string filter = tableFilter.Text.Trim().ToLowerInvariant();
            tree.BeginUpdate();
            tree.Nodes.Clear();
            // The server's own four first, in the order one needs them.
            string[] first = { "acore_world", "acore_characters", "acore_auth", "acore_playerbots" };
            foreach (var name in first.Where(tablesOf.ContainsKey).Concat(tablesOf.Keys.Where(k => !first.Contains(k)).OrderBy(k => k)))
            {
                var matches = tablesOf[name].Where(t => filter.Length == 0 || t.ToLowerInvariant().Contains(filter)).ToList();
                if (filter.Length > 0 && matches.Count == 0) continue;
                var node = tree.Nodes.Add(name);
                foreach (var t in matches) node.Nodes.Add(t);
                if (filter.Length > 0 && matches.Count <= 60) node.Expand();
            }
            tree.EndUpdate();
        }

        void Open(string database, string name)
        {
            if (busy || (database == db && name == table)) return;
            if (Dirty && !Ui.Confirm(this, "There are changes that were not saved; they are lost. Open " + name + " anyway?")) return;
            db = database; table = name; page = 0; where.Text = "";
            LoadPage(true);
        }

        void LoadPage() { LoadPage(false); }

        void LoadPage(bool force)
        {
            if (table == null || busy) return;
            if (!force && Dirty && !Ui.Confirm(this, "There are changes that were not saved; they are lost. Read the table again?")) return;
            string database = db, name = table, filter = where.Text.Trim(); int at = page;
            tableName.Text = database + "." + name;
            tableHint.Text = "Reading " + name + " …";
            Background(() =>
            {
                var meta = MySql.Tables(inst, Login, "SELECT COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE, COLUMN_KEY, EXTRA FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = " +
                    MySql.Quote(database) + " AND TABLE_NAME = " + MySql.Quote(name) + " ORDER BY ORDINAL_POSITION;", 5000);
                var cols = (meta.Count > 0 ? meta[0].Rows : new List<string[]>()).Select(r => new ColumnInfo
                {
                    Name = r[0], Type = r[1] ?? "", Nullable = r[2] == "YES", Key = r[3] == "PRI", Auto = (r[4] ?? "").IndexOf("auto_increment", StringComparison.OrdinalIgnoreCase) >= 0,
                    Binary = Regex.IsMatch(r[1] ?? "", @"^(tiny|medium|long)?blob|^(var)?binary|^bit", RegexOptions.IgnoreCase)
                }).ToList();
                if (cols.Count == 0) throw new InvalidOperationException("The table " + name + " was not found.");
                string order = cols.Any(c => c.Key) ? " ORDER BY " + string.Join(", ", cols.Where(c => c.Key).Select(c => Id(c.Name))) : "";
                // One row more than a page holds tells whether there is a next page.
                var data = MySql.Tables(inst, Login, "SELECT * FROM " + Id(database) + "." + Id(name) + (filter.Length > 0 ? " WHERE " + filter : "") + order +
                    " LIMIT " + (PageSize + 1) + " OFFSET " + ((long)at * PageSize) + ";", PageSize + 1);
                var rows = data.Count > 0 ? data[0].Rows : new List<string[]>();
                UI(() => Show(cols, rows, at));
            }, error => { tableHint.Text = "Not read: " + error; });
        }

        void Show(List<ColumnInfo> cols, List<string[]> rows, int at)
        {
            loading = true;
            columns = cols; deleted.Clear();
            more = rows.Count > PageSize;
            grid.SuspendLayout();
            grid.Rows.Clear(); grid.Columns.Clear();
            foreach (var c in cols)
            {
                int n = grid.Columns.Add(c.Name, c.Name);
                var col = grid.Columns[n];
                col.SortMode = DataGridViewColumnSortMode.NotSortable;
                col.ToolTipText = c.Type + (c.Key ? ", primary key" : "") + (c.Nullable ? ", may be NULL" : "") + (c.Auto ? ", counts up by itself" : "");
                col.Width = Math.Max(60, Math.Min(260, 24 + c.Name.Length * 7));
                if (c.Key) col.HeaderCell.Style.Font = new Font(Ui.Small, FontStyle.Bold);
            }
            // All rows at once: adding them one by one makes a wide table crawl.
            grid.Rows.AddRange(rows.Take(PageSize).Select(r =>
            {
                var row = new DataGridViewRow { Height = 21, Tag = new RowState { Original = r } };
                row.CreateCells(grid, r.Take(cols.Count).Cast<object>().ToArray());
                return row;
            }).ToArray());
            // Wide enough for what the first rows hold, within reason.
            for (int i = 0; i < cols.Count; i++)
            {
                int longest = rows.Take(40).Select(r => i < r.Length && r[i] != null ? r[i].Length : 4).DefaultIfEmpty(0).Max();
                grid.Columns[i].Width = Math.Max(grid.Columns[i].Width, Math.Min(260, 24 + longest * 7));
            }
            grid.ResumeLayout();
            loading = false;
            int shown = Math.Min(rows.Count, PageSize);
            pageInfo.Text = shown == 0 ? "no rows" : "rows " + ((long)at * PageSize + 1) + " to " + ((long)at * PageSize + shown) + (more ? ", more follow" : "");
            tableHint.Text = !HasKey ? "This table has no primary key, so rows cannot be told apart: it can be read here and changed in the SQL tab."
                : allow.Checked ? "Click a cell to change it; type NULL for an empty value where the column allows it. Nothing is written until \"Save changes\"."
                : "Tick \"Allow changes\" at the top to edit. The primary key is shown in bold.";
            Buttons();
        }

        // ---------------------------------------------------------------- editing

        void Mark(int rowIndex, int columnIndex)
        {
            var row = grid.Rows[rowIndex]; var state = row.Tag as RowState;
            if (state == null) return;
            var cell = row.Cells[columnIndex];
            string now = Value(cell, columns[columnIndex]);
            if (state.Original == null) { cell.Tag = "changed"; }
            else if (now == state.Original[columnIndex]) { cell.Tag = null; cell.Style.BackColor = Color.Empty; }
            else { cell.Tag = "changed"; cell.Style.BackColor = Changed; }
            Buttons();
        }

        /// <summary>What a cell stands for: its text, or NULL when it is empty of a value or says NULL in a column that allows it.</summary>
        static string Value(DataGridViewCell cell, ColumnInfo column)
        {
            string text = cell.Value as string;
            if (text == null) return null;
            return column.Nullable && text == "NULL" ? null : text;
        }

        void AddRow()
        {
            loading = true;
            int n = grid.Rows.Add();
            var row = grid.Rows[n];
            row.Tag = new RowState { Original = null };
            row.DefaultCellStyle.BackColor = Added;
            for (int i = 0; i < columns.Count; i++) row.Cells[i].Value = "";
            loading = false;
            grid.CurrentCell = row.Cells[0];
            tableHint.Text = "Fill in the new row. Cells left empty get the column's default value.";
            Buttons();
        }

        void DeleteRows()
        {
            var rows = grid.SelectedCells.Cast<DataGridViewCell>().Select(c => c.OwningRow).Distinct().ToList();
            if (rows.Count == 0) return;
            if (!Ui.Confirm(this, "Delete " + rows.Count + " row(s)? They are removed from the table when you press \"Save changes\".")) return;
            foreach (var row in rows)
            {
                var state = row.Tag as RowState;
                if (state != null && state.Original != null) deleted.Add(state.Original);
                grid.Rows.Remove(row);
            }
            Buttons();
        }

        static string Literal(string value, ColumnInfo column)
        {
            if (value == null) return "NULL";
            if (column.Binary && Regex.IsMatch(value, "^0x([0-9A-Fa-f]{2})*$")) return value.Length == 2 ? "''" : value;
            return MySql.Quote(value);
        }

        string KeyClause(string[] original)
        {
            var parts = new List<string>();
            for (int i = 0; i < columns.Count; i++)
                if (columns[i].Key) parts.Add(Id(columns[i].Name) + " <=> " + Literal(original[i], columns[i]));
            return string.Join(" AND ", parts);
        }

        void Save()
        {
            if (busy || !Dirty) return;
            grid.EndEdit();
            string target = Id(db) + "." + Id(table);
            var statements = new List<string>(); int updates = 0, inserts = 0;
            // For every statement the one that takes it back, to be run in reverse order.
            var back = new List<string>(); bool wholeTable = false;
            string allColumns = string.Join(", ", columns.Select(c => Id(c.Name)));
            foreach (var original in deleted)
            {
                statements.Add("DELETE FROM " + target + " WHERE " + KeyClause(original) + " LIMIT 1;");
                back.Add("INSERT INTO " + target + " (" + allColumns + ") VALUES (" + string.Join(", ", columns.Select((c, i) => Literal(original[i], c))) + ");");
            }
            foreach (DataGridViewRow row in grid.Rows)
            {
                var state = row.Tag as RowState;
                if (state == null || !IsDirty(row)) continue;
                if (state.Original == null)
                {
                    var names = new List<string>(); var values = new List<string>();
                    for (int i = 0; i < columns.Count; i++)
                    {
                        string v = Value(row.Cells[i], columns[i]);
                        if (v == "") continue;                  // left empty: the column's default
                        names.Add(Id(columns[i].Name)); values.Add(Literal(v, columns[i]));
                    }
                    statements.Add("INSERT INTO " + target + " (" + string.Join(", ", names) + ") VALUES (" + string.Join(", ", values) + ");");
                    ++inserts;
                    // Taking a new row back needs its key; one the database counts up itself is not known here.
                    var now = columns.Select((c, i) => Value(row.Cells[i], c)).ToArray();
                    if (columns.Where((c, i) => c.Key && string.IsNullOrEmpty(now[i])).Any()) wholeTable = true;
                    else back.Add("DELETE FROM " + target + " WHERE " + KeyClause(now) + " LIMIT 1;");
                }
                else
                {
                    var sets = new List<string>();
                    for (int i = 0; i < columns.Count; i++)
                        if (row.Cells[i].Tag as string == "changed") sets.Add(Id(columns[i].Name) + " = " + Literal(Value(row.Cells[i], columns[i]), columns[i]));
                    if (sets.Count == 0) continue;
                    statements.Add("UPDATE " + target + " SET " + string.Join(", ", sets) + " WHERE " + KeyClause(state.Original) + " LIMIT 1;");
                    ++updates;
                    var now = columns.Select((c, i) => Value(row.Cells[i], c)).ToArray();
                    var old = new List<string>();
                    for (int i = 0; i < columns.Count; i++)
                        if (row.Cells[i].Tag as string == "changed") old.Add(Id(columns[i].Name) + " = " + Literal(state.Original[i], columns[i]));
                    back.Add("UPDATE " + target + " SET " + string.Join(", ", old) + " WHERE " + KeyClause(now) + " LIMIT 1;");
                }
            }
            if (statements.Count == 0) return;
            string summary = string.Join(", ", new[] { updates > 0 ? updates + " row(s) changed" : null, inserts > 0 ? inserts + " added" : null, deleted.Count > 0 ? deleted.Count + " deleted" : null }.Where(x => x != null));
            if (!LiveOk(new[] { db })) return;
            if (!Ui.Confirm(this, "Write to " + db + "." + table + ": " + summary + "?")) return;
            // All or nothing where the table allows it (InnoDB); the client stops at the first error.
            string script = "START TRANSACTION;\n" + string.Join("\n", statements) + "\nCOMMIT;\n";
            tableHint.Text = "Saving …";
            string undoFile = NewUndoFile("edit of " + db + "." + table), database = db, name = table; var known = tablesOf;
            back.Reverse();
            Background(() =>
            {
                if (wholeTable) WriteCopies(undoFile, new Dictionary<string, SortedSet<string>> { { database, new SortedSet<string> { name } } }, known);
                else
                {
                    File.WriteAllText(undoFile, "-- " + Product.Name + " " + RowsMark + ", " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + ", databases: " + database +
                        "\nSTART TRANSACTION;\n" + string.Join("\n", back) + "\nCOMMIT;\n", new UTF8Encoding(false));
                    PruneUndo();
                }
                try { MySql.Tables(inst, Login, script, 1); }
                catch { try { File.Delete(undoFile); } catch { } throw; }
            }, () => { deleted.Clear(); LoadPage(true); }, error => { tableHint.Text = "Not saved."; Ui.Error(this, "The database refused the changes; nothing was written (tables of the MyISAM kind keep what came before the error):\n\n" + error); });
        }

        // ---------------------------------------------------------------- free SQL

        /// <summary>Does this text only look things up? Checked statement by statement, comments and quoted text set aside.</summary>
        internal static bool ReadsOnly(string text)
        {
            string bare = Regex.Replace(text, @"'(?:[^'\\]|\\.|'')*'|""(?:[^""\\]|\\.|"""")*""|`[^`]*`|/\*.*?\*/|--[^\r\n]*|#[^\r\n]*", " ", RegexOptions.Singleline);
            foreach (var statement in bare.Split(';'))
            {
                string s = statement.Trim();
                if (s.Length == 0) continue;
                if (!Regex.IsMatch(s, @"^\(*\s*(select|show|describe|desc|explain|use|with|table|values)\b", RegexOptions.IgnoreCase)) return false;
                if (Regex.IsMatch(s, @"\binto\s+(outfile|dumpfile)\b|\bfor\s+update\b", RegexOptions.IgnoreCase)) return false;
            }
            return true;
        }

        void RunSql()
        {
            string text = sql.SelectionLength > 0 ? sql.SelectedText : sql.Text;
            if (busy || text.Trim().Length == 0) return;
            if (!allow.Checked && !ReadsOnly(text)) { Ui.Error(this, "This would change something. Tick \"Allow changes\" at the top first."); return; }
            // Statements that change something: keep the tables they name first, so the change can be undone.
            Dictionary<string, SortedSet<string>> touched = null; string undoFile = null; var known = tablesOf;
            if (allow.Checked && !ReadsOnly(text))
            {
                var plan = Scan(new StringReader(text));
                touched = Touched(plan, null);
                if (plan.Other || plan.Loose || touched.Count == 0 || touched.Keys.Any(d => !known.ContainsKey(d)))
                {
                    if (!Ui.Confirm(this, Product.Name + " cannot tell for sure which tables this changes" + (plan.Loose ? " (name the database with each table: acore_world.creature_template)" : "") +
                        ", so \"Undo last change\" will not be able to take it back.\n\nRun it anyway?")) return;
                    if (!LiveOk(touched.Keys)) return;
                    touched = null;
                }
                else if (!LiveOk(touched.Keys)) return;
                else undoFile = NewUndoFile("SQL on " + string.Join(", ", touched.SelectMany(d => d.Value.Select(t => d.Key + "." + t)).Take(3)) + (touched.Sum(d => d.Value.Count) > 3 ? " and more" : ""));
            }
            sqlHint.Text = undoFile != null ? "Keeping a copy of " + touched.Sum(d => d.Value.Count) + " table(s) for the undo, then running … (large tables take a moment)" : "Running …";
            DateTime start = DateTime.Now;
            // The number of rows the last statement changed comes back as a result of its own.
            // Without the switch the session itself is read-only, whatever the text says.
            string script = (allow.Checked ? "" : "SET SESSION TRANSACTION READ ONLY;\n") + text.TrimEnd() + "\n;SELECT ROW_COUNT() AS afk_rows_changed;\n";
            Background(() =>
            {
                if (undoFile != null)
                {
                    try { WriteCopies(undoFile, touched, known); }
                    catch (Exception ex) { throw new InvalidOperationException("The copy for \"Undo last change\" could not be made, so nothing was run.\n\n" + ex.Message); }
                }
                var list = MySql.Tables(inst, Login, script, QueryRows);
                UI(() =>
                {
                    string changed = null;
                    if (list.Count > 0 && list[list.Count - 1].Columns.Count == 1 && list[list.Count - 1].Columns[0] == "afk_rows_changed")
                    {
                        string v = list[list.Count - 1].Rows.Count > 0 ? list[list.Count - 1].Rows[0][0] : null; int n;
                        if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) && n >= 0) changed = n + " row(s) changed by the last statement";
                        list.RemoveAt(list.Count - 1);
                    }
                    results = list;
                    resultPick.Items.Clear();
                    for (int i = 0; i < list.Count; i++)
                        resultPick.Items.Add("Result " + (i + 1) + " of " + list.Count + ", " + list[i].Total + " row(s): " + (list[i].Statement.Length > 60 ? list[i].Statement.Substring(0, 60) + " …" : list[i].Statement));
                    resultPick.Visible = list.Count > 1;
                    if (list.Count > 0) { resultPick.SelectedIndex = list.Count - 1; ShowResult(list.Count - 1); } else { result.Rows.Clear(); result.Columns.Clear(); }
                    string took = (DateTime.Now - start).TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";
                    Buttons();
                    sqlHint.Text = "Done in " + took + (changed != null ? ": " + changed : "") + (list.Count == 0 && changed == null ? ". No rows came back." : "") +
                        (list.Count > 0 ? ". " + list[list.Count - 1].Total + " row(s)" + (list[list.Count - 1].Total > QueryRows ? ", the first " + QueryRows + " are shown" : "") + "." : ".");
                });
            }, error => { sqlHint.Text = "Stopped with an error. Statements before it were carried out" + (undoFile != null && File.Exists(undoFile) ? "; \"Undo last change\" takes them back." : "."); Ui.Error(this, error); });
        }

        void ShowResult(int index)
        {
            if (index < 0 || index >= results.Count) return;
            var t = results[index];
            result.SuspendLayout();
            result.Rows.Clear(); result.Columns.Clear();
            for (int i = 0; i < t.Columns.Count; i++)
            {
                int n = result.Columns.Add("c" + i, t.Columns[i]);
                int longest = t.Rows.Take(40).Select(r => i < r.Length && r[i] != null ? r[i].Length : 4).DefaultIfEmpty(0).Max();
                result.Columns[n].Width = Math.Max(60, Math.Min(320, 24 + Math.Max(longest, t.Columns[i].Length) * 7));
            }
            result.Rows.AddRange(t.Rows.Select(r =>
            {
                var row = new DataGridViewRow { Height = 21 };
                row.CreateCells(result, r.Take(t.Columns.Count).Cast<object>().ToArray());
                return row;
            }).ToArray());
            result.ResumeLayout();
        }

        // ---------------------------------------------------------------- export and import

        /// <summary>The database the user is looking at: that of the open table, else that of the chosen node, else the world.</summary>
        string CurrentDb
        {
            get
            {
                if (db != null) return db;
                var n = tree.SelectedNode;
                if (n != null) return n.Parent != null ? n.Parent.Text : n.Text;
                return tablesOf.ContainsKey("acore_world") ? "acore_world" : tablesOf.Keys.FirstOrDefault();
            }
        }

        /// <summary>What people call the server's databases.</summary>
        static string Friendly(string database)
        {
            switch (database)
            {
                case "acore_world": return "World: creatures, items, quests, loot  (acore_world)";
                case "acore_characters": return "Characters and what they own  (acore_characters)";
                case "acore_auth": return "Accounts and realm  (acore_auth)";
                case "acore_playerbots": return "Playerbots  (acore_playerbots)";
                case "afk_modules": return "AFK Realm's record of module changes  (afk_modules)";
                default: return database;
            }
        }

        IEnumerable<string> DatabasesInOrder()
        {
            string[] first = { "acore_world", "acore_characters", "acore_auth", "acore_playerbots" };
            return first.Where(tablesOf.ContainsKey).Concat(tablesOf.Keys.Where(k => !first.Contains(k)).OrderBy(k => k));
        }

        void Export()
        {
            if (busy || tablesOf.Count == 0) return;
            // What is open or chosen on the left starts out ticked.
            string preDb = db, preTable = table; var node = tree.SelectedNode;
            if (preTable == null && node != null) { preDb = node.Parent != null ? node.Parent.Text : node.Text; preTable = node.Parent != null ? node.Text : null; }

            var chosen = new Dictionary<string, List<string>>();
            using (var f = new Form { Text = Product.Name + " – Export", Font = Ui.Base, BackColor = Color.White, ShowIcon = false, MinimizeBox = false, MaximizeBox = false,
                StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(520, 560), MinimumSize = new Size(420, 400), Padding = new Padding(16) })
            {
                var pick = new TreeView { Dock = DockStyle.Fill, CheckBoxes = true, Font = Ui.Base, BorderStyle = BorderStyle.FixedSingle, ShowLines = false };
                var count = new Label { Dock = DockStyle.Bottom, Height = 24, Font = Ui.Small, ForeColor = Ui.Muted, TextAlign = ContentAlignment.MiddleLeft };
                var ok = Ui.Primary("Export …"); var cancel = Ui.Secondary("Cancel"); ok.DialogResult = DialogResult.OK; cancel.DialogResult = DialogResult.Cancel;
                var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 6, 0, 0) }; buttons.Controls.Add(ok); buttons.Controls.Add(cancel);
                var head = new Label { Dock = DockStyle.Top, Height = 64, Text = "Tick what you want to export: a whole database, or open it and tick single tables. Everything goes into one file that can be imported again here or on another server." };
                TreeNode show = null;
                foreach (var name in DatabasesInOrder())
                {
                    var d = pick.Nodes.Add(name, Friendly(name)); d.Tag = name;
                    foreach (var t in tablesOf[name])
                    {
                        var n = d.Nodes.Add(t); n.Tag = t;
                        if (name == preDb && t == preTable) { n.Checked = true; show = n; }
                    }
                    if (name == preDb && preTable == null) { d.Checked = true; foreach (TreeNode n in d.Nodes) n.Checked = true; show = d; }
                }
                Action recount = () =>
                {
                    int tables = pick.Nodes.Cast<TreeNode>().Sum(d => d.Nodes.Cast<TreeNode>().Count(n => n.Checked));
                    count.Text = tables == 0 ? "Nothing ticked yet." : tables + " table(s) ticked.";
                    ok.Enabled = tables > 0;
                };
                bool ticking = false;
                pick.AfterCheck += (s, e) =>
                {
                    if (ticking) return;
                    ticking = true;
                    if (e.Node.Parent == null) foreach (TreeNode n in e.Node.Nodes) n.Checked = e.Node.Checked;      // a database: all its tables
                    else e.Node.Parent.Checked = e.Node.Parent.Nodes.Cast<TreeNode>().All(n => n.Checked);
                    ticking = false; recount();
                };
                f.Controls.Add(pick); f.Controls.Add(count); f.Controls.Add(buttons); f.Controls.Add(head); f.AcceptButton = ok; f.CancelButton = cancel;
                f.Shown += (s, e) => { if (show != null) { show.EnsureVisible(); pick.SelectedNode = show; } recount(); };
                if (f.ShowDialog(this) != DialogResult.OK) return;
                foreach (TreeNode d in pick.Nodes)
                {
                    var ticked = d.Nodes.Cast<TreeNode>().Where(n => n.Checked).Select(n => (string)n.Tag).ToList();
                    if (ticked.Count > 0) chosen[(string)d.Tag] = ticked.Count == d.Nodes.Count ? null : ticked;        // null: the whole database
                }
            }
            if (chosen.Count == 0) return;

            string suggestion = chosen.Count == 1 ? chosen.Keys.First() + (chosen.Values.First() != null && chosen.Values.First().Count == 1 ? "." + chosen.Values.First()[0] : "") : "server-export";
            string file;
            using (var d = new SaveFileDialog { Filter = "SQL file (*.sql)|*.sql", OverwritePrompt = true, FileName = suggestion + ".sql" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                file = d.FileName;
            }
            Label hint = tableTabShown() ? tableHint : sqlHint;
            hint.Text = "Exporting … large databases take a while.";
            Background(() =>
            {
                try
                {
                    using (var dst = File.Create(file))
                    {
                        var head = new UTF8Encoding(false).GetBytes("-- " + Product.Name + " export\n");
                        dst.Write(head, 0, head.Length);
                        foreach (var name in chosen.Keys) MySql.DumpTables(inst, Login, name, chosen[name], dst);
                    }
                }
                catch { try { File.Delete(file); } catch { } throw; }
            }, () =>
            {
                hint.Text = "Exported to " + file + " (" + Size(new FileInfo(file).Length) + ").";
                if (Ui.Confirm(this, "Exported to\n" + file + "\n\nShow the file in its folder?"))
                    try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + file + "\""); } catch { }
            }, error => { hint.Text = "Not exported."; Ui.Error(this, "The export failed:\n\n" + error); });
        }

        bool tableTabShown() { return tableHint.Visible; }
        static string Size(long bytes) { return bytes >= 1048576 ? (bytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB" : Math.Max(1, bytes / 1024) + " KB"; }

        /// <summary>What an SQL file is going to do, as far as its statements tell.</summary>
        internal class FilePlan
        {
            public readonly SortedSet<string> Rebuilt = new SortedSet<string>(), Created = new SortedSet<string>(), Written = new SortedSet<string>(),
                Emptied = new SortedSet<string>(), Altered = new SortedSet<string>(), Databases = new SortedSet<string>();
            public string Named;                        // the database the file says it belongs to
            public bool Partial, Other, Loose;         // Loose: statements that name no database, so one has to be chosen
        }

        internal static FilePlan Scan(string file)
        {
            using (var r = new StreamReader(file, new UTF8Encoding(false))) return Scan(r);
        }

        internal static FilePlan Scan(TextReader r)
        {
            const long limit = 96L * 1024 * 1024;
            var plan = new FilePlan();
            var verb = new Regex(@"(?:^|;)\s*(DROP\s+TABLE(?:\s+IF\s+EXISTS)?|CREATE\s+TABLE(?:\s+IF\s+NOT\s+EXISTS)?|INSERT(?:\s+IGNORE)?\s+INTO|REPLACE\s+INTO|DELETE\s+FROM|TRUNCATE(?:\s+TABLE)?|ALTER\s+TABLE|UPDATE|USE|DROP\s+DATABASE|CREATE\s+DATABASE(?:\s+IF\s+NOT\s+EXISTS)?)\s+((?:`[^`]+`|[A-Za-z0-9_$]+)(?:\.(?:`[^`]+`|[A-Za-z0-9_$]+))?)", RegexOptions.IgnoreCase);
            {
                long read = 0; string line, use = null;
                while ((line = r.ReadLine()) != null)
                {
                    read += line.Length + 1;
                    if (read > limit) { plan.Partial = true; break; }
                    var named = Regex.Match(line.Length > 200 ? line.Substring(0, 200) : line, @"^--\s*(?:Host:.*)?Database:\s*`?([A-Za-z0-9_$]+)");
                    if (named.Success && plan.Named == null) plan.Named = named.Groups[1].Value;
                    foreach (Match m in verb.Matches(line))
                    {
                        string what = Regex.Replace(m.Groups[1].Value.ToUpperInvariant(), @"\s+", " "), target = m.Groups[2].Value.Replace("`", "");
                        if (what == "USE") { use = target; plan.Databases.Add(target); continue; }
                        if (what.Contains("DATABASE")) { plan.Databases.Add(target); plan.Other = true; continue; }
                        if (target.Contains(".")) plan.Databases.Add(target.Substring(0, target.IndexOf('.')));
                        else if (use != null) target = use + "." + target;
                        else plan.Loose = true;
                        if (what.StartsWith("DROP TABLE")) plan.Rebuilt.Add(target);
                        else if (what.StartsWith("CREATE TABLE")) plan.Created.Add(target);
                        else if (what.StartsWith("DELETE") || what.StartsWith("TRUNCATE")) plan.Emptied.Add(target);
                        else if (what.StartsWith("ALTER")) plan.Altered.Add(target);
                        else plan.Written.Add(target);
                    }
                }
            }
            plan.Created.ExceptWith(plan.Rebuilt);
            return plan;
        }

        static string Names(IEnumerable<string> names)
        {
            var list = names.ToList();
            return string.Join(", ", list.Take(12)) + (list.Count > 12 ? " and " + (list.Count - 12) + " more" : "");
        }

        void Import()
        {
            if (busy) return;
            if (!allow.Checked) { Ui.Error(this, "Importing changes the database. Tick \"Allow changes\" at the top first."); return; }
            if (Dirty && !Ui.Confirm(this, "There are changes in the table that were not saved; they are lost. Go on?")) return;
            string file;
            using (var d = new OpenFileDialog { Filter = "SQL file (*.sql)|*.sql|All files (*.*)|*.*", Title = "Choose the SQL file to import" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                file = d.FileName;
            }
            FilePlan plan;
            try { plan = Scan(file); } catch (Exception ex) { Ui.Error(this, "The file could not be read:\n\n" + ex.Message); return; }

            // In one sentence, and the tables by name below it for those who want to know.
            var parts = new List<string>();
            if (plan.Rebuilt.Count > 0) parts.Add("replaces " + plan.Rebuilt.Count + " table(s) completely");
            if (plan.Created.Count > 0) parts.Add("creates " + plan.Created.Count + " new table(s)");
            if (plan.Altered.Count > 0) parts.Add("changes the structure of " + plan.Altered.Count + " table(s)");
            if (plan.Emptied.Count > 0) parts.Add("deletes rows from " + plan.Emptied.Count + " table(s)");
            int writes = plan.Written.Count(t => !plan.Rebuilt.Contains(t) && !plan.Created.Contains(t));
            if (writes > 0) parts.Add("writes rows into " + writes + " table(s)");
            if (plan.Other) parts.Add("creates or deletes whole databases");
            string summary = parts.Count == 0 ? "No changes to tables were recognised in this file; it may still do other things."
                : "This file " + (parts.Count == 1 ? parts[0] : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[parts.Count - 1]) + "." + (plan.Partial ? " (It is large; only its beginning was looked at.)" : "");
            var text = new StringBuilder();
            Action<string, ICollection<string>> say = (title, names) => { if (names.Count > 0) text.AppendLine(title + ": " + Names(names)); };
            say("Replaced completely", plan.Rebuilt);
            say("Created", plan.Created);
            say("Structure changed", plan.Altered);
            say("Rows deleted from", plan.Emptied);
            say("Rows written into", plan.Written.Where(t => !plan.Rebuilt.Contains(t) && !plan.Created.Contains(t)).ToList());

            // A file that says where it belongs (every export from here does) needs no choice.
            var named = plan.Databases.Where(tablesOf.ContainsKey).ToList();
            bool choose = plan.Loose || named.Count == 0;
            string target, fits = null;
            using (var f = new Form { Text = Product.Name + " – Import", Font = Ui.Base, BackColor = Color.White, ShowIcon = false, FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(560, 380), Padding = new Padding(16) })
            {
                var pick = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 400, Font = Ui.Base, Margin = new Padding(0, 4, 0, 0) };
                var order = DatabasesInOrder().ToList();
                foreach (var name in order) pick.Items.Add(Friendly(name));
                // The database most of the file's tables are found in is the likeliest one.
                var loose = plan.Rebuilt.Concat(plan.Created).Concat(plan.Altered).Concat(plan.Emptied).Concat(plan.Written).Where(t => !t.Contains(".")).Distinct().ToList();
                string home = order.Select(d => new { d, hits = loose.Count(t => tablesOf[d].Contains(t)) }).Where(x => x.hits > 0).OrderByDescending(x => x.hits).Select(x => x.d).FirstOrDefault();
                string guess = plan.Named != null && tablesOf.ContainsKey(plan.Named) ? plan.Named : home ?? (named.Count > 0 ? named[0] : tablesOf.ContainsKey("acore_world") ? "acore_world" : CurrentDb);
                if (home != null) fits = "Its tables are found in " + home + ".";
                pick.SelectedIndex = Math.Max(0, order.IndexOf(guess));
                var head = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
                head.Controls.Add(new Label { Text = "Import " + Path.GetFileName(file) + "  (" + Size(new FileInfo(file).Length) + ")", AutoSize = true, Font = Ui.Bold });
                if (choose)
                {
                    head.Controls.Add(new Label { Text = "Which database is this file for?", AutoSize = true, Margin = new Padding(0, 10, 0, 0) });
                    head.Controls.Add(pick);
                    head.Controls.Add(new Label { Text = fits ?? "The file does not say. The place you got it from usually does (world, characters or auth); most module files are for the world.",
                        AutoSize = true, MaximumSize = new Size(520, 0), Font = Ui.Small, ForeColor = Ui.Muted, Margin = new Padding(0, 2, 0, 0) });
                }
                else head.Controls.Add(new Label { Text = "Goes into: " + string.Join(", ", named), AutoSize = true, Margin = new Padding(0, 10, 0, 0) });
                head.Controls.Add(new Label { Text = summary, AutoSize = true, MaximumSize = new Size(520, 0), Margin = new Padding(0, 10, 0, 6) });
                var box = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = Ui.Small, BackColor = Ui.Panel, BorderStyle = BorderStyle.FixedSingle, Text = text.ToString().TrimEnd(), TabStop = false, Visible = text.Length > 0 };
                var foot = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
                foot.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(520, 0), Font = Ui.Small, ForeColor = Ui.Warn, Margin = new Padding(0, 8, 0, 0),
                    Text = "The whole file is carried out. Before that, AFK Realm keeps a copy of the tables the file touches, so \"Undo last change\" can put them back as they were. Still: only import files from a source you trust." });
                var ok = Ui.Primary("Import"); var cancel = Ui.Secondary("Cancel"); ok.DialogResult = DialogResult.OK; cancel.DialogResult = DialogResult.Cancel;
                var row = Ui.Row(); row.Margin = new Padding(0, 10, 0, 0); row.Controls.Add(ok); row.Controls.Add(cancel); foot.Controls.Add(row);
                f.Controls.Add(box); f.Controls.Add(foot); f.Controls.Add(head); f.CancelButton = cancel;
                f.Shown += (s, e) => { box.SelectionLength = 0; cancel.Focus(); };
                if (f.ShowDialog(this) != DialogResult.OK) return;
                target = choose ? order[pick.SelectedIndex] : named[0];
            }
            string where_ = choose ? target : string.Join(", ", named);

            // What to keep a copy of: the tables the file names, each in its database. When the file could not be
            // read to the end, or does things beyond tables, whole databases are kept instead.
            var touched = Touched(plan, target);
            if (plan.Partial || plan.Other || touched.Count == 0) { touched.Clear(); foreach (var d in named.Concat(choose ? new[] { target } : new string[0]).Distinct()) touched[d] = null; }
            var known = tablesOf;
            if (!LiveOk(touched.Keys)) return;
            string undoFile = NewUndoFile("import of " + Path.GetFileName(file));

            Label hint = tableTabShown() ? tableHint : sqlHint;
            hint.Text = "Keeping a copy of what the file touches, then importing " + Path.GetFileName(file) + " …";
            bool copied = false;
            Background(() =>
            {
                try { WriteCopies(undoFile, touched, known); }
                catch (Exception ex) { throw new InvalidOperationException("The copy for \"Undo last change\" could not be made, so nothing was imported.\n\n" + ex.Message); }
                copied = true;
                MySql.RunFile(inst, Login, target, file);
            }, () =>
            {
                MessageBox.Show(this, Path.GetFileName(file) + " was imported into " + where_ + ".\n\nThe worldserver reads most world tables only when it starts: restart the server for the change to show in the game.\n\n" +
                    "If it does not work out, \"Undo last change\" puts everything back as it was.", Product.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                Refill("Imported " + Path.GetFileName(file) + " into " + where_ + ".");
            }, error =>
            {
                if (!copied) { hint.Text = "Nothing was imported."; Ui.Error(this, error); return; }
                Ui.Error(this, "The import stopped with an error. Statements before it were carried out, so the database may hold only a part of the file. \"Undo last change\" puts everything back as it was.\n\n" + error);
                Refill("The import stopped with an error.");
            });
        }

        // ---------------------------------------------------------------- undo

        // Before anything is changed here - a save in the grid, statements in the SQL tab, an import - what is needed
        // to take it back is written to a file: the old rows for a save, whole tables otherwise. "Undo last change"
        // runs the newest file and removes it, so pressing it again goes one step further back.

        const string RowsMark = "undo (rows)", TablesMark = "undo (tables)";
        string UndoFolder { get { return Path.Combine(inst.Root, "Backups", "undo"); } }

        /// <summary>The undo files, newest first.</summary>
        List<string> UndoFiles()
        {
            try { return Directory.Exists(UndoFolder) ? Directory.GetFiles(UndoFolder, "*.sql").OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal).ToList() : new List<string>(); }
            catch { return new List<string>(); }
        }

        string NewUndoFile(string label)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) label = label.Replace(c, '_');
            if (label.Length > 90) label = label.Substring(0, 90);
            Directory.CreateDirectory(UndoFolder);
            return Path.Combine(UndoFolder, DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + " " + label.Trim() + ".sql");
        }

        /// <summary>Keeps the newest ten files, and of those only as many as fit into 2 GB (the newest always stays).</summary>
        void PruneUndo()
        {
            long total = 0; int n = 0;
            foreach (var f in UndoFiles())
            {
                try { total += new FileInfo(f).Length; if (++n > 10 || (n > 1 && total > 2L * 1024 * 1024 * 1024)) File.Delete(f); } catch { }
            }
        }

        /// <summary>The tables a text names, by database; names without a database go to fallback (or are left out when there is none).</summary>
        static Dictionary<string, SortedSet<string>> Touched(FilePlan plan, string fallback)
        {
            var touched = new Dictionary<string, SortedSet<string>>();
            foreach (var name in plan.Rebuilt.Concat(plan.Created).Concat(plan.Altered).Concat(plan.Emptied).Concat(plan.Written))
            {
                int dot = name.IndexOf('.');
                string d = dot < 0 ? fallback : name.Substring(0, dot), t = name.Substring(dot + 1);
                if (d == null) continue;
                if (!touched.ContainsKey(d)) touched[d] = new SortedSet<string>();
                touched[d].Add(t);
            }
            return touched;
        }

        /// <summary>Writes the tables as they are now to an undo file; a table that does not exist yet is dropped by the undo. null: the whole database.</summary>
        void WriteCopies(string undoFile, Dictionary<string, SortedSet<string>> touched, Dictionary<string, List<string>> known)
        {
            try
            {
                using (var dst = File.Create(undoFile))
                {
                    var enc = new UTF8Encoding(false);
                    Action<string> write = line => { var b = enc.GetBytes(line + "\n"); dst.Write(b, 0, b.Length); };
                    write("-- " + Product.Name + " " + TablesMark + ", " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + ", databases: " + string.Join(" ", touched.Keys));
                    foreach (var d in touched.Keys)
                    {
                        if (!known.ContainsKey(d)) continue;                    // a database that does not exist yet
                        if (touched[d] == null) { MySql.DumpTables(inst, Login, d, null, dst); continue; }
                        var there = touched[d].Where(known[d].Contains).ToList();
                        write("USE " + Id(d) + ";");
                        foreach (var t in touched[d].Where(t => !known[d].Contains(t))) write("DROP TABLE IF EXISTS " + Id(t) + ";");
                        if (there.Count > 0) MySql.DumpTables(inst, Login, d, there, dst);
                    }
                }
            }
            catch { try { File.Delete(undoFile); } catch { } throw; }
            PruneUndo();
        }

        /// <summary>
        /// Asked before every change while the worldserver runs: it keeps the characters that are online in its memory
        /// and writes them back, and it reads the world tables only at its start. Offers to stop the server first.
        /// False: the user cancelled.
        /// </summary>
        bool LiveOk(IEnumerable<string> databases)
        {
            if (ctl.World == null) return true;
            bool players = databases.Any(d => d == "acore_characters" || d == "acore_auth");
            var answer = MessageBox.Show(this, "The server is running. Changing its database now is risky.\n\n" +
                (players ? "This changes characters or accounts: the worldserver keeps the characters that are online in its memory and writes them back, so your change can be overwritten a moment later, or a character can end up half changed.\n\n"
                         : "The worldserver has read these tables when it started and goes on working with what it read; the change only shows after a restart, and until then the server and the database do not agree.\n\n") +
                "Stop the server first? All players are saved and logged out.\n\n" +
                "Yes: stop the server, then go on\nNo: go on while the server is running\nCancel: change nothing",
                Product.Name, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1);
            if (answer == DialogResult.Cancel) return false;
            if (answer == DialogResult.No) return true;
            return StopServer();
        }

        /// <summary>Stops world- and authserver (the database keeps running) behind a small waiting window. False when that failed.</summary>
        bool StopServer()
        {
            string error = null;
            using (var f = new Form { Text = Product.Name, Font = Ui.Base, BackColor = Color.White, ShowIcon = false, ControlBox = false, FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(460, 90) })
            {
                var note = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Text = "Stopping the server ..." };
                f.Controls.Add(note);
                f.Shown += (s, e) => System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { ctl.StopAll(text => { try { f.BeginInvoke((Action)(() => note.Text = text)); } catch { } }, false); }
                    catch (Exception ex) { error = ex.Message; }
                    try { f.BeginInvoke((Action)(() => f.Close())); } catch { }
                });
                f.ShowDialog(this);
            }
            if (error != null) { Ui.Error(this, "The server could not be stopped, so nothing was changed:\n\n" + error); return false; }
            return true;
        }

        void UndoLast()
        {
            if (busy) return;
            var files = UndoFiles();
            if (files.Count == 0) return;
            if (!allow.Checked) { Ui.Error(this, "Undoing a change changes the database. Tick \"Allow changes\" at the top first."); return; }
            string file = files[0], name = Path.GetFileNameWithoutExtension(file), what = name.Length > 16 ? name.Substring(16) : name; DateTime when;
            string at = DateTime.TryParseExact(name.Length >= 15 ? name.Substring(0, 15) : "", "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out when) ? when.ToString("g") : "";
            bool rows = false;
            string head = "";
            try { using (var r = new StreamReader(file)) head = r.ReadLine() ?? ""; } catch { }
            rows = head.Contains(RowsMark);
            int mark = head.IndexOf("databases: ", StringComparison.Ordinal);
            if (!LiveOk(mark < 0 ? new string[0] : head.Substring(mark + 11).Split(' '))) return;
            if (!Ui.Confirm(this, "Undo the last change: " + what + (at.Length > 0 ? ", " + at : "") + "?\n\n" +
                (rows ? "The rows you changed, added or deleted are put back as they were before."
                      : "The tables it touched are put back exactly as they were before. Whatever else changed in these tables since then is lost as well.") +
                (ctl.World != null ? "\n\nThe server is running: restart it afterwards for this to show in the game." : "") +
                (files.Count > 1 ? "\n\n" + (files.Count - 1) + " earlier change(s) can be undone after this one." : ""))) return;
            if (Dirty && !Ui.Confirm(this, "There are changes in the table that were not saved; they are lost. Go on?")) return;
            Label hint = tableTabShown() ? tableHint : sqlHint;
            hint.Text = "Putting it back …";
            string any = tablesOf.ContainsKey("acore_world") ? "acore_world" : tablesOf.Keys.First();
            Background(() => MySql.RunFile(inst, Login, any, file), () =>
            {
                try { File.Delete(file); } catch { }
                Refill("Undone: " + what + ".");
            }, error =>
            {
                Ui.Error(this, "Undoing stopped with an error. The file is kept in\n" + file + "\n\n" + error);
                Refill("Undoing stopped with an error.");
            });
        }

        /// <summary>After an import: tables may have come or gone, and the one on show may have changed.</summary>
        void Refill(string message)
        {
            string database = db, name = table;
            Dictionary<string, List<string>> found = null;
            Background(() =>
            {
                found = new Dictionary<string, List<string>>();
                var t = MySql.Tables(inst, Login, "SELECT TABLE_SCHEMA, TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA NOT IN " +
                    "('information_schema', 'performance_schema', 'mysql', 'sys') ORDER BY TABLE_SCHEMA, TABLE_NAME;", 100000);
                foreach (var row in t.Count > 0 ? t[0].Rows : new List<string[]>())
                {
                    if (!found.ContainsKey(row[0])) found[row[0]] = new List<string>();
                    found[row[0]].Add(row[1]);
                }
            }, () =>
            {
                tablesOf = found; FillTree();
                sqlHint.Text = message;
                if (name != null && found.ContainsKey(database) && found[database].Contains(name)) LoadPage(true);
                else { db = table = null; grid.Rows.Clear(); grid.Columns.Clear(); tableName.Text = "Choose a table on the left."; pageInfo.Text = ""; tableHint.Text = message; Buttons(); }
            }, error => { tableHint.Text = message; });
        }

        // ---------------------------------------------------------------- the switch

        bool asked;

        void Allow()
        {
            if (allow.Checked && !asked)
            {
                var answer = MessageBox.Show(this,
                    "From here on you can change and delete the server's data.\n\n" +
                    "A mistake can break quests, creatures, characters or accounts, in the worst case the whole server. \"Undo last change\" can take back what you change in this window, but it is no substitute for a backup.\n\n" +
                    "Back up the server first? The backup holds all databases and can be restored under \"Backups …\".\n\n" +
                    "Yes: back up now (this window closes)\nNo: go on without a backup\nCancel: do not allow changes",
                    Product.Name, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1);
                if (answer == DialogResult.Cancel) { allow.Checked = false; return; }
                if (answer == DialogResult.Yes) { BackUpNow = true; Close(); return; }
                asked = true;
            }
            if (table != null)
                tableHint.Text = !HasKey ? tableHint.Text
                    : allow.Checked ? "Click a cell to change it; type NULL for an empty value where the column allows it. Nothing is written until \"Save changes\"."
                    : "Tick \"Allow changes\" at the top to edit. The primary key is shown in bold.";
            Buttons();
        }
    }
}
