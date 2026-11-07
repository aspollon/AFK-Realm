using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace AfkClient;

internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(16, 13, 24);
    public static readonly Color Surface = Color.FromArgb(31, 26, 44);
    public static readonly Color Surface2 = Color.FromArgb(41, 35, 56);
    public static readonly Color Border = Color.FromArgb(85, 71, 112);
    public static readonly Color Text = Color.FromArgb(246, 242, 253);
    public static readonly Color Muted = Color.FromArgb(180, 170, 198);
    public static readonly Color Purple = Color.FromArgb(126, 79, 242);
    public static readonly Color PurpleLight = Color.FromArgb(161, 119, 255);
    public static readonly Color Gold = Color.FromArgb(236, 195, 106);
    public static readonly Color Ok = Color.FromArgb(98, 210, 139);
    public static readonly Color Warn = Color.FromArgb(240, 195, 107);
    public static readonly Color Bad = Color.FromArgb(255, 122, 122);

    public static readonly Font Font = new("Segoe UI", 10f);
    public static readonly Font Small = new("Segoe UI", 9f);
    public static readonly Font Title = new("Segoe UI Semibold", 25f);
    public static readonly Font Heading = new("Segoe UI Semibold", 15f);
    public static readonly Font ButtonFont = new("Segoe UI Semibold", 10f);

    public static Button Button(string text, EventHandler click, bool primary = false, string glyph = null)
    {
        var b = new RoundButton
        {
            Text = text,
            Glyph = glyph,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Height = 40,
            MinimumSize = new Size(100, 40),
            ForeColor = Text,
            Font = ButtonFont,
            Cursor = Cursors.Hand,
            Primary = primary,
            Margin = new Padding(0, 0, 8, 0)
        };
        if (click != null) b.Click += click;
        return b;
    }

    public static TextBox Input(string value = "")
    {
        return new TextBox
        {
            Text = value,
            BackColor = Color.FromArgb(17, 14, 25),
            ForeColor = Text,
            BorderStyle = BorderStyle.FixedSingle,
            Font = Font,
            Height = 30,
            Margin = new Padding(0, 4, 0, 8)
        };
    }

    public static Label Label(string text, Font font = null, Color? color = null)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Font = font ?? Font,
            ForeColor = color ?? Text,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };
    }

    public static GraphicsPath RoundPath(Rectangle r, int radius)
    {
        int d = Math.Max(1, radius * 2);
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    static Icon _appIcon;
    public static Icon AppIcon
    {
        get
        {
            if (_appIcon != null) return _appIcon;
            try
            {
                using var stream = typeof(Theme).Assembly.GetManifestResourceStream("AfkClient.icon.ico");
                if (stream == null) return null;
                using var icon = new Icon(stream);
                _appIcon = (Icon)icon.Clone();
            }
            catch { }
            return _appIcon;
        }
    }

    /// <summary>Windows 10/11: use the dark title bar so the window does not sit in a white frame.</summary>
    public static void DarkTitleBar(Form form)
    {
        void apply()
        {
            try
            {
                if (!form.IsHandleCreated) return;
                int one = 1;
                DwmSetWindowAttribute(form.Handle, 20, ref one, 4);
                DwmSetWindowAttribute(form.Handle, 19, ref one, 4);
            }
            catch { }
        }
        if (form.IsHandleCreated) apply();
        else form.HandleCreated += (_, _) => apply();
    }
}

internal sealed class RoundButton : Button
{
    bool _hover;
    public bool Primary { get; set; }
    public string Glyph { get; set; }

