using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace CoAInstaller
{
    /// <summary>Window frame: header, page area and footer navigation.</summary>
    class MainForm : Form
    {
        readonly Panel content = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        readonly Label headerTitle = new Label { ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 17f), AutoSize = true, Location = new Point(28, 10) };
        readonly Label headerTagline = new Label { ForeColor = Color.FromArgb(214, 206, 245), Font = new Font("Segoe UI", 10.5f, FontStyle.Italic), AutoSize = true, Location = new Point(160, 20) };
        readonly Label headerSub = new Label { ForeColor = Color.FromArgb(150, 145, 175), Font = new Font("Segoe UI", 8.5f), AutoSize = true, Location = new Point(30, 50) };
        readonly Button back = Ui.Secondary("Back");
        readonly Button next = Ui.Primary("Next");
        readonly Button close = Ui.Secondary("Close");
        readonly List<Page> history = new List<Page>();

        // Wizard state
        public Install Target;
        public string DbPassword;
        public int DbPort = 3307;
        public bool CreateShortcut = true;

        public MainForm()
        {
            Text = Product.WindowTitle;
            Font = Ui.Base;
            ClientSize = new Size(860, 620);
            MinimumSize = new Size(760, 560);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.White;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var header = new Panel { Dock = DockStyle.Top, Height = 74, BackColor = Ui.Header };
            header.Controls.Add(headerTitle); header.Controls.Add(headerTagline); header.Controls.Add(headerSub);
            // Logo left of the title (embedded resource; the window works without it).
            try
            {
                var logoStream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("CoAInstaller.logo.png");
                if (logoStream != null)
                {
                    header.Controls.Add(new PictureBox { Image = Image.FromStream(logoStream), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(52, 52), Location = new Point(18, 11), BackColor = Ui.Header });
                    headerTitle.Location = new Point(82, 10);
                    headerSub.Location = new Point(84, 50);
                }
            }
            catch { }
            // Product name first, tagline beside it, the project it builds in small print below.
            headerTitle.Text = Product.Name;
            headerTagline.Text = Product.Tagline;
            headerSub.Text = Product.BuildsFor;
            headerTitle.SizeChanged += (s, e) => headerTagline.Left = headerTitle.Right + 10;
            headerTagline.Left = headerTitle.Right + 10;

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 58, BackColor = Ui.Panel };
            var right = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(0, 12, 20, 0), WrapContents = false };
            right.Controls.Add(close); right.Controls.Add(next); right.Controls.Add(back);
            footer.Controls.Add(right);

            Controls.Add(content); Controls.Add(footer); Controls.Add(header);
            back.Click += (s, e) => GoBack();
            next.Click += (s, e) => { var p = Current; if (p != null && p.OnNext()) { } };
            close.Click += (s, e) => Close();
            FormClosing += (s, e) =>
            {
                var pp = Current as ProgressPage;
                if (pp != null && pp.Running && !pp.ConfirmCancel()) e.Cancel = true;
            };

            string last = Settings.LastInstall;
            if (!string.IsNullOrEmpty(last) && new Install(last).IsInstalled) { Target = new Install(last); Navigate(new ManagerPage(this)); }
            else Navigate(new WelcomePage(this));
        }

        public Page Current { get { return history.Count > 0 ? history[history.Count - 1] : null; } }

        public void Navigate(Page page, bool replaceHistory = false)
        {
            if (replaceHistory) history.Clear();
            history.Add(page);
            Display(page);
        }
        void GoBack()
        {
            if (history.Count < 2) return;
            history.RemoveAt(history.Count - 1);
            Display(Current);
        }
        void Display(Page page)
        {
            content.SuspendLayout();
            content.Controls.Clear();
            content.Controls.Add(page);
            content.ResumeLayout();
            RefreshButtons();
            page.OnShown();
        }
        public void RefreshButtons()
        {
            var p = Current;
            if (p == null) return;
            back.Visible = p.CanGoBack && history.Count > 1;
            next.Visible = p.NextText != null;
            if (p.NextText != null) next.Text = p.NextText;
            var pp = p as ProgressPage;
            close.Text = pp != null && pp.Running ? "Cancel" : "Close";
        }

        /// <summary>Runs work on a background thread while the window shows a busy state.</summary>
        public void RunBusy(Control statusTarget, Action<Action<string>> work, Action<Exception> done)
        {
            UseWaitCursor = true;
            Enabled = false;
            Action<string> status = t => BeginInvoke((Action)(() => statusTarget.Text = t));
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Exception error = null;
                try { work(status); } catch (Exception ex) { error = ex; }
                BeginInvoke((Action)(() => { Enabled = true; UseWaitCursor = false; done(error); }));
            });
        }
    }
}
