using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace CoAInstaller
{
    /// <summary>Drawing helpers shared by the controls of AFK Realm's own look.</summary>
    static class Draw
    {
        public static GraphicsPath Round(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 1) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Smooth(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        }

        public static Color Mix(Color a, Color b, double t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        /// <summary>The colour behind a control (what its rounded corners have to show).</summary>
        public static Color Behind(Control c)
        {
            for (var p = c.Parent; p != null; p = p.Parent)
                if (p.BackColor.A == 255) return p.BackColor;
            return Ui.Surface;
        }
    }

    /// <summary>Small line icons, drawn rather than taken from a font, so they look the same on every PC.</summary>
    static class Icons
    {
        public static readonly string[] Names = { "server", "settings", "modules", "gamemaster", "database", "accounts", "maintenance", "journey", "auction", "client", "globe" };

        public static void Paint(Graphics g, string name, RectangleF r, Color color, float width = 1.6f)
        {
            Draw.Smooth(g);
            using (var pen = new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var brush = new SolidBrush(color))
            {
                float x = r.X, y = r.Y, w = r.Width, h = r.Height;
                switch (name)
                {
                    case "server":
                        for (int i = 0; i < 3; i++)
                        {
                            var row = new RectangleF(x + w * 0.1f, y + h * (0.1f + i * 0.28f), w * 0.8f, h * 0.22f);
                            using (var p = Draw.Round(row, 2)) g.DrawPath(pen, p);
                            g.FillEllipse(brush, row.Right - w * 0.2f, row.Y + row.Height / 2 - 1.5f, 3f, 3f);
                        }
                        break;
                    case "settings":
                        for (int i = 0; i < 3; i++)
                        {
                            float ly = y + h * (0.2f + i * 0.3f);
                            g.DrawLine(pen, x + w * 0.1f, ly, x + w * 0.9f, ly);
                            float kx = x + w * (i == 1 ? 0.65f : i == 0 ? 0.3f : 0.45f);
                            using (var bg = new SolidBrush(color)) g.FillEllipse(bg, kx - 3.2f, ly - 3.2f, 6.4f, 6.4f);
                        }
                        break;
                    case "modules":
                        float s = w * 0.36f, gap = w * 0.12f;
                        for (int i = 0; i < 4; i++)
                        {
                            var cell = new RectangleF(x + w * 0.08f + (i % 2) * (s + gap), y + h * 0.08f + (i / 2) * (s + gap), s, s);
                            using (var p = Draw.Round(cell, 2.5f)) { if (i == 1) g.FillPath(brush, p); else g.DrawPath(pen, p); }
                        }
                        break;
                    case "gamemaster":
                        using (var p = new GraphicsPath())
                        {
                            p.AddLines(new[] { new PointF(x + w * 0.5f, y + h * 0.06f), new PointF(x + w * 0.88f, y + h * 0.2f), new PointF(x + w * 0.82f, y + h * 0.6f),
                                new PointF(x + w * 0.5f, y + h * 0.94f), new PointF(x + w * 0.18f, y + h * 0.6f), new PointF(x + w * 0.12f, y + h * 0.2f) });
                            p.CloseFigure();
                            g.DrawPath(pen, p);
                        }
                        g.DrawLines(pen, new[] { new PointF(x + w * 0.34f, y + h * 0.5f), new PointF(x + w * 0.46f, y + h * 0.62f), new PointF(x + w * 0.67f, y + h * 0.38f) });
                        break;
                    case "database":
                        var top = new RectangleF(x + w * 0.15f, y + h * 0.08f, w * 0.7f, h * 0.24f);
                        g.DrawEllipse(pen, top);
                        g.DrawLine(pen, top.X, top.Y + top.Height / 2, top.X, y + h * 0.8f);
                        g.DrawLine(pen, top.Right, top.Y + top.Height / 2, top.Right, y + h * 0.8f);
                        g.DrawArc(pen, top.X, y + h * 0.68f, top.Width, top.Height, 0, 180);
                        g.DrawArc(pen, top.X, y + h * 0.4f, top.Width, top.Height, 0, 180);
                        break;
                    case "accounts":
                        g.DrawEllipse(pen, x + w * 0.32f, y + h * 0.08f, w * 0.36f, h * 0.36f);
                        g.DrawArc(pen, x + w * 0.12f, y + h * 0.52f, w * 0.76f, h * 0.7f, 180, 180);
                        break;
                    case "maintenance":
                        var c = new PointF(x + w / 2, y + h / 2);
                        for (int i = 0; i < 8; i++)
                        {
                            double a = i * Math.PI / 4;
                            g.DrawLine(pen, c.X + (float)Math.Cos(a) * w * 0.3f, c.Y + (float)Math.Sin(a) * h * 0.3f, c.X + (float)Math.Cos(a) * w * 0.44f, c.Y + (float)Math.Sin(a) * h * 0.44f);
                        }
                        g.DrawEllipse(pen, c.X - w * 0.28f, c.Y - h * 0.28f, w * 0.56f, h * 0.56f);
                        g.DrawEllipse(pen, c.X - w * 0.1f, c.Y - h * 0.1f, w * 0.2f, h * 0.2f);
                        break;
                    case "journey":
                        g.DrawLines(pen, new[] { new PointF(x + w * 0.06f, y + h * 0.82f), new PointF(x + w * 0.36f, y + h * 0.36f), new PointF(x + w * 0.54f, y + h * 0.6f),
                            new PointF(x + w * 0.72f, y + h * 0.22f), new PointF(x + w * 0.94f, y + h * 0.82f) });
                        g.DrawLine(pen, x + w * 0.72f, y + h * 0.22f, x + w * 0.72f, y + h * 0.02f);
                        g.FillPolygon(brush, new[] { new PointF(x + w * 0.72f, y + h * 0.02f), new PointF(x + w * 0.92f, y + h * 0.08f), new PointF(x + w * 0.72f, y + h * 0.14f) });
                        break;
                    case "auction":
                        g.DrawLine(pen, x + w * 0.5f, y + h * 0.1f, x + w * 0.5f, y + h * 0.88f);
                        g.DrawLine(pen, x + w * 0.14f, y + h * 0.24f, x + w * 0.86f, y + h * 0.24f);
                        g.DrawLine(pen, x + w * 0.3f, y + h * 0.88f, x + w * 0.7f, y + h * 0.88f);
                        g.DrawArc(pen, x + w * 0.02f, y + h * 0.3f, w * 0.3f, h * 0.3f, 0, 180);
                        g.DrawArc(pen, x + w * 0.68f, y + h * 0.3f, w * 0.3f, h * 0.3f, 0, 180);
                        g.DrawLine(pen, x + w * 0.17f, y + h * 0.24f, x + w * 0.04f, y + h * 0.45f);
                        g.DrawLine(pen, x + w * 0.17f, y + h * 0.24f, x + w * 0.3f, y + h * 0.45f);
                        g.DrawLine(pen, x + w * 0.83f, y + h * 0.24f, x + w * 0.7f, y + h * 0.45f);
                        g.DrawLine(pen, x + w * 0.83f, y + h * 0.24f, x + w * 0.96f, y + h * 0.45f);
                        break;
                    case "client":
                        using (var p = Draw.Round(new RectangleF(x + w * 0.06f, y + h * 0.12f, w * 0.88f, h * 0.58f), 2.5f)) g.DrawPath(pen, p);
                        g.DrawLine(pen, x + w * 0.5f, y + h * 0.7f, x + w * 0.5f, y + h * 0.86f);
                        g.DrawLine(pen, x + w * 0.28f, y + h * 0.88f, x + w * 0.72f, y + h * 0.88f);
                        break;
                    case "globe":
                        g.DrawEllipse(pen, x + w * 0.08f, y + h * 0.08f, w * 0.84f, h * 0.84f);
                        g.DrawEllipse(pen, x + w * 0.3f, y + h * 0.08f, w * 0.4f, h * 0.84f);
                        g.DrawLine(pen, x + w * 0.08f, y + h * 0.5f, x + w * 0.92f, y + h * 0.5f);
                        break;
                    default:
                        g.DrawEllipse(pen, r);
                        break;
                }
            }
        }
    }

    enum ButtonKind { Primary, Secondary, Ghost, Danger }

    /// <summary>A flat button with rounded corners in the colours of AFK Realm.</summary>
    class FlatButton : Button
    {
        public ButtonKind Kind;
        public string Icon;
        bool hover, down;

        public FlatButton(string text, ButtonKind kind)
        {
            Kind = kind;
            Text = text;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Font = kind == ButtonKind.Primary || kind == ButtonKind.Danger ? Ui.Bold : Ui.Base;
            Cursor = Cursors.Hand;
            AutoSize = true;
            Padding = new Padding(16, 7, 16, 7);
            Margin = new Padding(0, 4, 8, 4);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            MouseEnter += (s, e) => { hover = true; Invalidate(); };
            MouseLeave += (s, e) => { hover = false; down = false; Invalidate(); };
            MouseDown += (s, e) => { down = true; Invalidate(); };
            MouseUp += (s, e) => { down = false; Invalidate(); };
            EnabledChanged += (s, e) => Invalidate();
        }

        public override Size GetPreferredSize(Size proposed)
        {
            var t = TextRenderer.MeasureText(Text ?? "", Font);
            int icon = Icon != null ? 22 : 0;
            return new Size(t.Width + Padding.Horizontal + icon, Math.Max(34, t.Height + Padding.Vertical));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Draw.Behind(this));
            Draw.Smooth(g);
            Color fill, border, ink;
            switch (Kind)
            {
                case ButtonKind.Primary:
                    fill = !Enabled ? Color.FromArgb(200, 194, 222) : down ? Ui.AccentDark : hover ? Draw.Mix(Ui.Accent, Ui.AccentDark, 0.5) : Ui.Accent;
                    border = fill; ink = Color.White; break;
                case ButtonKind.Danger:
                    fill = !Enabled ? Color.FromArgb(230, 200, 200) : down || hover ? Color.FromArgb(170, 40, 40) : Ui.Bad;
                    border = fill; ink = Color.White; break;
                case ButtonKind.Ghost:
                    fill = down ? Ui.AccentSoft : hover ? Draw.Mix(Ui.AccentSoft, Color.White, 0.5) : Draw.Behind(this);
                    border = fill; ink = Enabled ? Ui.Accent : Ui.Muted; break;
                default:
                    fill = !Enabled ? Ui.Canvas : down ? Ui.AccentSoft : hover ? Draw.Mix(Ui.AccentSoft, Color.White, 0.55) : Color.White;
                    border = hover && Enabled ? Draw.Mix(Ui.Accent, Ui.Line, 0.5) : Ui.Line; ink = Enabled ? Ui.Text : Ui.Muted; break;
            }
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var p = Draw.Round(r, 7))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, p);
                using (var pen = new Pen(border)) g.DrawPath(pen, p);
            }
            var textRect = ClientRectangle;
            if (Icon != null)
            {
                var t = TextRenderer.MeasureText(Text ?? "", Font);
                int start = (Width - t.Width - 22) / 2;
                Icons.Paint(g, Icon, new RectangleF(start, (Height - 16) / 2f, 16, 16), ink, 1.5f);
                textRect = new Rectangle(start + 22, 0, t.Width + 2, Height);
            }
            TextRenderer.DrawText(g, Text, Font, textRect, ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            if (Focused && ShowFocusCues)
                using (var p = Draw.Round(new RectangleF(2.5f, 2.5f, Width - 5.5f, Height - 5.5f), 5))
                using (var pen = new Pen(Color.FromArgb(120, ink)) { DashStyle = DashStyle.Dot }) g.DrawPath(pen, p);
        }
    }

    /// <summary>An on/off switch with its text beside it; works like a CheckBox.</summary>
    class Toggle : CheckBox
    {
        public Toggle(string text)
        {
            Text = text;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = Ui.Base;
            AutoSize = false;
            Cursor = Cursors.Hand;
            Margin = new Padding(0, 4, 12, 4);
            Height = 28;
            CheckedChanged += (s, e) => Invalidate();
            EnabledChanged += (s, e) => Invalidate();
            TextChanged += (s, e) => FitWidth();
            FitWidth();
        }

        /// <summary>The longest line before the text wraps (0: never wraps).</summary>
        public int WrapWidth;

        void FitWidth()
        {
            var t = TextRenderer.MeasureText(Text ?? "", Font);
            int w = t.Width + 56;
            if (WrapWidth > 0 && w > WrapWidth)
            {
                var wrapped = TextRenderer.MeasureText(Text ?? "", Font, new Size(WrapWidth - 56, 0), TextFormatFlags.WordBreak);
                Size = new Size(WrapWidth, Math.Max(28, wrapped.Height + 8));
            }
            else Size = new Size(w, 28);
        }

        public void Wrap(int width) { WrapWidth = width; FitWidth(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Draw.Behind(this) == Color.Empty ? Ui.Surface : Draw.Behind(this));
            Draw.Smooth(g);
            var track = new RectangleF(1, 5, 40, 22);
            Color on = Enabled ? Ui.Accent : Color.FromArgb(200, 194, 222), off = Enabled ? Color.FromArgb(200, 198, 212) : Ui.Line;
            using (var p = Draw.Round(track, 11)) using (var b = new SolidBrush(Checked ? on : off)) g.FillPath(b, p);
            float knob = Checked ? track.Right - 19 : track.X + 3;
            g.FillEllipse(Brushes.White, knob, track.Y + 3, 16, 16);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(50, 0, Width - 50, Height), Enabled ? Ui.Text : Ui.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.Left);
            if (Focused && ShowFocusCues) using (var p = Draw.Round(new RectangleF(0, 4, 42, 24), 12)) using (var pen = new Pen(Color.FromArgb(140, Ui.Accent))) g.DrawPath(pen, p);
        }
    }

    /// <summary>A slider for a number between two limits, with its value shown beside it. One or two thumbs.</summary>
    class Slider : Control
    {
        double min, max = 100, step = 1, low, high;
        public bool Range;                  // two thumbs: Low and High
        public int Decimals;
        public Func<double, string> Format;
        public event EventHandler ValueChanged;
        int dragging = -1;                  // 0 low, 1 high
        public int ValueWidth = 78;

        public Slider(double minimum, double maximum, double stepSize, bool range = false)
        {
            min = minimum; max = maximum; step = stepSize; Range = range;
            low = min; high = max;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Size = new Size(380, 36);
            Font = Ui.Base;
            Cursor = Cursors.Hand;
            TabStop = true;
            Margin = new Padding(0, 2, 0, 2);
            Decimals = step < 1 ? (step < 0.1 ? 2 : 1) : 0;
        }

        public double Minimum { get { return min; } }
        public double Maximum { get { return max; } }
        public double Value { get { return low; } set { SetLow(value, false); } }
        public double Low { get { return low; } set { SetLow(value, false); } }
        public double High { get { return high; } set { SetHigh(value, false); } }

        double Snap(double v) { v = Math.Max(min, Math.Min(max, v)); return Math.Round(Math.Round((v - min) / step) * step + min, 6); }
        void SetLow(double v, bool user)
        {
            v = Snap(v); if (Range && v > high) v = high;
            if (v == low) return; low = v; Invalidate(); if (user && ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }
        void SetHigh(double v, bool user)
        {
            v = Snap(v); if (v < low) v = low;
            if (v == high) return; high = v; Invalidate(); if (user && ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }

        string Text_(double v) { return Format != null ? Format(v) : v.ToString("F" + Decimals, CultureInfo.InvariantCulture); }
        RectangleF Track { get { return new RectangleF(10, Height / 2f - 2.5f, Width - ValueWidth - 26, 5); } }
        float X(double v) { var t = Track; return t.X + (float)((v - min) / (max - min)) * t.Width; }
        double V(float x) { var t = Track; return min + (x - t.X) / t.Width * (max - min); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Draw.Behind(this));
            Draw.Smooth(g);
            var t = Track;
            Color accent = Enabled ? Ui.Accent : Color.FromArgb(200, 194, 222);
            using (var p = Draw.Round(t, 2.5f)) using (var b = new SolidBrush(Ui.Line)) g.FillPath(b, p);
            float a = Range ? X(low) : t.X, z = Range ? X(high) : X(low);
            using (var p = Draw.Round(new RectangleF(a, t.Y, Math.Max(5, z - a), t.Height), 2.5f)) using (var b = new SolidBrush(accent)) g.FillPath(b, p);
            foreach (var x in Range ? new[] { X(low), X(high) } : new[] { X(low) })
            {
                g.FillEllipse(Brushes.White, x - 9, Height / 2f - 9, 18, 18);
                using (var pen = new Pen(accent, 2.5f)) g.DrawEllipse(pen, x - 8, Height / 2f - 8, 16, 16);
            }
            string label = Range ? Text_(low) + " – " + Text_(high) : Text_(low);
            var box = new RectangleF(Width - ValueWidth, Height / 2f - 13, ValueWidth - 2, 26);
            using (var p = Draw.Round(box, 6)) using (var b = new SolidBrush(Ui.AccentSoft)) g.FillPath(b, p);
            TextRenderer.DrawText(g, label, Ui.Bold, Rectangle.Round(box), Enabled ? Ui.AccentDark : Ui.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (Focused) using (var pen = new Pen(Color.FromArgb(90, Ui.Accent)) { DashStyle = DashStyle.Dot }) g.DrawRectangle(pen, 0, 0, Width - ValueWidth - 8, Height - 1);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button != MouseButtons.Left) return;
            dragging = Range && Math.Abs(e.X - X(high)) < Math.Abs(e.X - X(low)) ? 1 : 0;
            if (Range && Math.Abs(X(low) - X(high)) < 2) dragging = e.X > X(low) ? 1 : 0;
            Move(e.X);
        }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (dragging >= 0) Move(e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragging = -1; }
        void Move(float x) { if (dragging == 1) SetHigh(V(x), true); else SetLow(V(x), true); }
        protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int d = e.KeyCode == Keys.Left || e.KeyCode == Keys.Down ? -1 : e.KeyCode == Keys.Right || e.KeyCode == Keys.Up ? 1 : 0;
            if (d == 0) return;
            if (Range && e.Shift) SetHigh(high + d * step, true); else SetLow(low + d * step, true);
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    }

    /// <summary>A white card with rounded corners; its content goes into Body (a column).</summary>
    class Card : Panel
    {
        public readonly FlowLayoutPanel Body;
        readonly Label title, hint;
        public string Icon;
        public Color Stripe = Color.Empty;

        public Card(string heading = null, string text = null, int width = 760)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Ui.Surface;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(22, 16, 22, 16);
            Margin = new Padding(0, 0, 0, 14);
            MinimumSize = new Size(width, 0);
            MaximumSize = new Size(width, 0);
            Body = Ui.Column();
            Body.BackColor = Ui.Surface;
            Body.Location = new Point(22, 16);
            if (heading != null)
            {
                title = new Label { Text = heading, Font = Ui.H2, ForeColor = Ui.Text, AutoSize = true, Margin = new Padding(0, 0, 0, 2), BackColor = Ui.Surface };
                Body.Controls.Add(title);
            }
            if (text != null)
            {
                hint = new Label { Text = text, Font = Ui.Base, ForeColor = Ui.Muted, AutoSize = true, MaximumSize = new Size(width - 48, 0), Margin = new Padding(0, 2, 0, 8), BackColor = Ui.Surface };
                Body.Controls.Add(hint);
            }
            Controls.Add(Body);
            Body.SizeChanged += (s, e) => Invalidate();
        }

        public int Inner { get { return MaximumSize.Width - Body.Left - Padding.Right; } }

        /// <summary>Puts the title one step to the right to make room for an icon.</summary>
        public Card WithIcon(string icon)
        {
            Icon = icon;
            Body.Location = new Point(64, 16);
            MinimumSize = MaximumSize;
            if (hint != null) hint.MaximumSize = new Size(MaximumSize.Width - 92, 0);
            return this;
        }

        public T Add<T>(T c) where T : Control { c.BackColor = c is FlatButton || c is TextBox || c is ComboBox ? c.BackColor : Ui.Surface; Body.Controls.Add(c); return c; }

        public override Size GetPreferredSize(Size proposed)
        {
            var b = Body.GetPreferredSize(Size.Empty);
            return new Size(MaximumSize.Width, b.Height + Body.Top + Padding.Bottom);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Draw.Behind(this));
            Draw.Smooth(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var p = Draw.Round(r, 10))
            {
                g.FillPath(Brushes.White, p);
                using (var pen = new Pen(Ui.Line)) g.DrawPath(pen, p);
            }
            if (Stripe != Color.Empty)
                using (var p = Draw.Round(new RectangleF(0.5f, 0.5f, 5, Height - 1.5f), 2.5f)) using (var b = new SolidBrush(Stripe)) g.FillPath(b, p);
            if (Icon != null)
            {
                var ir = new RectangleF(20, 16, 32, 32);
                using (var p = Draw.Round(ir, 8)) using (var b = new SolidBrush(Ui.AccentSoft)) g.FillPath(b, p);
                Icons.Paint(g, Icon, new RectangleF(ir.X + 7, ir.Y + 7, 18, 18), Ui.Accent, 1.7f);
            }
        }
    }

    /// <summary>A large clickable choice: an icon, a title and a line below it.</summary>
    class Tile : Control
    {
        public string Title, Sub, Icon;
        bool hover;
        public Tile(string title, string sub, string icon)
        {
            Title = title; Sub = sub; Icon = icon;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Size = new Size(580, 78); Margin = new Padding(0, 14, 0, 0); Cursor = Cursors.Hand; TabStop = true;
            MouseEnter += (s, e) => { hover = true; Invalidate(); };
            MouseLeave += (s, e) => { hover = false; Invalidate(); };
        }
        protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) OnClick(EventArgs.Empty); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Draw.Behind(this)); Draw.Smooth(g);
            using (var p = Draw.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 10))
            {
                using (var b = new SolidBrush(hover ? Draw.Mix(Ui.AccentSoft, Color.White, 0.4) : Color.White)) g.FillPath(b, p);
                using (var pen = new Pen(hover || Focused ? Draw.Mix(Ui.Accent, Ui.Line, 0.4) : Ui.Line)) g.DrawPath(pen, p);
            }
            var ir = new RectangleF(18, Height / 2f - 21, 42, 42);
            using (var p = Draw.Round(ir, 10)) using (var b = new SolidBrush(Ui.AccentSoft)) g.FillPath(b, p);
            Icons.Paint(g, Icon, new RectangleF(ir.X + 10, ir.Y + 10, 22, 22), Ui.Accent, 1.8f);
            TextRenderer.DrawText(g, Title, Ui.H2, new Point(76, Height / 2 - 24), Ui.Text);
            TextRenderer.DrawText(g, Sub, Ui.Base, new Point(77, Height / 2 + 2), Ui.Muted);
            using (var pen = new Pen(Ui.Muted, 2f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round })
            { g.DrawLine(pen, Width - 30, Height / 2f - 6, Width - 24, Height / 2f); g.DrawLine(pen, Width - 24, Height / 2f, Width - 30, Height / 2f + 6); }
        }
    }

    /// <summary>A progress bar in AFK Realm's colours; "Marquee" runs a moving band.</summary>
    class FlatBar : ProgressBar
    {
        readonly Timer tick = new Timer { Interval = 40 };
        float phase;
        public FlatBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            tick.Tick += (s, e) => { phase = (phase + 0.012f) % 1.4f; Invalidate(); };
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.Clear(Draw.Behind(this)); Draw.Smooth(g);
            float r = Height / 2f;
            using (var p = Draw.Round(new RectangleF(0, 0, Width - 1, Height - 1), r)) using (var b = new SolidBrush(Ui.Line)) g.FillPath(b, p);
            if (Style == ProgressBarStyle.Marquee)
            {
                if (!tick.Enabled) tick.Start();
                float w = Width * 0.3f, x = (phase - 0.3f) * Width;
                var clip = g.Clip; using (var p = Draw.Round(new RectangleF(0, 0, Width - 1, Height - 1), r)) g.SetClip(p);
                using (var p = Draw.Round(new RectangleF(x, 0, w, Height - 1), r)) using (var b = new SolidBrush(Ui.Accent)) g.FillPath(b, p);
                g.Clip = clip;
                return;
            }
            if (tick.Enabled) tick.Stop();
            float f = Maximum > Minimum ? (float)(Value - Minimum) / (Maximum - Minimum) : 0;
            if (f > 0) using (var p = Draw.Round(new RectangleF(0, 0, Math.Max(Height, (Width - 1) * f), Height - 1), r)) using (var b = new SolidBrush(Ui.Accent)) g.FillPath(b, p);
        }
        protected override void Dispose(bool disposing) { if (disposing) tick.Dispose(); base.Dispose(disposing); }
    }

    /// <summary>A coloured dot with a text: the state of something.</summary>
    class StatusPill : Control
    {
        public Color Dot = Ui.Muted;
        public string Caption = "";
        public StatusPill(string caption)
        {
            Caption = caption;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(180, 46); Margin = new Padding(0, 0, 10, 0);
        }
        public void Set(string text, Color dot) { if (Text == text && Dot == dot) return; Text = text; Dot = dot; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Draw.Behind(this));
            Draw.Smooth(g);
            using (var p = Draw.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 9))
            {
                g.FillPath(Brushes.White, p);
                using (var pen = new Pen(Ui.Line)) g.DrawPath(pen, p);
            }
            using (var b = new SolidBrush(Color.FromArgb(50, Dot))) g.FillEllipse(b, 12, Height / 2f - 9, 18, 18);
            using (var b = new SolidBrush(Dot)) g.FillEllipse(b, 16, Height / 2f - 5, 10, 10);
            TextRenderer.DrawText(g, Caption, Ui.Small, new Rectangle(40, 5, Width - 44, 18), Ui.Muted, TextFormatFlags.Left);
            TextRenderer.DrawText(g, Text, Ui.Bold, new Rectangle(40, 21, Width - 44, 20), Ui.Text, TextFormatFlags.Left);
        }
    }

    /// <summary>One entry of the side bar.</summary>
    class NavItem : Control
    {
        public string Icon;
        public bool Selected;
        public string Badge;
        bool hover;
        public NavItem(string text, string icon)
        {
            Text = text; Icon = icon;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(200, 42); Margin = new Padding(10, 1, 10, 1);
            Cursor = Cursors.Hand; Font = Ui.Base;
            MouseEnter += (s, e) => { hover = true; Invalidate(); };
            MouseLeave += (s, e) => { hover = false; Invalidate(); };
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Ui.Sidebar);
            Draw.Smooth(g);
            if (Selected || hover)
                using (var p = Draw.Round(new RectangleF(0, 2, Width - 1, Height - 4), 8))
                using (var b = new SolidBrush(Selected ? Ui.SidebarActive : Ui.SidebarHover)) g.FillPath(b, p);
            if (Selected) using (var p = Draw.Round(new RectangleF(0, 10, 4, Height - 20), 2)) using (var b = new SolidBrush(Ui.AccentBright)) g.FillPath(b, p);
            var ink = Selected ? Color.White : Color.FromArgb(196, 190, 220);
            Icons.Paint(g, Icon, new RectangleF(16, Height / 2f - 9, 18, 18), ink, 1.6f);
            TextRenderer.DrawText(g, Text, Selected ? Ui.Bold : Ui.Base, new Rectangle(46, 0, Width - 50, Height), ink, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            if (!string.IsNullOrEmpty(Badge))
            {
                var size = TextRenderer.MeasureText(Badge, Ui.Small);
                var r = new RectangleF(Width - size.Width - 22, Height / 2f - 10, size.Width + 12, 20);
                using (var p = Draw.Round(r, 10)) using (var b = new SolidBrush(Ui.Warn)) g.FillPath(b, p);
                TextRenderer.DrawText(g, Badge, Ui.Small, Rectangle.Round(r), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }

    /// <summary>Several choices side by side, one of them chosen (like tabs or presets).</summary>
    class Segmented : Control
    {
        public readonly List<string> Items = new List<string>();
        int selected = -1, hover = -1;
        public event EventHandler SelectedChanged;
        public Segmented(params string[] items)
        {
            Items.AddRange(items);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = Ui.Base; Cursor = Cursors.Hand; Margin = new Padding(0, 4, 0, 6);
            Size = new Size(Items.Sum(i => TextRenderer.MeasureText(i, Ui.Bold).Width + 30) + 8, 36);
        }
        public int Selected { get { return selected; } set { if (value == selected) return; selected = value; Invalidate(); } }
        Rectangle Cell(int i)
        {
            int x = 4;
            for (int j = 0; j < i; j++) x += TextRenderer.MeasureText(Items[j], Ui.Bold).Width + 30;
            return new Rectangle(x, 4, TextRenderer.MeasureText(Items[i], Ui.Bold).Width + 30, Height - 8);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Draw.Behind(this));
            Draw.Smooth(g);
            using (var p = Draw.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 9)) using (var b = new SolidBrush(Ui.Canvas)) { g.FillPath(b, p); using (var pen = new Pen(Ui.Line)) g.DrawPath(pen, p); }
            for (int i = 0; i < Items.Count; i++)
            {
                var c = Cell(i);
                if (i == selected || i == hover)
                    using (var p = Draw.Round(c, 7)) using (var b = new SolidBrush(i == selected ? Ui.Accent : Ui.AccentSoft)) g.FillPath(b, p);
                TextRenderer.DrawText(g, Items[i], i == selected ? Ui.Bold : Ui.Base, c, i == selected ? Color.White : Ui.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int h = HitTest(e.Location); if (h != hover) { hover = h; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Invalidate(); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int h = HitTest(e.Location);
            if (h < 0) return;
            Selected = h;
            if (SelectedChanged != null) SelectedChanged(this, EventArgs.Empty);
        }
        int HitTest(Point p) { for (int i = 0; i < Items.Count; i++) if (Cell(i).Contains(p)) return i; return -1; }
    }

    /// <summary>Every window of AFK Realm: its font and colours, and its lists and tables in the same look.</summary>
    class AfkForm : Form
    {
        static readonly Dictionary<string, string> HeaderIcons = new Dictionary<string, string>
        {
            { "AccountsDialog", "accounts" }, { "BackupsDialog", "database" }, { "ClientDialog", "client" }, { "ConsolesDialog", "server" },
            { "DatabaseDialog", "database" }, { "GameMasterDialog", "gamemaster" }, { "MapDataDialog", "globe" }, { "ModulesDialog", "modules" },
            { "SettingsDialog", "settings" },
        };

        public AfkForm() { Font = Ui.Base; BackColor = Ui.Surface; ShowIcon = false; }

        protected override void OnLoad(EventArgs e)
        {
            string icon;
            if (HeaderIcons.TryGetValue(GetType().Name, out icon)) AddHeader(icon);
            Restyle.Apply(this);
            base.OnLoad(e);
        }

        /// <summary>The strip at the top of every larger window: an icon and the window's name.</summary>
        void AddHeader(string icon)
        {
            string title = Text;
            int dash = title.IndexOf(" – ");
            if (dash >= 0) title = title.Substring(dash + 3);
            var head = new Panel { Dock = DockStyle.Top, Height = 62, BackColor = Ui.Surface };
            head.Paint += (s, e) =>
            {
                var g = e.Graphics; Draw.Smooth(g);
                using (var p = Draw.Round(new RectangleF(20, 14, 34, 34), 9)) using (var b = new SolidBrush(Ui.AccentSoft)) g.FillPath(b, p);
                Icons.Paint(g, icon, new RectangleF(28, 22, 18, 18), Ui.Accent, 1.7f);
                TextRenderer.DrawText(g, title, Ui.H2, new Point(66, 18), Ui.Text);
                using (var pen = new Pen(Ui.Line)) g.DrawLine(pen, 0, head.Height - 1, head.Width, head.Height - 1);
            };
            var area = Screen.FromControl(this).WorkingArea;
            ClientSize = new Size(ClientSize.Width, Math.Min(ClientSize.Height + head.Height, area.Height - 60));
            Controls.Add(head);
            Controls.SetChildIndex(head, Controls.Count - 1);
        }
    }

    /// <summary>The look of lists, tables and inputs in AFK Realm's colours, applied to a whole window at once.</summary>
    static class Restyle
    {
        public static void Apply(Control root)
        {
            foreach (Control c in root.Controls) Apply(c);
            var list = root as ListView;
            if (list != null && list.View == View.Details && !list.OwnerDraw)
            {
                list.OwnerDraw = true;
                list.DrawColumnHeader += (s, e) =>
                {
                    using (var b = new SolidBrush(Ui.Canvas)) e.Graphics.FillRectangle(b, e.Bounds);
                    using (var pen = new Pen(Ui.Line)) { e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1); e.Graphics.DrawLine(pen, e.Bounds.Right - 1, e.Bounds.Top + 5, e.Bounds.Right - 1, e.Bounds.Bottom - 6); }
                    var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | (e.Header.TextAlign == HorizontalAlignment.Right ? TextFormatFlags.Right : TextFormatFlags.Left);
                    TextRenderer.DrawText(e.Graphics, e.Header.Text, Ui.Bold, new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height), Ui.Muted, flags);
                };
                list.DrawItem += (s, e) => { e.DrawDefault = true; };
                list.DrawSubItem += (s, e) => { e.DrawDefault = true; };
            }
            var grid = root as DataGridView;
            if (grid != null)
            {
                grid.EnableHeadersVisualStyles = false;
                grid.BorderStyle = BorderStyle.None;
                grid.BackgroundColor = Ui.Surface;
                grid.GridColor = Ui.Line;
                grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
                grid.ColumnHeadersDefaultCellStyle.BackColor = Ui.Canvas;
                grid.ColumnHeadersDefaultCellStyle.ForeColor = Ui.Muted;
                grid.ColumnHeadersDefaultCellStyle.Font = Ui.Bold;
                grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Ui.Canvas;
                grid.DefaultCellStyle.SelectionBackColor = Ui.AccentSoft;
                grid.DefaultCellStyle.SelectionForeColor = Ui.Text;
                grid.RowHeadersDefaultCellStyle.BackColor = Ui.Canvas;
                grid.RowHeadersDefaultCellStyle.SelectionBackColor = Ui.AccentSoft;
            }
            var box = root as TextBox;
            if (box != null && box.BorderStyle == BorderStyle.Fixed3D) box.BorderStyle = BorderStyle.FixedSingle;
        }
    }
}