    public RoundButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.Transparent;
        TabStop = true;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var size = base.GetPreferredSize(proposedSize);
        if (!string.IsNullOrEmpty(Glyph)) size.Width += 28;
        return size;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 1, Width - 1, Height - 2);
        using var path = Theme.RoundPath(rect, 9);
        Color top, bottom, border;
        if (!Enabled)
        {
            top = Color.FromArgb(40, 35, 52);
            bottom = Color.FromArgb(34, 30, 44);
            border = Color.FromArgb(60, 52, 76);
        }
        else if (Primary)
        {
            top = _hover ? Color.FromArgb(177, 137, 255) : Theme.PurpleLight;
            bottom = _hover ? Theme.PurpleLight : Theme.Purple;
            border = Color.FromArgb(198, 169, 255);
        }
        else
        {
            top = _hover ? Color.FromArgb(57, 48, 75) : Theme.Surface2;
            bottom = _hover ? Color.FromArgb(47, 40, 63) : Color.FromArgb(32, 27, 44);
            border = _hover ? Theme.PurpleLight : Theme.Border;
        }
        using (var brush = new LinearGradientBrush(rect, top, bottom, 90f))
            e.Graphics.FillPath(brush, path);
        using (var pen = new Pen(border, 1f))
            e.Graphics.DrawPath(pen, path);
        Color textColor = Enabled ? ForeColor : Color.FromArgb(120, 112, 136);
        if (string.IsNullOrEmpty(Glyph))
        {
            TextRenderer.DrawText(e.Graphics, Text, Font, rect, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
        else
        {
            using var iconFont = new Font("Segoe MDL2 Assets", 11f, FontStyle.Regular, GraphicsUnit.Point);
            Size text = TextRenderer.MeasureText(Text, Font);
            Size icon = TextRenderer.MeasureText(Glyph, iconFont);
            int total = icon.Width + 7 + text.Width;
            int x = Math.Max(8, (Width - total) / 2);
            int y = Math.Max(0, (Height - Math.Max(icon.Height, text.Height)) / 2);
            TextRenderer.DrawText(e.Graphics, Glyph, iconFont, new Rectangle(x, y, icon.Width + 2, Height), textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(x + icon.Width + 7, y, text.Width + 2, Height), textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
        if (Focused && ShowFocusCues)
        {
            var inner = Rectangle.Inflate(rect, -3, -3);
            using var focus = new Pen(Color.FromArgb(160, 230, 220, 255), 1f);
            e.Graphics.DrawPath(focus, Theme.RoundPath(inner, 7));
        }
    }
}

internal sealed class GlyphBadge : Control
{
    readonly string _glyph;
    readonly Color _color;
    public GlyphBadge(string glyph, Color color)
    {
        _glyph = glyph;
        _color = color;
        Size = new Size(42, 42);
        MinimumSize = new Size(42, 42);
        MaximumSize = new Size(42, 42);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Theme.RoundPath(rect, 11);
        using (var brush = new LinearGradientBrush(rect, Color.FromArgb(78, 54, 118), Color.FromArgb(43, 32, 64), 90f))
            e.Graphics.FillPath(brush, path);
        using (var pen = new Pen(Color.FromArgb(130, 143, 116, 202), 1f))
            e.Graphics.DrawPath(pen, path);
        using var font = new Font("Segoe MDL2 Assets", 15f, FontStyle.Regular, GraphicsUnit.Point);
        TextRenderer.DrawText(e.Graphics, _glyph, font, rect, _color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

internal sealed class AccentProgress : Control
{
    int _value;
    public int Maximum { get; set; } = 100;
    public int Value
    {
        get => _value;
        set { _value = Math.Clamp(value, 0, Maximum); Invalidate(); }
    }

    public AccentProgress()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = 10;
        BackColor = Theme.Surface;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var track = new Rectangle(0, 0, Width - 1, Height - 1);
        using var trackPath = Theme.RoundPath(track, Height / 2);
        using (var b = new SolidBrush(Color.FromArgb(46, 38, 62))) e.Graphics.FillPath(b, trackPath);
        int w = Maximum <= 0 ? 0 : (int)((Width - 1) * (double)_value / Maximum);
        if (w <= 1) return;
        var fill = new Rectangle(0, 0, w, Height - 1);
        using var fillPath = Theme.RoundPath(fill, Height / 2);
        using var brush = new LinearGradientBrush(new Rectangle(0, 0, Math.Max(2, Width), Height), Theme.PurpleLight, Theme.Gold, 0f);
        e.Graphics.FillPath(brush, fillPath);
    }
}

internal sealed class HeroPanel : Panel
{
    readonly Image _artwork;
    public HeroPanel(Image artwork)
    {
        _artwork = artwork;
        DoubleBuffered = true;
        BackColor = Theme.Background;
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using (var background = new LinearGradientBrush(ClientRectangle, Color.FromArgb(30, 23, 44), Theme.Background, 90f))
            e.Graphics.FillRectangle(background, ClientRectangle);
        if (_artwork != null)
        {
            // Show the top of the artwork (the AFK Realm lettering), not its middle.
            float scale = Width / (float)_artwork.Width;
            int sourceHeight = Math.Min(_artwork.Height, Math.Max(1, (int)(Height / Math.Max(0.01f, scale))));
            var source = new Rectangle(0, 0, _artwork.Width, sourceHeight);
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(_artwork, ClientRectangle, source, GraphicsUnit.Pixel);
        }
        using (var shade = new LinearGradientBrush(ClientRectangle, Color.FromArgb(70, 10, 7, 18), Color.FromArgb(245, Theme.Background), 90f))
            e.Graphics.FillRectangle(shade, ClientRectangle);
        using var purple = new SolidBrush(Color.FromArgb(28, 142, 82, 235));
        e.Graphics.FillRectangle(purple, ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Color.FromArgb(90, 151, 130, 216), 1);
        e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
    }
}

internal sealed class LauncherCard : CardPanel
{
    public TableLayoutPanel Body { get; } = new()
    {
        AutoSize = false,
        Dock = DockStyle.Fill,
        ColumnCount = 1,
        RowCount = 1
    };

    public Color Accent { get; set; } = Color.Transparent;
}

internal class CardPanel : Panel
{
    public CardPanel()
    {
        BackColor = Theme.Surface;
        Padding = new Padding(22, 19, 22, 19);
        Margin = new Padding(0, 0, 0, 14);
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        if (Width > 2 && Height > 2)
        {
            using var path = Theme.RoundPath(new Rectangle(0, 0, Width, Height), 14);
            Region = new Region(path);
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (Width <= 0 || Height <= 0) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new LinearGradientBrush(ClientRectangle, Color.FromArgb(39, 33, 55), Color.FromArgb(25, 21, 36), 90f);
        e.Graphics.FillRectangle(brush, ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = Theme.RoundPath(rect, 14);
        using var pen = new Pen(Color.FromArgb(105, 132, 109, 178), 1);
        e.Graphics.DrawPath(pen, path);
        if (this is LauncherCard card && card.Accent != Color.Transparent)
        {
            using var accent = new Pen(card.Accent, 2);
            e.Graphics.DrawLine(accent, 22, 0, Math.Max(22, Width - 22), 0);
        }
    }
}
