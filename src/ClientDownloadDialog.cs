using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace CoAInstaller
{
    /// <summary>The progress window of the client download. It has a real Cancel button and keeps partial files for
    /// the next try; the normal Busy window cannot stop a 43 GB download.</summary>
    class ClientDownloadDialog : AfkForm
    {
        readonly Install inst;
        readonly string client;
        readonly ClientDownload.Manifest manifest;
        readonly Label phase = new Label { AutoSize = false, Dock = DockStyle.Top, Height = 26, Font = Ui.Bold, ForeColor = Ui.Text, TextAlign = ContentAlignment.MiddleLeft };
        readonly Label file = new Label { AutoSize = false, Dock = DockStyle.Top, Height = 24, Font = Ui.Small, ForeColor = Ui.Muted, TextAlign = ContentAlignment.MiddleLeft };
        readonly FlatBar bar = new FlatBar { Dock = DockStyle.Top, Height = 16, Style = ProgressBarStyle.Continuous, Maximum = 1000 };
        readonly Label percent = new Label { AutoSize = false, Dock = DockStyle.Top, Height = 22, Font = Ui.Small, ForeColor = Ui.Muted, TextAlign = ContentAlignment.MiddleLeft };
        readonly Button cancel = Ui.Secondary(L.T("Cancel"));
        readonly Button close = Ui.Secondary(L.T("Close"));
        volatile bool stop;
        bool running;
        Exception error;
        public ClientDownload.Result Result;
        public bool Cancelled { get; private set; }
        public string Failure { get; private set; }

        public ClientDownloadDialog(Install i, string target, ClientDownload.Manifest m)
        {
            inst = i; client = target; manifest = m;
            Text = Product.Name + " - " + L.T("Download game client");
            Font = Ui.Base; BackColor = Ui.Surface; ShowIcon = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(660, 230); MinimumSize = new Size(560, 220);

            var pad = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18, 16, 18, 12), BackColor = Ui.Surface };
            phase.Text = L.T("Preparing the game client ...");
            file.Text = L.T("Reading the client list ...");
            percent.Text = "";
            pad.Controls.Add(percent); pad.Controls.Add(bar); pad.Controls.Add(file); pad.Controls.Add(phase);
            Controls.Add(pad);

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(14, 8, 14, 8), FlowDirection = FlowDirection.RightToLeft, BackColor = Ui.Panel, WrapContents = false };
            close.Click += (s, e) => { if (!running) Close(); };
            cancel.Click += (s, e) =>
            {
                if (!running || stop) return;
                stop = true;
                cancel.Enabled = false;
                cancel.Text = L.T("Stopping ...");
                phase.Text = L.T("Stopping after the current block ...");
            };
            bottom.Controls.Add(close); bottom.Controls.Add(cancel);
            Controls.Add(bottom);

            Shown += (s, e) => Start();
            FormClosing += (s, e) =>
            {
                if (running)
                {
                    stop = true;
                    e.Cancel = true;
                }
            };
        }

        void Start()
        {
            if (running) return;
            running = true;
            close.Enabled = false;
            var thread = new Thread(() =>
            {
                try
                {
                    Result = ClientDownload.Update(client, manifest, Report, () => stop);
                }
                catch (OperationCanceledException)
                {
                    Cancelled = true;
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    running = false;
                    try { BeginInvoke((Action)Finish); } catch { }
                }
            });
            thread.IsBackground = true;
            thread.Start();
        }

        void Report(string p, long done, long total, string current)
        {
            if (!IsHandleCreated) return;
            try
            {
                BeginInvoke((Action)(() =>
                {
                    if (done < 0) done = 0;
                    if (total < 1) total = 1;
                    int value = (int)Math.Max(0, Math.Min(1000, done * 1000 / total));
                    bar.Value = value;
                    percent.Text = ClientDownload.FormatBytes(done) + " / " + ClientDownload.FormatBytes(total) + "  (" + (value / 10) + "%)";
                    file.Text = string.IsNullOrEmpty(current) ? "" : current;
                    phase.Text = p;
                }));
            }
            catch { }
        }

        void Finish()
        {
            close.Enabled = true;
            cancel.Enabled = false;
            if (Cancelled)
            {
                phase.Text = L.T("Download stopped. Already downloaded files are kept.");
                file.Text = L.T("Continue the download whenever you like; it resumes where it stopped.");
                percent.Text = "";
                DialogResult = DialogResult.Cancel;
                Close();
            }
            else if (error != null)
            {
                Failure = error.Message;
                phase.Text = L.T("The client download failed.");
                file.Text = error.Message;
                DialogResult = DialogResult.Abort;
                Close();
            }
            else
            {
                phase.Text = L.T("The game client is ready.");
                file.Text = "";
                percent.Text = L.F("{0} files downloaded, {1} already present, {2} kept as yours.", Result.Downloaded, Result.AlreadyPresent, Result.Kept);
                DialogResult = DialogResult.OK;
                Close();
            }
        }
    }
}
