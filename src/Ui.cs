using System;
using System.Drawing;
using System.Windows.Forms;

namespace CoAInstaller
{
    /// <summary>Colors, fonts and small control factories for a consistent flat look.</summary>
    static class Ui
    {
        public static readonly Color Accent = Color.FromArgb(108, 76, 196);
        public static readonly Color AccentDark = Color.FromArgb(78, 52, 150);
        public static readonly Color Header = Color.FromArgb(28, 24, 44);
        public static readonly Color Text = Color.FromArgb(33, 33, 40);
        public static readonly Color Muted = Color.FromArgb(105, 105, 120);
        public static readonly Color Ok = Color.FromArgb(34, 139, 84);
        public static readonly Color Warn = Color.FromArgb(196, 120, 20);
        public static readonly Color Bad = Color.FromArgb(200, 50, 50);
        public static readonly Color Panel = Color.FromArgb(246, 245, 250);

        public static readonly Font Base = new Font("Segoe UI", 10f);
        public static readonly Font Small = new Font("Segoe UI", 9f);
        public static readonly Font Bold = new Font("Segoe UI Semibold", 10f);
        public static readonly Font H1 = new Font("Segoe UI Semibold", 16f);
        public static readonly Font H2 = new Font("Segoe UI Semibold", 12f);
        public static readonly Font Mono = new Font("Consolas", 9f);

        public static Label Title(string text)
        {
            return new Label { Text = text, Font = H1, ForeColor = Text, AutoSize = true, Margin = new Padding(0, 0, 0, 6) };
        }
        public static Label Heading(string text)
        {
            return new Label { Text = text, Font = H2, ForeColor = Text, AutoSize = true, Margin = new Padding(0, 12, 0, 4) };
        }
        public static Label Para(string text, int width = 640)
        {
            return new Label { Text = text, Font = Base, ForeColor = Text, AutoSize = true, MaximumSize = new Size(width, 0), Margin = new Padding(0, 2, 0, 8) };
        }
        public static Label Hint(string text, int width = 640)
        {
            return new Label { Text = text, Font = Small, ForeColor = Muted, AutoSize = true, MaximumSize = new Size(width, 0), Margin = new Padding(0, 0, 0, 6) };
        }
        public static Button Primary(string text)
        {
            var b = new Button { Text = text, Font = Bold, ForeColor = Color.White, BackColor = Accent, FlatStyle = FlatStyle.Flat, AutoSize = true, Padding = new Padding(14, 6, 14, 6), Cursor = Cursors.Hand };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = AccentDark;
            return b;
        }
        public static Button Secondary(string text)
        {
            var b = new Button { Text = text, Font = Base, ForeColor = Text, BackColor = Color.White, FlatStyle = FlatStyle.Flat, AutoSize = true, Padding = new Padding(12, 5, 12, 5), Cursor = Cursors.Hand };
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 198, 210);
            return b;
        }
        public static FlowLayoutPanel Column()
        {
            return new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0) };
        }
        public static FlowLayoutPanel Row()
        {
            return new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 4, 0, 4) };
        }
        public static TextBox Input(int width = 360, bool password = false)
        {
            return new TextBox { Font = Base, Width = width, UseSystemPasswordChar = password, Margin = new Padding(0, 2, 8, 2) };
        }
        public static Label FieldLabel(string text, int width = 170)
        {
            return new Label { Text = text, Font = Base, ForeColor = Text, Width = width, TextAlign = ContentAlignment.MiddleLeft, Height = 28, Margin = new Padding(0, 2, 6, 2) };
        }
        public static void Error(IWin32Window owner, string text)
        {
            MessageBox.Show(owner, text, Product.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        public static bool Confirm(IWin32Window owner, string text)
        {
            return MessageBox.Show(owner, text, Product.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }
    }

    /// <summary>Base class of all wizard pages: scrollable content column plus footer buttons defined by the page.</summary>
    abstract class Page : UserControl
    {
        protected readonly MainForm Main;
        protected readonly FlowLayoutPanel Body;
        protected Page(MainForm main)
        {
            Main = main;
            Dock = DockStyle.Fill;
            BackColor = Color.White;
            AutoScroll = true;
            Padding = new Padding(36, 26, 36, 16);
            Body = Ui.Column();
            // A FlowLayoutPanel that is not docked ignores the parent's padding, so it is placed explicitly.
            Body.Location = new Point(36, 24);
            Body.Margin = new Padding(0, 0, 0, 24);
            Controls.Add(Body);
        }
        /// <summary>Text of the right footer button; null hides it.</summary>
        public virtual string NextText { get { return "Next"; } }
        public virtual bool CanGoBack { get { return true; } }
        /// <summary>Validates and returns true to move on.</summary>
        public virtual bool OnNext() { return true; }
        public virtual void OnShown() { }
    }
}
