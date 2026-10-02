using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace CoAInstaller
{
    /// <summary>Day-to-day management of an installed server.</summary>
    class ManagerPage : Page
    {
        readonly Install inst;
        readonly ServerControl ctl;
        readonly Label dbState = State(), authState = State(), worldState = State();
        readonly Label busy = Ui.Hint("");
        readonly Label mapState = Ui.Hint("");
        readonly Button start = Ui.Primary("Start server");
        readonly Button stop = Ui.Secondary("Stop server");
        readonly TextBox accName = Ui.Input(200), accPw1 = Ui.Input(200, true), accPw2 = Ui.Input(200, true);
        readonly ComboBox accLevel = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, Font = Ui.Base };
        readonly TextBox realm = Ui.Input(200);
        readonly LinkLabel serverUpdate = UpdateLink(), toolUpdate = UpdateLink();
        Panel serverBanner, toolBanner;
        string toolUrl;
        static LinkLabel UpdateLink()
        {
            return new LinkLabel { AutoSize = true, Font = Ui.Bold, LinkColor = Ui.Accent, ActiveLinkColor = Ui.AccentDark, Margin = new Padding(0) };
        }
        /// <summary>A tinted strip around an update link; it is shown together with the link.</summary>
        static Panel Banner(LinkLabel link)
        {
            var p = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Visible = false, WrapContents = false,
                BackColor = Color.FromArgb(240, 236, 252), Padding = new Padding(10, 7, 10, 7), Margin = new Padding(0, 8, 0, 0) };
            p.Controls.Add(link);
            return p;
        }
        readonly System.Windows.Forms.Timer poll = new System.Windows.Forms.Timer { Interval = 2000 };
        readonly Button consoles = Ui.Secondary("Open server consoles …");
        ConsolesDialog consolesWindow;

        static Label State() { return new Label { AutoSize = true, Font = Ui.Bold, Margin = new Padding(0, 6, 0, 2) }; }

        public ManagerPage(MainForm main) : base(main)
        {
            inst = main.Target; ctl = new ServerControl(inst);
            Body.Controls.Add(Ui.Title("Server management"));
            Body.Controls.Add(Ui.Hint(inst.Root));
            serverBanner = Banner(serverUpdate); toolBanner = Banner(toolUpdate);
            Body.Controls.Add(serverBanner);
            Body.Controls.Add(toolBanner);
            serverUpdate.LinkClicked += (s, e) => RunEngine("Update", "Install the server update now?\n\nThe current server is backed up first, then rebuilt with the newest CoA core and Playerbots, which can take a while. A running server is stopped cleanly first.");
            toolUpdate.LinkClicked += (s, e) => { if (toolUrl != null) Process.Start(toolUrl); };

            // --- status and start/stop
            Body.Controls.Add(Ui.Heading("Server"));
            Body.Controls.Add(StatusRow("Database", dbState));
            Body.Controls.Add(StatusRow("Authserver (login)", authState));
            Body.Controls.Add(StatusRow("Worldserver (game world)", worldState));
            var buttons = Ui.Row(); buttons.Controls.Add(start); buttons.Controls.Add(stop); buttons.Controls.Add(consoles);
            consoles.Padding = new Padding(12, 6, 12, 6); consoles.Margin = new Padding(16, 3, 3, 3);
            // Not modal: it can stay open next to the server management, for example while the server starts.
            consoles.Click += (s, e) =>
            {
                if (consolesWindow == null || consolesWindow.IsDisposed) { consolesWindow = new ConsolesDialog(inst, ctl); consolesWindow.Show(FindForm()); }
                else { consolesWindow.WindowState = FormWindowState.Normal; consolesWindow.Activate(); }
            };
            Body.Controls.Add(buttons);
            Body.Controls.Add(busy);
            Body.Controls.Add(Ui.Hint("\"Stop server\" saves all characters and shuts down cleanly. The servers run in the background; " +
                "\"Open server consoles\" shows what they print and takes GM commands."));
            start.Click += (s, e) => StartServer();
            stop.Click += (s, e) => StopServer();

            // --- map data
            Body.Controls.Add(Ui.Heading("Map data"));
            Body.Controls.Add(mapState);
            var mapBtn = Ui.Secondary("Create map data from the game client …");
            mapBtn.Click += (s, e) => new MapDataDialog(inst).ShowDialog(this);
            Body.Controls.Add(mapBtn);

            // --- settings
            Body.Controls.Add(Ui.Heading("Server settings"));
            Body.Controls.Add(Ui.Hint("XP and drop rates, Playerbots, convenience options and every other setting of the worldserver and its modules, each with its description."));
            var settingsBtn = Ui.Primary("Open server settings …");
            settingsBtn.Click += (s, e) =>
            {
                if (!File.Exists(inst.WorldConf)) { Ui.Error(this, "worldserver.conf was not found. Run \"Repair setup\" first."); return; }
                using (var d = new SettingsDialog(inst, ctl.World != null)) d.ShowDialog(this);
            };
            Body.Controls.Add(settingsBtn);

            // --- game master
            Body.Controls.Add(Ui.Heading("Game master"));
            Body.Controls.Add(Ui.Hint("Help players on the running server: give, complete and reward quests (found by NPC name or around a character), " +
                "unstuck, revive, level, gold and mail, announcements, and a console for every other GM command."));
            var gmBtn = Ui.Secondary("Game master tools …");
            gmBtn.Click += (s, e) =>
            {
                if (ctl.Db == null) { Ui.Error(this, "The database is not running. Start the server first."); return; }
                using (var d = new GameMasterDialog(inst, ctl)) d.ShowDialog(this);
            };
            Body.Controls.Add(gmBtn);

            // --- modules
            Body.Controls.Add(Ui.Heading("Modules"));
            Body.Controls.Add(Ui.Hint("Add or remove AzerothCore modules from the module catalog. The server is backed up and rebuilt; " +
                "database changes of modules installed here are recorded, so removing a module undoes them."));
            var modulesBtn = Ui.Secondary("Manage modules …");
            modulesBtn.Click += (s, e) => ManageModules();
            Body.Controls.Add(modulesBtn);

            // --- accounts
            Body.Controls.Add(Ui.Heading("Create account"));
            accLevel.Items.AddRange(new object[] { "Player", "Moderator (GM 1)", "Game Master (GM 2)", "Administrator (GM 3)" });
            accLevel.SelectedIndex = 0;
            Body.Controls.Add(Field("Account name", accName));
            Body.Controls.Add(Field("Password", accPw1));
            Body.Controls.Add(Field("Repeat password", accPw2));
            Body.Controls.Add(Field("Access level", accLevel));
            var create = Ui.Primary("Create account");
            create.Click += (s, e) => CreateAccount();
            var manage = Ui.Secondary("Manage player accounts …");
            manage.Click += (s, e) => { using (var d = new AccountsDialog(inst, ctl)) d.ShowDialog(this); };
            var accRow = Ui.Row(); accRow.Controls.Add(create); accRow.Controls.Add(manage);
            Body.Controls.Add(accRow);

            // --- playing with others
            Body.Controls.Add(Ui.Heading("Play with others"));
            Body.Controls.Add(Ui.Hint("Enter the address other players use to reach your PC, for example your Radmin VPN or Hamachi IP (26.x.x.x / 25.x.x.x) " +
                "or your LAN IP. " + Product.Name + " then adjusts everything needed and allows the servers through the Windows Firewall. " +
                "You keep playing via 127.0.0.1 yourself. Other players put this address into their realmlist.wtf."));
            var realmRow = Ui.Row(); realmRow.Controls.Add(Ui.FieldLabel("Realm address")); realmRow.Controls.Add(realm);
            var apply = Ui.Secondary("Apply"); realmRow.Controls.Add(apply);
            apply.Click += (s, e) => ApplyRealm();
            Body.Controls.Add(realmRow);

            // --- maintenance
            Body.Controls.Add(Ui.Heading("Maintenance"));
            var upd = Ui.Primary("Check for updates and install");
            upd.Click += (s, e) => RunEngine("Update", "Check for updates?\n\nIf there are new versions of CoA or Playerbots, the current server is backed up first and then recompiled, which can take a while. A running server is stopped cleanly first.");
            var repair = Ui.Secondary("Repair setup");
            repair.Click += (s, e) => RunEngine("Setup", "Set up the database and configuration again (without recompiling)?\n\nCharacters and accounts are kept.");
            var backups = Ui.Secondary("Backups …");
            backups.Click += (s, e) =>
            {
                using (var d = new BackupsDialog(inst))
                {
                    d.ShowDialog(this);
                    if (d.BackUpNow) RunEngine("Backup", null, false);
                    else if (d.RestoreName != null) RunEngine("Restore", null, true, "-Snapshot \"" + d.RestoreName + "\"");
                }
            };
            var botReset = Ui.Secondary("Reset random bots …");
            botReset.Click += (s, e) => ResetBots();
            var r = Ui.Row(); r.Controls.Add(upd); r.Controls.Add(backups); r.Controls.Add(repair); r.Controls.Add(botReset); Body.Controls.Add(r);
            var r2 = Ui.Row();
            var openDir = Ui.Secondary("Open folder"); openDir.Click += (s, e) => Process.Start("explorer.exe", "\"" + inst.Root + "\"");
            var openLogs = Ui.Secondary("Open server logs"); openLogs.Click += (s, e) => Process.Start("explorer.exe", "\"" + inst.ServerDir + "\"");
            var other = Ui.Secondary("Other server / new install"); other.Click += (s, e) => Main.Navigate(new WelcomePage(Main), true);
            r2.Controls.Add(openDir); r2.Controls.Add(openLogs); r2.Controls.Add(other);
            Body.Controls.Add(r2);

            poll.Tick += (s, e) => RefreshStatus();
        }

        static FlowLayoutPanel StatusRow(string name, Label state)
        {
            var row = Ui.Row(); row.Controls.Add(Ui.FieldLabel(name, 200)); row.Controls.Add(state); return row;
        }
        static FlowLayoutPanel Field(string name, Control c)
        {
            var row = Ui.Row(); row.Controls.Add(Ui.FieldLabel(name)); row.Controls.Add(c); return row;
        }

        public override string NextText { get { return null; } }
        public override bool CanGoBack { get { return false; } }
        public override void OnShown()
        {
            Settings.LastInstall = inst.Root;
            RefreshStatus(); poll.Start();
            LoadRealm();
            CheckForUpdates();
        }
        protected override void Dispose(bool disposing)
        {
            poll.Stop();
            if (disposing && consolesWindow != null && !consolesWindow.IsDisposed) consolesWindow.Close();
            base.Dispose(disposing);
        }

        /// <summary>Asks GitHub in the background whether newer server code or a newer AFK Realm exists.</summary>
        void CheckForUpdates()
        {
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                var r = UpdateCheck.Run(inst);
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        if (r.Server.Count > 0)
                        {
                            serverUpdate.Text = "Server update available (" + string.Join(", ", r.Server) + ")  –  click to install";
                            serverBanner.Visible = true;
                        }
                        if (r.ToolVersion != null)
                        {
                            toolUrl = r.ToolUrl;
                            toolUpdate.Text = "New: " + Product.Name + " " + r.ToolVersion + " is available  –  click to download";
                            toolBanner.Visible = true;
                        }
                    }));
                }
                catch { }
            });
        }

        bool polling;
        /// <summary>Process and file checks run in the background so the window never waits for them.</summary>
        void RefreshStatus()
        {
            if (polling) return;
            polling = true;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                bool db = false, auth = false, world = false, worldReady = false, maps = false, dbc = false;
                try
                {
                    db = ctl.Db != null; auth = ctl.Auth != null; world = ctl.World != null;
                    worldReady = world && Net.PortOpen(ctl.WorldPort);
                    maps = inst.HasMapData; dbc = inst.HasClientDbc;
                }
                catch { }
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        polling = false;
                        ShowState(dbState, db, true); ShowState(authState, auth, true); ShowState(worldState, world, worldReady);
                        start.Enabled = !(auth && world);
                        stop.Enabled = db || auth || world;
                        mapState.ForeColor = maps && dbc ? Ui.Ok : Ui.Warn;
                        mapState.Text = !maps ? "Map data is still missing. The worldserver cannot run without it."
                            : !dbc ? "The CoA DBC tables are missing. Use \"Create map data\" and tick \"Only refresh the CoA DBC tables\"."
                            : "Map data is present.";
                    }));
                }
                catch { polling = false; }
            });
        }
        static void ShowState(Label l, bool running, bool ready)
        {
            l.Text = !running ? "○  stopped" : ready ? "●  running" : "◐  loading …";
            l.ForeColor = !running ? Ui.Muted : ready ? Ui.Ok : Ui.Warn;
        }

        void StartServer()
        {
            if (!inst.HasMapData && !Ui.Confirm(this, "Map data is missing. The worldserver will not run properly without it.\n\nStart anyway?")) return;
            if (inst.HasMapData && !inst.HasClientDbc)
            { Ui.Error(this, "The CoA DBC tables are missing, so the worldserver would stop right away.\n\nOpen \"Create map data\", choose your CoA game folder and tick \"Only refresh the CoA DBC tables\"."); return; }
            Process world = null;
            Main.RunBusy(busy, st =>
                {
                    ctl.RestoreAfterBotReset(); ctl.StartDatabase(st); ctl.StartAuth(st);
                    // The line for the game master tools; a problem here must not keep the server from starting.
                    if (ctl.World == null) { try { AdminLink.Prepare(inst); } catch { } }
                    world = ctl.StartWorld(st);
                },
                err =>
                {
                    RefreshStatus(); LoadRealm();
                    if (err != null) { busy.Text = ""; Ui.Error(this, err.Message); return; }
                    busy.Text = "The worldserver is loading. You can keep using " + Product.Name + " meanwhile.";
                    ctl.WatchWorld(world, (ready, problem) =>
                    {
                        try
                        {
                            BeginInvoke((Action)(() =>
                            {
                                RefreshStatus();
                                if (ready) busy.Text = "The server is running. You can log in now.";
                                else { busy.Text = ""; Ui.Error(this, problem); }
                            }));
                        }
                        catch { }
                    });
                });
        }
        void ResetBots()
        {
            if (!Ui.Confirm(this, "Delete all random bots and create new ones?\n\n" +
                "This removes every random bot account with its characters, guilds, arena teams and mail. " +
                "Your own accounts and characters are kept, including bots you created on your own accounts.\n\n" +
                "The server is stopped first. Deleting can take a while; the new bots are created at the next server start.")) return;
            Main.RunBusy(busy, st => ctl.ResetRandomBots(st),
                err =>
                {
                    RefreshStatus();
                    if (err != null) { busy.Text = ""; Ui.Error(this, err.Message); return; }
                    busy.Text = "All random bots were deleted.";
                    if (Ui.Confirm(this, "All random bots were deleted.\n\nStart the server now? The new bots are created while it starts, which takes a bit longer than usual."))
                        StartServer();
                });
        }

        void ManageModules()
        {
            using (var d = new ModulesDialog(inst))
            {
                if (d.ShowDialog(this) != DialogResult.OK || (d.Add.Count == 0 && d.Remove.Count == 0)) return;
                string args = (d.Add.Count > 0 ? "-AddModules \"" + string.Join(";", d.Add) + "\"" : "") +
                              (d.Remove.Count > 0 ? " -RemoveModules \"" + string.Join(";", d.Remove) + "\"" : "");
                RunEngine("Modules", null, true, args.Trim());
            }
        }

        void StopServer()
        {
            if (!Ui.Confirm(this, "Stop the server now? All players are saved and logged out.")) return;
            Main.RunBusy(busy, st => { ctl.StopAll(st, true); st("Server is stopped."); },
                err => { if (err != null) { busy.Text = ""; Ui.Error(this, err.Message); } RefreshStatus(); });
        }

        void EnsureDatabase(Action<string> st) { if (ctl.Db == null) ctl.StartDatabase(st); }

        void CreateAccount()
        {
            string name = accName.Text.Trim(), pw = accPw1.Text, pw2 = accPw2.Text; int level = accLevel.SelectedIndex;
            string err = Accounts.Validate(name, pw, pw2);
            if (err != null) { Ui.Error(this, err); return; }
            Main.RunBusy(busy, st => { EnsureDatabase(st); st("Creating account ..."); Accounts.Create(inst, name, pw, level); },
                ex =>
                {
                    if (ex != null) { busy.Text = ""; Ui.Error(this, "The account could not be created:\n" + ex.Message); return; }
                    busy.Text = "Account \"" + name.ToUpperInvariant() + "\" was created.";
                    accPw1.Text = accPw2.Text = "";
                    RefreshStatus();
                });
        }

        void LoadRealm()
        {
            if (ctl.Db == null) { realm.Text = realm.Text.Length > 0 ? realm.Text : ""; return; }
            try { realm.Text = Accounts.GetRealmAddress(inst); } catch { }
        }
        void ApplyRealm()
        {
            string a = realm.Text.Trim();
            System.Net.IPAddress ip;
            if (!System.Net.IPAddress.TryParse(a, out ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            { Ui.Error(this, "Please enter an IPv4 address, for example 26.66.211.210 or 192.168.1.20."); return; }
            Main.RunBusy(busy, st => { EnsureDatabase(st); st("Setting the realm address ..."); Accounts.SetRealmAddress(inst, a); },
                ex =>
                {
                    if (ex != null) { busy.Text = ""; Ui.Error(this, ex.Message); return; }
                    busy.Text = "Realm address set: " + a + ". Restart the auth- and worldserver for it to take effect.";
                    RefreshStatus();
                });
        }

        /// <summary>Runs the engine on the progress page. The server is stopped first unless the task can run beside it.</summary>
        public void RunEngine(string mode, string question, bool stopServer = true, string engineArgs = null)
        {
            if (question != null && !Ui.Confirm(this, question)) return;
            string pw;
            try { pw = DbLogin.FromConfig(inst).Password; }
            catch (Exception ex) { Ui.Error(this, ex.Message); return; }
            Main.RunBusy(busy, st => { if (stopServer) ctl.StopAll(st, true); },
                ex =>
                {
                    if (ex != null) { Ui.Error(this, ex.Message); return; }
                    Main.DbPassword = pw;
                    Main.DbPort = ctl.DbPort;
                    Main.EngineArgs = engineArgs;
                    Main.Navigate(new ProgressPage(Main, mode), true);
                });
        }
    }

    /// <summary>Copies the extractors into the WoW client folder, runs them in a visible console and moves the result to Server\Data.</summary>
    class MapDataDialog : Form
    {
        readonly Install inst;
        readonly TextBox wow = Ui.Input(420);
        readonly CheckBox mmaps = new CheckBox { Text = "Also create mmaps (pathfinding for bots and creatures, highly recommended; may take a long time)", Checked = true, AutoSize = true, MaximumSize = new Size(560, 0) };
        readonly CheckBox dbcOnly = new CheckBox { Text = "Only refresh the CoA DBC tables (for a server that already has maps)", Checked = false, AutoSize = true, MaximumSize = new Size(560, 0) };

        public MapDataDialog(Install i)
        {
            inst = i;
            Text = "Create map data"; Font = Ui.Base; BackColor = Color.White;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(620, 370);
            var col = Ui.Column(); col.Padding = new Padding(22); col.Dock = DockStyle.Fill;
            col.Controls.Add(Ui.Heading("Map data from the game client"));
            col.Controls.Add(Ui.Para("Choose the folder of your CoA game client (the one with the \"Data\" folder). The extraction runs in its own window. " +
                "When it finishes, the data is moved into the server automatically. Keep the game and its launcher closed meanwhile.", 570));
            var row = Ui.Row(); row.Controls.Add(wow);
            var browse = Ui.Secondary("Browse …");
            browse.Click += (s, e) => { using (var d = new FolderBrowserDialog { Description = "Choose the game folder (it contains the \"Data\" folder)" }) if (d.ShowDialog(this) == DialogResult.OK) wow.Text = d.SelectedPath; };
            row.Controls.Add(browse); col.Controls.Add(row);
            col.Controls.Add(mmaps);
            if (inst.NeedsClientDbc) col.Controls.Add(dbcOnly);
            dbcOnly.CheckedChanged += (s, e) => mmaps.Enabled = !dbcOnly.Checked;
            var go = Ui.Primary("Start extraction"); go.Margin = new Padding(0, 16, 0, 0);
            go.Click += (s, e) => Run();
            col.Controls.Add(go);
            Controls.Add(col);
        }

        static string Q(string path) { return "\"" + path + "\""; }

        void Run()
        {
            string dir = wow.Text.Trim().TrimEnd('\\');
            string clientData = Path.Combine(dir, "Data");
            if (dir.Length == 0 || !Directory.Exists(clientData) || !Directory.EnumerateFiles(clientData, "*.mpq").Any())
            { Ui.Error(this, "This is not a game folder: it needs a \"Data\" folder with the game's .MPQ archives."); return; }
            bool coaDbc = inst.NeedsClientDbc;
            bool onlyDbc = coaDbc && dbcOnly.Checked;
            if (coaDbc && (!File.Exists(inst.PythonExe) || !File.Exists(inst.MpqCliExe)))
            { Ui.Error(this, "The tools for the CoA DBC extraction are missing. Run \"Repair\" once, then try again."); return; }

            string[] tools = onlyDbc ? new string[0] : new[] { "map_extractor.exe", "vmap4_extractor.exe", "vmap4_assembler.exe", "mmaps_generator.exe", "mmaps-config.yaml" };
            try
            {
                foreach (var t in tools)
                {
                    string src = Path.Combine(inst.ServerDir, t);
                    if (File.Exists(src)) File.Copy(src, Path.Combine(dir, t), true);
                }
                // Some OpenSSL/MySQL DLLs are needed by the tools as well.
                if (!onlyDbc) foreach (var dll in Directory.GetFiles(inst.ServerDir, "*.dll")) File.Copy(dll, Path.Combine(dir, Path.GetFileName(dll)), true);
            }
            catch (Exception ex) { Ui.Error(this, "The tools could not be copied into the game folder:\n" + ex.Message); return; }

            string data = inst.DataDir;
            var b = new System.Text.StringBuilder();
            b.AppendLine("@echo off");
            b.AppendLine("title Creating map data - please keep this window open");
            b.AppendLine("cd /d " + Q(dir));
            if (!onlyDbc)
            {
                b.AppendLine("echo Step 1: dbc, maps and cameras ...");
                b.AppendLine("map_extractor.exe");
                b.AppendLine("if not exist maps goto :fail");
            }
            if (coaDbc)
            {
                // The stock extractor does not read CoA's custom patch archives; the fork's own
                // tool extracts the complete table set the CoA worldserver requires.
                b.AppendLine("echo CoA DBC tables from the client archives ...");
                b.AppendLine("if exist coa-dbc rmdir /s /q coa-dbc");
                b.AppendLine(Q(inst.PythonExe) + " " + Q(inst.DbcScript) + " extract " + Q(clientData) + " coa-dbc --original --mpqcli " + Q(inst.MpqCliExe));
                b.AppendLine("if errorlevel 1 goto :dbcfail");
                b.AppendLine("if exist dbc rmdir /s /q dbc");
                b.AppendLine("ren coa-dbc dbc");
                // Old stock tables must not stay behind next to the CoA set.
                b.AppendLine("if exist " + Q(Path.Combine(data, "dbc")) + " rmdir /s /q " + Q(Path.Combine(data, "dbc")));
            }
            if (!onlyDbc)
            {
                b.AppendLine("echo Step 2: vmaps (messages like \"Couldn't open RootWmo\" are normal) ...");
                b.AppendLine("vmap4_extractor.exe");
                b.AppendLine("if not exist vmaps mkdir vmaps");
                b.AppendLine("vmap4_assembler.exe Buildings vmaps");
                b.AppendLine("rmdir /s /q Buildings");
                if (mmaps.Checked)
                {
                    b.AppendLine("echo Step 3: mmaps - this may take a long time ...");
                    b.AppendLine("if not exist mmaps mkdir mmaps");
                    b.AppendLine("mmaps_generator.exe");
                }
            }
            b.AppendLine("echo Moving the data into the server ...");
            b.AppendLine("if not exist " + Q(data) + " mkdir " + Q(data));
            foreach (var f in new[] { "dbc", "maps", "vmaps", "mmaps", "Cameras" })
                b.AppendLine("if exist \"" + f + "\" robocopy \"" + f + "\" " + Q(Path.Combine(data, f)) + " /E /MOVE /NFL /NDL /NJH /NJS >nul");
            foreach (var t in tools) b.AppendLine("del /q \"" + t + "\" 2>nul");
            b.AppendLine("echo.");
            b.AppendLine("echo DONE. The data is now in the server. You can close this window.");
            b.AppendLine("pause");
            b.AppendLine("exit /b 0");
            b.AppendLine(":dbcfail");
            b.AppendLine("echo.");
            b.AppendLine("echo ERROR: the CoA DBC tables could not be extracted (see the messages above).");
            b.AppendLine("echo Is this the CoA client folder, and are the game and its launcher closed?");
            b.AppendLine("pause");
            b.AppendLine("exit /b 1");
            b.AppendLine(":fail");
            b.AppendLine("echo.");
            b.AppendLine("echo ERROR during extraction. Is this the right game folder, and is the game closed?");
            b.AppendLine("pause");
            string bat = Path.Combine(dir, "CoA-Create-Map-Data.bat");
            File.WriteAllText(bat, b.ToString(), System.Text.Encoding.Default);
            Process.Start(new ProcessStartInfo("cmd.exe", "/c \"\"" + bat + "\"\"") { UseShellExecute = true, WorkingDirectory = dir });
            MessageBox.Show(this, "The extraction is now running in a new window. Keep it open until it says DONE.", Product.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
    }
}
