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
        readonly List<ConfigFile> also = new List<ConfigFile>();     // other files a page writes a few options of
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
            var save = Ui.Primary(L.T("Save"));
            var cancel = Ui.Secondary(L.T("Cancel"));
            var defaults = Ui.Ghost(L.T("Reset to defaults"));
            var file = Ui.Ghost(L.T("Open the file"));
            save.Click += (s, e) => Save();
            cancel.Click += (s, e) => Close();
            defaults.Click += (s, e) =>
            {
                if (!Ui.Confirm(this, L.T("Set every option on this page back to the value the module comes with? Nothing is written until you save."))) return;
                ResetDefaults(); Reload();
            };
            file.Click += (s, e) => { try { if (!File.Exists(conf.Path) && File.Exists(conf.Path + ".dist")) File.Copy(conf.Path + ".dist", conf.Path); Process.Start("notepad.exe", "\"" + conf.Path + "\""); } catch (Exception ex) { Ui.Error(this, ex.Message); } };
            foot.Controls.Add(cancel); foot.Controls.Add(save); foot.Controls.Add(changes); foot.Controls.Add(defaults); foot.Controls.Add(file);

            Controls.Add(scroller); Controls.Add(foot); Controls.Add(head);
            tabs.SelectedChanged += (s, e) => ShowPage(tabs.Selected);
            FormClosing += (s, e) =>
            {
                if (DialogResult == DialogResult.OK || Unsaved() == 0) return;
                var answer = MessageBox.Show(this, L.T("Save your changes before closing?"), Product.Name, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer == DialogResult.Cancel) e.Cancel = true;
                else if (answer == DialogResult.Yes && !Save()) e.Cancel = true;
            };
            Shown += (s, e) =>
            {
                if (!File.Exists(conf.Path) && !File.Exists(conf.Path + ".dist"))
                    Ui.Error(this, L.F("{0} was not found in the server's configs\\modules folder. Build the server with the module first.", confName));
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

        /// <summary>Another config file this page writes options of; saved together with the module's.</summary>
        protected ConfigFile AlsoEdit(string path, string name)
        {
            var f = ConfigFile.Load(path, name);
            also.Add(f);
            return f;
        }

        int Unsaved() { return new[] { conf }.Concat(also).Sum(f => f.Pending.Count + f.Removed.Count); }

        protected string Raw(string key, string fallback) { return RawIn(conf, key, fallback); }
        protected string RawIn(ConfigFile conf, string key, string fallback)
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

        protected void Set(string key, string value) { SetIn(conf, key, value); }
        protected void SetIn(ConfigFile conf, string key, string value)
        {
            if (loading > 0) return;
            ConfigOption o;
            bool quoted = conf.ByKey.TryGetValue(key, out o) && o.Quoted;
            // Some callers pass a text in the quotes a quoted option stores (the file choices do): keep it as it is,
            // or the file gets a second pair that the page then reads as part of the path.
            bool already = ConfigOption.IsQuoted(value);
            string raw = quoted && !already ? "\"" + value + "\"" : value;
            string bare = already ? ConfigOption.Unquote(value) : value;
            string current;
            bool known = conf.Values.TryGetValue(key, out current) || (o != null && o.DefaultRaw != null && (current = o.DefaultRaw) != null);
            conf.Removed.Remove(key);
            if (known && ConfigOption.Unquote(current) == bare && conf.Values.ContainsKey(key)) conf.Pending.Remove(key);
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
            int n = Unsaved();
            changes.Text = n == 0 ? "" : n == 1 ? L.T("1 change not saved yet") : L.F("{0} changes not saved yet", n);
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

        /// <summary>Runs after the settings are written: the files that follow from them.</summary>
        protected virtual void Saved() { }

        bool Save()
        {
            var written = new[] { conf }.Concat(also).Where(f => f.Pending.Count + f.Removed.Count > 0).Select(f => f.Name).ToList();
            if (written.Count == 0) written.Add(conf.Name);
            try { conf.Save(); foreach (var f in also) f.Save(); }
            catch (Exception ex) { Ui.Error(this, L.T("The settings could not be saved:") + "\n" + ex.Message); return false; }
            Saved();
            Changed();
            DialogResult = DialogResult.OK;
            MessageBox.Show(this, L.F("Saved in {0}.", string.Join(", ", written)) + "\n\n" + L.T("The changes take effect when the worldserver is started the next time (Restart server)."), Product.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            if (labelWidth + 8 + seg.Width <= card.Inner)
            {
                row.Controls.Add(seg);
                card.Add(row);
                if (hint != null) card.Add(Small(hint, card.Inner - labelWidth - 40, labelWidth + 8));
            }
            else
            {
                // Longer texts (some languages): the choice goes below its label.
                row.Controls[0].Width = card.Inner; row.Controls[0].Height = 30;
                card.Add(row);
                seg.Margin = new Padding(0, 0, 0, 4);
                card.Add(seg);
                if (hint != null) card.Add(Small(hint, card.Inner - 40, 0));
            }
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
        protected void AddFlags(Card card, string key, int[] values, string[] names, int columns = 3, char separator = ',')
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
                var on = new HashSet<string>(Raw(key, "").Split(separator).Select(x => x.Trim()));
                for (int i = 0; i < values.Length; i++) toggles[i].Checked = on.Contains(values[i].ToString());
            });
            foreach (var t in toggles)
                t.CheckedChanged += (s, e) =>
                {
                    var keep = new List<string>();
                    var other = Raw(key, "").Split(separator).Select(x => x.Trim()).Where(x => x.Length > 0 && !values.Select(v => v.ToString()).Contains(x));
                    for (int i = 0; i < values.Length; i++) if (toggles[i].Checked) keep.Add(values[i].ToString());
                    Set(key, string.Join(separator.ToString(), keep.Concat(other).OrderBy(x => { int n; return int.TryParse(x, out n) ? n : 999; })));
                };
        }
    }

    // ==================================================================================== Classic classes

    class ClassicSetup : SetupDialog
    {
        public ClassicSetup(Install i) : base(i, "mod_classic_classes.conf", L.T("Classic classes"), L.T("mod-classic-classes  ·  modules\\mod_classic_classes.conf"), "gamemaster", "ClassicClasses.Enable", L.T("Classic classes on"))
        {
            Tab(L.T("Classes"));
            var classes = Card(L.T("Which classic classes can be played"), L.T("Next to the classes of Conquest of Azeroth, in the way Ascension's Warcraft Reborn realms played them: their Reborn spells, trainers, talents and summons. The random bots are created in the same classes."), "gamemaster");
            AddFlags(classes, "ClassicClasses.Classes", new[] { 1, 2, 3, 4, 5, 7, 8, 9, 11 },
                new[] { L.T("Warrior"), L.T("Paladin"), L.T("Hunter"), L.T("Rogue"), L.T("Priest"), L.T("Shaman"), L.T("Mage"), L.T("Warlock"), L.T("Druid") }, 3, ' ');
            classes.Add(Small(L.T("The Death Knight is left out: his spells begin at level 55, so he would skip almost the whole world."), classes.Inner));

            var after = Card(L.T("After a change"), null, "client");
            after.Add(Small(L.F("The class choice of the game shows the classes of the client patch. After a change, open Modules › Game client and choose Install: {0} builds the patch anew. The other players need the new ZIP. Bots already made keep their class; Maintenance › Reset random bots makes them anew.", Product.Name), after.Inner));
            Start();
        }
    }

    // ==================================================================================== Custom races

    /// <summary>
    /// mod-custom-races: which races of the game client can be played, and the stock race each leans on. The races are
    /// read from the player's game client (ClientRaces): every race it has a body for. What the module knows beyond the
    /// client - a text, the base race that fits, how a race looks in game, a rating - comes from its races.json. A race
    /// without a race number of its own takes one of the client's free numbers (CustomRaces.&lt;race&gt;.Id) when it is
    /// chosen, and keeps it while it is switched off, so that its characters get it back; "Free its number" gives it up.
    /// Everything the server and the client patch need of a race is written into the module's settings.
    /// </summary>
    class RacesSetup : SetupDialog
    {
        /// <summary>The patched game exe of a CoA Custom pack: Ascension.exe in 1.8.3 and later, Ascension-b.exe in some others.</summary>
        static bool IsGameExe(string path)
        {
            string name = Path.GetFileName(path);
            return name.Equals("Ascension.exe", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("Ascension-b.exe", StringComparison.OrdinalIgnoreCase);
        }

        class Known { public string Base, Looks, About, Rating, Note; }

        readonly Dictionary<string, Known> known = new Dictionary<string, Known>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, KeyValuePair<int, string>> virtuals = new Dictionary<string, KeyValuePair<int, string>>();
        readonly List<string> bases = new List<string>();
        readonly List<ClientRace> races = new List<ClientRace>();
        readonly List<int> slots = new List<int>();
        readonly List<int> moreSlots = new List<int>();          // 32 to 63, with the 64-race patch of the client
        readonly List<int> highSlots = new List<int>();          // 64 to 127, free in the client (CoA Custom's game)
        bool More { get { return Raw("CustomRaces.MoreRaces", "0") == "1"; } }
        /// <summary>The game of CoA Custom (its own exe): its races keep the numbers CoA Custom gave them, up to 127.</summary>
        bool High { get { return Raw("CustomRaces.HighRaces", "0") == "1"; } }

        /// <summary>CoA Custom's EsteriaAppearance.dll knows these races by their numbers in CoA Custom's game and keeps more
        /// of their looks than the five bytes of the game: 1 a sixth byte (Highmountain Tauren, Earthen), 2 a 64-bit number
        /// (Haranir). The server stores it (CustomRaces.&lt;race&gt;.Appearance).</summary>
        static readonly Dictionary<int, int> CoaExtraLooks = new Dictionary<int, int> { { 66, 1 }, { 68, 1 }, { 69, 1 }, { 70, 2 }, { 71, 2 } };

        /// <summary>Which races of the client keep their own number above 31: with CoA Custom's game, every one up to 127
        /// that has no other number yet (a race played under a copied number keeps that one, so its characters stay).</summary>
        void MarkOwnNumbers()
        {
            foreach (var r in races)
            {
                if (r.Pack != null || r.Virtual || r.Source < 32) { r.KeepsNumber = false; continue; }
                int n;
                bool other = int.TryParse(Raw("CustomRaces." + r.Key + ".Id", ""), out n) && n > 0 && n != r.Source;
                r.KeepsNumber = High && r.Source < 128 && !other;
            }
        }

        int ExtraLooksOf(ClientRace r, int id)
        {
            int kind;
            if (!High || !CoaExtraLooks.TryGetValue(id, out kind)) return 0;
            if (r.Pack == null) return r.KeepsNumber && r.Source == id ? kind : 0;
            return r.Pack.SourceRace == id && IsCoaNumber(r) ? kind : 0;
        }

        Dictionary<int, string> coaRaceFiles;
        /// <summary>The races of CoA Custom's game by number (its ClientFilestring), from the race table of its pack
        /// (files\dbc\ChrRaces.dbc next to files\client_root); empty when it is not there.</summary>
        Dictionary<int, string> CoaRaceFiles()
        {
            if (coaRaceFiles != null) return coaRaceFiles;
            coaRaceFiles = new Dictionary<int, string>();
            try
            {
                string root = ConfigOption.Unquote(Raw("CustomRaces.ClientRootFolder", ""));
                if (root.Length == 0) return coaRaceFiles;
                string dbc = Path.Combine(Path.GetDirectoryName(root.TrimEnd('\\', '/')), "dbc", "ChrRaces.dbc");
                if (!File.Exists(dbc)) return coaRaceFiles;
                var t = DbcTable.Parse(File.ReadAllBytes(dbc), "ChrRaces.dbc");
                foreach (var row in t.Records)
                {
                    int id = (int)t.Field(row, 0);
                    if (!coaRaceFiles.ContainsKey(id)) coaRaceFiles[id] = ClientRaces.Str(t, row, 11);
                }
            }
            catch { }
            return coaRaceFiles;
        }

        /// <summary>A race of the race folder made from CoA Custom's client, under the number CoA Custom gave it.</summary>
        bool IsCoaNumber(ClientRace r)
        {
            if (r.Pack == null) return false;
            int n = r.Pack.SourceRace;
            string file;
            // the same race of CoA Custom's table: by the file name of its row there (DrakonidHorde, DrakkinAlliance), or
            // by the file name the race folder gives it (HighmountainTauren)
            return n >= 32 && n < 128 && CoaRaceFiles().TryGetValue(n, out file) &&
                (file.Equals(r.Pack.SourceFile, StringComparison.OrdinalIgnoreCase) || file.Equals(r.Pack.File.Length > 0 ? r.Pack.File : r.Pack.Key, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>With CoA Custom's game: the number CoA Custom gave a race of the race folder, while it is free here (no
        /// row of this client and no other race has it); 0 otherwise.</summary>
        int CoaNumberOf(ClientRace r)
        {
            if (!High || !IsCoaNumber(r)) return 0;
            int n = r.Pack.SourceRace;
            bool free = n < 64 ? moreSlots.Contains(n) : highSlots.Contains(n);
            if (!free || races.Any(o => o != r && IdOf(o) == n)) return 0;
            return n;
        }
        List<int> Slots { get { return More ? slots.Concat(moreSlots).ToList() : slots; } }
        readonly HashSet<string> helmetPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string folder;
        readonly Label usage = new Label { AutoSize = true, Font = Ui.Bold, ForeColor = Ui.Text, BackColor = Ui.Surface, Margin = new Padding(0, 4, 0, 8) };
        Label importStatus;

        public RacesSetup(Install i) : base(i, "mod_custom_races.conf", L.T("Custom races"), L.T("mod-custom-races  ·  modules\\mod_custom_races.conf"), "accounts", "CustomRaces.Enable", L.T("Custom races on"))
        {
            ReadKnown();
            string problem = ReadClient();
            ApplyClientMoves();
            ReadFolder();
            HealNumbers();

            Tab(L.T("Races"));
            int own = races.Count(r => r.OwnNumber);
            var head = Card(L.T("Which races can be played"), L.T("These are the races your game client carries a body for, read from the client itself. Switch on the ones you want: each starts like its base race - where that race starts, with its quests, stats, racial abilities and faction - and keeps its own looks.") +
                (slots.Count > 0 ? " " + L.F("There are {0} free race numbers below 32 and {1} more above 31; together with the {2} races that bring their own number, up to {3} races can be on at once.", slots.Count, moreSlots.Count, own, own + slots.Count + moreSlots.Count) : ""), "accounts");
            if (problem != null) head.Add(Small(problem, head.Inner));
            else head.Add(usage);
            OnChange(UpdateUsage);

            // One path, one button: pick the CoA Custom pack and take its files and its races in one go.
            var importCard = Card(L.T("Import races"), L.T("Choose the game client or repack once. AFK Realm finds its races and the files it needs for your game client, takes the races from that client and keeps the required files up to date when the client patch is installed."), "client");
            var importButton = Ui.Primary(L.T("Import races …"));
            importButton.Margin = new Padding(0, 2, 0, 6);
            importCard.Add(importButton);
            importStatus = Small("", importCard.Inner - 40);
            importCard.Add(importStatus);
            UpdateImportStatus();
            importButton.Click += (s, e) => ImportRaces();

            // The imported races first: these are the ones the user brought in and usually looks for.
            var imported = races.Where(r => r.Pack != null).OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            if (imported.Count > 0)
            {
                var importedCard = Card(L.T("Imported races"), L.T("These races come from your race folder. They are the ones you added to the game; switch them on here."), "client");
                foreach (var r in imported) AddRace(importedCard, r);
            }

            // Then the races that were already in the game client. They stay separate on purpose: switching one on
            // is not an import, and moving its number is the only way to free that number for an imported race.
            var clientRaces = races.Where(r => r.Pack == null).OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            if (clientRaces.Count > 0)
            {
                var clientCard = Card(L.T("Races already in your game client"), L.T("These races are part of your game client. You can switch them on, or move one to a free number when an imported race needs its number."), "client");
                foreach (var r in clientRaces) AddRace(clientCard, r);
            }


            var after = Card(L.T("After a change"), null, "client");
            after.Add(Small(L.F("The race choice of the game shows the races of the client patch. After a change, open Modules › Game client and choose Install: {0} builds the patch anew. The other players need the new ZIP. A race switched off keeps its characters as their base race until it is on again; changing the base race of a race with characters may move them to the other faction. When the game client changes with an update, open this page again: it reads the races anew.", Product.Name), after.Inner));
            Start();
            AutoDetect();
            Shown += (s, e) => Refresh();
        }

        void UpdateImportStatus()
        {
            if (importStatus == null) return;
            string raceFolder = ConfigOption.Unquote(Raw("CustomRaces.ImportFolder", ""));
            if (raceFolder.Length == 0) raceFolder = folder ?? "";
            int packs = races.Count(r => r.Pack != null || r.Alt != null);
            string client = ClientAddons.ClientFolder ?? L.T("no game client chosen");
            string root = ConfigOption.Unquote(Raw("CustomRaces.ClientRootFolder", ""));
            string files = root.Length == 0 || !Directory.Exists(root) ? L.T("no extra client files") : L.F("{0} files", Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length);
            importStatus.Text = L.F("Race folder: {0} ({1} races)  ·  Game client: {2}  ·  {3}  ·  Race numbers: automatic", raceFolder.Length == 0 ? L.T("not chosen") : raceFolder, packs, client, files);
        }

        /// <summary>The single import button: choose the game client or repack, detect its files and race archives,
        /// then open the race selection for that pack.</summary>
        void ImportRaces()
        {
            using (var d = new FolderBrowserDialog { Description = L.T("The game client or repack with the race archives"), SelectedPath = "" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                string picked = d.SelectedPath;
                string root = ClientPatch.ClientRootOf(picked);
                string source = picked;
                if (root != null)
                {
                    string files = Path.GetDirectoryName(root.TrimEnd('\\', '/')) ?? "";
                    string archives = Path.Combine(files, "client");
                    if (Directory.Exists(archives)) source = archives;
                    Set("CustomRaces.ClientRootFolder", "\"" + root + "\"");
                    string exe = ClientPatch.GameExeOf(root);
                    if (exe != null) Set("CustomRaces.ClientExe", "\"" + exe + "\"");
                    string patch = ClientPatch.CoAPatchOf(root);
                    if (patch != null && RacePacker.IsRacePatch(patch)) Set("CustomRaces.MoreRacesFile", "\"" + patch + "\"");
                    Set("CustomRaces.MoreRaces", "1");
                    Set("CustomRaces.HighRaces", "1");
                }
                else
                {
                    // a plain repack: keep its archives, but do not invent a CoA client_root it does not have
                    try { if (RacePacker.ArchivesOf(picked).Count == 0) { Ui.Error(this, L.T("This folder has no game archives (.MPQ) and no files\\client_root.")); return; } }
                    catch (Exception ex) { Ui.Error(this, L.T("This folder could not be read:") + " " + ex.Message); return; }
                }
                string target = folder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "AFK-Rassen");
                using (var make = new RacePackDialog(target, ClientAddons.ClientFolder, inst.DataDir, source))
                {
                    make.ShowDialog(this);
                    if (make.Made == 0) { UpdateImportStatus(); return; }
                }
                if (Raw("CustomRaces.ImportFolder", "") != target) Set("CustomRaces.ImportFolder", "\"" + target + "\"");
                UpdateImportStatus();
                MessageBox.Show(this, L.T("The races were taken from the selected client.") + "\n\n" +
                    L.T("Close this page and open it again: the imported races are now at the top. Switch on the ones you want, save, then install the client patch under Modules > Game client."), Product.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        /// <summary>Fills the exe and the 64-race patch from the chosen client_root. A path that points
        /// outside that pack is stale and is replaced, so choosing the pack is the only step.</summary>
        void AutoDetect()
        {
            string root = ConfigOption.Unquote(Raw("CustomRaces.ClientRootFolder", ""));
            bool changed = false;
            if (root.Length == 0)
            {
                string found = DefaultClientRoot();
                if (found != null) { Set("CustomRaces.ClientRootFolder", found); root = found; changed = true; }
            }
            if (root.Length == 0) { UpdateImportStatus(); return; }
            string currentExe = ConfigOption.Unquote(Raw("CustomRaces.ClientExe", ""));
            string exe = ClientPatch.GameExeOf(root);
            if (exe != null && !SamePath(currentExe, exe) && (currentExe.Length == 0 || !IsInside(currentExe, root)))
            {
                Set("CustomRaces.ClientExe", exe); changed = true;
            }
            string currentPatch = ConfigOption.Unquote(Raw("CustomRaces.MoreRacesFile", ""));
            string patch = ClientPatch.CoAPatchOf(root);
            string files = Path.GetDirectoryName(root.TrimEnd('\\', '/'));
            if (patch != null && RacePacker.IsRacePatch(patch) && !SamePath(currentPatch, patch) &&
                (currentPatch.Length == 0 || files == null || !IsInside(currentPatch, files)))
            {
                Set("CustomRaces.MoreRacesFile", patch); changed = true;
            }
            if (changed) Reload(); else UpdateImportStatus();
        }

        /// <summary>The usual place of the CoA Custom pack, so a fresh server needs no folder dialog.</summary>
        static string DefaultClientRoot()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (string pack in new[] { Path.Combine(home, "Downloads", "CoA-Custom", "CoA-Custom"), Path.Combine(home, "Downloads", "CoA-Custom") })
            {
                string root = ClientPatch.ClientRootOf(pack);
                if (root != null) return root;
            }
            return null;
        }

        static bool SamePath(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try { return Path.GetFullPath(a).Equals(Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        static bool IsInside(string file, string folder)
        {
            if (string.IsNullOrEmpty(file) || string.IsNullOrEmpty(folder)) return false;
            try
            {
                string f = Path.GetFullPath(folder).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                return Path.GetFullPath(file).StartsWith(f, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>Reads, before the page opens and in the background, what it shows: the races of the game client and
        /// those of the race folder (both kept for the page, which then opens at once).</summary>
        public static void Preload(Install inst)
        {
            var virtuals = new Dictionary<string, KeyValuePair<int, string>>();
            try
            {
                string file = Path.Combine(ModuleCatalog.ModulesDir(inst), "mod-custom-races", "races.json");
                var data = new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(File.ReadAllText(file)) as Dictionary<string, object>;
                foreach (var kv in (Dictionary<string, object>)data["known"])
                {
                    object virt;
                    var r = (Dictionary<string, object>)kv.Value;
                    if (r.TryGetValue("virtual", out virt) && virt is Dictionary<string, object>)
                    {
                        var v = (Dictionary<string, object>)virt;
                        virtuals[kv.Key] = new KeyValuePair<int, string>(Convert.ToInt32(v["source"]), Convert.ToString(v["name"]));
                    }
                }
            }
            catch { }
            string client = ClientAddons.ClientFolder;
            if (client != null && ClientAddons.IsClientFolder(client))
                try { ClientRaces.Read(client, virtuals); } catch { }
            string folder = ClientPatch.ConfValue(inst, "mod_custom_races.conf", "CustomRaces.ImportFolder") ?? "";
            if (folder.Length == 0) folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "AFK-Rassen");
            try { RaceImport.Read(folder); } catch { }
        }

        void ReadKnown()
        {
            string file = Path.Combine(ModuleCatalog.ModulesDir(inst), "mod-custom-races", "races.json");
            try
            {
                var data = new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(File.ReadAllText(file)) as Dictionary<string, object>;
                foreach (var b in (object[])data["bases"]) bases.Add(Convert.ToString(b));
                foreach (var kv in (Dictionary<string, object>)data["known"])
                {
                    var r = (Dictionary<string, object>)kv.Value;
                    Func<string, string> text = k => { object v; return r.TryGetValue(k, out v) ? Convert.ToString(v) : ""; };
                    known[kv.Key] = new Known { Base = text("base"), Looks = text("looks"), About = text("about"), Rating = text("rating"), Note = text("note") };
                    object virt;
                    if (r.TryGetValue("virtual", out virt) && virt is Dictionary<string, object>)
                    {
                        var v = (Dictionary<string, object>)virt;
                        virtuals[kv.Key] = new KeyValuePair<int, string>(Convert.ToInt32(v["source"]), Convert.ToString(v["name"]));
                    }
                }
                // CoA Custom's racials by its race numbers (its server has them compiled in)
                object coa;
                if (data.TryGetValue("coaCustom", out coa) && coa is Dictionary<string, object>)
                {
                    var c = (Dictionary<string, object>)coa;
                    var table = new Dictionary<int, uint[]>();
                    object racials, scripted;
                    if (c.TryGetValue("racials", out racials) && racials is Dictionary<string, object>)
                        foreach (var pair in (Dictionary<string, object>)racials)
                        {
                            int race;
                            if (int.TryParse(pair.Key, out race) && pair.Value is object[])
                                table[race] = ((object[])pair.Value).Select(x => (uint)Convert.ToInt64(x)).ToArray();
                        }
                    var code = c.TryGetValue("scripted", out scripted) && scripted is object[] ? ((object[])scripted).Select(x => (uint)Convert.ToInt64(x)).ToArray() : new uint[0];
                    object ported, rows;
                    bool hasCode = c.TryGetValue("ported", out ported) && ported is bool && (bool)ported;
                    string rowsFile = c.TryGetValue("rows", out rows) && rows is string ? Path.Combine(Path.GetDirectoryName(file), ((string)rows).Replace('/', Path.DirectorySeparatorChar)) : null;
                    ServerRacials.UseCoaCustom(table, code, hasCode, rowsFile);
                    // the rows of the CoA Custom pack chosen on this page win over the copy of the module: a newer
                    // CoA Custom brings its changed rows along by itself
                    string coaFolder = Raw("CustomRaces.ClientRootFolder", "");
                    if (ConfigOption.IsQuoted(coaFolder)) coaFolder = ConfigOption.Unquote(coaFolder);
                    string packRows = ServerRacials.PackRows(coaFolder);
                    if (packRows != null) ServerRacials.UseCoaRows(packRows);
                }
            }
            catch (Exception ex) { Shown += (s, e) => Ui.Error(this, L.F("What mod-custom-races knows of the races could not be read ({0}): {1}", file, ex.Message)); }
            if (bases.Count == 0) bases.AddRange(new[] { "Human", "Orc", "Dwarf", "NightElf", "Undead", "Tauren", "Gnome", "Troll", "BloodElf", "Draenei" });
        }

        List<KeyValuePair<int, int>> ClientMoves()
        {
            var pairs = new List<KeyValuePair<int, int>>();
            foreach (string word in Raw("CustomRaces.MoveClientRaces", "").Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] p = word.Split(':');
                int from, to;
                if (p.Length == 2 && int.TryParse(p[0], out from) && int.TryParse(p[1], out to) && from != to)
                    pairs.Add(new KeyValuePair<int, int>(from, to));
            }
            return pairs;
        }

        /// <summary>The races read from the client are the client before the patch: apply the moves the patch will
        /// make, so the page sees the freed numbers and the moved races under their new ones.</summary>
        void ApplyClientMoves()
        {
            foreach (var pair in ClientMoves())
            {
                var race = races.FirstOrDefault(r => r.Pack == null && r.Source == pair.Key);
                if (race == null || races.Any(r => r.Pack == null && r.Source == pair.Value)) continue;
                race.Source = pair.Value;
                slots.Remove(pair.Value); moreSlots.Remove(pair.Value); highSlots.Remove(pair.Value);
                if (!slots.Contains(pair.Key) && !moreSlots.Contains(pair.Key) && !highSlots.Contains(pair.Key))
                {
                    if (pair.Key < 32) slots.Add(pair.Key);
                    else if (pair.Key < 64) moreSlots.Add(pair.Key);
                    else highSlots.Add(pair.Key);
                }
            }
        }

        /// <summary>Reads the races of the game client; a text saying why not, or null.</summary>
        string ReadClient()
        {
            string client = ClientAddons.ClientFolder;
            if (client == null || !ClientAddons.IsClientFolder(client))
                return L.T("No game client is chosen yet. Choose your game folder under Modules › Game client, then open this page again: the races are read from it.");
            try
            {
                var scan = ClientRaces.Read(client, virtuals);
                races.AddRange(scan.Races);
                MarkOwnNumbers();
                slots.AddRange(scan.FreeNumbers);
                moreSlots.AddRange(scan.MoreNumbers);
                highSlots.AddRange(scan.HighNumbers);
                foreach (var h in scan.HelmetPrefixes) helmetPrefixes.Add(h);
                return races.Count == 0 ? L.T("Your game client has no races beyond the stock ones.") : null;
            }
            catch (Exception ex) { return L.T("The races of your game client could not be read:") + " " + ex.Message; }
        }

        /// <summary>The races of the race folder: new ones, and other looks for races this client has.</summary>
        void ReadFolder()
        {
            folder = Raw("CustomRaces.ImportFolder", "");
            if (folder.Length == 0)
            {
                string guess = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "AFK-Rassen");
                folder = Directory.Exists(guess) ? guess : null;
            }
            if (folder == null || !Directory.Exists(folder)) { folder = null; return; }
            foreach (var p in RaceImport.Read(folder))
            {
                // The key is the usual match. A pack can also belong to a client race under another key: an older
                // version keyed by name only, or the client names two rows alike but this pack names one by its file
                // (VulperaEsteria beside Vulpera). The source row (number and file) says which one it is.
                // A source race number alone is not enough: CoA Custom's row 62 is Earthen, while the Lightforged
                // pack came from a client whose row 62 was the Lightforged Draenei. Matching the number alone turned
                // the pack into an alternative look of Earthen, so it never got a card of its own.
                var same = races.FirstOrDefault(r => r.Pack == null && (
                    r.Key.Equals(p.Key, StringComparison.OrdinalIgnoreCase) ||
                    (p.SourceFile.Length > 0 && p.SourceFile.Equals(r.File, StringComparison.OrdinalIgnoreCase) &&
                        (p.Name.Equals(r.Name, StringComparison.OrdinalIgnoreCase) || p.Name.Equals(r.Key, StringComparison.OrdinalIgnoreCase))) ||
                    (p.SourceRace > 0 && r.Source == p.SourceRace && p.SourceFile.Length > 0 &&
                        p.SourceFile.Equals(r.File, StringComparison.OrdinalIgnoreCase))));
                if (same != null) { same.Alt = p; continue; }
                races.Add(new ClientRace { Key = p.Key, Name = p.Name, Prefix = p.Prefix, File = p.File, MaleOnly = p.MaleOnly, Pack = p });
            }
        }

        Known KnownOf(ClientRace r) { Known k; return known.TryGetValue(r.Key, out k) ? k : null; }

        /// <summary>A stock race (its key, "NightElf") as the game names it, in the language shown.</summary>
        static string BaseName(string key)
        {
            switch (key)
            {
                case "Human": return L.T("Human"); case "Orc": return L.T("Orc"); case "Dwarf": return L.T("Dwarf"); case "NightElf": return L.T("Night Elf");
                case "Undead": return L.T("Undead"); case "Tauren": return L.T("Tauren"); case "Gnome": return L.T("Gnome"); case "Troll": return L.T("Troll");
                case "BloodElf": return L.T("Blood Elf"); case "Draenei": return L.T("Draenei");
                default: return key;
            }
        }

        bool UsesFolder(ClientRace r) { return r.Pack != null || (r.Alt != null && Raw("CustomRaces." + r.Key + ".Import", "0") == "1"); }

        RaceImport.Pack PackOf(ClientRace r) { return r.Pack ?? (UsesFolder(r) ? r.Alt : null); }

        /// <summary>A warning when the race's own number in its source client is a different race here. The pack then
        /// takes a free number, and appearance data the other client keeps under that number cannot follow it.</summary>
        string PackNumberConflict(ClientRace r)
        {
            var pack = r.Pack;
            if (pack == null || pack.SourceRace <= 0) return null;
            var other = OtherAtNumber(pack.SourceRace, r);
            if (other == null) return null;
            return L.F("Its source race number {0} is {1} in your game client, so it gets a free number. Looks that the other client keeps under {0} cannot follow it.", pack.SourceRace, other.Name);
        }

        int PreferredNumber(ClientRace r)
        {
            if (r.Pack != null)
            {
                int id = IdOf(r);
                return id > 0 ? id : r.Pack.SourceRace;
            }
            return r.Source;
        }

        ClientRace OtherAtNumber(int number, ClientRace self)
        {
            if (number <= 0) return null;
            return races.FirstOrDefault(x => !ReferenceEquals(x, self) && PreferredNumber(x) == number && !SameRace(self, x));
        }

        int CharactersOf(int race)
        {
            try
            {
                var rows = MySql.Query(inst, DbLogin.FromConfig(inst), "SELECT COUNT(*) FROM acore_characters.characters WHERE race = " + race + ";");
                int n;
                return rows.Count > 0 && int.TryParse(rows[0], out n) ? n : -1;
            }
            catch { return -1; }
        }

        string EsteriaFile(ClientRace r)
        {
            try
            {
                string client = ClientAddons.ClientFolder;
                if (client == null || !Directory.Exists(client)) return null;
                string wanted = "Esteria" + (r.Name ?? "").Replace(" ", "");
                foreach (var file in Directory.GetFiles(client, "Esteria*.bin", SearchOption.TopDirectoryOnly))
                    if (Path.GetFileNameWithoutExtension(file).Equals(wanted, StringComparison.OrdinalIgnoreCase))
                        return Path.GetFileName(file);
            }
            catch { }
            return null;
        }

        int FreeHighNumber(ClientRace self)
        {
            var used = new HashSet<int>(races.Where(x => !ReferenceEquals(x, self)).Select(PreferredNumber));
            foreach (int slot in highSlots.Where(s => s >= 64).Concat(moreSlots.Where(s => s >= 32)).Concat(slots.Where(s => s >= 12)))
                if (!used.Contains(slot)) return slot;
            return 0;
        }

        /// <summary>Moves a race of the game client to a free number. Only for a race that is not stock and has no
        /// characters; the client patch then writes its rows under the new number.</summary>
        bool MoveClientRace(ClientRace r, int to)
        {
            if (r == null || r.Pack != null || ClientRaces.Stock.Contains(r.Source) || to <= 0) return false;
            var pairs = ClientMoves().Where(p => p.Key != r.Source && p.Value != to).ToList();
            pairs.Add(new KeyValuePair<int, int>(r.Source, to));
            Set("CustomRaces.MoveClientRaces", "\"" + string.Join(" ", pairs.Select(p => p.Key + ":" + p.Value)) + "\"");
            int from = r.Source;
            r.Source = to;
            slots.Remove(to); moreSlots.Remove(to); highSlots.Remove(to);
            if (from < 32) slots.Add(from);
            else if (from < 64) moreSlots.Add(from);
            else highSlots.Add(from);
            return true;
        }

        /// <summary>Asks what to do when an imported race wants a number another race already has. False keeps the
        /// race switched off.</summary>
        bool ResolveNumberConflict(ClientRace r)
        {
            int wanted = r.Pack != null ? r.Pack.SourceRace : 0;
            var other = wanted > 0 ? OtherAtNumber(wanted, r) : null;
            if (other == null) return true;

            int characters = other.Pack == null ? CharactersOf(other.Source) : 0;
            string esteria = other.Pack == null ? EsteriaFile(other) : null;
            bool canMove = other.Pack == null && !ClientRaces.Stock.Contains(other.Source) && characters == 0;
            int target = canMove ? FreeHighNumber(other) : 0;
            var lines = new List<string>();
            lines.Add(L.F("The imported race {0} wants race number {1}.", r.Name, wanted));
            lines.Add(L.F("That number belongs to {0} in your game client.", other.Name));
            if (other.Pack != null)
                lines.Add(L.T("It is another imported race. Switch it off or give this race a free number."));
            else if (ClientRaces.Stock.Contains(other.Source))
                lines.Add(L.T("It is one of the stock races. Its number cannot be moved."));
            else
            {
                lines.Add(L.T("It is a race of your game client. Switching it off would not free its number; it has to be moved."));
                lines.Add(characters < 0 ? L.T("Its characters could not be checked, so moving is not offered.") :
                    characters == 0 ? L.T("It has no characters.") : L.F("It has {0} character(s); moving would change them.", characters));
                if (esteria != null)
                    lines.Add(L.F("It looks like an Esteria race ({0}); moving its number can break its appearance.", esteria));
            }
            lines.Add(L.F("With a free number for {0}, appearance data tied to {1} may be missing.", r.Name, wanted));
            string move = canMove && target > 0 ? L.F("Move {0} to {1}", other.Name, target) : "";
            var answer = RaceNumberDialog.Ask(this, string.Join("\n\n", lines), L.T("Use a free number"), move);
            if (answer == RaceNumberChoice.Cancel) return false;
            if (answer == RaceNumberChoice.MoveExisting)
            {
                if (!canMove || target <= 0 || !MoveClientRace(other, target)) return false;
                Set("CustomRaces." + r.Key + ".Id", wanted.ToString());
            }
            return true;
        }

        /// <summary>The stock race a race of the client most likely leans on, by its name.</summary>
        /// <summary>The helmet prefix of each stock race a race can lean on.</summary>
        static readonly Dictionary<string, string> BasePrefixes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Human", "Hu" }, { "Orc", "Or" }, { "Dwarf", "Dw" }, { "NightElf", "Ni" }, { "Undead", "Sc" }, { "Tauren", "Ta" },
            { "Gnome", "Gn" }, { "Troll", "Tr" }, { "BloodElf", "Be" }, { "Draenei", "Dr" },
        };

        string DefaultBase(ClientRace r)
        {
            var k = KnownOf(r);
            if (k != null && bases.Contains(k.Base)) return Side(k.Base, r);
            string n = r.Name.ToLowerInvariant() + " " + r.File.ToLowerInvariant();
            foreach (var pair in new[] { new[] { "troll", "Troll" }, new[] { "orc", "Orc" }, new[] { "dwar", "Dwarf" }, new[] { "gnom", "Gnome" },
                new[] { "tauren", "Tauren" }, new[] { "taunka", "Tauren" }, new[] { "skelet", "Undead" }, new[] { "undead", "Undead" }, new[] { "scourge", "Undead" },
                new[] { "night", "NightElf" }, new[] { "blood", "BloodElf" }, new[] { "elf", "BloodElf" }, new[] { "draenei", "Draenei" } })
                if (n.Contains(pair[0])) return Side(pair[1], r);
            return Side("Human", r);
        }

        /// <summary>For a race of the race folder: a base race of the side it had in its client (an Eredar of the Horde
        /// leans on a Horde race).</summary>
        string Side(string guess, ClientRace r)
        {
            if (r.Pack == null) return guess;
            int side = r.Pack.Side;
            if (side == 2)
            {
                string name = (r.Name + " " + r.File + " " + r.Key).ToLowerInvariant();
                if (name.Contains("horde")) side = 1;
                else if (name.Contains("alliance")) side = 0;
            }
            var horde = new HashSet<string> { "Orc", "Undead", "Tauren", "Troll", "BloodElf" };
            if (side == 0 && horde.Contains(guess)) return guess == "BloodElf" ? "NightElf" : guess == "Troll" ? "Dwarf" : "Human";
            if (side == 1 && !horde.Contains(guess)) return guess == "Draenei" || guess == "NightElf" ? "BloodElf" : guess == "Dwarf" ? "Troll" : "Orc";
            return guess;
        }

        List<string> Chosen() { return Raw("CustomRaces.Races", "").Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries).ToList(); }

        /// <summary>After the settings: the racial rows of every race that is on are written for the server.</summary>
        protected override void Saved()
        {
            try
            {
                HealNumbers();
                var on = new List<ServerRacials.Wanted>();
                foreach (var key in Chosen())
                {
                    var r = races.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
                    string name = Raw("CustomRaces." + key + ".Name", "");
                    var wanted = new ServerRacials.Wanted { Key = key, Name = name.Length > 0 ? name : r != null ? r.Name : key, CoaRace = r != null ? CoaRaceOf(r) : 0 };
                    var pack = r != null ? PackOf(r) : null;
                    if (pack != null)
                        foreach (var spell in pack.Spells)
                        {
                            object id, row;
                            if (spell.TryGetValue("id", out id) && spell.TryGetValue("row", out row) && row is object[])
                                wanted.Rows[(uint)Convert.ToInt64(id)] = (object[])row;
                        }
                    on.Add(wanted);
                }
                ServerRacials.Sync("", RacialsFolder, ClientAddons.ClientFolder, on);
            }
            catch (Exception ex) { Ui.Error(this, L.T("The racial rows could not be written:") + "\n" + ex.Message); }
        }

        /// <summary>Where the racial rows of the races go for the server (CustomRaces.RacialsFolder).</summary>
        string RacialsFolder { get { return Path.Combine(inst.ConfigDir, "modules", "custom-races-racials"); } }

        /// <summary>The race's number in the client it comes from, for CoA Custom's racials: a race of the client its own
        /// row, a race of the race folder the number it had in its client.</summary>
        int CoaRaceOf(ClientRace r)
        {
            var pack = PackOf(r);
            if (pack == null)
                return r.Virtual ? 0 : r.Source;
            int source = pack.SourceRace;
            // A race folder pack may reuse a model row of another race (the Pandaren Horde model can sit in
            // a Human row). CoA Custom gives the racials under its own race number, so use the name the pack
            // carries rather than the row it was copied from.
            if (pack.Source.Equals("CoA Custom", StringComparison.OrdinalIgnoreCase))
            {
                string n = (pack.Name + " " + pack.File + " " + pack.Key).ToLowerInvariant();
                if (n.Contains("pandaren")) return 18;
                if (n.Contains("dracthyr")) return 54;
                if (n.Contains("vulpera")) return n.Contains("esteria") ? 74 : 20;
                if (n.Contains("drakonid")) return pack.Side == 0 ? 100 : 98;
                if (n.Contains("darkfallen")) return pack.Side == 0 ? 80 : 81;
                if (n.Contains("earthen")) return pack.Side == 0 ? 68 : 69;
                if (n.Contains("haranir")) return pack.Side == 0 ? 71 : 70;
                if (n.Contains("murloc")) return 65;
                if (n.Contains("drakkin")) return 99;
                if (n.Contains("highmountain")) return 66;
                if (n.Contains("mechagnome")) return 67;
                if (n.Contains("gnoll")) return 83;
                if (n.Contains("sethrak")) return 82;
                if (n.Contains("saberon")) return 97;
            }
            return source;
        }

        int IdOf(ClientRace r)
        {
            int n;
            if (int.TryParse(Raw("CustomRaces." + r.Key + ".Id", ""), out n)) return n;
            return r.OwnNumber ? r.Source : 0;
        }

        /// <summary>Whether a race number already belongs to a different race of this client. An imported Gnoll
        /// must not take 63: in CoA Custom's table that is Nightborne, and EsteriaAppearance.dll would give the
        /// Gnoll Nightborne's appearance.</summary>
        bool NumberTakenByOther(ClientRace r, int id)
        {
            if (id <= 0) return false;
            if (ClientRaces.Stock.Contains(id)) return true;
            foreach (var o in races)
            {
                if (ReferenceEquals(o, r) || IdOf(o) != id || SameRace(r, o)) continue;
                return true;
            }
            return false;
        }

        static bool SameRace(ClientRace a, ClientRace b)
        {
            string af = a.Pack != null ? a.Pack.SourceFile : a.File;
            string bf = b.Pack != null ? b.Pack.SourceFile : b.File;
            string an = a.Pack != null ? a.Pack.Name : a.Name;
            string bn = b.Pack != null ? b.Pack.Name : b.Name;
            return (af.Length > 0 && af.Equals(bf, StringComparison.OrdinalIgnoreCase)) ||
                (an.Length > 0 && an.Equals(bn, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Gives a race whose saved number belongs to another race a free number, before the page or the
        /// server uses it. The common case is an older Gnoll import that was saved under 63 (Nightborne).</summary>
        void HealNumbers()
        {
            var moved = new List<ClientRace>();
            foreach (var r in races)
            {
                if (r.OwnNumber) continue;
                int id = IdOf(r);
                if (!NumberTakenByOther(r, id)) continue;
                Set("CustomRaces." + r.Key + ".Id", "0");
                moved.Add(r);
            }
            foreach (var r in moved)
            {
                int id = CoaNumberOf(r);
                if (id == 0) id = FreeSlot(r);
                if (id > 0) Set("CustomRaces." + r.Key + ".Id", id.ToString());
            }
        }

        int FreeSlot(ClientRace r)
        {
            var slots = Slots;
            var used = new HashSet<int>(races.Where(x => !x.OwnNumber).Select(IdOf).Where(slots.Contains));
            // A race imported from CoA Custom already has a number of its own there: Gnoll 83,
            // Nightborne 63, Sethrak 82, Saberon 97. If its own number is taken here, it should not
            // fall back to another CoA number: 63 is Nightborne, and EsteriaAppearance.dll would
            // load Nightborne's looks for a Gnoll under it. Prefer a free number outside CoA
            // Custom's table, but never refuse a race because every free number is in that table.
            HashSet<int> coa = High && r != null && r.Pack != null &&
                r.Pack.Source.Equals("CoA Custom", StringComparison.OrdinalIgnoreCase)
                ? new HashSet<int>(CoaRaceFiles().Keys) : null;
            foreach (int s in slots)
                if (!used.Contains(s) && (coa == null || !coa.Contains(s))) return s;
            // Some clients' free numbers all appear in CoA Custom's race table. Take one of them
            // rather than claiming that all numbers are taken.
            foreach (int s in slots)
                if (!used.Contains(s)) return s;
            return 0;
        }

        /// <summary>The helmet prefix of the models in a race pack, if it has any. A race may bring helmets under a
        /// prefix other than the prefix of its source row (the Esteria Vulpera uses Vu, its row says Gn).</summary>
        static string PackHelmetPrefix(RaceImport.Pack pack)
        {
            var prefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in pack.Files)
            {
                if (!f.StartsWith("Item\\ObjectComponents\\Head\\", StringComparison.OrdinalIgnoreCase) ||
                    !f.EndsWith(".m2", StringComparison.OrdinalIgnoreCase)) continue;
                string stem = Path.GetFileNameWithoutExtension(f);
                if (stem.Length < 4) continue;
                char sex = stem[stem.Length - 1];
                if (sex != 'M' && sex != 'F') continue;
                prefixes.Add(stem.Substring(stem.Length - 3, 2));
            }
            if (prefixes.Count == 0) return "";
            if (pack.Prefix.Length > 0 && prefixes.Contains(pack.Prefix)) return pack.Prefix;
            return prefixes.Count == 1 ? prefixes.First() : "";
        }

        /// <summary>Writes what the server and the client patch need of a race, as the client has it now.</summary>
        void Describe(ClientRace r, int id)
        {
            Action<string, string> put = (name, value) => { if (Raw("CustomRaces." + r.Key + "." + name, null) != value) Set("CustomRaces." + r.Key + "." + name, value); };
            Action<string, string> putText = (name, value) => { if (Raw("CustomRaces." + r.Key + "." + name, null) != value) Set("CustomRaces." + r.Key + "." + name, "\"" + value.Replace("\"", "") + "\""); };
            var pack = PackOf(r);
            string ownHelmetPrefix = "";
            if (pack != null)
            {
                // from the race folder: its rows and files; its model gets new display numbers
                put("Source", "0");
                put("Id", id.ToString());
                put("Copy", "0");
                put("Import", "1");
                putText("Name", pack.Name);
                putText("Prefix", pack.Prefix);
                putText("File", pack.File.Length > 0 ? pack.File : pack.Key);
                put("MaleDisplay", id > 0 ? (RaceImport.DisplayBase + id * 2).ToString() : "0");
                put("FemaleDisplay", id > 0 ? (RaceImport.DisplayBase + id * 2 + 1).ToString() : "0");
                put("MaleOnly", pack.MaleOnly ? "1" : "0");
                // its own helmets: in the game client, or in its race pack (made from a client that has them)
                bool packHelmets = pack.Prefix.Length > 0 && pack.Files.Any(f => f.StartsWith("Item\\ObjectComponents\\Head\\", StringComparison.OrdinalIgnoreCase)
                    && (f.EndsWith("_" + pack.Prefix + "M.m2", StringComparison.OrdinalIgnoreCase) || f.EndsWith("_" + pack.Prefix + "F.m2", StringComparison.OrdinalIgnoreCase)));
                put("OwnHelmets", helmetPrefixes.Count == 0 || helmetPrefixes.Contains(pack.Prefix) || packHelmets ? "1" : "0");
                ownHelmetPrefix = PackHelmetPrefix(pack);
            }
            else
            {
                put("Source", r.Source.ToString());
                put("Id", id.ToString());
                put("Copy", r.OwnNumber ? "0" : "1");
                put("Import", "0");
                putText("Name", r.Name);
                putText("Prefix", r.Prefix);
                putText("File", r.File);
                put("MaleDisplay", r.MaleDisplay.ToString());
                put("FemaleDisplay", r.FemaleDisplay.ToString());
                put("MaleOnly", r.MaleOnly ? "1" : "0");
                put("OwnHelmets", r.HasHelmets ? "1" : "0");
            }
            if (Raw("CustomRaces." + r.Key + ".Base", "").Length == 0) putText("Base", DefaultBase(r));
            // a race without helmet models of its own wears those of its base race, as CoA Custom does it (its races take
            // the helmet prefix of the closest stock race): the client finds helmets by the prefix of the race
            // (Item\ObjectComponents\Head\<helmet>_<prefix><M|F>.m2), and without any every helmet is a checkered box
            string borrowed;
            if (ownHelmetPrefix.Length > 0)
            {
                putText("HelmetPrefix", ownHelmetPrefix);
                put("OwnHelmets", "1");
            }
            else if (Raw("CustomRaces." + r.Key + ".OwnHelmets", "1") == "0" && BasePrefixes.TryGetValue(ConfigOption.Unquote(Raw("CustomRaces." + r.Key + ".Base", "")), out borrowed))
            {
                putText("HelmetPrefix", borrowed);
                put("OwnHelmets", "1");
            }
            else
                putText("HelmetPrefix", "");
            put("Appearance", ExtraLooksOf(r, id).ToString());
            // its racials: the set the server pack gives it (its rows travel to the server data folder as well)
            var racials = ServerRacials.Spells("", pack != null ? pack.Name : r.Name, CoaRaceOf(r));
            if (racials.Length > 0) putText("Racials", string.Join(" ", racials));
            // its icon: from its folder, or from the icons of the race folder (_Icons)
            string icon = null;
            if (pack != null)
            {
                var f = pack.Files.FirstOrDefault(x => x.StartsWith("Interface\\Icons\\", StringComparison.OrdinalIgnoreCase));
                if (f != null) icon = Path.Combine(pack.Dir, "files", f.Replace('\\', Path.DirectorySeparatorChar));
            }
            if ((icon == null || !File.Exists(icon)) && folder != null) icon = Path.Combine(folder, "_Icons", "CoA_Race_" + r.Key + ".blp");
            if (icon != null && File.Exists(icon))
            {
                putText("IconFile", icon);
                putText("Icon", "Interface\\Icons\\CoA_Race_" + r.Key);
            }
            else { putText("IconFile", ""); putText("Icon", ""); }
        }

        /// <summary>A race described before, as the client has it now (it may have changed with an update of the game).</summary>
        void Refresh()
        {
            // the race folder found by itself (Downloads\AFK-Rassen) is written, so the client patch finds it too
            if (folder != null && Raw("CustomRaces.ImportFolder", "").Length == 0) Set("CustomRaces.ImportFolder", "\"" + folder + "\"");
            // the racial rows go into a folder of the server's own configuration (the module reads them from there)
            if (ConfigOption.Unquote(Raw("CustomRaces.RacialsFolder", "")) != RacialsFolder) Set("CustomRaces.RacialsFolder", "\"" + RacialsFolder + "\"");
            foreach (var r in races)
                if (Raw("CustomRaces." + r.Key + ".Source", "").Length > 0) Describe(r, IdOf(r));
        }

        void UpdateUsage()
        {
            var slots = Slots;
            int high = High ? highSlots.Count : 0;
            int used = races.Count(r => !r.OwnNumber &&
                (slots.Contains(IdOf(r)) || (High && highSlots.Contains(IdOf(r)))));
            int on = Chosen().Count(k => races.Any(r => r.Key.Equals(k, StringComparison.OrdinalIgnoreCase)));
            usage.Text = L.F("{0} of up to {1} races on", on, races.Count(r => r.OwnNumber) + slots.Count + high) + "  ·  " +
                (More ? L.T("64-race patch on") : L.T("64-race patch off")) + "  ·  " +
                L.F("{0} of {1} free race numbers in use", used, slots.Count + high);
            usage.ForeColor = used >= slots.Count + high ? Ui.Warn : Ui.Text;
        }

        void AddRace(Card card, ClientRace r)
        {
            var k = KnownOf(r);
            var row = Ui.Row(); row.BackColor = Ui.Surface;
            var toggle = new Toggle(L.Outside(r.Name)) { BackColor = Ui.Surface };
            toggle.Wrap(220);
            toggle.Width = 230;
            var basePick = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130, Font = Ui.Base, Margin = new Padding(4, 3, 8, 0) };
            foreach (var b in bases) basePick.Items.Add(BaseName(b));
            var number = new Label { AutoSize = false, Width = 175, Height = 30, Font = Ui.Small, ForeColor = Ui.Muted, TextAlign = ContentAlignment.MiddleLeft, BackColor = Ui.Surface };
            var free = Ui.Ghost(L.T("Free its number"));
            free.Visible = !r.OwnNumber;
            var move = Ui.Ghost(L.T("Move to a free number"));
            move.Visible = r.Pack == null && !r.Virtual && !ClientRaces.Stock.Contains(r.Source);
            row.Controls.Add(toggle); row.Controls.Add(new Label { Text = L.T("leans on"), Font = Ui.Small, ForeColor = Ui.Muted, AutoSize = true, Margin = new Padding(0, 9, 0, 0), BackColor = Ui.Surface });
            row.Controls.Add(basePick); row.Controls.Add(number); row.Controls.Add(free); row.Controls.Add(move);
            card.Add(row);
            string about = r.Pack != null ? L.F("From your race folder ({0}, {1} MB).", r.Pack.Source.Length > 0 ? r.Pack.Source : L.T("another client"), r.Pack.Bytes / 1000000) +
                    (r.Pack.Missing > 0 ? " " + L.F("{0} of its files are missing; the choices that need them are left out.", r.Pack.Missing) : "")
                : k != null && k.About.Length > 0 ? L.Outside(k.About) : r.Virtual ? L.T("A race of your game client.") : L.F("A race of your game client (row {0} of its race table).", r.Source);
            if (k != null && k.Note.Length > 0) about += " " + L.Outside(k.Note);
            string conflict = PackNumberConflict(r);
            if (conflict != null) about += " " + conflict;
            var racialSpells = ServerRacials.Spells("", r.Pack != null ? r.Pack.Name : r.Name, CoaRaceOf(r));
            var needCode = ServerRacials.NeedCode(racialSpells);
            if (needCode.Length > 0)
                about += " " + L.F("{0} of its {1} racial abilities need code of CoA Custom's server and do nothing here yet.", needCode.Length, racialSpells.Length);
            var aboutLabel = Small(about, card.Inner - 40, 46);
            card.Add(aboutLabel);
            if (r.Pack != null)
            {
                var del = Ui.Ghost(L.T("Delete from the race folder"));
                del.Margin = new Padding(46, 0, 0, 10);
                card.Add(del);
                del.Click += (s, e) => DeletePack(r, row, aboutLabel, del);
            }
            if (r.Alt != null)
            {
                var alt = new Toggle(L.F("Looks from the race folder ({0}, {1} MB) instead of your client", r.Alt.Source.Length > 0 ? r.Alt.Source : L.T("another client"), r.Alt.Bytes / 1000000)) { BackColor = Ui.Surface, Margin = new Padding(46, 0, 0, 8) };
                alt.Wrap(card.Inner - 80);
                card.Add(alt);
                AddLoader(() => alt.Checked = Raw("CustomRaces." + r.Key + ".Import", "0") == "1");
                alt.CheckedChanged += (s, e) =>
                {
                    if (Loading) return;
                    Set("CustomRaces." + r.Key + ".Import", alt.Checked ? "1" : "0");
                    if (Raw("CustomRaces." + r.Key + ".Source", "").Length > 0 || alt.Checked) Describe(r, IdOf(r) > 0 ? IdOf(r) : r.OwnNumber ? r.Source : 0);
                };
                var delAlt = Ui.Ghost(L.T("Delete these looks from the race folder"));
                delAlt.Margin = new Padding(46, 0, 0, 10);
                card.Add(delAlt);
                delAlt.Click += (s, e) => DeleteAltPack(r, alt, delAlt);
            }

            Action show = () =>
            {
                int id = IdOf(r);
                number.Text = r.OwnNumber ? L.F("race number {0} (its own)", r.Source) : id > 0 ? L.F("race number {0}", id) : L.T("no race number yet");
                free.Enabled = !r.OwnNumber && id > 0 && !toggle.Checked;
            };
            AddLoader(() =>
            {
                toggle.Checked = Chosen().Any(c => c.Equals(r.Key, StringComparison.OrdinalIgnoreCase));
                int at = bases.FindIndex(b => b.Equals(Raw("CustomRaces." + r.Key + ".Base", DefaultBase(r)), StringComparison.OrdinalIgnoreCase));
                basePick.SelectedIndex = at < 0 ? Math.Max(0, bases.IndexOf(DefaultBase(r))) : at;
                show();
            });
            OnChange(show);
            toggle.CheckedChanged += (s, e) =>
            {
                if (Loading) return;
                var chosen = Chosen().Where(c => !c.Equals(r.Key, StringComparison.OrdinalIgnoreCase)).ToList();
                if (toggle.Checked)
                {
                    if (!ResolveNumberConflict(r)) { toggle.Checked = false; return; }
                    int id = IdOf(r);
                    if (id == 0) id = CoaNumberOf(r);
                    if (id == 0)
                    {
                        id = FreeSlot(r);
                        if (id == 0)
                        {
                            Ui.Error(this, L.F("All {0} free race numbers are taken{1}. Free the number of a race you do not want any more (switch it off, then \"Free its number\") to make room for the {2}.", Slots.Count, More ? "" : " " + L.T("(switch on the 64-race patch for more)"), L.Outside(r.Name)));
                            toggle.Checked = false;
                            return;
                        }
                    }
                    if (Raw("CustomRaces." + r.Key + ".Base", "").Length == 0 && basePick.SelectedIndex >= 0) Set("CustomRaces." + r.Key + ".Base", "\"" + bases[basePick.SelectedIndex] + "\"");
                    Describe(r, id);
                    chosen.Add(r.Key);
                }
                Set("CustomRaces.Races", string.Join(" ", chosen));
            };
            basePick.SelectedIndexChanged += (s, e) =>
            {
                if (Loading || basePick.SelectedIndex < 0) return;
                Set("CustomRaces." + r.Key + ".Base", "\"" + bases[basePick.SelectedIndex] + "\"");
                if (Raw("CustomRaces." + r.Key + ".Source", "").Length > 0) Describe(r, IdOf(r));
            };
            free.Click += (s, e) =>
            {
                if (!Ui.Confirm(this, L.F("Give up the race number of the {0}? Its characters stay their base race for good, and another race can take the number.", L.Outside(r.Name)))) return;
                Set("CustomRaces." + r.Key + ".Id", "0");
            };
            move.Click += (s, e) =>
            {
                if (r.Pack != null || r.Virtual || ClientRaces.Stock.Contains(r.Source)) return;
                int characters = CharactersOf(r.Source);
                if (characters < 0)
                {
                    Ui.Error(this, L.T("The characters of this race could not be checked. Start the database and try again; moving is not offered without that check."));
                    return;
                }
                if (characters > 0)
                {
                    Ui.Error(this, L.F("{0} still has {1} character(s). A race with characters cannot be moved.", L.Outside(r.Name), characters));
                    return;
                }
                int target = FreeHighNumber(r);
                if (target == 0)
                {
                    Ui.Error(this, L.T("There is no free race number left for this move."));
                    return;
                }
                string esteria = EsteriaFile(r);
                if (esteria != null && !Ui.Confirm(this, L.F("{0} looks like an Esteria race ({1}). Moving it can break its appearance. Continue?", L.Outside(r.Name), esteria))) return;
                if (!Ui.Confirm(this, L.F("Move {0} from race number {1} to {2}? The client patch must be built again afterwards.", L.Outside(r.Name), r.Source, target))) return;
                if (!MoveClientRace(r, target)) { Ui.Error(this, L.T("The race could not be moved.")); return; }
                show();
                UpdateUsage();
                MessageBox.Show(this, L.F("{0} will use race number {1} after the next client patch.", L.Outside(r.Name), target) + "\n\n" +
                    L.T("Save the page and build the client patch again."), Product.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
        }

        /// <summary>Takes a race of the race folder out of it: its folder with its files, its settings and its number.</summary>
        void DeletePack(ClientRace r, Control row, Control about, Control button)
        {
            var pack = r.Pack;
            if (pack == null) return;
            if (!Ui.Confirm(this, L.F("Delete {0} from the race folder?", L.Outside(r.Name)) + "\n\n" + pack.Dir + "\n\n" +
                L.T("Its files are removed from disk; if the race is switched on, it is switched off first, its race number becomes free for another race, and its characters play their base race from now on. This cannot be undone."))) return;
            if (!DeleteFolder(pack.Dir)) return;
            var chosen = Chosen().Where(c => !c.Equals(r.Key, StringComparison.OrdinalIgnoreCase)).ToList();
            Set("CustomRaces.Races", string.Join(" ", chosen));
            foreach (var option in new[] { "Source", "Id", "Copy", "Import", "Name", "Prefix", "File", "MaleDisplay", "FemaleDisplay", "MaleOnly", "OwnHelmets", "HelmetPrefix", "Base", "Racials", "IconFile", "Icon", "Appearance" })
                Unset("CustomRaces." + r.Key + "." + option);
            row.Visible = false; about.Visible = false; button.Visible = false;
            races.Remove(r);
            UpdateUsage();
            MessageBox.Show(this, L.F("{0} was deleted from the race folder.", L.Outside(r.Name)) + "\n\n" +
                L.T("Save the page and build the client patch anew so the race disappears from the game."), Product.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>Takes the looks a race has in the race folder out of it again.</summary>
        void DeleteAltPack(ClientRace r, Control toggle, Control button)
        {
            var pack = r.Alt;
            if (pack == null) return;
            if (!Ui.Confirm(this, L.F("Delete the race folder looks of {0}?", L.Outside(r.Name)) + "\n\n" + pack.Dir + "\n\n" +
                L.T("Its files are removed from disk; where the race used the folder looks, it shows the looks of your game client again. This cannot be undone."))) return;
            if (!DeleteFolder(pack.Dir)) return;
            Set("CustomRaces." + r.Key + ".Import", "0");
            toggle.Visible = false; button.Visible = false;
            r.Alt = null;
            MessageBox.Show(this, L.F("The race folder looks of {0} were deleted.", L.Outside(r.Name)) + "\n\n" +
                L.T("Save the page and build the client patch anew."), Product.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        bool DeleteFolder(string dir)
        {
            try { Cursor = Cursors.WaitCursor; Directory.Delete(dir, true); return true; }
            catch (Exception ex) { Ui.Error(this, L.T("The race could not be deleted:") + "\n" + ex.Message); return false; }
            finally { Cursor = Cursors.Default; }
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
                Tuple.Create(L.T("Old world"), Rules.ClassicFrom, Rules.ClassicTo, OldWorld),
                Tuple.Create(L.T("Outland"), Rules.OutlandFrom, Rules.OutlandTo, Outland),
                Tuple.Create(L.T("Northrend"), Rules.NorthrendFrom, Rules.NorthrendTo, Northrend),
                Tuple.Create(L.T("Raids and heroics"), Rules.Endgame, Rules.Endgame, End),
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
                ? L.F("A character of level {0} meets the weakest creatures of a zone at {1}, the strongest at {2}, elites and rares up to {3}.", Level, Level - Below, Level + Above, Level + Above + 1)
                : L.T("Without the window every lifted creature stands at the same level below the character (DestinyWeaver.Scaling.Offset).");
            TextRenderer.DrawText(g, text, Ui.Small, new Rectangle(8, 74, Width - 16, 18), Ui.Muted, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, L.T("Character"), Ui.Small, new Point((int)X(Level) - 26, 0), Ui.Accent);
        }
    }

    class JourneySetup : SetupDialog
    {
        class Place { public int Id, Part, Kind, Original, Content; public string Name; public bool Instance; }
        readonly List<Place> places = new List<Place>();
        readonly JourneyRules rules = new JourneyRules();
        readonly JourneyBar bar = new JourneyBar();
        readonly ListView placeList = new ListView { View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, Font = Ui.Base, BorderStyle = BorderStyle.FixedSingle, Height = 330 };
        readonly Segmented placeKind = new Segmented(L.T("Zones"), L.T("Dungeons and raids"));
        readonly Toggle byHand = new Toggle(L.T("Set this one by hand"));
        Slider handLevel;
        readonly Label placeName = new Label { AutoSize = true, Font = Ui.Bold, ForeColor = Ui.Text, BackColor = Ui.Surface, Margin = new Padding(0, 8, 0, 2) };
        static readonly string[] Parts = { "OldWorld", "Outland", "Northrend", "Endgame" };
        static readonly string[] PartNames = { L.T("Old world"), L.T("Outland"), L.T("Northrend"), L.T("Raids and heroics") };
        static readonly string[] Ranks = { "Normal", "Elite", "RareElite", "WorldBoss", "Rare" };
        static readonly string[] RankNames = { L.T("Normal"), L.T("Elite"), L.T("Rare elite"), L.T("World boss"), L.T("Rare") };
        static readonly string[] Stats = { "Health", "Damage", "SpellDamage", "Armor" };
        static readonly string[] StatNames = { L.T("Health"), L.T("Damage"), L.T("Spell damage"), L.T("Armor") };
        readonly Segmented diffPart = new Segmented(L.T("All parts"), L.T("Old world"), L.T("Outland"), L.T("Northrend"), L.T("Raids and heroics"));
        readonly Toggle partOwn = new Toggle(L.T("Own values for this part of the world"));
        readonly Slider[,] diff = new Slider[5, 4];
        readonly Segmented preset = new Segmented(L.T("Relaxed"), L.T("Standard"), L.T("Challenging"), L.T("Hard"));
        readonly PillToggle extraHard = new PillToggle(L.T("Extra hard")) { BackColor = Ui.Surface };
        ConfigFile coa;
        const string CoaSwitch = "CoA.CreatureScaling.Enable";

        public JourneySetup(Install i) : base(i, "mod_world_journey.conf", L.T("World Journey"), L.T("mod-world-journey  ·  modules\\mod_world_journey.conf"), "journey", "Journey.Enable", L.T("World Journey on"))
        {
            LoadPlaces();

            // ---- the journey
            Tab(L.T("The journey"));
            var shape = Card(L.T("Where each part of the world begins"), L.T("The zones of a part open over its range, in their original order; every zone stays worth playing until 60, because CoA lifts its creatures to a character. The ranges may overlap: that makes the journey a choice instead of a tube."), "journey");
            shape.Add(bar).Width = shape.Inner;
            AddRange(shape, "Journey.Classic.Range", L.T("Old world (was 1–55)"), 1, 60, null, JourneyBar.OldWorld);
            AddRange(shape, "Journey.Outland.Range", L.T("Outland (was 58–67)"), 1, 60, null, JourneyBar.Outland);
            AddRange(shape, "Journey.Northrend.Range", L.T("Northrend (was 68–77)"), 1, 60, null, JourneyBar.Northrend);

            var end = Card(L.T("The end of the journey"), L.T("The raids and heroic dungeons of Outland and Northrend are played at the end, after those of the old world, which stay as they are."), "gamemaster");
            AddSlider(end, "Journey.Endgame.Level", L.T("Level of raids and heroics"), 50, 60, 1);
            AddSlider(end, "Journey.Endgame.BossLevels", L.T("Their bosses stand higher by"), 0, 3, 1, v => "+" + (int)v);
            AddRange(end, "Journey.Endgame.Outland.ItemLevels", L.T("Item levels of Outland's rewards"), 60, 200, L.T("The raids of the old world give 66 to 92: Outland's rewards come next on the ladder, Northrend's last."));
            AddRange(end, "Journey.Endgame.Northrend.ItemLevels", L.T("Item levels of Northrend's rewards"), 60, 200);

            var guard = Card(L.T("The rules of the realm"), null, "settings");
            AddToggle(guard, "Journey.Scaling.Forced", L.T("Every character plays the scaled world"),
                L.T("CoA's level scaling is on for everyone. The question at character creation still appears, but \"off\" no longer counts. Off: each character keeps the choice it made at creation."));
            AddToggle(guard, "Journey.Guard.Enable", L.T("Keep the realm at 60"),
                L.T("The realm runs with the highest level 60, the Dungeon Finder with the dungeons of Outland and Northrend and Wintergrasp from 60, whatever worldserver.conf says."));

            // ---- zones and dungeons
            Tab(L.T("Zones and dungeons"));
            var where = Card(L.T("Zones and dungeons"), L.T("Where each one opens on the journey, worked out from the ranges. Set one by hand to move it: its creatures and quests move with it, and the bots and the world map follow."), "globe");
            where.Add(placeKind);
            placeList.Width = where.Inner;
            placeList.Columns.Add(L.T("Name"), 300); placeList.Columns.Add(L.T("Part"), 150); placeList.Columns.Add(L.T("Originally"), 100, HorizontalAlignment.Right);
            placeList.Columns.Add(L.T("On the journey"), 130, HorizontalAlignment.Right);
            where.Add(placeList);
            where.Add(placeName);
            byHand.BackColor = Ui.Surface;
            where.Add(byHand);
            handLevel = AddSlider(where, null, L.T("Opens at level"), 1, 60, 1);
            placeKind.SelectedChanged += (s, e) => FillPlaces();
            placeList.SelectedIndexChanged += (s, e) => ShowPlace();
            byHand.CheckedChanged += (s, e) => { if (!filling) SetHand(); };
            handLevel.ValueChanged += (s, e) => { if (!filling && byHand.Checked) SetHand(); };
            if (places.Count == 0) Note(L.T("The list of zones comes with the module (data\\journey-ui.json). Update World Journey to see it here."));

            // ---- level window
            Tab(L.T("Level window"));
            var window = Card(L.T("The level window"), L.T("Creatures lifted to a character keep their place in their zone: the weakest stand a little below the character, the strongest a little above, elites and rares one more. A creature is never set below its own level."), "server");
            var winOn = AddToggle(window, "Journey.Window.Enable", L.T("Use the level window"));
            var below = AddSlider(window, "Journey.Window.Below", L.T("Weakest creatures below"), 0, 10, 1, v => "−" + (int)v);
            var above = AddSlider(window, "Journey.Window.Above", L.T("Strongest creatures above"), 0, 10, 1, v => "+" + (int)v);
            var preview = window.Add(new WindowPreview());
            preview.Width = window.Inner;
            var level = AddSlider(window, null, L.T("Try it with a character of level"), 10, 60, 1);
            level.Value = 40;
            Action showWindow = () => { preview.On = winOn.Checked; preview.Below = (int)below.Value; preview.Above = (int)above.Value; preview.Level = (int)level.Value; preview.Invalidate(); };
            level.ValueChanged += (s, e) => showWindow();
            OnChange(showWindow);
            Note(L.F("The level window and the forced scaling need World Journey's change to the core, which {0} brings in when it builds the server.", Product.Name));

            // ---- difficulty
            Tab(L.T("Difficulty"));
            var presets = Card(L.T("How hard"), L.T("A starting point for the multipliers below. They come on top of the creatures' base stats and of the Rate.Creature.* settings of the server."), "gamemaster");
            var howHard = Ui.Row(); howHard.BackColor = Ui.Surface;
            howHard.Controls.Add(preset);
            extraHard.Margin = new Padding(28, 4, 0, 6);
            howHard.Controls.Add(extraHard);
            presets.Add(howHard);
            presets.Add(Small(L.T("Relaxed: a bit softer than the original. Standard: as the module comes, rares stronger. Challenging and Hard: everything hits harder and lasts longer."), presets.Inner));
            var extraText = presets.Add(Small("", presets.Inner));
            preset.SelectedChanged += (s, e) => ApplyPreset(preset.Selected);
            coa = AlsoEdit(inst.CoaConf, "modules\\coa.conf");
            bool coaHasIt = coa.ByKey.ContainsKey(CoaSwitch);
            extraHard.Enabled = coaHasIt;
            extraHard.CheckedChanged += (s, e) => SetIn(coa, CoaSwitch, extraHard.Checked ? "1" : "0");
            AddLoader(() => extraHard.Checked = coaHasIt && RawIn(coa, CoaSwitch, "0") == "1");
            OnChange(() =>
            {
                if (!coaHasIt) { extraText.Text = L.T("Extra hard needs a CoA core from October 2026 or later, which has creature multipliers of its own. Update the server to use it."); return; }
                Func<string, string, string> x = (key, fallback) => "×" + RawIn(coa, "CoA.CreatureScaling." + key, fallback);
                extraText.Text = L.F("Extra hard adds CoA's own creature multipliers (coa.conf) on top of the level chosen here: " +
                    "in the open world creatures have {0} health and hit players and pets {1} as hard, " +
                    "in dungeons {2} health (some maps more) and {3} damage, in raids {4} health. The values themselves are set in coa.conf (Settings).",
                    x("World.Health", "2.5"), x("World.Damage", "2.0"), x("Dungeon.Health", "2.5"), x("Dungeon.Damage", "1.5"), x("Raid.Health", "2.2"));
                extraText.ForeColor = extraHard.Checked ? Ui.Bad : Ui.Muted;
            });

            var grid = Card(L.T("Multipliers by rank"), null, "settings");
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

            var creatures = Card(L.T("Creatures on the way"), null, "server");
            AddChoice(creatures, "Journey.Creatures.BaseStats", L.T("Base stats of Outland and Northrend"), new[] { "journey", "original" }, new[] { L.T("The old world's (recommended)"), L.T("Their own game's") },
                L.T("Those of the old world rise evenly to 60; the tables of Outland and Northrend have gaps and jumps below 60. Raids and heroics always keep those of their game."));

            var xp = Card(L.T("Quest experience"), L.T("On top of Rate.XP.Quest of the server."), "journey");
            AddSlider(xp, "Journey.Quests.XpRate", L.T("All quests"), 0, 3, 0.05, v => "×" + v.ToString("0.##", CultureInfo.InvariantCulture));

            var alone = Card(L.T("Dungeons alone"), L.T("A player who ticks \"Go alone\" in the Dungeon Finder (or types .journey solo) gets his dungeon at once, just for him; without the tick the Dungeon Finder waits for a group of five as always. Only in a dungeon entered that way do the creatures get the factors below - other instances of the same dungeon stay as they are."), "gamemaster");
            AddToggle(alone, "Journey.SoloDungeon.Enable", L.T("Players may go into dungeons alone"));
            Func<double, string> times = v => "×" + v.ToString("0.##", CultureInfo.InvariantCulture);
            AddSlider(alone, "Journey.SoloDungeon.Health", L.T("Health of the creatures"), 0.1, 1.5, 0.05, times);
            AddSlider(alone, "Journey.SoloDungeon.Damage", L.T("Their melee damage"), 0.1, 1.5, 0.05, times);
            AddSlider(alone, "Journey.SoloDungeon.SpellDamage", L.T("Their spell damage"), 0.1, 1.5, 0.05, times);
            AddSlider(alone, "Journey.SoloDungeon.Boss.Health", L.T("Health of the bosses"), 0.1, 1.5, 0.05, times);
            AddSlider(alone, "Journey.SoloDungeon.Boss.Damage", L.T("Melee damage of the bosses"), 0.1, 1.5, 0.05, times);
            AddSlider(alone, "Journey.SoloDungeon.Boss.SpellDamage", L.T("Spell damage of the bosses"), 0.1, 1.5, 0.05, times);
            alone.Add(Small(L.F("For comparison: mod-autobalance gives a single player about ×0.66 of health and damage. The tick in the Dungeon Finder needs the add-on ZoneLevels, which {0} puts into your game; the Dungeon Finder needs World Journey's change to the core, which {0} brings in when it builds the server.", Product.Name), alone.Inner));

            // ---- what follows
            Tab(L.T("What follows"));
            var follows = Card(L.T("What follows the journey"), L.T("Switch off what should stay as it came."), "modules");
            AddToggle(follows, "Journey.Items.Enable", L.T("Items: required level, item level, stats, armor, damage and price"));
            AddToggle(follows, "Journey.Consumables.Enable", L.T("Potions, food, elixirs, flasks and the spells of trinkets and procs"));
            AddToggle(follows, "Journey.Enchantments.Enable", L.T("Gems, socket bonuses and the enchantments of Outland's and Northrend's professions"));
            AddToggle(follows, "Journey.Trainers.Enable", L.T("Riding and the professions (riding at 13 and 26, flying at 33 and 47)"));
            AddToggle(follows, "Journey.Battlegrounds.Enable", L.T("Eye of the Storm, Strand of the Ancients, Isle of Conquest and Wintergrasp open at 60"));
            var client = Card(L.T("In the game client"), L.F("Needs the add-on ZoneLevels, which {0} puts into your game (Modules › Game client add-ons).", Product.Name), "client");
            AddToggle(client, "Journey.Map.Enable", L.T("The world map shows the zone levels of the journey"));
            AddToggle(client, "Journey.Tooltips.Enable", L.T("Tooltips show the real values of items' spells, gems and enchantments"));
            var debug = Card(L.T("For testing"), null, "maintenance");
            AddToggle(debug, "Journey.Debug", L.T("More detail in the server log"));

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
            if (p.Kind == 2) return L.T("Raid");
            if (p.Kind == 1) return L.T("Heroic");
            return L.T("Dungeon");
        }
        int LevelOf(Place p) { return p.Instance ? rules.Instance(p.Id, p.Kind, p.Original, p.Content) : rules.Zone(p.Id, p.Part, p.Original); }
        string Key(Place p) { return (p.Instance ? "Journey.Instance." : "Journey.Zone.") + p.Id; }

        void FillPlaces()
        {
            filling = true;
            bool instances = placeKind.Selected == 1;
            if (placeKind.Selected < 0) placeKind.Selected = 0;
            placeList.BeginUpdate(); placeList.Items.Clear();
            foreach (var p in places.Where(p => p.Instance == instances).OrderBy(p => LevelOf(p)).ThenBy(p => L.Outside(p.Name)))
            {
                var it = new ListViewItem(L.Outside(p.Name)) { Tag = p };
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
                string text = LevelOf(p) + (Has(Key(p)) ? "  " + L.T("(by hand)") : "");
                if (it.SubItems[3].Text != text) it.SubItems[3].Text = text;
                it.ForeColor = Has(Key(p)) ? Ui.Accent : Ui.Text;
            }
        }

        void ShowPlace()
        {
            filling = true;
            var p = placeList.SelectedItems.Count == 1 ? (Place)placeList.SelectedItems[0].Tag : null;
            byHand.Enabled = handLevel.Enabled = p != null;
            placeName.Text = p == null ? L.T("Choose a zone or dungeon in the list to move it.") : L.F("{0} – opens at {1} on the journey (originally {2})", L.Outside(p.Name), LevelOf(p), p.Original);
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
            placeName.Text = L.F("{0} – opens at {1} on the journey (originally {2})", L.Outside(p.Name), LevelOf(p), p.Original);
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
            if (coa != null && coa.ByKey.ContainsKey(CoaSwitch)) SetIn(coa, CoaSwitch, "0");     // the journey's own difficulty only
        }
    }

    // ==================================================================================== Azeroth flying

    class AzerothFlyingSetup : SetupDialog
    {
        public AzerothFlyingSetup(Install i) : base(i, "mod_azeroth_flying.conf", L.T("Azeroth flying"),
            L.T("mod-azeroth-flying  ·  modules\\mod_azeroth_flying.conf"), "journey", "AzerothFlying.Enable", L.T("Flying in old Azeroth on"))
        {
            Tab(L.T("Old Azeroth"));
            var fly = Card(L.T("Flying mounts"),
                L.T("Mounts with a flying effect may fly in Eastern Kingdoms and Kalimdor. Outland, Northrend, battlegrounds and instances keep their normal rules."), "journey");
            AddText(fly, "AzerothFlying.NoFlyAreas", L.T("No-fly areas"),
                L.T("Area or zone ids separated by commas. Entering one of them while flying dismounts the player. Empty: no no-fly list."));
            Note(L.T("Azeroth was not built for flying: expect clipping, stuck spots, skipped quest paths, unintended shortcuts and PvP advantages. Use it at your own risk."));
            Start();
        }
    }

    // ==================================================================================== Auction house bots

    class AuctionSetup : SetupDialog
    {
        const string P = "PlayerbotsAuctions.";
        static readonly string[] Qualities = { L.C("quality", "Poor"), L.C("quality", "Common"), L.C("quality", "Uncommon"), L.C("quality", "Rare"), L.C("quality", "Epic"), L.C("quality", "Legendary") };
        static string Minutes(double v) { return v >= 120 && v % 60 == 0 ? L.F("{0} h", v / 60) : L.F("{0} min", (int)v); }
        static string Times(double v) { return "×" + v.ToString("0.##", CultureInfo.InvariantCulture); }
        static string Percent(double v) { return (int)v + " %"; }
        static string Plain(double v) { return ((int)v).ToString(CultureInfo.InvariantCulture); }

        public AuctionSetup(Install i) : base(i, "mod_playerbots_auctions.conf", L.T("Auction house bots"), L.T("mod-playerbots-auctions  ·  modules\\mod_playerbots_auctions.conf"), "auction", P + "Enable", L.T("Auction house bots on"))
        {
            // ---- the bots
            Tab(L.T("The bots"));
            var who = Card(L.T("Who trades"), L.T("Every bot has a character of its own that never changes: whether it collects or tidies up, haggles or pays, crafts for profit or for skill. These settings set the frame."), "accounts");
            AddSlider(who, P + "MinBotLevel", L.T("Lowest level of trading bots"), 1, 80, 1);
            AddSlider(who, P + "Character.AvoidersPercent", L.T("Bots that never use the auction house"), 0, 100, 1, Percent, L.T("They sell everything to a vendor, as many players do."));
            var trips = Card(L.T("Trips to the auction house"), L.T("Like a player, a bot decides for itself when it is time to go: when its bags fill up or it has collected enough to sell. Out in the world it takes its hearthstone to a capital, walks to the auctioneers and later returns."), "globe");
            AddToggle(trips, P + "CityTrip.Enable", L.T("Bots set out for the auction house on their own"));
            AddPair(trips, P + "CityTrip.ItemsMin", P + "CityTrip.ItemsMax", L.T("Things to sell before setting out"), 1, 30, 1, Plain, L.T("Tidy bots go with few, collectors with many."));
            AddPair(trips, P + "CityTrip.CooldownMinutesMin", P + "CityTrip.CooldownMinutesMax", L.T("Time between two trips"), 10, 600, 10, Minutes);
            AddPair(trips, P + "VisitCooldownMinutesMin", P + "VisitCooldownMinutesMax", L.T("Time between visits in town"), 5, 240, 5, Minutes);
            AddSlider(trips, P + "AuctioneerRange", L.T("Distance to the auctioneer"), 5, 100, 1, v => L.F("{0} yd", (int)v));
            var pace = Card(L.T("Pace"), L.T("How often the module looks at the bots and how many of them do their business each time; keeps the load even with thousands of bots."), "server");
            AddSlider(pace, P + "IntervalSeconds", L.T("Look at the bots every"), 5, 300, 5, v => L.F("{0} s", (int)v));
            AddSlider(pace, P + "BotsPerCycle", L.T("Bots per look"), 1, 100, 1, Plain);
            AddToggle(pace, P + "DailyRhythm", L.T("Busier in the evening and at the weekend than at night"));
            var memory = Card(L.T("Memory and mail"), null, "database");
            AddToggle(memory, P + "SaveMemory", L.T("Bots remember prices over a restart"), L.T("What things sold for and what came back unsold is kept in two small tables of the characters database."));
            AddToggle(memory, P + "PauseAuctionsWhileOffline", L.T("Auctions pause while the server is off"), L.T("On a server that only runs for an evening, the auctions would otherwise be gone by the next one."));
            AddToggle(memory, P + "CollectAuctionMail", L.T("Bots empty their auction mail"));
            AddToggle(memory, P + "Debug", L.T("Write what the bots do to the server log (for testing)"));

            // ---- selling
            Tab(L.T("Selling"));
            var what = Card(L.T("What is sold"), L.T("A bot only sells what it does not need itself and what is not soulbound. Items that bind when picked up and quest items are never sold."), "auction");
            AddChoice(what, P + "Sell.MinQuality.Equipment", L.T("Weapons and armor from"), new[] { "0", "1", "2", "3", "4" }, new[] { L.C("quality", "Poor"), L.C("quality", "Common"), L.C("quality", "Uncommon"), L.C("quality", "Rare"), L.C("quality", "Epic") });
            AddChoice(what, P + "Sell.MinQuality.Other", L.T("Everything else from"), new[] { "0", "1", "2", "3", "4" }, new[] { L.C("quality", "Poor"), L.C("quality", "Common"), L.C("quality", "Uncommon"), L.C("quality", "Rare"), L.C("quality", "Epic") });
            AddChoice(what, P + "Sell.MaxQuality", L.T("Up to"), new[] { "2", "3", "4", "5" }, new[] { L.C("quality", "Uncommon"), L.C("quality", "Rare"), L.C("quality", "Epic"), L.C("quality", "Legendary") });
            var classes = Card(L.T("Kinds of items"), L.T("What may go into the auction house. Projectiles are left out: bots are handed their ammunition."), "modules");
            AddFlags(classes, P + "Sell.ItemClasses", new[] { 0, 1, 2, 3, 4, 5, 7, 9, 11, 12, 13, 15, 16 },
                new[] { L.T("Consumables"), L.T("Bags"), L.T("Weapons"), L.T("Gems"), L.T("Armor"), L.T("Reagents"), L.T("Trade goods"), L.T("Recipes"), L.T("Quivers"), L.T("Quest items (tradable)"), L.T("Keys"), L.T("Pets, mounts, misc."), L.T("Glyphs") });
            var how = Card(L.T("How"), null, "settings");
            AddToggle(how, P + "Sell.ChargeDeposit", L.T("Bots pay the deposit like players"));
            AddToggle(how, P + "Sell.KeepFromVendor", L.T("Bots keep what is worth an auction from the vendor"));
            AddToggle(how, P + "Sell.JunkToVendor", L.T("Bots sell their junk to a vendor nearby"));
            AddSlider(how, P + "Sell.MinAuctionValueSilver", L.T("Smallest auction worth creating"), 0, 100, 1, v => L.F("{0} silver", (int)v));
            AddSlider(how, P + "Sell.MaxAuctionsPerHouse", L.T("Auctions per auction house at most"), 1000, 50000, 1000, Plain);
            var never = Card(L.T("Never sold"), null, "maintenance");
            AddText(never, P + "Sell.ExcludedItemIDs", L.T("Item ids"), L.T("Separated by commas."));
            AddText(never, P + "Sell.ExcludedNameParts", L.T("Names containing"), L.T("Parts of names separated by commas; keeps test and technical items out."));

            // ---- loot filter
            Tab(L.T("Loot filter"));
            var filter = Card(L.T("What bots pick up"),
                L.T("Random bots leave behind what they do not need, so their bags stay free for the auction house. Quest items, items of a quest the bot is doing and the always-loot list are never filtered. Bots of real players are left alone."), "maintenance");
            AddToggle(filter, P + "LootFilter.Enable", L.T("Filter what random bots pick up"));
            AddChoice(filter, P + "LootFilter.MinQuality", L.T("Bots pick up from"), new[] { "0", "1", "2", "3", "4", "5" }, Qualities,
                L.T("Anything below this quality stays on the corpse."));
            AddToggle(filter, P + "LootFilter.SkipFood", L.T("Leave food and drink on the corpse"),
                L.T("The bot gets its own food and drink from Playerbots when it needs them."));
            AddToggle(filter, P + "LootFilter.SkipOtherConsumables", L.T("Leave potions, elixirs, flasks, scrolls and bandages"),
                L.T("Food and drink are covered by the switch above."));
            var neverLoot = Card(L.T("Never picked up"), null, "maintenance");
            AddText(neverLoot, P + "LootFilter.SkipItemIDs", L.T("Item ids"), L.T("Separated by commas."));
            AddText(neverLoot, P + "LootFilter.SkipNameParts", L.T("Names containing"),
                L.T("Parts of names separated by commas; upper and lower case do not matter."));

            // ---- prices
            Tab(L.T("Prices"));
            var factors = Card(L.T("Starting prices"), L.T("The vendor value of an item times the factor of its quality. What auctions really sell for moves this usual price over time."), "auction");
            AddSlider(factors, P + "Price.Poor", L.T("Poor (grey)"), 0.5, 10, 0.5, Times);
            AddSlider(factors, P + "Price.Normal", L.T("Common (white)"), 0.5, 20, 0.5, Times);
            AddSlider(factors, P + "Price.Uncommon", L.T("Uncommon (green)"), 1, 40, 0.5, Times);
            AddSlider(factors, P + "Price.Rare", L.T("Rare (blue)"), 1, 60, 1, Times);
            AddSlider(factors, P + "Price.Epic", L.T("Epic (purple)"), 1, 100, 1, Times);
            AddSlider(factors, P + "Price.Legendary", L.T("Legendary (orange)"), 1, 200, 1, Times);
            var limits = Card(L.T("Limits"), null, "settings");
            AddToggle(limits, P + "Price.LearnFromSales", L.T("Prices learn from what really sold"));
            AddSlider(limits, P + "Price.MinVendorFactor", L.T("Never below the vendor value"), 1, 5, 0.1, Times);
            AddSlider(limits, P + "Price.MaxBuyoutGold", L.T("Highest buyout"), 0, 20000, 100, v => v == 0 ? L.T("no limit") : L.F("{0} g", (int)v));
            var band = Card(L.T("Price band"), L.T("Bots ask within the low, usual and high prices an item really sold for. Until there are enough sales, a band around the usual vendor-based price is used. No external price data is needed."), "auction");
            AddToggle(band, P + "Price.Band.Enable", L.T("Only ask within what really sold"));
            AddSlider(band, P + "Price.Band.MinSales", L.T("Sales before the band is its own"), 1, 20, 1, Plain);
            AddSlider(band, P + "Price.Band.FallbackLowPercent", L.T("Fallback low"), 10, 100, 5, Percent);
            AddSlider(band, P + "Price.Band.FallbackHighPercent", L.T("Fallback high"), 100, 300, 5, Percent);
            AddSlider(band, P + "Price.Band.MaxRegularPercent", L.T("Highest price"), 100, 1000, 25, Percent, L.T("Of the usual vendor-based price. A wild sale cannot lift the band above this."));

            // ---- buying
            Tab(L.T("Buying"));
            var buy = Card(L.T("Buying and bidding"), L.T("A bot looks through some of the items on offer for things it has a use for: upgrades, things it uses up, bags, materials for its professions. Of several offers it takes the cheapest."), "auction");
            AddToggle(buy, P + "Buy.Enable", L.T("Bots buy and bid"));
            AddToggle(buy, P + "Buy.FromPlayers", L.T("From players"));
            AddToggle(buy, P + "Buy.FromBots", L.T("From other bots"));
            AddToggle(buy, P + "Buy.PlaceBids", L.T("Bots bid too (a bid can outbid a player)"));
            AddToggle(buy, P + "Buy.UseBotMoney", L.T("Bots pay with their own money"));
            AddSlider(buy, P + "Buy.ItemsLookedAt", L.T("Items looked at per visit"), 10, 500, 10, Plain);
            AddSlider(buy, P + "Buy.MinVendorFactor", L.T("Always willing to pay"), 1, 5, 0.1, Times, L.T("Times the vendor value, so selling to the bots is never worse than selling to a vendor."));
            AddSlider(buy, P + "Buy.VendorItemMaxPercent", L.T("For vendor goods at most"), 10, 100, 5, Percent, L.T("Of the vendor's price, so nobody can buy from a vendor and sell to the bots for a profit."));

            // ---- crafting
            Tab(L.T("Crafting"));
            var craft = Card(L.T("Crafting"), L.T("At the auctioneers a bot thinks through its recipes and compares what a product would bring with what its materials cost. What it makes and does not need, it sells."), "maintenance");
            AddToggle(craft, P + "Crafting.Enable", L.T("Bots craft"));
            AddToggle(craft, P + "Crafting.WalkToAnvilForgeFire", L.T("They walk to an anvil, forge or fire when a recipe needs one"));
            AddToggle(craft, P + "Crafting.BuyMaterials", L.T("They buy missing materials in the auction house"));
            AddToggle(craft, P + "Crafting.BuyVendorMaterials", L.T("They buy thread, vials and the like from a vendor"));
            AddToggle(craft, P + "Crafting.TakeApart", L.T("Prospecting, milling and disenchanting"));
            AddToggle(craft, P + "Crafting.Scrolls", L.T("Enchanters sell their enchantments as scrolls"));
            AddToggle(craft, P + "Crafting.Glyphs", L.T("Scribes make glyphs"), L.T("The classes of Conquest of Azeroth cannot use the glyphs of Wrath of the Lich King."));
            var worth = Card(L.T("When it is worth it"), null, "settings");
            AddSlider(worth, P + "Crafting.MinProfitPercent", L.T("Least profit"), 0, 200, 5, Percent);
            AddSlider(worth, P + "Crafting.SkillUpBonusPercent", L.T("A recipe that raises skill is worth more"), 0, 300, 10, v => "+" + (int)v + " %");
            AddSlider(worth, P + "Crafting.MaxMaterialPriceFactor", L.T("Most paid for a material"), 1, 5, 0.1, Times, L.T("Times its usual price."));
            AddSlider(worth, P + "Crafting.RecipesPerVisit", L.T("Recipes thought through per visit"), 5, 200, 5, Plain);
            AddSlider(worth, P + "Crafting.MaxInARow", L.T("Most of one thing in one go"), 1, 20, 1, Plain);

            // ---- chat
            Tab(L.T("Chat"));
            var deals = Card(L.T("Deals by chat"), L.T("A player writes WTB or WTS into a channel - the item typed out or linked, a number before it, a price after it - and bots answer by whisper. The goods come by mail, cash on delivery."), "accounts");
            AddToggle(deals, P + "Chat.Enable", L.T("Bots answer WTB and WTS"));
            AddSlider(deals, P + "Chat.Answers", L.T("Bots answering one line at most"), 1, 10, 1, Plain);
            AddText(deals, P + "Chat.Channels", L.T("Channels read"), L.T("Parts of channel names separated by commas, for example general, trade. Empty: every channel."));
            AddToggle(deals, P + "Chat.Meet", L.T("A bot nearby offers to meet"));
            AddSlider(deals, P + "Chat.MeetDistance", L.T("Nearby means within"), 10, 2000, 10, v => L.F("{0} yd", (int)v));
            AddSlider(deals, P + "Chat.MailDelay", L.T("The parcel takes"), -1, 3600, 1, v => v < 0 ? L.T("like mail") : v < 120 ? L.F("{0} s", (int)v) : L.F("{0} min", (int)(v / 60)), L.T("\"Like mail\" follows Mail.DeliveryDelay of worldserver.conf."));
            var chatter = Card(L.T("Chatter"), L.T("Every bot is somebody in chat too: 65 personalities with more than 16000 lines between them."), "accounts");
            AddToggle(chatter, P + "Chatter.Enable", L.T("The bots talk"));
            AddSlider(chatter, P + "Chatter.Interval", L.T("Something said out of the blue about every"), 20, 3600, 10, v => v < 120 ? L.F("{0} s", (int)v) : L.F("{0} min", (v / 60).ToString("0.#", CultureInfo.InvariantCulture)), L.T("Lower is livelier."));
            AddToggle(chatter, P + "Chatter.Replies", L.T("They answer a hello, a ding, a thanks, a joke request"));
            AddToggle(chatter, P + "Chatter.Nearby", L.T("A bot within earshot says something now and then"));
            AddToggle(chatter, P + "Chatter.WorldChannel", L.T("Use the realm's channel when no bot is in the player's zone"));
            AddSlider(chatter, P + "Chatter.Mixed", L.T("Bots that mix two personalities"), 0, 100, 5, Percent);
            AddSlider(chatter, P + "Chatter.EasterEggs", L.T("A wink at AFK Realm once in"), 0, 2000, 10, v => v == 0 ? L.T("never") : L.F("{0} lines", (int)v));

            // ---- trading post
            Tab(L.T("Trading Post"));
            var post = Card(L.T("The Trading Post"), L.T("A last resort at every auctioneer for what bots hardly come by: materials and what crafters make for everybody, well above the usual price. Only players can use it; the money is gone from the world."), "auction");
            AddToggle(post, P + "TradingPost.Enable", L.T("Auctioneers offer the Trading Post"));
            AddToggle(post, P + "TradingPost.Flyer", L.T("Players get its flyer once a day"));
            AddToggle(post, P + "TradingPost.FreeSample", L.T("A free sample once a week (always junk)"));
            AddChoice(post, P + "TradingPost.MaxQuality", L.T("Best quality sold"), new[] { "1", "2", "3", "4" }, new[] { L.C("quality", "Common"), L.C("quality", "Uncommon"), L.C("quality", "Rare"), L.C("quality", "Epic") });
            AddSlider(post, P + "TradingPost.ScarceBelow", L.T("Sells a material while the house has fewer than"), 1, 200, 1, v => L.F("{0} pcs", (int)v));
            var postPrice = Card(L.T("Its prices and stock"), L.T("Prices as multiples of what a thing usually goes for: the buyout is delivered at once, a bid when the auction ends."), "settings");
            AddSlider(postPrice, P + "TradingPost.Price.Materials", L.T("Buyout of materials"), 1, 10, 0.1, Times);
            AddSlider(postPrice, P + "TradingPost.Price.Crafted", L.T("Buyout of crafted goods"), 1, 10, 0.1, Times);
            AddSlider(postPrice, P + "TradingPost.Bid.Materials", L.T("Lowest bid for materials"), 1, 10, 0.1, Times);
            AddSlider(postPrice, P + "TradingPost.Bid.Crafted", L.T("Lowest bid for crafted goods"), 1, 10, 0.1, Times);
            AddSlider(postPrice, P + "TradingPost.Stock.Materials", L.T("Pieces of a material per day"), 1, 500, 1, Plain);
            AddSlider(postPrice, P + "TradingPost.Stock.Crafted", L.T("Pieces of a crafted thing per day"), 1, 200, 1, Plain);
            AddSlider(postPrice, P + "TradingPost.Daily.Materials", L.T("Most a character buys a day (materials)"), 1, 200, 1, Plain);
            AddSlider(postPrice, P + "TradingPost.Daily.Crafted", L.T("Most a character buys a day (crafted)"), 1, 100, 1, Plain);
            var postNever = Card(L.T("Never at the Trading Post"), null, "maintenance");
            AddText(postNever, P + "TradingPost.ExcludedItemIDs", L.T("Item ids"), L.T("What is meant to be scarce: materials made on a cooldown and the like."));
            Start();
        }
    }
}
