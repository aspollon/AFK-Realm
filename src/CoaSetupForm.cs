using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace CoAInstaller
{
    /// <summary>Choose the CoA Custom package, server and client, then check or copy it.</summary>
    class CoaSetupForm : Form
    {
        readonly TextBox package, server, client;
        readonly CheckBox clientPatch;
        readonly RichTextBox log;
        readonly Button check, rebuild, setup, download, export;
        bool busy;

        public CoaSetupForm() : this(null) { }

        public CoaSetupForm(Install install)
        {
            Text = L.T("CoA Custom Setup");
            ClientSize = new Size(860, 620);
            MinimumSize = new Size(720, 520);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);

            var table = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, RowCount = 6 };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            string defaultPackage = !string.IsNullOrEmpty(Settings.CoaPackage) ? Settings.CoaPackage : Path.Combine(downloads, "COA CUSTOM");
            string defaultServer = !string.IsNullOrEmpty(Settings.CoaServer) ? Settings.CoaServer :
                (install != null ? install.Root : Path.Combine(downloads, "AFK-Server"));
            string defaultClient = !string.IsNullOrEmpty(ClientAddons.ClientFolder) ? ClientAddons.ClientFolder : Path.Combine(downloads, "Descension");

            package = AddPathRow(table, L.T("CoA package"), defaultPackage);
            server = AddPathRow(table, L.T("Server folder"), install != null ? install.Root : defaultServer);
            client = AddPathRow(table, L.T("Game client"), defaultClient);

            var options = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Margin = new Padding(0, 4, 0, 6) };
            clientPatch = new CheckBox { Text = L.T("Install the AFK client patch (Classic Classes, races, hides the CoA button)"), AutoSize = true, Checked = true };
            options.Controls.Add(clientPatch);
            table.Controls.Add(new Label { Text = "", AutoSize = true }, 0, 3);
            table.Controls.Add(options, 1, 3);

            log = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.FromArgb(30, 30, 30), ForeColor = Color.Gainsboro, Font = new Font("Consolas", 9f), BorderStyle = BorderStyle.FixedSingle };
            table.SetColumnSpan(log, 2);
            table.Controls.Add(log, 0, 4);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Padding = new Padding(0, 8, 0, 0) };
            check = new Button { Text = L.T("Check only"), AutoSize = true, Padding = new Padding(12, 4, 12, 4) };
            rebuild = new Button { Text = L.T("Rebuild client patch"), AutoSize = true, Padding = new Padding(12, 4, 12, 4) };
            setup = new Button { Text = L.T("Setup client && server"), AutoSize = true, Padding = new Padding(12, 4, 12, 4) };
            download = new Button { Text = L.T("Download CoA Custom ..."), AutoSize = true, Padding = new Padding(12, 4, 12, 4) };
            export = new Button { Text = L.T("Export client ZIP ..."), AutoSize = true, Padding = new Padding(12, 4, 12, 4) };
            var close = new Button { Text = L.T("Close"), AutoSize = true, Padding = new Padding(12, 4, 12, 4) };
            buttons.Controls.Add(check);
            buttons.Controls.Add(rebuild);
            buttons.Controls.Add(setup);
            buttons.Controls.Add(download);
            buttons.Controls.Add(export);
            buttons.Controls.Add(close);
            table.SetColumnSpan(buttons, 2);
            table.Controls.Add(buttons, 0, 5);
            Controls.Add(table);

            check.Click += delegate { RunSetup("--check"); };
            rebuild.Click += delegate { RunSetup("--client-patch-only"); };
            download.Click += delegate { DownloadCoa(); };
            export.Click += delegate { ExportClientZip(); };
            setup.Click += delegate
            {
                if (MessageBox.Show(this, L.T("This copies the CoA client files, the CoA server DBCs/SQL and the settings, and installs the AFK client patch (Classic Classes).\r\n\r\nClose the game and stop the server first. Continue?"), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                RunSetup("");
            };
            close.Click += delegate { if (!busy) Close(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (busy) e.Cancel = true; };
        }

        TextBox AddPathRow(TableLayoutPanel table, string label, string value)
        {
            int row = table.Controls.Count / 2;
            table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 7, 6, 0) }, 0, row);
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, AutoSize = true, Margin = new Padding(0, 3, 0, 3) };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            var box = new TextBox { Dock = DockStyle.Fill, Text = value };
            var browse = new Button { Text = L.T("Browse ..."), Dock = DockStyle.Fill };
            browse.Click += delegate
            {
                using (var dialog = new FolderBrowserDialog { SelectedPath = Directory.Exists(box.Text) ? box.Text : "" })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    box.Text = dialog.SelectedPath;
                }
            };
            panel.Controls.Add(box, 0, 0);
            panel.Controls.Add(browse, 1, 0);
            table.Controls.Add(panel, 1, row);
            return box;
        }

        void ExportClientZip()
        {
            if (busy) return;
            if (!Directory.Exists(package.Text)) { MessageBox.Show(this, L.F("CoA package folder not found:\n{0}", package.Text), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (!Directory.Exists(server.Text)) { MessageBox.Show(this, L.F("Server folder not found:\n{0}", server.Text), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (!Directory.Exists(client.Text)) { MessageBox.Show(this, L.F("Game client folder not found:\n{0}", client.Text), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            RememberPaths();
            string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            using (var dialog = new SaveFileDialog
            {
                Filter = L.T("ZIP archive") + " (*.zip)|*.zip",
                FileName = "CoA-Custom-client.zip",
                InitialDirectory = downloads,
                Title = L.T("Save the complete CoA Custom client as ZIP")
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                string file = dialog.FileName;
                busy = true;
                check.Enabled = rebuild.Enabled = setup.Enabled = download.Enabled = export.Enabled = false;
                log.Clear();
                AppendLog(L.T("Packing the complete CoA Custom client ...") + Environment.NewLine);
                var thread = new Thread(delegate()
                {
                    try
                    {
                        string result = CoaSetup.ExportClientZip(file, package.Text, new Install(server.Text), client.Text,
                            delegate(string text, int done, int total) { AppendLog(text + Environment.NewLine); });
                        AppendLog(L.F("Saved the CoA Custom client ZIP: {0}", result) + Environment.NewLine);
                        BeginInvoke((Action)delegate
                        {
                            MessageBox.Show(this, L.F("Saved: {0}\n\nThe ZIP can be unpacked into the game folder on another computer, the same way as an add-on.", result),
                                Product.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                        });
                    }
                    catch (Exception ex)
                    {
                        AppendLog(L.T("ERROR:") + " " + ex.Message + Environment.NewLine);
                    }
                    finally
                    {
                        BeginInvoke((Action)delegate
                        {
                            busy = false;
                            check.Enabled = rebuild.Enabled = setup.Enabled = download.Enabled = export.Enabled = true;
                        });
                    }
                });
                thread.IsBackground = true;
                thread.Start();
            }
        }

        void DownloadCoa()
        {
            if (busy) return;
            string start = Directory.Exists(package.Text) ? package.Text :
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            using (var dialog = new FolderBrowserDialog { Description = L.T("Folder for the downloaded CoA Custom package"), SelectedPath = start })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                string folder = dialog.SelectedPath;
                busy = true;
                check.Enabled = rebuild.Enabled = setup.Enabled = download.Enabled = export.Enabled = false;
                log.Clear();
                AppendLog(L.T("Downloading the latest CoA Custom release ...") + Environment.NewLine);
                var thread = new Thread(delegate()
                {
                    try
                    {
                        string version = CoaCustom.DownloadAndExtract(folder, delegate(string text, int done, int total)
                        {
                            AppendLog(text + Environment.NewLine);
                        });
                        AppendLog(L.F("Downloaded CoA Custom {0} to {1}", version, folder) + Environment.NewLine);
                        BeginInvoke((Action)delegate { package.Text = folder; RememberPaths(); });
                    }
                    catch (Exception ex)
                    {
                        AppendLog("ERROR: " + ex.Message + Environment.NewLine);
                    }
                    finally
                    {
                        BeginInvoke((Action)delegate
                        {
                            busy = false;
                            check.Enabled = rebuild.Enabled = setup.Enabled = download.Enabled = export.Enabled = true;
                        });
                    }
                });
                thread.IsBackground = true;
                thread.Start();
            }
        }

        void AppendLog(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try
            {
                if (log.IsDisposed || !log.IsHandleCreated) return;
                log.BeginInvoke((Action)delegate
                {
                    log.AppendText(text);
                    log.SelectionStart = log.TextLength;
                    log.ScrollToCaret();
                });
            }
            catch { }
        }

        void RememberPaths()
        {
            if (Directory.Exists(package.Text)) Settings.CoaPackage = package.Text;
            if (Directory.Exists(server.Text)) Settings.CoaServer = server.Text;
            if (Directory.Exists(client.Text)) ClientAddons.ClientFolder = client.Text;
        }

        void RunSetup(string mode)
        {
            if (busy) return;
            if (!Directory.Exists(package.Text)) { MessageBox.Show(this, L.F("CoA package folder not found:\n{0}", package.Text), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (!Directory.Exists(server.Text)) { MessageBox.Show(this, L.F("Server folder not found:\n{0}", server.Text), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            if (!Directory.Exists(client.Text)) { MessageBox.Show(this, L.F("Game client folder not found:\n{0}", client.Text), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            RememberPaths();
            busy = true;
            check.Enabled = rebuild.Enabled = setup.Enabled = download.Enabled = export.Enabled = false;
            log.Clear();
            string[] args = new string[]
            {
                "--package", package.Text,
                "--server", server.Text,
                "--client", client.Text,
            };
            var extraList = new System.Collections.Generic.List<string>();
            if (mode.Length > 0) extraList.Add(mode);
            if (mode.Length == 0)
            {
                if (!clientPatch.Checked) extraList.Add("--no-client-patch");
            }
            string[] extra = extraList.ToArray();
            var all = new string[args.Length + extra.Length];
            Array.Copy(args, all, args.Length);
            Array.Copy(extra, 0, all, args.Length, extra.Length);
            var thread = new Thread(delegate()
            {
                TextWriter oldOut = Console.Out, oldErr = Console.Error;
                var writer = new BoxWriter(this, log);
                Console.SetOut(writer);
                Console.SetError(writer);
                int code = 1;
                try
                {
                    code = CoaSetup.Run(all);
                }
                catch (Exception ex)
                {
                    writer.WriteLine(L.T("ERROR:") + " " + ex.Message);
                }
                finally
                {
                    Console.SetOut(oldOut);
                    Console.SetError(oldErr);
                    writer.WriteLine();
                    writer.WriteLine(L.F("Exit code: {0}", code));
                    BeginInvoke((Action)delegate
                    {
                        busy = false;
                        check.Enabled = rebuild.Enabled = setup.Enabled = download.Enabled = export.Enabled = true;
                    });
                }
            });
            thread.IsBackground = true;
            thread.Start();
        }

        sealed class BoxWriter : TextWriter
        {
            readonly CoaSetupForm form;
            readonly RichTextBox box;
            public BoxWriter(CoaSetupForm form, RichTextBox box) { this.form = form; this.box = box; }
            public override Encoding Encoding { get { return Encoding.UTF8; } }
            public override void Write(char value) { Append(value.ToString()); }
            public override void Write(string value) { Append(value); }
            public override void WriteLine(string value) { Append((value ?? "") + Environment.NewLine); }
            void Append(string text)
            {
                if (string.IsNullOrEmpty(text)) return;
                try
                {
                    if (box.IsDisposed || !box.IsHandleCreated) return;
                    box.BeginInvoke((Action)delegate
                    {
                        box.AppendText(text);
                        box.SelectionStart = box.TextLength;
                        box.ScrollToCaret();
                    });
                }
                catch { }
            }
        }
    }
}
