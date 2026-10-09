using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace CoAInstaller
{
    /// <summary>
    /// The frame of a setup page for one of the modules made for AFK Realm: a header with the module's
    /// name, tabs along the top, cards with switches and sliders, and a footer to save. Everything is read
    /// from and written to the module's own .conf file, so the file and the page always say the same.
    /// </summary>
    abstract class SetupDialog : AfkForm
    {
        protected readonly Install inst;
        protected readonly ConfigFile conf;
        readonly Segmented tabs = new Segmented();
        readonly Panel scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Ui.Canvas };
        readonly FlowLayoutPanel shown = Ui.Column();
        readonly List<List<Control>> pages = new List<List<Control>>();
        readonly Label changes = new Label { AutoSize = true, Font = Ui.Small, ForeColor = Ui.Muted, Margin = new Padding(0, 14, 16, 0) };
        protected readonly Toggle master;
        protected const int CardWidth = 800;
        int loading;            // while controls are filled from the file, their events do not count as changes
        readonly List<Action> refreshers = new List<Action>();

        protected SetupDialog(Install i, string confName, string title, string subtitle, string icon, string masterKey, string masterText)
        {
            inst = i;
            string path = Path.Combine(inst.ConfigDir, "modules", confName);
            conf = ConfigFile.Load(path, "modules\\" + confName);
            Text = Product.Name + " – " + title; Font = Ui.Base; BackColor = Ui.Canvas; ShowIcon = false;
            StartPosition = FormStartPosition.CenterParent; var area = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(900, Math.Min(760, area.Height - 60)); MinimumSize = new Size(900, Math.Min(600, area.Height - 60));

            var head = new Panel { Dock = DockStyle.Top, Height = 118, BackColor = Ui.Surface };
            head.Paint += (s, e) =>
            {
                var g = e.Graphics; Draw.Smooth(g);
                using (var p = Draw.Round(new RectangleF(24, 22, 48, 48), 12)) using (var b = new SolidBrush(Ui.AccentSoft)) g.FillPath(b, p);
                Icons.Paint(g, icon, new RectangleF(36, 34, 24, 24), Ui.Accent, 1.9f);
                using (var pen = new Pen(Ui.Line)) g.DrawLine(pen, 0, head.Height - 1, head.Width, head.Height - 1);
            };
            head.Controls.Add(new Label { Text = title, Font = Ui.H1, ForeColor = Ui.Text, AutoSize = true, Location = new Point(86, 20), BackColor = Ui.Surface });
            head.Controls.Add(new Label { Text = subtitle, Font = Ui.Small, ForeColor = Ui.Muted, AutoSize = true, Location = new Point(88, 52), BackColor = Ui.Surface });
            master = new Toggle(masterText) { BackColor = Ui.Surface };
            head.Controls.Add(master);
            head.Resize += (s, e) => master.Location = new Point(head.Width - master.Width - 24, 26);
            tabs.Location = new Point(22, 76); tabs.BackColor = Ui.Surface;
            head.Controls.Add(tabs);
            BindToggle(master, masterKey);

            shown.Location = new Point(24, 20); shown.BackColor = Ui.Canvas;
            scroller.Controls.Add(shown);

            var foot = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 62, Padding = new Padding(14, 12, 14, 10), FlowDirection = FlowDirection.RightToLeft, BackColor = Ui.Surface, WrapContents = false };
            foot.Paint += (s, e) => { using (var pen = new Pen(Ui.Line)) e.Graphics.DrawLine(pen, 0, 0, foot.Width, 0); };
            var save = Ui.Primary("Save");
            var cancel = Ui.Secondary("Cancel");
            var defaults = Ui.Ghost("Reset to defaults");
            var file = Ui.Ghost("Open the file");
            save.Click += (s, e) => Save();
            cancel.Click += (s, e) => Close();
            defaults.Click += (s, e) =>
            {
                if (!Ui.Confirm(this, "Set every option on this page back to the value the module comes with? Nothing is written until you save.")) return;
                ResetDefaults(); Reload();
            };
            file.Click += (s, e) => { try { if (!File.Exists(conf.Path) && File.Exists(conf.Path + ".dist")) File.Copy(conf.Path + ".dist", conf.Path); Process.Start("notepad.exe", "\"" + conf.Path + "\""); } catch (Exception ex) { Ui.Error(this, ex.Message); } };
            foot.Controls.Add(cancel); foot.Controls.Add(save); foot.Controls.Add(changes); foot.Controls.Add(defaults); foot.Controls.Add(file);

            Controls.Add(scroller); Controls.Add(foot); Controls.Add(head);
            tabs.SelectedChanged += (s, e) => ShowPage(tabs.Selected);
            FormClosing += (s, e) =>
            {
                if (DialogResult == DialogResult.OK || (conf.Pending.Count == 0 && conf.Removed.Count == 0)) return;
                var answer = MessageBox.Show(this, "Save your changes before closing?", Product.Name, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer == DialogResult.Cancel) e.Cancel = true;
                else if (answer == DialogResult.Yes && !Save()) e.Cancel = true;
            };
            Shown += (s, e) =>
            {
                if (!File.Exists(conf.Path) && !File.Exists(conf.Path + ".dist"))
                    Ui.Error(this, confName + " was not found in the server's configs\\modules folder. Build the server with the module first.");
            };
        }

        /// <summary>Adds a tab; the cards for it are added with Card() right after.</summary>
        protected void Tab(string name)
        {
            tabs.Items.Add(name);
            tabs.Width = tabs.Items.Sum(t => TextRenderer.MeasureText(t, Ui.Bold).Width + 30) + 8;
            pages.Add(new List<Control>());
        }

        protected Card Card(string title, string text, string icon = null)
        {
            var c = new Card(title, text, CardWidth);
            if (icon != null) c.WithIcon(icon);
            pages[pages.Count - 1].Add(c);
            return c;
        }

        protected void Note(string text)
        {
            pages[pages.Count - 1].Add(new Label { Text = text, Font = Ui.Small, ForeColor = Ui.Muted, AutoSize = true, MaximumSize = new Size(CardWidth, 0), Margin = new Padding(4, 0, 0, 14), BackColor = Ui.Canvas });
        }

        void ShowPage(int index)
        {
            if (index < 0 || index >= pages.Count) return;
            scroller.SuspendLayout(); shown.SuspendLayout();
            shown.Controls.Clear();
            foreach (var c in pages[index]) shown.Controls.Add(c);
            shown.ResumeLayout(); scroller.AutoScrollPosition = new Point(0, 0); scroller.ResumeLayout();
        }

        protected void Start()
        {
            Reload();
            tabs.Selected = 0;
            ShowPage(0);
            foreach (var p in pages) foreach (var c in p) Restyle.Apply(c);
        }

        // ---------------------------------------------------------------- values

        protected string Raw(string key, string fallback)
        {
            string v;
            if (conf.Pending.TryGetValue(key, out v)) return ConfigOption.Unquote(v);
            if (conf.Removed.Contains(key)) return fallback;
            if (conf.Values.TryGetValue(key, out v)) return ConfigOption.Unquote(v);
            ConfigOption o;
            if (conf.ByKey.TryGetValue(key, out o) && o.DefaultRaw != null) return ConfigOption.Unquote(o.DefaultRaw);
            return fallback;
        }
        protected bool Has(string key)
        {
            if (conf.Pending.ContainsKey(key)) return true;
            if (conf.Removed.Contains(key)) return false;
            return conf.Values.ContainsKey(key);
        }
        protected double Number(string key, double fallback)
        {
            double d;
            return double.TryParse(Raw(key, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : fallback;
        }
        protected bool Flag(string key, bool fallback) { string v = Raw(key, fallback ? "1" : "0"); return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase); }
        protected string DefaultOf(string key, string fallback) { ConfigOption o; return conf.ByKey.TryGetValue(key, out o) && o.DefaultRaw != null ? ConfigOption.Unquote(o.DefaultRaw) : fallback; }

        protected void Set(string key, string value)
        {
            if (loading > 0) return;
            ConfigOption o;
            bool quoted = conf.ByKey.TryGetValue(key, out o) && o.Quoted;
            string raw = quoted ? "\"" + value + "\"" : value;
            string current;
            bool known = conf.Values.TryGetValue(key, out current) || (o != null && o.DefaultRaw != null && (current = o.DefaultRaw) != null);
            conf.Removed.Remove(key);
            if (known && ConfigOption.Unquote(current) == value && conf.Values.ContainsKey(key)) conf.Pending.Remove(key);
            else conf.Pending[key] = raw;
            Changed();
        }
        protected void Unset(string key)
        {
            if (loading > 0) return;
            conf.Pending.Remove(key);
            if (conf.Values.ContainsKey(key)) conf.Removed.Add(key);
            Changed();
        }
        protected static string Num(double v, int decimals) { return v.ToString("F" + decimals, CultureInfo.InvariantCulture); }

        protected virtual void Changed()
        {
            int n = conf.Pending.Count + conf.Removed.Count;
            changes.Text = n == 0 ? "" : n == 1 ? "1 change not saved yet" : n + " changes not saved yet";
            changes.ForeColor = n == 0 ? Ui.Muted : Ui.Warn;
            foreach (var r in refreshers) r();
        }

        /// <summary>Something that shows a result of the values (a preview); it is redrawn after every change.</summary>
        protected void OnChange(Action refresh) { refreshers.Add(refresh); }

        readonly List<Action> loaders = new List<Action>();
        protected void Reload()
        {
            loading++;
            try { foreach (var l in loaders) l(); }
            finally { loading--; }
            Changed();
        }
        protected bool Loading { get { return loading > 0; } }

        protected virtual void ResetDefaults()
        {
            foreach (var o in conf.Options.Where(o => o.DefaultRaw != null)) Set(o.Key, ConfigOption.Unquote(o.DefaultRaw));
        }

        bool Save()
        {
            try { conf.Save(); }
            catch (Exception ex) { Ui.Error(this, "The settings could not be saved:\n" + ex.Message); return false; }
            Changed();
            DialogResult = DialogResult.OK;
            MessageBox.Show(this, "Saved in " + conf.Name + ".\n\nThe changes take effect when the worldserver is started the next time (Restart server).", Product.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
            return true;
        }

        // ---------------------------------------------------------------- bound controls

        protected void BindToggle(Toggle t, string key)
        {
            loaders.Add(() => t.Checked = Flag(key, true));
            t.CheckedChanged += (s, e) => Set(key, t.Checked ? "1" : "0");
        }

        protected Toggle AddToggle(Card card, string key, string text, string hint = null)
        {
            var t = new Toggle(text);
            t.Wrap(card.Inner - 20);
            card.Add(t);
            BindToggle(t, key);
            if (hint != null) card.Add(Small(hint, card.Inner - 50, 50));
            return t;
        }

        protected static Label Small(string text, int width, int left = 0)
        {
            return new Label { Text = text, Font = Ui.Small, ForeColor = Ui.Muted, AutoSize = true, MaximumSize = new Size(width, 0), Margin = new Padding(left, 0, 0, 10), BackColor = Ui.Surface };
        }

        /// <summary>A labelled slider row. With a key it is bound to that option.</summary>
        protected Slider AddSlider(Card card, string key, string label, double min, double max, double step, Func<double, string> format = null, string hint = null, int labelWidth = 250)
        {
            var row = Ui.Row(); row.BackColor = Ui.Surface;
            var name = new Label { Text = label, Font = Ui.Base, ForeColor = Ui.Text, Width = labelWidth, Height = 36, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 0, 8, 0), BackColor = Ui.Surface };
            var slider = new Slider(min, max, step) { Width = card.Inner - labelWidth - 12, Format = format };
            row.Controls.Add(name); row.Controls.Add(slider);
            card.Add(row);
            if (hint != null) card.Add(Small(hint, card.Inner - labelWidth - 100, labelWidth + 8));
            if (key != null)
            {
                double fallback = min;
                loaders.Add(() => slider.Value = Number(key, Number(key, fallback)));
                slider.ValueChanged += (s, e) => Set(key, Num(slider.Value, slider.Decimals));
            }
            return slider;
        }

        /// <summary>A slider with two thumbs for an option written as "from-to".</summary>
        protected Slider AddRange(Card card, string key, string label, double min, double max, string hint = null, Color? swatch = null, int labelWidth = 250)
        {
            var row = Ui.Row(); row.BackColor = Ui.Surface;
            var name = new Label { Text = label, Font = Ui.Base, ForeColor = Ui.Text, Width = labelWidth, Height = 36, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 0, 8, 0), BackColor = Ui.Surface };
            if (swatch != null)
            {
                Color c = swatch.Value;
                name.Padding = new Padding(22, 0, 0, 0);
                name.Paint += (s, e) => { Draw.Smooth(e.Graphics); using (var b = new SolidBrush(c)) e.Graphics.FillEllipse(b, 2, 12, 12, 12); };
            }
            var slider = new Slider(min, max, 1, true) { Width = card.Inner - labelWidth - 12, Format = v => ((int)v).ToString() };
            row.Controls.Add(name); row.Controls.Add(slider);
            card.Add(row);
            if (hint != null) card.Add(Small(hint, card.Inner - labelWidth - 100, labelWidth + 8));
            loaders.Add(() =>
            {
                int a, b;
                if (!ParseRange(Raw(key, ""), out a, out b)) ParseRange(DefaultOf(key, ""), out a, out b);
                slider.Low = min; slider.High = b; slider.Low = a;
            });
            slider.ValueChanged += (s, e) => Set(key, (int)slider.Low + "-" + (int)slider.High);
            return slider;
        }

        protected static bool ParseRange(string text, out int from, out int to)
        {
            from = to = 0;
            var parts = (text ?? "").Split('-');
            return parts.Length == 2 && int.TryParse(parts[0].Trim(), out from) && int.TryParse(parts[1].Trim(), out to) && from >= 1 && to >= from;
        }

        /// <summary>A choice between a few values, shown side by side.</summary>
        protected Segmented AddChoice(Card card, string key, string label, string[] values, string[] texts, string hint = null, int labelWidth = 250)
        {
            var row = Ui.Row(); row.BackColor = Ui.Surface;
            row.Controls.Add(new Label { Text = label, Font = Ui.Base, ForeColor = Ui.Text, Width = labelWidth, Height = 40, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 0, 8, 0), BackColor = Ui.Surface });
            var seg = new Segmented(texts) { BackColor = Ui.Surface };
            row.Controls.Add(seg);
            card.Add(row);
            if (hint != null) card.Add(Small(hint, card.Inner - labelWidth - 40, labelWidth + 8));
            loaders.Add(() => { int i = Array.IndexOf(values, Raw(key, values[0])); seg.Selected = i < 0 ? 0 : i; });
            seg.SelectedChanged += (s, e) => Set(key, values[seg.Selected]);
            return seg;
        }

        protected void AddLoader(Action a) { loaders.Add(a); }

        /// <summary>A slider with two thumbs for two options, the lower and the upper value.</summary>
        protected Slider AddPair(Card card, string keyLow, string keyHigh, string label, double min, double max, double step, Func<double, string> format = null, string hint = null)
        {
            var row = Ui.Row(); row.BackColor = Ui.Surface;
            row.Controls.Add(new Label { Text = label, Font = Ui.Base, ForeColor = Ui.Text, Width = 250, Height = 36, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 0, 8, 0), BackColor = Ui.Surface });
            var slider = new Slider(min, max, step, true) { Width = card.Inner - 262, Format = format, ValueWidth = 110 };
            row.Controls.Add(slider);
            card.Add(row);
            if (hint != null) card.Add(Small(hint, card.Inner - 350, 258));
            loaders.Add(() => { slider.Low = min; slider.High = Number(keyHigh, max); slider.Low = Number(keyLow, min); });
            slider.ValueChanged += (s, e) => { Set(keyLow, Num(slider.Low, slider.Decimals)); Set(keyHigh, Num(slider.High, slider.Decimals)); };
            return slider;
        }

        /// <summary>A text field for an option (lists of item ids, channel names, ...).</summary>
        protected TextBox AddText(Card card, string key, string label, string hint = null)
        {
            var row = Ui.Row(); row.BackColor = Ui.Surface;
            row.Controls.Add(new Label { Text = label, Font = Ui.Base, ForeColor = Ui.Text, Width = 250, Height = 30, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 0, 8, 0), BackColor = Ui.Surface });
            var box = new TextBox { Font = Ui.Base, Width = card.Inner - 262, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 4, 0, 4) };
            row.Controls.Add(box);
            card.Add(row);
            if (hint != null) card.Add(Small(hint, card.Inner - 262, 258));
            loaders.Add(() => box.Text = Raw(key, ""));
            box.TextChanged += (s, e) => { if (box.Text.IndexOf('"') < 0) Set(key, box.Text.Trim()); };
            return box;
        }

        /// <summary>A list of numbers written as "1,2,5", shown as one switch per number.</summary>
        protected void AddFlags(Card card, string key, int[] values, string[] names, int columns = 3)
        {
            var grid = new TableLayoutPanel { ColumnCount = columns, AutoSize = true, BackColor = Ui.Surface, Margin = new Padding(0, 2, 0, 8) };
            var toggles = new List<Toggle>();
            int width = (card.Inner - 10) / columns;
            for (int c = 0; c < columns; c++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width));
            for (int i = 0; i < values.Length; i++)
            {
                var t = new Toggle(names[i]) { BackColor = Ui.Surface };
                t.Wrap(width - 6);
                toggles.Add(t);
                grid.Controls.Add(t, i % columns, i / columns);
            }
            card.Add(grid);
            loaders.Add(() =>
            {
                var on = new HashSet<string>(Raw(key, "").Split(',').Select(x => x.Trim()));
                for (int i = 0; i < values.Length; i++) toggles[i].Checked = on.Contains(values[i].ToString());
            });
            foreach (var t in toggles)
                t.CheckedChanged += (s, e) =>
                {
                    var keep = new List<string>();
                    var other = Raw(key, "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0 && !values.Select(v => v.ToString()).Contains(x));
                    for (int i = 0; i < values.Length; i++) if (toggles[i].Checked) keep.Add(values[i].ToString());
                    Set(key, string.Join(",", keep.Concat(other).OrderBy(x => { int n; return int.TryParse(x, out n) ? n : 999; })));
                };
        }
    }

    // ==================================================================================== World Journey

    /// <summary>The rules of mod-world-journey (src/JourneyRules.cpp), so the page can show where zones and dungeons land.</summary>
    class JourneyRules
    {
        public int ClassicFrom = 1, ClassicTo = 35, OutlandFrom = 30, OutlandTo = 43, NorthrendFrom = 40, NorthrendTo = 55, Endgame = 60;
        public Dictionary<int, int> Zones = new Dictionary<int, int>(), Instances = new Dictionary<int, int>();
        static readonly HashSet<int> OutlandInstances = new HashSet<int> { 269, 540, 542, 543, 545, 546, 547, 552, 553, 554, 555, 556, 557, 558, 560, 585, 532, 534, 544, 548, 550, 564, 565, 568, 580 };
        static readonly HashSet<int> NorthrendInstances = new HashSet<int> { 574, 575, 576, 578, 595, 599, 600, 601, 602, 604, 608, 619, 632, 650, 658, 668, 249, 533, 603, 615, 616, 624, 631, 649, 724 };
        static readonly HashSet<int> ClassicRaids = new HashSet<int> { 309, 409, 469, 509, 531 };

        static int Clamp(double v) { return (int)Math.Max(1, Math.Min(63, Math.Round(v, MidpointRounding.AwayFromZero))); }
        static double Lay(int level, int low, int high, int from, int to) { return from + (double)(level - low) * (to - from) / (high - low); }
        public int Classic(int l) { return Clamp(Lay(l, 1, 55, ClassicFrom, ClassicTo)); }
        public int Outland(int l) { return l < 58 ? Classic(l) : Clamp(Lay(l, 58, 67, OutlandFrom, OutlandTo)); }
        public int Northrend(int l) { return l < 68 ? Classic(l) : Math.Min(Clamp(Lay(l, 68, 77, NorthrendFrom, NorthrendTo)), Endgame); }

        public int Zone(int zone, int part, int original, bool manual = true)
        {
            int set;
            if (manual && Zones.TryGetValue(zone, out set)) return Math.Max(1, Math.Min(Endgame, set));
            int entry = original <= 1 ? 1 : part == 1 ? Outland(Math.Max(original, 58)) : part == 2 ? Northrend(Math.Max(original, 68)) : Classic(original);
            if (zone == 4080) entry = Endgame - 5;
            return Math.Max(1, Math.Min(entry, Math.Max(1, Endgame - 5)));
        }

        public int Instance(int map, int kind, int original, int content, bool manual = true)
        {
            int set;
            if (manual && Instances.TryGetValue(map, out set)) return Math.Max(1, Math.Min(Endgame, set));
            bool outland = OutlandInstances.Contains(map), northrend = NorthrendInstances.Contains(map);
            if (kind == 2 && ClassicRaids.Contains(map)) return original;
            if (kind != 0) return Endgame;
            int level = Math.Max(content, original), entry;
            if (outland) { if (map == 560) level = 66; entry = Math.Max(OutlandFrom, Outland(Math.Min(level, 70) - 3)); }
            else if (northrend) entry = Math.Max(NorthrendFrom, Northrend(Math.Min(level, 80) - 3));
            else entry = Classic(Math.Min(original + 3, 55));
            return Math.Max(1, Math.Min(entry, Endgame));
        }
    }

    /// <summary>The levels 1 to 60 as a bar, with where the three parts of the world are played and the endgame.</summary>
    class JourneyBar : Control
    {
        public JourneyRules Rules = new JourneyRules();
        public static readonly Color OldWorld = Color.FromArgb(86, 160, 110), Outland = Color.FromArgb(196, 110, 60), Northrend = Color.FromArgb(70, 130, 200), End = Color.FromArgb(108, 76, 196);
        public JourneyBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 150; BackColor = Ui.Surface; Margin = new Padding(0, 6, 0, 10);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Ui.Surface); Draw.Smooth(g);
            float left = 10, right = Width - 14, top = 8, w = right - left;
            Func<double, float> X = l => left + (float)((l - 1) / 59.0) * w;
            var lanes = new[]
            {
                Tuple.Create("Old world", Rules.ClassicFrom, Rules.ClassicTo, OldWorld),
                Tuple.Create("Outland", Rules.OutlandFrom, Rules.OutlandTo, Outland),
                Tuple.Create("Northrend", Rules.NorthrendFrom, Rules.NorthrendTo, Northrend),
                Tuple.Create("Raids and heroics", Rules.Endgame, Rules.Endgame, End),
            };
            for (int i = 0; i < lanes.Length; i++)
            {
                var lane = lanes[i];
                float y = top + i * 28;
                using (var p = Draw.Round(new RectangleF(left, y, w, 20), 6)) using (var b = new SolidBrush(Ui.Canvas)) g.FillPath(b, p);
                float a = X(lane.Item2), z = Math.Max(X(lane.Item3), a + 0);
                // A part is played from its first zone to 60: the solid bar is where its zones open, the light one where they are still worth going.
                if (lane.Item2 != lane.Item3) using (var p = Draw.Round(new RectangleF(a, y, X(60) - a, 20), 6)) using (var b = new SolidBrush(Color.FromArgb(55, lane.Item4))) g.FillPath(b, p);
                if (lane.Item2 == lane.Item3) { using (var b = new SolidBrush(lane.Item4)) g.FillEllipse(b, a - 10, y, 20, 20); z = a - 10; }
                else using (var p = Draw.Round(new RectangleF(a, y, Math.Max(20, z - a + 6), 20), 6)) using (var b = new SolidBrush(lane.Item4)) g.FillPath(b, p);
                string label = lane.Item1 + (lane.Item2 == lane.Item3 ? "  " + lane.Item2 : "  " + lane.Item2 + "–" + lane.Item3);
                var size = TextRenderer.MeasureText(label, Ui.Small);
                float tx = a + 6; var ink = Color.White;
                if (lane.Item2 == lane.Item3) { tx = a - size.Width - 14; ink = lane.Item4; }
                else if (size.Width + 10 > z - a + 6) { tx = Math.Min(z + 12, right - size.Width); ink = lane.Item4; if (tx + size.Width > right) { tx = a - size.Width - 6; } }
                TextRenderer.DrawText(g, label, Ui.Small, new Point((int)tx, (int)y + 2), ink);
            }
            float axis = top + 4 * 28 + 4;
            using (var pen = new Pen(Ui.Line)) g.DrawLine(pen, left, axis, right, axis);
            foreach (int l in new[] { 1, 10, 20, 30, 40, 50, 60 })
            {
                float x = X(l);
                using (var pen = new Pen(Ui.Line)) g.DrawLine(pen, x, axis - 3, x, axis + 3);
                var t = l.ToString(); var sz = TextRenderer.MeasureText(t, Ui.Small);
                TextRenderer.DrawText(g, t, Ui.Small, new Point((int)(x - sz.Width / 2f), (int)axis + 5), Ui.Muted);
            }
        }
    }

    /// <summary>What a character meets in a zone with the level window: the weakest and the strongest creatures around its level.</summary>
    class WindowPreview : Control
    {
        public int Below = 2, Above = 2, Level = 40;
        public bool On = true;
        public WindowPreview()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = 92; BackColor = Ui.Surface; Margin = new Padding(0, 6, 0, 6);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Ui.Surface); Draw.Smooth(g);
            int span = 12; float left = 10, right = Width - 10, w = right - left;
            Func<int, float> X = l => left + (l - (Level - span) + 0.5f) / (2 * span + 1) * w;
            for (int l = Level - span; l <= Level + span; l++)
            {
                float x0 = left + (l - (Level - span)) / (float)(2 * span + 1) * w, cw = w / (2 * span + 1) - 2;
                bool inWindow = On ? l >= Level - Below && l <= Level + Above : l == Level - 2;
                bool elite = On && l == Level + Above + 1;
                Color fill = l == Level ? Ui.Accent : inWindow ? Color.FromArgb(160, 140, 225) : elite ? Color.FromArgb(220, 205, 250) : Ui.Canvas;
                using (var p = Draw.Round(new RectangleF(x0 + 1, 18, cw, 34), 4)) using (var b = new SolidBrush(fill)) g.FillPath(b, p);
                if (l > 0 && (l % 5 == 0 || l == Level))
                {
                    var t = l.ToString(); var sz = TextRenderer.MeasureText(t, Ui.Small);
                    TextRenderer.DrawText(g, t, Ui.Small, new Point((int)(x0 + cw / 2 - sz.Width / 2f + 1), 56), l == Level ? Ui.Accent : Ui.Muted);
                }
            }
            string text = On
                ? "A character of level " + Level + " meets the weakest creatures of a zone at " + (Level - Below) + ", the strongest at " + (Level + Above) + ", elites and rares up to " + (Level + Above + 1) + "."
                : "Without the window every lifted creature stands at the same level below the character (DestinyWeaver.Scaling.Offset).";
            TextRenderer.DrawText(g, text, Ui.Small, new Rectangle(8, 74, Width - 16, 18), Ui.Muted, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, "Character", Ui.Small, new Point((int)X(Level) - 26, 0), Ui.Accent);
        }
    }

    class JourneySetup : SetupDialog
    {
        class Place { public int Id, Part, Kind, Original, Content; public string Name; public bool Instance; }
        readonly List<Place> places = new List<Place>();
        readonly JourneyRules rules = new JourneyRules();
        readonly JourneyBar bar = new JourneyBar();
        readonly ListView placeList = new ListView { View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, Font = Ui.Base, BorderStyle = BorderStyle.FixedSingle, Height = 330 };
        readonly Segmented placeKind = new Segmented("Zones", "Dungeons and raids");
        readonly Toggle byHand = new Toggle("Set this one by hand");
        Slider handLevel;
        readonly Label placeName = new Label { AutoSize = true, Font = Ui.Bold, ForeColor = Ui.Text, BackColor = Ui.Surface, Margin = new Padding(0, 8, 0, 2) };
        static readonly string[] Parts = { "OldWorld", "Outland", "Northrend", "Endgame" };
        static readonly string[] PartNames = { "Old world", "Outland", "Northrend", "Raids and heroics" };
        static readonly string[] Ranks = { "Normal", "Elite", "RareElite", "WorldBoss", "Rare" };
        static readonly string[] RankNames = { "Normal", "Elite", "Rare elite", "World boss", "Rare" };
        static readonly string[] Stats = { "Health", "Damage", "SpellDamage", "Armor" };
        static readonly string[] StatNames = { "Health", "Damage", "Spell damage", "Armor" };
        readonly Segmented diffPart = new Segmented("All parts", "Old world", "Outland", "Northrend", "Raids and heroics");
        readonly Toggle partOwn = new Toggle("Own values for this part of the world");
        readonly Slider[,] diff = new Slider[5, 4];
        readonly Segmented preset = new Segmented("Relaxed", "Standard", "Challenging", "Hard");

        public JourneySetup(Install i) : base(i, "mod_world_journey.conf", "World Journey", "mod-world-journey  ·  modules\\mod_world_journey.conf", "journey", "Journey.Enable", "World Journey on")
        {
            LoadPlaces();

            // ---- the journey
            Tab("The journey");
            var shape = Card("Where each part of the world begins", "The zones of a part open over its range, in their original order; every zone stays worth playing until 60, because CoA lifts its creatures to a character. " +
                "The ranges may overlap: that makes the journey a choice instead of a tube.", "journey");
            shape.Add(bar).Width = shape.Inner;
            AddRange(shape, "Journey.Classic.Range", "Old world (was 1–55)", 1, 60, null, JourneyBar.OldWorld);
            AddRange(shape, "Journey.Outland.Range", "Outland (was 58–67)", 1, 60, null, JourneyBar.Outland);
            AddRange(shape, "Journey.Northrend.Range", "Northrend (was 68–77)", 1, 60, null, JourneyBar.Northrend);

            var end = Card("The end of the journey", "The raids and heroic dungeons of Outland and Northrend are played at the end, after those of the old world, which stay as they are.", "gamemaster");
            AddSlider(end, "Journey.Endgame.Level", "Level of raids and heroics", 50, 60, 1);
            AddSlider(end, "Journey.Endgame.BossLevels", "Their bosses stand higher by", 0, 3, 1, v => "+" + (int)v);
            AddRange(end, "Journey.Endgame.Outland.ItemLevels", "Item levels of Outland's rewards", 60, 200, "The raids of the old world give 66 to 92: Outland's rewards come next on the ladder, Northrend's last.");
            AddRange(end, "Journey.Endgame.Northrend.ItemLevels", "Item levels of Northrend's rewards", 60, 200);

            var guard = Card("The rules of the realm", null, "settings");
            AddToggle(guard, "Journey.Scaling.Forced", "Every character plays the scaled world",
                "CoA's level scaling is on for everyone. The question at character creation still appears, but \"off\" no longer counts. Off: each character keeps the choice it made at creation.");
            AddToggle(guard, "Journey.Guard.Enable", "Keep the realm at 60",
                "The realm runs with the highest level 60, the Dungeon Finder with the dungeons of Outland and Northrend and Wintergrasp from 60, whatever worldserver.conf says.");

            // ---- zones and dungeons
            Tab("Zones and dungeons");
            var where = Card("Zones and dungeons", "Where each one opens on the journey, worked out from the ranges. Set one by hand to move it: its creatures and quests move with it, and the bots and the world map follow.", "globe");
            where.Add(placeKind);
            placeList.Width = where.Inner;
            placeList.Columns.Add("Name", 300); placeList.Columns.Add("Part", 150); placeList.Columns.Add("Originally", 100, HorizontalAlignment.Right);
            placeList.Columns.Add("On the journey", 130, HorizontalAlignment.Right);
            where.Add(placeList);
            where.Add(placeName);
            byHand.BackColor = Ui.Surface;
            where.Add(byHand);
            handLevel = AddSlider(where, null, "Opens at level", 1, 60, 1);
            placeKind.SelectedChanged += (s, e) => FillPlaces();
            placeList.SelectedIndexChanged += (s, e) => ShowPlace();
            byHand.CheckedChanged += (s, e) => { if (!filling) SetHand(); };
            handLevel.ValueChanged += (s, e) => { if (!filling && byHand.Checked) SetHand(); };
            if (places.Count == 0) Note("The list of zones comes with the module (data\\journey-ui.json). Update World Journey to see it here.");

            // ---- level window
            Tab("Level window");
            var window = Card("The level window", "Creatures lifted to a character keep their place in their zone: the weakest stand a little below the character, the strongest a little above, elites and rares one more. " +
                "A creature is never set below its own level.", "server");
            var winOn = AddToggle(window, "Journey.Window.Enable", "Use the level window");
            var below = AddSlider(window, "Journey.Window.Below", "Weakest creatures below", 0, 10, 1, v => "−" + (int)v);
            var above = AddSlider(window, "Journey.Window.Above", "Strongest creatures above", 0, 10, 1, v => "+" + (int)v);
            var preview = window.Add(new WindowPreview());
            preview.Width = window.Inner;
            var level = AddSlider(window, null, "Try it with a character of level", 10, 60, 1);
            level.Value = 40;
            Action showWindow = () => { preview.On = winOn.Checked; preview.Below = (int)below.Value; preview.Above = (int)above.Value; preview.Level = (int)level.Value; preview.Invalidate(); };
            level.ValueChanged += (s, e) => showWindow();
            OnChange(showWindow);
            Note("The level window and the forced scaling need World Journey's change to the core, which " + Product.Name + " brings in when it builds the server.");

            // ---- difficulty
            Tab("Difficulty");
            var presets = Card("How hard", "A starting point for the multipliers below. They come on top of the creatures' base stats and of the Rate.Creature.* settings of the server.", "gamemaster");
            presets.Add(preset);
            presets.Add(Small("Relaxed: a bit softer than the original. Standard: as the module comes, rares stronger. Challenging and Hard: everything hits harder and lasts longer.", presets.Inner));
            preset.SelectedChanged += (s, e) => ApplyPreset(preset.Selected);

            var grid = Card("Multipliers by rank", null, "settings");
            grid.Add(diffPart);
            partOwn.BackColor = Ui.Surface; partOwn.Wrap(grid.Inner);
            grid.Add(partOwn);
            var head = Ui.Row(); head.BackColor = Ui.Surface;
            head.Controls.Add(new Label { Width = 110, BackColor = Ui.Surface });
            int cell = (grid.Inner - 112) / 4 - 4;
            foreach (var st in StatNames) head.Controls.Add(new Label { Text = st, Width = cell, Font = Ui.Small, ForeColor = Ui.Muted, BackColor = Ui.Surface, Margin = new Padding(0, 6, 4, 0) });
            grid.Add(head);
            for (int r = 0; r < 5; r++)
            {
                var row = Ui.Row(); row.BackColor = Ui.Surface;
                row.Controls.Add(new Label { Text = RankNames[r], Width = 110, Height = 36, TextAlign = ContentAlignment.MiddleLeft, Font = Ui.Base, ForeColor = Ui.Text, BackColor = Ui.Surface });
                for (int c = 0; c < 4; c++)
                {
                    int rr = r, cc = c;
                    var sl = new Slider(0.25, 3, 0.05) { Width = cell, Format = v => "×" + v.ToString("0.##", CultureInfo.InvariantCulture), Margin = new Padding(0, 2, 4, 2) };
                    SetValueWidth(sl);
                    sl.ValueChanged += (s, e) => SetDiff(rr, cc, sl.Value);
                    diff[r, c] = sl;
                    row.Controls.Add(sl);
                }
                grid.Add(row);
            }
            diffPart.SelectedChanged += (s, e) => ShowDiff();
            partOwn.CheckedChanged += (s, e) => { if (!filling) PartOwn(partOwn.Checked); };

            var creatures = Card("Creatures on the way", null, "server");
            AddChoice(creatures, "Journey.Creatures.BaseStats", "Base stats of Outland and Northrend", new[] { "journey", "original" }, new[] { "The old world's (recommended)", "Their own game's" },
                "Those of the old world rise evenly to 60; the tables of Outland and Northrend have gaps and jumps below 60. Raids and heroics always keep those of their game.");

            var xp = Card("Quest experience", "On top of Rate.XP.Quest of the server.", "journey");
            AddSlider(xp, "Journey.Quests.XpRate", "All quests", 0, 3, 0.05, v => "×" + v.ToString("0.##", CultureInfo.InvariantCulture));

            // ---- what follows
            Tab("What follows");
            var follows = Card("What follows the journey", "Switch off what should stay as it came.", "modules");
            AddToggle(follows, "Journey.Items.Enable", "Items: required level, item level, stats, armor, damage and price");
            AddToggle(follows, "Journey.Consumables.Enable", "Potions, food, elixirs, flasks and the spells of trinkets and procs");
            AddToggle(follows, "Journey.Enchantments.Enable", "Gems, socket bonuses and the enchantments of Outland's and Northrend's professions");
            AddToggle(follows, "Journey.Trainers.Enable", "Riding and the professions (riding at 13 and 26, flying at 33 and 47)");
            AddToggle(follows, "Journey.Battlegrounds.Enable", "Eye of the Storm, Strand of the Ancients, Isle of Conquest and Wintergrasp open at 60");
            var client = Card("In the game client", "Needs the add-on ZoneLevels, which " + Product.Name + " puts into your game (Modules › Game client add-ons).", "client");
            AddToggle(client, "Journey.Map.Enable", "The world map shows the zone levels of the journey");
            AddToggle(client, "Journey.Tooltips.Enable", "Tooltips show the real values of items' spells, gems and enchantments");
            var debug = Card("For testing", null, "maintenance");
            AddToggle(debug, "Journey.Debug", "More detail in the server log");

            AddLoader(() => { ReadRules(); FillPlaces(); ShowDiff(); preset.Selected = MatchingPreset(); });
            OnChange(() => { ReadRules(); bar.Rules = rules; bar.Invalidate(); if (!filling) RefreshPlaceLevels(); });
            Start();
        }

        static void SetValueWidth(Slider s) { s.ValueWidth = 54; }

        bool filling;

        void LoadPlaces()
        {
            string file = Path.Combine(ModuleCatalog.ModulesDir(inst), "mod-world-journey", "data", "journey-ui.json");
            try
            {
                if (!File.Exists(file)) return;
                var json = new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(File.ReadAllText(file)) as Dictionary<string, object>;
                foreach (Dictionary<string, object> z in ((object[])json["zones"]).OfType<Dictionary<string, object>>())
                    places.Add(new Place { Id = Convert.ToInt32(z["zone"]), Name = Convert.ToString(z["name"]), Part = Convert.ToInt32(z["part"]), Original = Convert.ToInt32(z["originalEntry"]) });
                foreach (Dictionary<string, object> d in ((object[])json["instances"]).OfType<Dictionary<string, object>>())
                    places.Add(new Place { Instance = true, Id = Convert.ToInt32(d["map"]), Name = Convert.ToString(d["name"]), Kind = Convert.ToInt32(d["kind"]),
                        Original = Convert.ToInt32(d["originalEntry"]), Content = Convert.ToInt32(d["contentLevel"]) });
            }
            catch { places.Clear(); }
        }

        void ReadRules()
        {
            int a, b;
            if (ParseRange(Raw("Journey.Classic.Range", "1-35"), out a, out b)) { rules.ClassicFrom = a; rules.ClassicTo = b; }
            if (ParseRange(Raw("Journey.Outland.Range", "30-43"), out a, out b)) { rules.OutlandFrom = a; rules.OutlandTo = b; }
            if (ParseRange(Raw("Journey.Northrend.Range", "40-55"), out a, out b)) { rules.NorthrendFrom = a; rules.NorthrendTo = b; }
            rules.Endgame = (int)Math.Max(50, Math.Min(60, Number("Journey.Endgame.Level", 60)));
            rules.Zones.Clear(); rules.Instances.Clear();
            foreach (var p in places)
            {
                string key = (p.Instance ? "Journey.Instance." : "Journey.Zone.") + p.Id;
                if (Has(key)) (p.Instance ? rules.Instances : rules.Zones)[p.Id] = (int)Number(key, p.Original);
            }
        }

        string PartOf(Place p)
        {
            if (!p.Instance) return PartNames[p.Part];
            if (p.Kind == 2) return "Raid";
            if (p.Kind == 1) return "Heroic";
            return "Dungeon";
        }
        int LevelOf(Place p) { return p.Instance ? rules.Instance(p.Id, p.Kind, p.Original, p.Content) : rules.Zone(p.Id, p.Part, p.Original); }
        string Key(Place p) { return (p.Instance ? "Journey.Instance." : "Journey.Zone.") + p.Id; }

        void FillPlaces()
        {
            filling = true;
            bool instances = placeKind.Selected == 1;
            if (placeKind.Selected < 0) placeKind.Selected = 0;
            placeList.BeginUpdate(); placeList.Items.Clear();
            foreach (var p in places.Where(p => p.Instance == instances).OrderBy(p => LevelOf(p)).ThenBy(p => p.Name))
            {
                var it = new ListViewItem(p.Name) { Tag = p };
                it.SubItems.Add(PartOf(p)); it.SubItems.Add(p.Original.ToString()); it.SubItems.Add("");
                placeList.Items.Add(it);
            }
            placeList.EndUpdate();
            filling = false;
            RefreshPlaceLevels();
            ShowPlace();
        }

        void RefreshPlaceLevels()
        {
            foreach (ListViewItem it in placeList.Items)
            {
                var p = (Place)it.Tag;
                string text = LevelOf(p) + (Has(Key(p)) ? "  (by hand)" : "");
                if (it.SubItems[3].Text != text) it.SubItems[3].Text = text;
                it.ForeColor = Has(Key(p)) ? Ui.Accent : Ui.Text;
            }
        }

        void ShowPlace()
        {
            filling = true;
            var p = placeList.SelectedItems.Count == 1 ? (Place)placeList.SelectedItems[0].Tag : null;
            byHand.Enabled = handLevel.Enabled = p != null;
            placeName.Text = p == null ? "Choose a zone or dungeon in the list to move it." : p.Name + " – opens at " + LevelOf(p) + " on the journey (originally " + p.Original + ")";
            byHand.Checked = p != null && Has(Key(p));
            handLevel.Value = p == null ? 1 : LevelOf(p);
            handLevel.Enabled = p != null && byHand.Checked;
            filling = false;
        }

        void SetHand()
        {
            var p = placeList.SelectedItems.Count == 1 ? (Place)placeList.SelectedItems[0].Tag : null;
            if (p == null) return;
            if (byHand.Checked) Set(Key(p), ((int)handLevel.Value).ToString()); else Unset(Key(p));
            ReadRules();
            filling = true; handLevel.Enabled = byHand.Checked; if (!byHand.Checked) handLevel.Value = LevelOf(p); filling = false;
            RefreshPlaceLevels();
            placeName.Text = p.Name + " – opens at " + LevelOf(p) + " on the journey (originally " + p.Original + ")";
        }

        // ---- difficulty

        string DiffKey(int part, int rank, int stat) { return "Journey.Difficulty." + (part < 0 ? "" : Parts[part] + ".") + Ranks[rank] + "." + Stats[stat]; }
        int SelectedPart { get { return diffPart.Selected <= 0 ? -1 : diffPart.Selected - 1; } }
        bool PartHasOwn(int part) { for (int r = 0; r < 5; r++) for (int c = 0; c < 4; c++) if (Has(DiffKey(part, r, c))) return true; return false; }
        double General(int r, int c) { return Number(DiffKey(-1, r, c), Number(DiffKey(-1, r, c), 1)); }

        void ShowDiff()
        {
            filling = true;
            if (diffPart.Selected < 0) diffPart.Selected = 0;
            int part = SelectedPart;
            partOwn.Visible = part >= 0;
            bool own = part >= 0 && PartHasOwn(part);
            partOwn.Checked = own;
            for (int r = 0; r < 5; r++)
                for (int c = 0; c < 4; c++)
                {
                    diff[r, c].Value = part >= 0 && own ? Number(DiffKey(part, r, c), General(r, c)) : General(r, c);
                    diff[r, c].Enabled = part < 0 || own;
                }
            filling = false;
        }

        void SetDiff(int r, int c, double v)
        {
            if (filling) return;
            int part = SelectedPart;
            if (part >= 0 && !partOwn.Checked) return;
            Set(DiffKey(part, r, c), v.ToString("0.##", CultureInfo.InvariantCulture));
            preset.Selected = MatchingPreset();
        }

        void PartOwn(bool on)
        {
            int part = SelectedPart;
            if (part < 0) return;
            for (int r = 0; r < 5; r++)
                for (int c = 0; c < 4; c++)
                    if (on) Set(DiffKey(part, r, c), General(r, c).ToString("0.##", CultureInfo.InvariantCulture)); else Unset(DiffKey(part, r, c));
            ShowDiff();
        }

        // health, damage, spell damage, armor for normal creatures and for rares; elites and world bosses go with normal ones
        static readonly double[][] normal = { new[] { 0.85, 0.85, 0.85, 1.0 }, new[] { 1.0, 1.0, 1.0, 1.0 }, new[] { 1.2, 1.15, 1.15, 1.05 }, new[] { 1.5, 1.3, 1.3, 1.15 } };
        static readonly double[][] rare = { new[] { 1.25, 1.1, 1.1, 1.0 }, new[] { 1.5, 1.25, 1.25, 1.0 }, new[] { 1.75, 1.4, 1.4, 1.05 }, new[] { 2.0, 1.6, 1.6, 1.15 } };

        /// <summary>The preset the multipliers match, or -1.</summary>
        int MatchingPreset()
        {
            for (int part = 0; part < 4; part++) if (PartHasOwn(part)) return -1;
            for (int which = 0; which < 4; which++)
            {
                bool all = true;
                for (int r = 0; r < 5 && all; r++)
                    for (int c = 0; c < 4 && all; c++)
                        all = Math.Abs(General(r, c) - ((r == 2 || r == 4) ? rare[which][c] : normal[which][c])) < 0.001;
                if (all) return which;
            }
            return -1;
        }

        void ApplyPreset(int which)
        {
            if (filling || which < 0) return;
            for (int r = 0; r < 5; r++)
                for (int c = 0; c < 4; c++)
                    Set(DiffKey(-1, r, c), ((r == 2 || r == 4) ? rare[which][c] : normal[which][c]).ToString("0.##", CultureInfo.InvariantCulture));
            for (int part = 0; part < 4; part++) for (int r = 0; r < 5; r++) for (int c = 0; c < 4; c++) Unset(DiffKey(part, r, c));
            int keep = which;
            diffPart.Selected = 0;
            ShowDiff();
            preset.Selected = MatchingPreset() < 0 ? keep : MatchingPreset();
        }

        protected override void ResetDefaults()
        {
            base.ResetDefaults();
            foreach (var p in places) Unset(Key(p));
            for (int part = 0; part < 4; part++) for (int r = 0; r < 5; r++) for (int c = 0; c < 4; c++) Unset(DiffKey(part, r, c));
            foreach (var part in Parts) Unset("Journey.Quests.XpRate." + part);
        }
    }

    // ==================================================================================== Auction house bots

    class AuctionSetup : SetupDialog
    {
        const string P = "PlayerbotsAuctions.";
        static readonly string[] Qualities = { "Poor", "Common", "Uncommon", "Rare", "Epic", "Legendary" };
        static string Minutes(double v) { return v >= 120 && v % 60 == 0 ? (v / 60) + " h" : (int)v + " min"; }
        static string Times(double v) { return "×" + v.ToString("0.##", CultureInfo.InvariantCulture); }
        static string Percent(double v) { return (int)v + " %"; }
        static string Plain(double v) { return ((int)v).ToString(CultureInfo.InvariantCulture); }

        public AuctionSetup(Install i) : base(i, "mod_playerbots_auctions.conf", "Auction house bots", "mod-playerbots-auctions  ·  modules\\mod_playerbots_auctions.conf", "auction", P + "Enable", "Auction house bots on")
        {
            // ---- the bots
            Tab("The bots");
            var who = Card("Who trades", "Every bot has a character of its own that never changes: whether it collects or tidies up, haggles or pays, crafts for profit or for skill. These settings set the frame.", "accounts");
            AddSlider(who, P + "MinBotLevel", "Lowest level of trading bots", 1, 80, 1);
            AddSlider(who, P + "Character.AvoidersPercent", "Bots that never use the auction house", 0, 100, 1, Percent, "They sell everything to a vendor, as many players do.");
            var trips = Card("Trips to the auction house", "Like a player, a bot decides for itself when it is time to go: when its bags fill up or it has collected enough to sell. Out in the world it takes its hearthstone to a capital, walks to the auctioneers and later returns.", "globe");
            AddToggle(trips, P + "CityTrip.Enable", "Bots set out for the auction house on their own");
            AddPair(trips, P + "CityTrip.ItemsMin", P + "CityTrip.ItemsMax", "Things to sell before setting out", 1, 30, 1, Plain, "Tidy bots go with few, collectors with many.");
            AddPair(trips, P + "CityTrip.CooldownMinutesMin", P + "CityTrip.CooldownMinutesMax", "Time between two trips", 10, 600, 10, Minutes);
            AddPair(trips, P + "VisitCooldownMinutesMin", P + "VisitCooldownMinutesMax", "Time between visits in town", 5, 240, 5, Minutes);
            AddSlider(trips, P + "AuctioneerRange", "Distance to the auctioneer", 5, 100, 1, v => (int)v + " yd");
            var pace = Card("Pace", "How often the module looks at the bots and how many of them do their business each time; keeps the load even with thousands of bots.", "server");
            AddSlider(pace, P + "IntervalSeconds", "Look at the bots every", 5, 300, 5, v => (int)v + " s");
            AddSlider(pace, P + "BotsPerCycle", "Bots per look", 1, 100, 1, Plain);
            AddToggle(pace, P + "DailyRhythm", "Busier in the evening and at the weekend than at night");
            var memory = Card("Memory and mail", null, "database");
            AddToggle(memory, P + "SaveMemory", "Bots remember prices over a restart", "What things sold for and what came back unsold is kept in two small tables of the characters database.");
            AddToggle(memory, P + "PauseAuctionsWhileOffline", "Auctions pause while the server is off", "On a server that only runs for an evening, the auctions would otherwise be gone by the next one.");
            AddToggle(memory, P + "CollectAuctionMail", "Bots empty their auction mail");
            AddToggle(memory, P + "Debug", "Write what the bots do to the server log (for testing)");

            // ---- selling
            Tab("Selling");
            var what = Card("What is sold", "A bot only sells what it does not need itself and what is not soulbound. Items that bind when picked up and quest items are never sold.", "auction");
            AddChoice(what, P + "Sell.MinQuality.Equipment", "Weapons and armor from", new[] { "0", "1", "2", "3", "4" }, new[] { "Poor", "Common", "Uncommon", "Rare", "Epic" });
            AddChoice(what, P + "Sell.MinQuality.Other", "Everything else from", new[] { "0", "1", "2", "3", "4" }, new[] { "Poor", "Common", "Uncommon", "Rare", "Epic" });
            AddChoice(what, P + "Sell.MaxQuality", "Up to", new[] { "2", "3", "4", "5" }, new[] { "Uncommon", "Rare", "Epic", "Legendary" });
            var classes = Card("Kinds of items", "What may go into the auction house. Projectiles are left out: bots are handed their ammunition.", "modules");
            AddFlags(classes, P + "Sell.ItemClasses", new[] { 0, 1, 2, 3, 4, 5, 7, 9, 11, 12, 13, 15, 16 },
                new[] { "Consumables", "Bags", "Weapons", "Gems", "Armor", "Reagents", "Trade goods", "Recipes", "Quivers", "Quest items (tradable)", "Keys", "Pets, mounts, misc.", "Glyphs" });
            var how = Card("How", null, "settings");
            AddToggle(how, P + "Sell.ChargeDeposit", "Bots pay the deposit like players");
            AddToggle(how, P + "Sell.KeepFromVendor", "Bots keep what is worth an auction from the vendor");
            AddToggle(how, P + "Sell.JunkToVendor", "Bots sell their junk to a vendor nearby");
            AddSlider(how, P + "Sell.MinAuctionValueSilver", "Smallest auction worth creating", 0, 100, 1, v => (int)v + " silver");
            AddSlider(how, P + "Sell.MaxAuctionsPerHouse", "Auctions per auction house at most", 1000, 50000, 1000, Plain);
            var never = Card("Never sold", null, "maintenance");
            AddText(never, P + "Sell.ExcludedItemIDs", "Item ids", "Separated by commas.");
            AddText(never, P + "Sell.ExcludedNameParts", "Names containing", "Parts of names separated by commas; keeps test and technical items out.");

            // ---- prices
            Tab("Prices");
            var factors = Card("Starting prices", "The vendor value of an item times the factor of its quality. What auctions really sell for moves this usual price over time.", "auction");
            AddSlider(factors, P + "Price.Poor", "Poor (grey)", 0.5, 10, 0.5, Times);
            AddSlider(factors, P + "Price.Normal", "Common (white)", 0.5, 20, 0.5, Times);
            AddSlider(factors, P + "Price.Uncommon", "Uncommon (green)", 1, 40, 0.5, Times);
            AddSlider(factors, P + "Price.Rare", "Rare (blue)", 1, 60, 1, Times);
            AddSlider(factors, P + "Price.Epic", "Epic (purple)", 1, 100, 1, Times);
            AddSlider(factors, P + "Price.Legendary", "Legendary (orange)", 1, 200, 1, Times);
            var limits = Card("Limits", null, "settings");
            AddToggle(limits, P + "Price.LearnFromSales", "Prices learn from what really sold");
            AddSlider(limits, P + "Price.MinVendorFactor", "Never below the vendor value", 1, 5, 0.1, Times);
            AddSlider(limits, P + "Price.MaxBuyoutGold", "Highest buyout", 0, 20000, 100, v => v == 0 ? "no limit" : (int)v + " g");

            // ---- buying
            Tab("Buying");
            var buy = Card("Buying and bidding", "A bot looks through some of the items on offer for things it has a use for: upgrades, things it uses up, bags, materials for its professions. Of several offers it takes the cheapest.", "auction");
            AddToggle(buy, P + "Buy.Enable", "Bots buy and bid");
            AddToggle(buy, P + "Buy.FromPlayers", "From players");
            AddToggle(buy, P + "Buy.FromBots", "From other bots");
            AddToggle(buy, P + "Buy.PlaceBids", "Bots bid too (a bid can outbid a player)");
            AddToggle(buy, P + "Buy.UseBotMoney", "Bots pay with their own money");
            AddSlider(buy, P + "Buy.ItemsLookedAt", "Items looked at per visit", 10, 500, 10, Plain);
            AddSlider(buy, P + "Buy.MinVendorFactor", "Always willing to pay", 1, 5, 0.1, Times, "Times the vendor value, so selling to the bots is never worse than selling to a vendor.");
            AddSlider(buy, P + "Buy.VendorItemMaxPercent", "For vendor goods at most", 10, 100, 5, Percent, "Of the vendor's price, so nobody can buy from a vendor and sell to the bots for a profit.");

            // ---- crafting
            Tab("Crafting");
            var craft = Card("Crafting", "At the auctioneers a bot thinks through its recipes and compares what a product would bring with what its materials cost. What it makes and does not need, it sells.", "maintenance");
            AddToggle(craft, P + "Crafting.Enable", "Bots craft");
            AddToggle(craft, P + "Crafting.WalkToAnvilForgeFire", "They walk to an anvil, forge or fire when a recipe needs one");
            AddToggle(craft, P + "Crafting.BuyMaterials", "They buy missing materials in the auction house");
            AddToggle(craft, P + "Crafting.BuyVendorMaterials", "They buy thread, vials and the like from a vendor");
            AddToggle(craft, P + "Crafting.TakeApart", "Prospecting, milling and disenchanting");
            AddToggle(craft, P + "Crafting.Scrolls", "Enchanters sell their enchantments as scrolls");
            AddToggle(craft, P + "Crafting.Glyphs", "Scribes make glyphs", "The classes of Conquest of Azeroth cannot use the glyphs of Wrath of the Lich King.");
            var worth = Card("When it is worth it", null, "settings");
            AddSlider(worth, P + "Crafting.MinProfitPercent", "Least profit", 0, 200, 5, Percent);
            AddSlider(worth, P + "Crafting.SkillUpBonusPercent", "A recipe that raises skill is worth more", 0, 300, 10, v => "+" + (int)v + " %");
            AddSlider(worth, P + "Crafting.MaxMaterialPriceFactor", "Most paid for a material", 1, 5, 0.1, Times, "Times its usual price.");
            AddSlider(worth, P + "Crafting.RecipesPerVisit", "Recipes thought through per visit", 5, 200, 5, Plain);
            AddSlider(worth, P + "Crafting.MaxInARow", "Most of one thing in one go", 1, 20, 1, Plain);

            // ---- chat
            Tab("Chat");
            var deals = Card("Deals by chat", "A player writes WTB or WTS into a channel - the item typed out or linked, a number before it, a price after it - and bots answer by whisper. The goods come by mail, cash on delivery.", "accounts");
            AddToggle(deals, P + "Chat.Enable", "Bots answer WTB and WTS");
            AddSlider(deals, P + "Chat.Answers", "Bots answering one line at most", 1, 10, 1, Plain);
            AddText(deals, P + "Chat.Channels", "Channels read", "Parts of channel names separated by commas, for example general, trade. Empty: every channel.");
            AddToggle(deals, P + "Chat.Meet", "A bot nearby offers to meet");
            AddSlider(deals, P + "Chat.MeetDistance", "Nearby means within", 10, 2000, 10, v => (int)v + " yd");
            AddSlider(deals, P + "Chat.MailDelay", "The parcel takes", -1, 3600, 1, v => v < 0 ? "like mail" : v < 120 ? (int)v + " s" : (int)(v / 60) + " min", "\"Like mail\" follows Mail.DeliveryDelay of worldserver.conf.");
            var chatter = Card("Chatter", "Every bot is somebody in chat too: 65 personalities with more than 16000 lines between them.", "accounts");
            AddToggle(chatter, P + "Chatter.Enable", "The bots talk");
            AddSlider(chatter, P + "Chatter.Interval", "Something said out of the blue about every", 20, 3600, 10, v => v < 120 ? (int)v + " s" : (v / 60).ToString("0.#", CultureInfo.InvariantCulture) + " min", "Lower is livelier.");
            AddToggle(chatter, P + "Chatter.Replies", "They answer a hello, a ding, a thanks, a joke request");
            AddToggle(chatter, P + "Chatter.Nearby", "A bot within earshot says something now and then");
            AddToggle(chatter, P + "Chatter.WorldChannel", "Use the realm's channel when no bot is in the player's zone");
            AddSlider(chatter, P + "Chatter.Mixed", "Bots that mix two personalities", 0, 100, 5, Percent);
            AddSlider(chatter, P + "Chatter.EasterEggs", "A wink at AFK Realm once in", 0, 2000, 10, v => v == 0 ? "never" : (int)v + " lines");

            // ---- trading post
            Tab("Trading Post");
            var post = Card("The Trading Post", "A last resort at every auctioneer for what bots hardly come by: materials and what crafters make for everybody, well above the usual price. Only players can use it; the money is gone from the world.", "auction");
            AddToggle(post, P + "TradingPost.Enable", "Auctioneers offer the Trading Post");
            AddToggle(post, P + "TradingPost.Flyer", "Players get its flyer once a day");
            AddToggle(post, P + "TradingPost.FreeSample", "A free sample once a week (always junk)");
            AddChoice(post, P + "TradingPost.MaxQuality", "Best quality sold", new[] { "1", "2", "3", "4" }, new[] { "Common", "Uncommon", "Rare", "Epic" });
            AddSlider(post, P + "TradingPost.ScarceBelow", "Sells a material while the house has fewer than", 1, 200, 1, v => (int)v + " pcs");
            var postPrice = Card("Its prices and stock", "Prices as multiples of what a thing usually goes for: the buyout is delivered at once, a bid when the auction ends.", "settings");
            AddSlider(postPrice, P + "TradingPost.Price.Materials", "Buyout of materials", 1, 10, 0.1, Times);
            AddSlider(postPrice, P + "TradingPost.Price.Crafted", "Buyout of crafted goods", 1, 10, 0.1, Times);
            AddSlider(postPrice, P + "TradingPost.Bid.Materials", "Lowest bid for materials", 1, 10, 0.1, Times);
            AddSlider(postPrice, P + "TradingPost.Bid.Crafted", "Lowest bid for crafted goods", 1, 10, 0.1, Times);
            AddSlider(postPrice, P + "TradingPost.Stock.Materials", "Pieces of a material per day", 1, 500, 1, Plain);
            AddSlider(postPrice, P + "TradingPost.Stock.Crafted", "Pieces of a crafted thing per day", 1, 200, 1, Plain);
            AddSlider(postPrice, P + "TradingPost.Daily.Materials", "Most a character buys a day (materials)", 1, 200, 1, Plain);
            AddSlider(postPrice, P + "TradingPost.Daily.Crafted", "Most a character buys a day (crafted)", 1, 100, 1, Plain);
            var postNever = Card("Never at the Trading Post", null, "maintenance");
            AddText(postNever, P + "TradingPost.ExcludedItemIDs", "Item ids", "What is meant to be scarce: materials made on a cooldown and the like.");
            Start();
        }
    }
}
