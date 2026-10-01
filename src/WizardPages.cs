using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace CoAInstaller
{
    class WelcomePage : Page
    {
        public WelcomePage(MainForm main) : base(main)
        {
            Body.Controls.Add(Ui.Title("Welcome"));
            Body.Controls.Add(Ui.Para("This program builds your own Conquest of Azeroth server with Playerbots, straight from the current source code. " +
                "It downloads every tool it needs, compiles the server and sets up the database including the CoA world data."));
            Body.Controls.Add(Ui.Hint("You need: Windows 10 or 11 (64-bit), about 40 GB of free disk space, an internet connection and some patience. " +
                "Bring your own CoA game client; it is not included."));

            var install = BigChoice("Install a new server", "Set everything up from scratch.");
            install.Click += (s, e) => Main.Navigate(new FolderPage(Main));
            var manage = BigChoice("Manage an existing server", "Start, stop, updates, accounts, map data …");
            manage.Click += (s, e) =>
            {
                using (var d = new FolderBrowserDialog { Description = "Choose your server folder (the one containing \"Server\", \"DB\" and \"Dependencies\")." })
                {
                    if (d.ShowDialog(this) != DialogResult.OK) return;
                    var inst = new Install(d.SelectedPath);
                    if (!inst.IsInstalled) { Ui.Error(this, "No finished server was found in this folder."); return; }
                    Main.Target = inst; Settings.LastInstall = inst.Root;
                    Main.Navigate(new ManagerPage(Main), true);
                }
            };
            Body.Controls.Add(install);
            Body.Controls.Add(manage);
        }
        Button BigChoice(string title, string sub)
        {
            var b = new Button
            {
                Text = title + "\n" + sub, TextAlign = ContentAlignment.MiddleLeft, Font = Ui.Bold, ForeColor = Ui.Text,
                BackColor = Ui.Panel, FlatStyle = FlatStyle.Flat, Width = 560, Height = 70, Margin = new Padding(0, 14, 0, 0),
                Padding = new Padding(16, 0, 0, 0), Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(215, 210, 235);
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(236, 232, 250);
            return b;
        }
        public override string NextText { get { return null; } }
    }

    class FolderPage : Page
    {
        readonly TextBox path = Ui.Input(460);
        readonly Label check = Ui.Hint("");
        public FolderPage(MainForm main) : base(main)
        {
            Body.Controls.Add(Ui.Title("Where should the server go?"));
            Body.Controls.Add(Ui.Para("Everything goes into this folder: tools, source code, database and the finished server. " +
                "Best is a short path without special characters, e.g. C:\\CoA-Server."));
            var row = Ui.Row();
            path.Text = SuggestPath();
            var browse = Ui.Secondary("Browse …");
            browse.Click += (s, e) =>
            {
                using (var d = new FolderBrowserDialog { Description = "Choose a folder for the server", ShowNewFolderButton = true })
                    if (d.ShowDialog(this) == DialogResult.OK) path.Text = d.SelectedPath;
            };
            row.Controls.Add(path); row.Controls.Add(browse);
            Body.Controls.Add(row);
            Body.Controls.Add(check);
            path.TextChanged += (s, e) => Evaluate();
            Evaluate();
        }
        static string SuggestPath()
        {
            var fixedDrives = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).OrderByDescending(d => d.AvailableFreeSpace).ToList();
            string drive = fixedDrives.Count > 0 ? fixedDrives[0].RootDirectory.FullName : "C:\\";
            return Path.Combine(drive, "CoA-Server");
        }
        /// <summary>Returns an error (blocks) or null; shows warnings and free space.</summary>
        string Evaluate()
        {
            string p = path.Text.Trim();
            string error = null; var notes = new System.Collections.Generic.List<string>();
            try
            {
                if (p.Length < 4 || !Path.IsPathRooted(p)) error = "Please enter a full path, e.g. C:\\CoA-Server.";
                else
                {
                    string full = Path.GetFullPath(p);
                    if (full.Any(ch => ch > 127)) error = "The path contains non-English or special characters, which some build tools cannot handle.";
                    else if (full.Length > 60) error = "The path is too long (60 characters at most). Compiling creates very long file paths.";
                    else if (full.IndexOf("OneDrive", StringComparison.OrdinalIgnoreCase) >= 0) error = "Please do not use a OneDrive folder: syncing interferes with the database and the build.";
                    else if (full.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), StringComparison.OrdinalIgnoreCase)
                          || full.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase))
                        error = "Please do not install under \"Program Files\" or \"Windows\".";
                    else
                    {
                        if (full.Contains(" ")) notes.Add("Note: spaces in the path work, but a path without them is more robust.");
                        var drive = new DriveInfo(Path.GetPathRoot(full));
                        double freeGb = drive.AvailableFreeSpace / 1024.0 / 1024 / 1024;
                        notes.Add(string.Format("Free space on {0}: {1:N0} GB (needed: about 40 GB).", drive.Name, freeGb));
                        if (freeGb < 40) error = "There is not enough free space on this drive.";
                        if (drive.DriveType != DriveType.Fixed) error = "Please use an internal drive.";
                        var inst = new Install(full);
                        if (inst.IsInstalled) notes.Add("A server is already installed in this folder. You can open it via \"Manage an existing server\".");
                        else if (Directory.Exists(full) && Directory.EnumerateFileSystemEntries(full).Any() && !Directory.Exists(Path.Combine(full, "Dependencies")))
                            notes.Add("The folder is not empty. Existing files are kept, but an empty folder is cleaner.");
                    }
                }
            }
            catch (Exception ex) { error = "Invalid path: " + ex.Message; }
            check.ForeColor = error != null ? Ui.Bad : Ui.Muted;
            check.Text = error ?? string.Join("\n", notes);
            return error;
        }
        public override bool OnNext()
        {
            string err = Evaluate();
            if (err != null) { Ui.Error(this, err); return false; }
            var inst = new Install(path.Text);
            if (inst.IsInstalled && !Ui.Confirm(this, "A server is already installed here. Recompile it completely?\n\nDatabase, characters and map data are kept."))
                return false;
            Main.Target = inst;
            Main.Navigate(new PasswordPage(Main));
            return true;
        }
    }

    class PasswordPage : Page
    {
        readonly TextBox pw1 = Ui.Input(300, true), pw2 = Ui.Input(300, true);
        readonly CheckBox show = new CheckBox { Text = "Show password", AutoSize = true, Font = Ui.Small, Margin = new Padding(176, 2, 0, 8) };
        public PasswordPage(MainForm main) : base(main)
        {
            Body.Controls.Add(Ui.Title("Database password"));
            Body.Controls.Add(Ui.Para("The server gets its own private MySQL database. Choose a password for it. " +
                "You will not need it day to day; " + Product.Name + " keeps it in the server settings."));
            var r1 = Ui.Row(); r1.Controls.Add(Ui.FieldLabel("Password")); r1.Controls.Add(pw1);
            var r2 = Ui.Row(); r2.Controls.Add(Ui.FieldLabel("Repeat")); r2.Controls.Add(pw2);
            Body.Controls.Add(r1); Body.Controls.Add(r2); Body.Controls.Add(show);
            show.CheckedChanged += (s, e) => { pw1.UseSystemPasswordChar = pw2.UseSystemPasswordChar = !show.Checked; };
            Body.Controls.Add(Ui.Hint("At least 10 characters. Not allowed: semicolon ; quote \" and backslash \\ (AzerothCore cannot handle them in its configuration)."));
            if (!string.IsNullOrEmpty(main.DbPassword)) { pw1.Text = pw2.Text = main.DbPassword; }
        }
        public override bool OnNext()
        {
            string a = pw1.Text, b = pw2.Text;
            if (a.Length < 10) { Ui.Error(this, "The password needs at least 10 characters."); return false; }
            if (a.IndexOfAny(new[] { ';', '"', '\\', '\r', '\n' }) >= 0) { Ui.Error(this, "Please do not use a semicolon, quote or backslash."); return false; }
            if (a != b) { Ui.Error(this, "The two passwords do not match."); return false; }
            Main.DbPassword = a;
            Main.Navigate(new SummaryPage(Main));
            return true;
        }
    }

    class SummaryPage : Page
    {
        readonly CheckBox shortcut = new CheckBox { Text = "Create a \"" + Product.ShortName + "\" shortcut on the desktop", Checked = true, AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
        readonly CheckBox understood = new CheckBox { Text = "I understand the installation may take a long time, depending on my PC.", AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
        public SummaryPage(MainForm main) : base(main)
        {
            Body.Controls.Add(Ui.Title("Ready to install"));
            Body.Controls.Add(Ui.Para("Target: " + main.Target.Root));
            Body.Controls.Add(Ui.Heading("What happens now"));
            foreach (var ph in EngineRunner.For("Install"))
                Body.Controls.Add(new Label { Text = "•  " + ph.Title, AutoSize = true, Font = Ui.Base, Margin = new Padding(8, 1, 0, 1) });
            Body.Controls.Add(Ui.Hint("\nIf Visual Studio is missing, its installer window opens along the way. That is normal; please do not close it. " +
                "Compiling puts a heavy load on your PC. Close games and other large programs if you can."));
            Body.Controls.Add(shortcut);
            Body.Controls.Add(understood);
            understood.CheckedChanged += (s, e) => Main.RefreshButtons();
        }
        public override string NextText { get { return "Install"; } }
        public override bool OnNext()
        {
            if (!understood.Checked) { Ui.Error(this, "Please confirm that you have the time."); return false; }
            Main.CreateShortcut = shortcut.Checked;
            Main.Navigate(new ProgressPage(Main, "Install"), true);
            return true;
        }
    }

    /// <summary>Shows the running engine: phase list, overall bar, current action, optional live log.</summary>
    class ProgressPage : Page
    {
        readonly string mode;
        readonly EngineRunner runner;
        readonly Label[] phaseLabels;
        readonly ProgressBar overall = new ProgressBar { Width = 640, Height = 18, Maximum = 1000, Margin = new Padding(0, 10, 0, 4) };
        readonly ProgressBar sub = new ProgressBar { Width = 640, Height = 8, Maximum = 1000, Margin = new Padding(0, 2, 0, 2) };
        readonly Label action = Ui.Hint("Starting …");
        readonly Label elapsed = Ui.Hint("");
        readonly TextBox log = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = Ui.Mono, Width = 640, Height = 170, BackColor = Color.FromArgb(24, 22, 32), ForeColor = Color.FromArgb(215, 215, 225), Visible = false, WordWrap = false };
        readonly LinkLabel toggleLog = new LinkLabel { Text = "Show details", AutoSize = true, Font = Ui.Small, Margin = new Padding(0, 6, 0, 2) };
        readonly System.Windows.Forms.Timer clock = new System.Windows.Forms.Timer { Interval = 1000 };
        readonly DateTime started = DateTime.Now;
        readonly System.Collections.Generic.List<string> changes = new System.Collections.Generic.List<string>();
        int phase = -1; double subValue;
        public bool Running { get; private set; }

        public ProgressPage(MainForm main, string mode) : base(main)
        {
            this.mode = mode;
            runner = new EngineRunner(mode);
            string title = mode == "Update" ? "Updating" : mode == "Setup" ? "Setting up" : mode == "Backup" ? "Backing up" : mode == "Restore" ? "Restoring a backup" : "Installing";
            Body.Controls.Add(Ui.Title(title));
            Body.Controls.Add(Ui.Hint("You can leave this window open in the background. Please do not let the PC go to sleep."));
            phaseLabels = runner.Phases.Select(p => new Label { Text = "○   " + p.Title, AutoSize = true, Font = Ui.Base, ForeColor = Ui.Muted, Margin = new Padding(4, 2, 0, 2) }).ToArray();
            foreach (var l in phaseLabels) Body.Controls.Add(l);
            Body.Controls.Add(overall); Body.Controls.Add(action); Body.Controls.Add(sub); Body.Controls.Add(elapsed);
            Body.Controls.Add(toggleLog); Body.Controls.Add(log);
            toggleLog.LinkClicked += (s, e) => { log.Visible = !log.Visible; toggleLog.Text = log.Visible ? "Hide details" : "Show details"; };
            clock.Tick += (s, e) => elapsed.Text = "Elapsed: " + (DateTime.Now - started).ToString(@"hh\:mm\:ss");

            runner.PhaseStarted += i => UI(() => SetPhase(i));
            runner.SubProgress += (f, t) => UI(() => { subValue = f; if (t != null) action.Text = t; sub.Style = f < 0 ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous; if (f >= 0) sub.Value = (int)(f * 1000); UpdateOverall(); });
            runner.LogLine += l => UI(() => AppendLog(l));
            runner.Change += c => UI(() => changes.Add(c));
            runner.Finished += (ok, text) => UI(() => Done(ok, text));
        }
        void UI(Action a) { if (IsHandleCreated && !IsDisposed) BeginInvoke(a); }

        public override string NextText { get { return null; } }
        public override bool CanGoBack { get { return false; } }

        public override void OnShown()
        {
            if (Running) return;
            Running = true; Main.RefreshButtons(); clock.Start();
            try
            {
                if (mode == "Install") CopySelfAndShortcut();
                runner.Start(Main.Target, mode, Main.DbPassword, Main.DbPort, Main.EngineArgs);
            }
            catch (Exception ex) { Done(false, "The installation could not be started: " + ex.Message); }
        }

        void CopySelfAndShortcut()
        {
            // A copy of the installer inside the server folder serves as the management tool later on.
            string dir = Path.Combine(Main.Target.Root, "Builder");
            Directory.CreateDirectory(dir);
            string target = Path.Combine(dir, Product.FileStem + ".exe");
            if (!string.Equals(Path.GetFullPath(Application.ExecutablePath), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                File.Copy(Application.ExecutablePath, target, true);
            Settings.LastInstall = Main.Target.Root;
            if (!Main.CreateShortcut) return;
            try
            {
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(shellType);
                string lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Product.ShortName + ".lnk");
                object sc = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                var t = sc.GetType();
                t.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { target });
                t.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { dir });
                t.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { "Manage the CoA server" });
                t.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, sc, null);
            }
            catch { /* a missing shortcut is not worth failing the installation */ }
        }

        void SetPhase(int i)
        {
            phase = i; subValue = 0;
            for (int k = 0; k < phaseLabels.Length; k++)
            {
                if (k < i) { phaseLabels[k].Text = "✔   " + runner.Phases[k].Title; phaseLabels[k].ForeColor = Ui.Ok; phaseLabels[k].Font = Ui.Base; }
                else if (k == i) { phaseLabels[k].Text = "►   " + runner.Phases[k].Title; phaseLabels[k].ForeColor = Ui.Accent; phaseLabels[k].Font = Ui.Bold; }
            }
            UpdateOverall();
        }
        void UpdateOverall() { overall.Value = (int)(runner.Overall(phase, Math.Max(0, subValue)) * 1000); }
        void AppendLog(string l)
        {
            if (log.TextLength > 400000) log.Text = log.Text.Substring(200000);
            log.AppendText(l + Environment.NewLine);
            if (phase < 0 || runner.Phases[phase].Id != "compile") action.Text = l.Length > 110 ? l.Substring(0, 110) + " …" : l;
        }

        public bool ConfirmCancel()
        {
            if (!Ui.Confirm(this, "Really cancel?\n\nThe current step is terminated. You can start the installation again later; completed steps are skipped."))
                return false;
            runner.Cancel();
            return true;
        }

        void Done(bool ok, string text)
        {
            if (!Running) return;
            Running = false; clock.Stop();
            if (ok) { for (int k = 0; k < phaseLabels.Length; k++) { phaseLabels[k].Text = "✔   " + runner.Phases[k].Title; phaseLabels[k].ForeColor = Ui.Ok; phaseLabels[k].Font = Ui.Base; } overall.Value = 1000; }
            else if (phase >= 0) { phaseLabels[phase].Text = "✖   " + runner.Phases[phase].Title; phaseLabels[phase].ForeColor = Ui.Bad; }
            sub.Style = ProgressBarStyle.Continuous;
            Main.RefreshButtons();
            Main.Navigate(new ResultPage(Main, mode, ok, text, changes), true);
        }
    }

    class ResultPage : Page
    {
        readonly bool ok; readonly string mode;
        public ResultPage(MainForm main, string mode, bool ok, string text, System.Collections.Generic.List<string> changes) : base(main)
        {
            this.ok = ok; this.mode = mode;
            if (ok && text == "uptodate")
            {
                Body.Controls.Add(Ui.Title("Everything is up to date"));
                Body.Controls.Add(Ui.Para("There are no new updates. Nothing on the server was changed."));
            }
            else if (ok)
            {
                Body.Controls.Add(Ui.Title(mode == "Update" ? "Update complete" : mode == "Backup" ? "Backup complete" : mode == "Restore" ? "Backup restored" : "Installation complete"));
                if (mode == "Update") Body.Controls.Add(Ui.Hint("The server as it was before this update was backed up first. If the new version causes problems, you can go back under \"Backups\" in the server management."));
                if (mode == "Restore") Body.Controls.Add(Ui.Hint("The server, its databases and its settings are back at the state of the backup. \"Check for updates\" offers the newer version again whenever you want it."));
                if (changes.Count > 0) { Body.Controls.Add(Ui.Heading("Updated")); foreach (var c in changes) Body.Controls.Add(Ui.Hint("•  " + c)); }
                if (mode == "Backup" || mode == "Restore") { }
                else if (!main.Target.HasMapData)
                {
                    Body.Controls.Add(Ui.Heading("One more step: map data"));
                    Body.Controls.Add(Ui.Para("The server needs map data from your CoA game client. Create it with one click in the server management " +
                        "(\"Create map data\"). Then create an account and start the server."));
                }
                else Body.Controls.Add(Ui.Para("You can now start the server in the server management."));
            }
            else
            {
                Body.Controls.Add(Ui.Title("Unfortunately, that did not work"));
                Body.Controls.Add(new Label { Text = text, ForeColor = Ui.Bad, Font = Ui.Bold, AutoSize = true, MaximumSize = new Size(640, 0), Margin = new Padding(0, 4, 0, 10) });
                Body.Controls.Add(Ui.Para("Completed steps are kept. Trying again continues where it got stuck. " +
                    "If the error persists, the log file helps narrow it down, for example when you share it with the community."));
                var row = Ui.Row();
                var retry = Ui.Primary("Try again");
                retry.Click += (s, e) => Main.Navigate(new ProgressPage(Main, mode), true);
                var openLog = Ui.Secondary("Open log");
                openLog.Click += (s, e) => { if (File.Exists(main.Target.InstallLog)) Process.Start("notepad.exe", "\"" + main.Target.InstallLog + "\""); };
                row.Controls.Add(retry); row.Controls.Add(openLog);
                Body.Controls.Add(row);
                if (mode == "Install" && string.IsNullOrEmpty(main.DbPassword)) retry.Enabled = false;
            }
        }
        public override string NextText { get { return Main.Target != null && Main.Target.IsInstalled ? "Go to server management" : null; } }
        public override bool CanGoBack { get { return false; } }
        public override bool OnNext() { Main.Navigate(new ManagerPage(Main), true); return true; }
    }
}
