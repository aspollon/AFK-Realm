using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace CoAInstaller
{
    /// <summary>
    /// Shows a server's log file as it grows, like its console window would. The servers run without
    /// a visible window and write the same lines to Server.log / Auth.log; reading the files (instead
    /// of the servers' output) keeps the servers independent of AFK Realm, which may be closed and
    /// opened again while they run.
    /// </summary>
    class LogView : UserControl
    {
        readonly TextBox box = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = Ui.Mono,
            BackColor = Color.FromArgb(24, 22, 32), ForeColor = Color.FromArgb(215, 215, 225), WordWrap = false, BorderStyle = BorderStyle.None };
        readonly Timer timer = new Timer { Interval = 1000 };
        string file;
        long position;
        string pending = "";

        public LogView()
        {
            Controls.Add(box);
            timer.Tick += (s, e) => Read();
        }
        // Sized by hand: it has to fill the control whatever size the page gives it.
        protected override void OnLayout(LayoutEventArgs e) { base.OnLayout(e); box.SetBounds(0, 0, ClientSize.Width, ClientSize.Height); }

        /// <summary>Switches to a log file and shows its end.</summary>
        public void Watch(string path)
        {
            file = path; position = -1; pending = "";
            box.Clear();
            Read();
            timer.Start();
        }
        public void Pause() { timer.Stop(); }
        /// <summary>Adds lines of our own between the log lines, for example a command and its answer.</summary>
        public void Print(string text) { Read(); Append(text.TrimEnd('\r', '\n') + "\n"); }
        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }

        void Read()
        {
            if (file == null || !Visible) return;
            try
            {
                if (!File.Exists(file)) { if (position != 0) { box.Text = "(no log yet – it appears when the server starts)"; position = 0; } return; }
                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    // The servers start a new file at every start.
                    if (position < 0) { position = Math.Max(0, fs.Length - 12000); pending = ""; box.Clear(); if (position > 0) pending = null; }
                    else if (fs.Length < position) { position = 0; pending = ""; box.Clear(); }
                    if (fs.Length == position) return;
                    fs.Seek(position, SeekOrigin.Begin);
                    var buffer = new byte[Math.Min(fs.Length - position, 256 * 1024)];
                    int n = fs.Read(buffer, 0, buffer.Length);
                    position += n;
                    string text = Encoding.UTF8.GetString(buffer, 0, n);
                    // When starting in the middle of a file, the first (cut) line is dropped.
                    if (pending == null) { int nl = text.IndexOf('\n'); text = nl >= 0 ? text.Substring(nl + 1) : ""; pending = ""; }
                    text = pending + text;
                    int last = text.LastIndexOf('\n');
                    if (last < 0) { pending = text; return; }
                    pending = text.Substring(last + 1);
                    Append(text.Substring(0, last + 1));
                }
            }
            catch { /* the file is busy for a moment; the next tick reads it */ }
        }

        void Append(string text)
        {
            if (box.TextLength > 300000) box.Text = box.Text.Substring(box.TextLength - 150000);
            box.AppendText(text.Replace("\r\n", "\n").Replace("\n", Environment.NewLine));
            box.SelectionStart = box.TextLength; box.ScrollToCaret();
        }
    }
}
