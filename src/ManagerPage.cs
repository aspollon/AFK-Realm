using System;
using System.Collections.Generic;
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
                readonly Label busy = Ui.Hint("");
        readonly Label mapState = Ui.Hint("");
        readonly Button start = Ui.Primary("Start server");
        readonly Button stop = Ui.Secondary("Stop server");
        readonly Button restart = Ui.Secondary("Restart server");
        readonly TextBox accName = Ui.Input(200), accPw1 = Ui.Input(200, true), accPw2 = Ui.Input(200, true);
        readonly ComboBox accLevel = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, Font = Ui.Base };
        readonly TextBox realm = Ui.Input(200);
        readonly TextBox realmName = Ui.Input(200);
        readonly Label nameNote = Ui.Hint("");
        readonly LinkLabel serverUpdate = UpdateLink(), toolUpdate = UpdateLink();
        Panel serverBanner, toolBanner;
        List<string> serverNews = new List<string>();                  // what the server update brings, one line per part
        DateTime? serverNewsLimited;
        readonly ToolTip bannerTip = new ToolTip { AutoPopDelay = 30000, InitialDelay = 300 };
        string toolUrl, toolVersion, toolDownload, toolSha256;
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

        // ---- scheduled restart (see RestartPlan)
        RestartPlan plan;
        readonly ComboBox planMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 290, Font = Ui.Base, Margin = new Padding(0, 2, 8, 2) };
        readonly TextBox planValue = Ui.Input(70), planWarn = Ui.Input(44);
        readonly Label planUnit = new Label { AutoSize = true, Font = Ui.Base, ForeColor = Ui.Text, Margin = new Padding(0, 6, 14, 0) };
        readonly Label planWarnLead = new Label { Text = "Tell players", AutoSize = true, Font = Ui.Base, ForeColor = Ui.Text, Margin = new Padding(0, 6, 6, 0) };
        readonly Label planWarnTail = new Label { Text = "minutes before", AutoSize = true, Font = Ui.Base, ForeColor = Ui.Text, Margin = new Padding(0, 6, 14, 0) };
        readonly Button planApply = Ui.Secondary("Apply");
        readonly Button planPostpone = Ui.Secondary("Postpone by an hour");
        readonly Label planState = Ui.Hint("");
        DateTime? restartAt;                    // the announced restart, once players have to be told
        readonly HashSet<int> restartTold = new HashSet<int>();
        DateTime restartSnooze = DateTime.MinValue;
        int memoryOver;                         // status checks in a row with the worldserver over its limit
        bool autoRestarting;
        string lastAutoRestart = "";
        readonly Button consoles = Ui.Secondary("Open server consoles …");
        ConsolesDialog consolesWindow;

        /// <summary>
        /// The consoles are a window of their own with its own taskbar entry. It runs on its own
        /// thread, so it stays usable while AFK Realm shows another window (settings, modules,
        /// game master tools) or is busy.
        /// </summary>
        void OpenConsoles()
        {
            var open = consolesWindow;
            if (open != null && !open.IsDisposed && open.IsHandleCreated)
            {
                try { open.BeginInvoke((Action)(() => { if (open.WindowState == FormWindowState.Minimized) open.WindowState = FormWindowState.Normal; open.Activate(); })); return; }
                catch { }
            }
            var thread = new System.Threading.Thread(() =>
            {
                // A problem in this window must never take the server management down with it.
                try
                {
                    var window = new ConsolesDialog(inst, ctl);
                    consolesWindow = window;
                    Application.Run(window);
                }
                catch { }
                finally { consolesWindow = null; }
            });
            thread.SetApartmentState(System.Threading.ApartmentState.STA);
            thread.IsBackground = true;     // closes with AFK Realm
            thread.Start();
        }

        static Label State() { return new Label { AutoSize = true, Font = Ui.Bold, Margin = new Padding(0, 6, 0, 2) }; }

        readonly Label clientState = Ui.Hint("");

        /// <summary>Shows whether the game has every add-on of the installed modules.</summary>
        string ShowClientState()
        {
            string pending = ClientAddons.Pending(inst);
            clientState.Text = pending.Length == 0 ? "" : "Not everything the installed modules bring for the game client is in your game yet.";
            clientState.ForeColor = Ui.Warn;
            NavItem item;
            if (nav.TryGetValue("modules", out item)) { item.Badge = pending.Length == 0 ? null : "!"; item.Invalidate(); }
            return pending;
        }

        /// <summary>After a module brought, changed or lost an add-on, the game client dialog opens by itself (once for each new state).</summary>
        void OfferClient()
        {
            string pending = ShowClientState();
            // The same state is offered only once, also across starts; the line above the button keeps saying what is open.
            string offered = Path.Combine(inst.Root, "Dependencies", "client-offered.txt");
            bool offerClient;
            try
            {
                offerClient = pending.Length > 0 && !(File.Exists(offered) && File.ReadAllText(offered).Trim() == pending);
                if (offerClient) File.WriteAllText(offered, pending);
                else if (pending.Length == 0 && File.Exists(offered)) File.Delete(offered);   // all done: the next change is offered again
            }
            catch { offerClient = false; }
            var bots = File.Exists(inst.WorldExe) ? ModuleBots.NotOffered(inst, File.GetLastWriteTimeUtc(inst.WorldExe)) : new List<KeyValuePair<string, string>>();
            if (!offerClient && bots.Count == 0) return;
            Action offer = () =>
            {
                if (offerClient)
                {
                    using (var d = new ClientDialog(inst, "A module brings a new or changed add-on or client patch for the game client (or one is no longer needed)."))
                        d.ShowDialog(this);
                    ShowClientState();
                }
                if (bots.Count > 0) OfferBotReset(bots);
            };
            // When AFK Realm starts, the page is shown before its window exists: the offer waits for the window.
            if (IsHandleCreated) BeginInvoke(offer);
            else
            {
                EventHandler once = null;
                once = (s, e) => { HandleCreated -= once; BeginInvoke(offer); };
                HandleCreated += once;
            }
        }

        // ---- the frame: side bar, status bar and the section shown
        readonly Panel sidebar = new Panel { Dock = DockStyle.Left, Width = 230, BackColor = Ui.Sidebar };
        readonly Panel statusBar = new Panel { Dock = DockStyle.Top, Height = 122, BackColor = Ui.Canvas };
        readonly Panel scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Ui.Canvas };
        readonly FlowLayoutPanel shown = Ui.Column();
        readonly Dictionary<string, List<Control>> sections = new Dictionary<string, List<Control>>();
        readonly Dictionary<string, NavItem> nav = new Dictionary<string, NavItem>();
        readonly StatusPill dbPill = new StatusPill("Database"), authPill = new StatusPill("Login (authserver)"), worldPill = new StatusPill("Game world (worldserver)");
        readonly Label uptime = new Label { AutoSize = true, Font = Ui.Small, ForeColor = Ui.Muted, Margin = new Padding(4, 2, 0, 0), BackColor = Ui.Canvas };
        string current;
        const int CardWidth = 780;

        Card Section(string section, string title, string text, string icon = null)
        {
            var c = new Card(title, text, CardWidth);
            if (icon != null) c.WithIcon(icon);
            List<Control> list;
            if (!sections.TryGetValue(section, out list)) sections[section] = list = new List<Control>();
            list.Add(c);
            return c;
        }

        void AddNav(string key, string text, string icon)
        {
            var item = new NavItem(text, icon);
            item.Click += (s, e) => Show(key);
            nav[key] = item;
        }

        /// <summary>Shows one section of the management in the area right of the side bar.</summary>
        void Show(string key)
        {
            current = key;
            foreach (var n in nav) { n.Value.Selected = n.Key == key; n.Value.Invalidate(); }
            scroller.SuspendLayout();
            shown.SuspendLayout();
            shown.Controls.Clear();
            var heading = new Label { Text = nav[key].Text, Font = Ui.H1, ForeColor = Ui.Text, AutoSize = true, Margin = new Padding(2, 0, 0, 12), BackColor = Ui.Canvas };
            shown.Controls.Add(heading);
            List<Control> list;
            if (sections.TryGetValue(key, out list)) foreach (var c in list) if (c.Tag as string != "hidden") shown.Controls.Add(c);
            shown.ResumeLayout();
            scroller.AutoScrollPosition = new Point(0, 0);
            scroller.ResumeLayout();
        }

        public ManagerPage(MainForm main) : base(main)
        {
            inst = main.Target; ctl = new ServerControl(inst);
            AutoScroll = false; Padding = new Padding(0); BackColor = Ui.Canvas;
            Body.Visible = false;

            // --- side bar
            AddNav("server", "Server", "server");
            AddNav("settings", "Settings", "settings");
            AddNav("modules", "Modules", "modules");
            AddNav("gm", "Game master", "gamemaster");
            AddNav("accounts", "Accounts", "accounts");
            AddNav("database", "Database", "database");
            AddNav("maintenance", "Maintenance", "maintenance");
            var navList = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Fill, BackColor = Ui.Sidebar, Padding = new Padding(0, 16, 0, 0) };
            foreach (var n in nav.Values) navList.Controls.Add(n);
            var foot = new Label { Dock = DockStyle.Bottom, Height = 54, ForeColor = Color.FromArgb(150, 145, 175), Font = Ui.Small, BackColor = Ui.Sidebar,
                Padding = new Padding(20, 0, 10, 14), TextAlign = ContentAlignment.BottomLeft, Text = Product.Name + " " + Product.Version + "\n" + inst.Root, AutoEllipsis = true };
            sidebar.Controls.Add(navList); sidebar.Controls.Add(foot);

            // --- status bar: the three parts of the server, the main buttons, updates and what is going on
            var pills = Ui.Row(); pills.Location = new Point(28, 16); pills.BackColor = Ui.Canvas;
            dbPill.Width = 150; authPill.Width = 190; worldPill.Width = 220;
            pills.Controls.Add(dbPill); pills.Controls.Add(authPill); pills.Controls.Add(worldPill);
            var buttons = Ui.Row(); buttons.Location = new Point(28, 70); buttons.BackColor = Ui.Canvas;
            Ui.WithIcon(start, "server");
            buttons.Controls.Add(start); buttons.Controls.Add(stop); buttons.Controls.Add(restart); buttons.Controls.Add(consoles);
            consoles.Margin = new Padding(16, 4, 8, 4);
            consoles.Click += (s, e) => OpenConsoles();
            start.Click += (s, e) => StartServer();
            stop.Click += (s, e) => StopServer();
            restart.Click += (s, e) => RestartServer();
            serverBanner = Banner(serverUpdate); toolBanner = Banner(toolUpdate);
            // Below the buttons: how long the server runs, then the update notes, each as wide as the window allows -
            // a long text wraps instead of running out of the window, and the bar grows with them.
            var notes = Ui.Column(); notes.BackColor = Ui.Canvas; notes.Location = new Point(28, 116);
            notes.Controls.Add(uptime); notes.Controls.Add(serverBanner); notes.Controls.Add(toolBanner);
            busy.Location = new Point(30, 116); busy.BackColor = Ui.Canvas; busy.MaximumSize = new Size(900, 0);
            statusBar.Controls.Add(pills); statusBar.Controls.Add(buttons); statusBar.Controls.Add(notes); statusBar.Controls.Add(busy);
            statusBar.Paint += (s, e) => { using (var pen = new Pen(Ui.Line)) e.Graphics.DrawLine(pen, 0, statusBar.Height - 1, statusBar.Width, statusBar.Height - 1); };
            bool fitting = false;
            Action fit = () =>
            {
                if (fitting) return;
                fitting = true;
                try
                {
                    int room = Math.Max(240, statusBar.Width - 56 - 20);
                    foreach (var link in new[] { serverUpdate, toolUpdate }) link.MaximumSize = new Size(room, 0);
                    uptime.MaximumSize = new Size(room, 0);
                    uptime.Visible = uptime.Text.Length > 0;
                    notes.Top = busy.Text.Length > 0 ? busy.Bottom + 4 : 112;
                    bool any = uptime.Visible || serverBanner.Visible || toolBanner.Visible;
                    int bottom = Math.Max(busy.Text.Length > 0 ? busy.Bottom + 6 : 122, any ? notes.Top + notes.PreferredSize.Height + 12 : 0);
                    if (statusBar.Height != bottom) statusBar.Height = bottom;
                }
                finally { fitting = false; }
            };
            fit();
            statusBar.Resize += (s, e) => fit();
            notes.SizeChanged += (s, e) => fit();
            busy.TextChanged += (s, e) => fit();
            uptime.TextChanged += (s, e) => fit();
            serverBanner.VisibleChanged += (s, e) => fit();
            toolBanner.VisibleChanged += (s, e) => fit();
            serverUpdate.LinkClicked += (s, e) => RunEngine("Update", "Install the server update now?\n\n" +
                (serverNews.Count > 0 ? "New:\n  " + string.Join("\n  ", serverNews) + "\n" +
                    (serverNewsLimited != null ? "(GitHub counts the changes again from " + serverNewsLimited.Value.ToString("HH:mm") + ")\n" : "") + "\n" : "") +
                "The current server is backed up first, then rebuilt with the newest code, which can take a while. A running server is stopped cleanly first.");
            toolUpdate.LinkClicked += (s, e) => UpdateTool();

            shown.Location = new Point(28, 24); shown.BackColor = Ui.Canvas; shown.Margin = new Padding(0, 0, 0, 30);
            scroller.Controls.Add(shown);
            Controls.Add(scroller); Controls.Add(statusBar); Controls.Add(sidebar);

            // --- Server
            var restartCard = Section("server", "Scheduled restart", "A world that has run for many hours can grow slow and use more and more memory; a restart gives it back. " + Product.Name +
                " can do that on its own: players are told in the game beforehand, then the server is stopped cleanly and started again. This works while the server runs and " + Product.Name + " stays open.", "server");
            planMode.Items.AddRange(new object[] { "Off", "After the server has run for", "Every day at", "When the worldserver uses more than" });
            var planRow = Ui.Row();
            planRow.Controls.Add(planMode); planRow.Controls.Add(planValue); planRow.Controls.Add(planUnit);
            restartCard.Add(planRow);
            var warnRow = Ui.Row();
            warnRow.Controls.Add(planWarnLead); warnRow.Controls.Add(planWarn); warnRow.Controls.Add(planWarnTail); warnRow.Controls.Add(planApply);
            restartCard.Add(warnRow);
            restartCard.Add(planState);
            planPostpone.Visible = false; planPostpone.Margin = new Padding(0, 0, 0, 6);
            restartCard.Add(planPostpone);
            plan = RestartPlan.Load(inst);
            planMode.SelectedIndex = plan.Mode;
            ShowPlanFields();
            planMode.SelectedIndexChanged += (s, e) => ShowPlanFields();
            planMode.DropDownClosed += (s, e) => ShowPlanFields();
            planApply.Click += (s, e) => ApplyPlan();
            planPostpone.Click += (s, e) => PostponeRestart();

            var mapCard = Section("server", "Map data", "The worldserver needs the maps, vmaps and mmaps of the game and CoA's own DBC tables. They are made once from your game client.", "globe");
            mapCard.Add(mapState);
            var mapBtn = mapCard.Add(Ui.Secondary("Create map data from the game client …"));
            mapBtn.Click += (s, e) => { using (var d = new MapDataDialog(inst)) d.ShowDialog(this); };

            // --- Settings
            var settingsCard = Section("settings", "Server settings", "XP and drop rates, Playerbots, convenience options and every other setting of the worldserver and its modules, each with its description.", "settings");
            var settingsBtn = settingsCard.Add(Ui.Primary("Open server settings …"));
            settingsBtn.Click += (s, e) =>
            {
                if (!File.Exists(inst.WorldConf)) { Ui.Error(this, "worldserver.conf was not found. Run \"Repair setup\" first."); return; }
                using (var d = new SettingsDialog(inst, ctl.World != null)) d.ShowDialog(this);
            };

            var nameCard = Section("settings", "Server name", "The name players see in the realm list when they log in. The game client keeps each character's interface settings " +
                "in a folder named after the server (WTF\\Account\\<account>\\<server name>), so after renaming it starts with fresh ones unless you rename that folder too.", "server");
            var nameRow = Ui.Row(); nameRow.Controls.Add(Ui.FieldLabel("Server name")); nameRow.Controls.Add(realmName);
            var rename = Ui.Secondary("Rename"); nameRow.Controls.Add(rename);
            rename.Click += (s, e) => ApplyRealmName();
            realmName.MaxLength = 32;
            nameCard.Add(nameRow);
            nameCard.Add(nameNote);

            var realmCard = Section("settings", "Play with others", "Enter the address other players use to reach your PC, for example your Radmin VPN or Hamachi IP (26.x.x.x / 25.x.x.x) " +
                "or your LAN IP. " + Product.Name + " then adjusts everything needed and allows the servers through the Windows Firewall. " +
                "You keep playing via 127.0.0.1 yourself. Other players put this address into their realmlist.wtf.", "globe");
            var realmRow = Ui.Row(); realmRow.Controls.Add(Ui.FieldLabel("Realm address")); realmRow.Controls.Add(realm);
            var apply = Ui.Secondary("Apply"); realmRow.Controls.Add(apply);
            apply.Click += (s, e) => ApplyRealm();
            realmCard.Add(realmRow);

            // --- Modules
            var modulesCard = Section("modules", "Manage modules", "Add or remove modules: those made for this server and the AzerothCore module catalog. The server is backed up and rebuilt; " +
                "database changes of modules installed here are recorded, so removing a module undoes them.", "modules");
            var modulesBtn = modulesCard.Add(Ui.Primary("Manage modules …"));
            modulesBtn.Click += (s, e) => ManageModules();

            journeyCard = Section("modules", "World Journey", "The old world, Outland and Northrend as one journey from 1 to 60: where each part begins, how the level window works, " +
                "how hard creatures are and what follows the journey.", "journey");
            journeyCard.Stripe = Ui.Accent;
            var journeyBtn = journeyCard.Add(Ui.Primary("Set up World Journey …"));
            journeyBtn.Click += (s, e) => OpenSetup(new JourneySetup(inst));

            auctionCard = Section("modules", "Auction house bots", "How the bots of mod-playerbots-auctions trade: how often they come, what they sell and buy, their prices, crafting, " +
                "deals by chat, the Trading Post and their chatter.", "auction");
            auctionCard.Stripe = Ui.Accent;
            var auctionBtn = auctionCard.Add(Ui.Primary("Set up the auction house bots …"));
            auctionBtn.Click += (s, e) => OpenSetup(new AuctionSetup(inst));

            classicCard = Section("modules", "Classic classes", "Which classic classes - Warrior to Druid - can be played next to the classes of Conquest of Azeroth, by players and bots.", "gamemaster");
            classicCard.Stripe = Ui.Accent;
            var classicBtn = classicCard.Add(Ui.Primary("Set up the classic classes …"));
            classicBtn.Click += (s, e) => OpenSetup(new ClassicSetup(inst));

            var clientCard = Section("modules", "Game client", "Some modules bring an add-on or a patch for the game client (for example World Journey's map levels and tooltips, or the class choice of the classic classes). " + Product.Name +
                " puts them into your game and clears its cache when a module asks for it; for other players it packs them into a ZIP.", "client");
            clientCard.Add(clientState);
            var clientBtn = clientCard.Add(Ui.Secondary("Game client …"));
            clientBtn.Click += (s, e) => { using (var d = new ClientDialog(inst)) d.ShowDialog(this); ShowClientState(); };

            // --- Game master
            var gmCard = Section("gm", "Game master tools", "Help players on the running server: give, complete and reward quests (found by NPC name or around a character), " +
                "unstuck, revive, level, gold and mail, announcements, and a console for every other GM command.", "gamemaster");
            var gmBtn = gmCard.Add(Ui.Primary("Open game master tools …"));
            gmBtn.Click += (s, e) =>
            {
                if (ctl.Db == null) { Ui.Error(this, "The database is not running. Start the server first."); return; }
                using (var d = new GameMasterDialog(inst, ctl)) d.ShowDialog(this);
            };

            // --- Accounts
            var accCard = Section("accounts", "Create account", "A new login for the game, with the access level you choose.", "accounts");
            accLevel.Items.AddRange(new object[] { "Player", "Moderator (GM 1)", "Game Master (GM 2)", "Administrator (GM 3)" });
            accLevel.SelectedIndex = 0;
            accCard.Add(Field("Account name", accName));
            accCard.Add(Field("Password", accPw1));
            accCard.Add(Field("Repeat password", accPw2));
            accCard.Add(Field("Access level", accLevel));
            var create = accCard.Add(Ui.Primary("Create account"));
            create.Click += (s, e) => CreateAccount();
            var manageCard = Section("accounts", "Player accounts", "The accounts of real players with their characters: a new password, the access level, deleting, and moving an account to another server (export and import).", "accounts");
            var manage = manageCard.Add(Ui.Secondary("Manage player accounts …"));
            manage.Click += (s, e) => { using (var d = new AccountsDialog(inst, ctl)) d.ShowDialog(this); };

            // --- Database
            var dbCard = Section("database", "Database editor", "Look into the server's tables, change values and run SQL without a separate program. Read-only until you allow changes; " +
                "meant for people who know what the tables are for - a wrong change can break the server, so back it up first.", "database");
            var dbBtn = dbCard.Add(Ui.Primary("Open the database editor …"));
            dbBtn.Click += (s, e) =>
            {
                if (ctl.Db == null) { Ui.Error(this, "The database is not running. Start the server first."); return; }
                using (var d = new DatabaseDialog(inst, ctl))
                {
                    d.ShowDialog(this);
                    if (d.BackUpNow) RunEngine("Backup", null, false);
                }
            };
            var backupCard = Section("database", "Backups", "Before every update and module change " + Product.Name + " saves the whole server: programs, settings, all databases. Restore one when something went wrong.", "database");
            var backups = backupCard.Add(Ui.Secondary("Backups …"));
            backups.Click += (s, e) =>
            {
                using (var d = new BackupsDialog(inst))
                {
                    d.ShowDialog(this);
                    if (d.BackUpNow) RunEngine("Backup", null, false);
                    else if (d.RestoreName != null) RunEngine("Restore", null, true, "-Snapshot \"" + d.RestoreName + "\"");
                }
            };

            // --- Maintenance
            var updCard = Section("maintenance", "Updates", "Checks the CoA core, Playerbots and your modules for new versions; the server is backed up first and rebuilt only when something changed.", "maintenance");
            var upd = updCard.Add(Ui.Primary("Check for updates and install"));
            upd.Click += (s, e) => RunEngine("Update", "Check for updates?\n\nIf there are new versions of CoA or Playerbots, the current server is backed up first and then recompiled, which can take a while. A running server is stopped cleanly first.");
            var repairCard = Section("maintenance", "Repair and reset", "\"Repair setup\" sets up the database and configuration again without recompiling; characters and accounts are kept. " +
                "\"Reset random bots\" deletes every random bot and lets the server create new ones.", "maintenance");
            var r = Ui.Row();
            var repair = Ui.Secondary("Repair setup");
            repair.Click += (s, e) => RunEngine("Setup", "Set up the database and configuration again (without recompiling)?\n\nCharacters and accounts are kept.");
            var botReset = Ui.Secondary("Reset random bots …");
            botReset.Click += (s, e) => ResetBots();
            r.Controls.Add(repair); r.Controls.Add(botReset);
            repairCard.Add(r);
            var filesCard = Section("maintenance", "Files", "The server's folder and its logs, or another server.", "server");
            var r2 = Ui.Row();
            var openDir = Ui.Secondary("Open folder"); openDir.Click += (s, e) => Process.Start("explorer.exe", "\"" + inst.Root + "\"");
            var openLogs = Ui.Secondary("Open server logs"); openLogs.Click += (s, e) => Process.Start("explorer.exe", "\"" + inst.ServerDir + "\"");
            var other = Ui.Secondary("Other server / new install"); other.Click += (s, e) => Main.Navigate(new WelcomePage(Main), true);
            r2.Controls.Add(openDir); r2.Controls.Add(openLogs); r2.Controls.Add(other);
            filesCard.Add(r2);

            // A note without text takes no room in its card.
            foreach (var note in new[] { clientState, nameNote, planState })
            {
                var l = note;
                l.Visible = l.Text.Length > 0;
                l.TextChanged += (s, e) => l.Visible = l.Text.Length > 0;
            }
            ShowModuleCards();
            Show("server");
            poll.Tick += (s, e) => RefreshStatus();
        }

        Card journeyCard, auctionCard, classicCard;

        /// <summary>The setup pages of the modules made for AFK Realm are only there when the module is installed.</summary>
        void ShowModuleCards()
        {
            string dir = ModuleCatalog.ModulesDir(inst);
            journeyCard.Tag = Directory.Exists(Path.Combine(dir, "mod-world-journey")) ? null : "hidden";
            auctionCard.Tag = Directory.Exists(Path.Combine(dir, "mod-playerbots-auctions")) ? null : "hidden";
            classicCard.Tag = Directory.Exists(Path.Combine(dir, "mod-classic-classes")) ? null : "hidden";
            if (current == "modules") Show("modules");
        }

        void OpenSetup(Form setup)
        {
            using (setup)
                if (setup.ShowDialog(this) == DialogResult.OK)
                    OfferClient();      // a setting the client patch reads may have changed
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
        public override bool ShowsFooter { get { return false; } }
        public override void OnShown()
        {
            Settings.LastInstall = inst.Root;
            RefreshStatus(); poll.Start();
            LoadRealm();
            if (SelfUpdate.JustUpdated) { SelfUpdate.JustUpdated = false; busy.Text = Product.Name + " was updated to version " + Product.Version + "."; }
            CheckForUpdates();
            OfferClient();
        }
        protected override void Dispose(bool disposing)
        {
            poll.Stop();
            var open = consolesWindow;
            if (disposing && open != null && !open.IsDisposed && open.IsHandleCreated) { try { open.BeginInvoke((Action)open.Close); } catch { } }
            base.Dispose(disposing);
        }

        /// <summary>A short line for the banner - the parts with news, without their numbers; the whole list is in the
        /// tooltip and in the question before the update.</summary>
        static string NewsSummary(List<string> news)
        {
            var parts = news.ConvertAll(n => n.Split(':')[0]);
            if (parts.Count <= 3) return string.Join(", ", parts);
            int modules = parts.Count - 2;
            return parts[0] + ", " + parts[1] + " and " + modules + " more";
        }

        /// <summary>Asks in the background whether newer server code or a newer AFK Realm exists.</summary>
        void CheckForUpdates()
        {
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                var r = UpdateCheck.Run(inst);
                Action show = () =>
                    {
                        if (r.Server.Count > 0)
                        {
                            serverNews = r.Server; serverNewsLimited = r.LimitedUntil;
                            serverUpdate.Text = "Server update available: " + NewsSummary(r.Server) + "  –  click to install";
                            bannerTip.SetToolTip(serverUpdate, string.Join("\n", r.Server) +
                                (r.LimitedUntil != null ? "\n(GitHub counts the changes again from " + r.LimitedUntil.Value.ToString("HH:mm") + ")" : ""));
                            serverBanner.Visible = true;
                            nav["maintenance"].Badge = "new"; nav["maintenance"].Invalidate();
                        }
                        if (r.ToolVersion != null)
                        {
                            toolUrl = r.ToolUrl; toolVersion = r.ToolVersion; toolDownload = r.ToolDownload; toolSha256 = r.ToolSha256;
                            toolUpdate.Text = "New: " + Product.Name + " " + r.ToolVersion + " is available  –  click to " + (toolDownload != null ? "update" : "download");
                            toolBanner.Visible = true;
                        }
                    };
                // A quick answer can come before the window exists: then it waits for the window.
                try
                {
                    if (IsHandleCreated) BeginInvoke(show);
                    else
                    {
                        EventHandler once = null;
                        once = (s, e) => { HandleCreated -= once; BeginInvoke(show); };
                        HandleCreated += once;
                        if (IsHandleCreated) { HandleCreated -= once; BeginInvoke(show); }
                    }
                }
                catch { }
            });
        }

        /// <summary>Downloads the newer release, puts it in place of this program and restarts. Servers keep running.</summary>
        void UpdateTool()
        {
            if (toolUrl == null) return;
            if (toolDownload == null) { Process.Start(toolUrl); return; }      // a release without the exe: show its page
            if (!Ui.Confirm(this, "Update " + Product.Name + " to version " + toolVersion + " now?\n\n" +
                "The new version is downloaded from GitHub, then " + Product.Name + " restarts. A running server keeps running.")) return;
            string copy = Path.Combine(inst.Root, "Builder", Product.FileStem + ".exe");
            Main.RunBusy(busy, st => { st("Downloading the update ..."); SelfUpdate.Download(toolDownload, toolSha256, st); st("Installing the update ..."); SelfUpdate.Replace(copy); },
                ex =>
                {
                    if (ex == null) { SelfUpdate.Restart(); return; }
                    busy.Text = "";
                    if (Ui.Confirm(this, "The update could not be installed:\n\n" + ex.Message + "\n\nOpen the download page instead?")) Process.Start(toolUrl);
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
                DateTime worldStart = DateTime.MinValue; long worldMemory = 0;
                try
                {
                    db = ctl.Db != null; auth = ctl.Auth != null;
                    var worldProcess = ctl.World;
                    world = worldProcess != null;
                    if (world) { try { worldStart = worldProcess.StartTime; worldMemory = Math.Max(worldProcess.PrivateMemorySize64, worldProcess.WorkingSet64); } catch { } }
                    worldReady = world && Net.PortOpen(ctl.WorldPort);
                    maps = inst.HasMapData; dbc = inst.HasClientDbc;
                }
                catch { }
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        polling = false;
                        ShowState(dbPill, db, true); ShowState(authPill, auth, true); ShowState(worldPill, world, worldReady);
                        uptime.Text = worldReady && worldStart != DateTime.MinValue ? "Running for " + RestartPlan.Span(DateTime.Now - worldStart) + (worldMemory > 0 ? "  ·  " + (worldMemory / 1073741824.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " GB of memory" : "") : "";
                        start.Enabled = !(auth && world);
                        stop.Enabled = db || auth || world;
                        restart.Enabled = auth || world;
                        Schedule(worldReady, worldStart, worldMemory);
                        mapState.ForeColor = maps && dbc ? Ui.Ok : Ui.Warn;
                        mapState.Text = !maps ? "Map data is still missing. The worldserver cannot run without it."
                            : !dbc ? "The CoA DBC tables are missing. Use \"Create map data\" and tick \"Only refresh the CoA DBC tables\"."
                            : "Map data is present.";
                    }));
                }
                catch { polling = false; }
            });
        }
        static void ShowState(StatusPill p, bool running, bool ready)
        {
            p.Set(!running ? "stopped" : ready ? "running" : "loading …", !running ? Ui.Muted : ready ? Ui.Ok : Ui.Warn);
        }

        void StartServer()
        {
            if (RestartInProgress()) return;
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
        /// <summary>A module that changes what the random bots are made of asks once, after it came, for new bots.</summary>
        void OfferBotReset(List<KeyValuePair<string, string>> modules)
        {
            ModuleBots.MarkOffered(inst, modules.Select(m => m.Key));
            if (!File.Exists(Path.Combine(inst.ConfigDir, "modules", "playerbots.conf"))) return;
            string why = string.Join("\n", modules.Select(m => "• " + m.Key + (m.Value.Length > 0 ? ": " + m.Value : "")));
            ResetBots("A new module changes what the random bots are made of:\n\n" + why + "\n\n");
        }

        void ResetBots(string reason = null)
        {
            if (!Ui.Confirm(this, (reason ?? "") + "Delete all random bots and create new ones?\n\n" +
                "This removes every random bot account with its characters, guilds, arena teams and mail. " +
                "Your own accounts and characters are kept, including bots you created on your own accounts.\n\n" +
                "The server is stopped first. Deleting can take a while; the new bots are created at the next server start." +
                (reason != null ? "\n\nNot now? \"Reset random bots …\" under Maintenance does the same later." : ""))) return;
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
            if (RestartInProgress()) return;
            if (!Ui.Confirm(this, "Stop the server now? All players are saved and logged out.")) return;
            Main.RunBusy(busy, st => { ctl.StopAll(st, true); st("Server is stopped."); },
                err => { if (err != null) { busy.Text = ""; Ui.Error(this, err.Message); } RefreshStatus(); });
        }

        /// <summary>Stops auth- and worldserver cleanly and starts them again; the database keeps running.</summary>
        void RestartServer()
        {
            if (RestartInProgress()) return;
            if (!Ui.Confirm(this, "Restart the server now? All players are saved and logged out; they can log in again once the world has loaded.")) return;
            Main.RunBusy(busy, st => { ctl.StopAll(st, false); st("Server is stopped. Starting it again ..."); },
                err =>
                {
                    RefreshStatus();
                    if (err != null) { busy.Text = ""; Ui.Error(this, err.Message); return; }
                    StartServer();
                });
        }

        // ---- scheduled restart -----------------------------------------------------------------

        void ShowPlanFields()
        {
            int mode = planMode.SelectedIndex;
            bool on = mode != RestartPlan.Off;
            // Greyed out rather than hidden: the row keeps its shape.
            planValue.Enabled = planWarn.Enabled = on;
            planUnit.ForeColor = planWarnLead.ForeColor = planWarnTail.ForeColor = on ? Ui.Text : Ui.Muted;
            planValue.Text = mode == RestartPlan.AfterUptime ? plan.Hours.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                : mode == RestartPlan.Daily ? plan.DailyText
                : mode == RestartPlan.Memory ? plan.MemoryGb.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : "";
            planUnit.Text = mode == RestartPlan.AfterUptime ? "hours" : mode == RestartPlan.Daily ? "(24-hour clock)" : mode == RestartPlan.Memory ? "GB of memory" : "";
            planWarn.Text = plan.WarnMinutes.ToString();
            planApply.Margin = new Padding(8, 0, 0, 0);
        }

        void ApplyPlan()
        {
            int mode = planMode.SelectedIndex;
            double hours = plan.Hours, gb = plan.MemoryGb; int daily = plan.DailyMinutes, warn = plan.WarnMinutes;
            if (mode == RestartPlan.AfterUptime && !RestartPlan.ParseHours(planValue.Text, out hours))
            { Ui.Error(this, "Enter after how many hours the server is restarted, for example 6 or 5.5 (at least half an hour)."); return; }
            if (mode == RestartPlan.Daily && !RestartPlan.ParseTime(planValue.Text, out daily))
            { Ui.Error(this, "Enter the time of day on the 24-hour clock, for example 05:00 or 17:30."); return; }
            if (mode == RestartPlan.Memory && !RestartPlan.ParseGb(planValue.Text, out gb))
            { Ui.Error(this, "Enter the memory limit in GB, for example 8 (at least 1)."); return; }
            if (mode != RestartPlan.Off && (!int.TryParse(planWarn.Text.Trim(), out warn) || warn < 0 || warn > 60))
            { Ui.Error(this, "Enter how many minutes before the restart players are told: 0 to 60."); return; }
            bool announced = restartAt != null && restartTold.Count > 0;
            plan.Mode = mode; plan.Hours = hours; plan.DailyMinutes = daily; plan.MemoryGb = gb; plan.WarnMinutes = warn;
            try { plan.Save(inst); } catch (Exception ex) { Ui.Error(this, "The setting could not be saved:\n" + ex.Message); return; }
            restartAt = null; restartTold.Clear(); restartSnooze = DateTime.MinValue; memoryOver = 0;
            if (announced) Announce("The announced restart is off.", false);
            ShowPlanFields();
            RefreshStatus();
        }

        void PostponeRestart()
        {
            bool announced = restartTold.Count > 0;
            restartAt = null; restartTold.Clear(); memoryOver = 0;
            restartSnooze = DateTime.Now.AddHours(1);
            if (announced) Announce("The restart has been postponed.", false);
            RefreshStatus();
        }

        bool RestartInProgress()
        {
            if (!autoRestarting) return false;
            Ui.Error(this, "The scheduled restart is running right now. Wait until the server is up again.");
            return true;
        }

        /// <summary>Says something to everybody in the game; a server that does not answer is no reason to stop.</summary>
        void Announce(string text, bool alsoOnScreen)
        {
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try { AdminLink.Run(inst, "announce " + text); } catch { }
                if (alsoOnScreen) { try { AdminLink.Run(inst, "notify " + text); } catch { } }
            });
        }

        void RestartLog(string text)
        {
            try
            {
                Directory.CreateDirectory(inst.LogDir);
                File.AppendAllText(Path.Combine(inst.LogDir, "scheduled-restart.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text + "\r\n");
            }
            catch { }
        }

        /// <summary>Called with every status check: announces a restart that is due, and does it when its time has come.</summary>
        void Schedule(bool worldReady, DateTime worldStart, long worldMemory)
        {
            if (autoRestarting) return;
            var now = DateTime.Now;
            if (!worldReady || worldStart == DateTime.MinValue)
            {
                restartAt = null; restartTold.Clear(); memoryOver = 0;
                planPostpone.Visible = false;
                planState.ForeColor = Ui.Muted;
                planState.Text = lastAutoRestart;
                return;
            }

            double gb = worldMemory / 1073741824.0;
            bool over = plan.Mode == RestartPlan.Memory && gb > plan.MemoryGb;
            memoryOver = over ? memoryOver + 1 : 0;
            if (restartAt == null && plan.Mode != RestartPlan.Off && now >= restartSnooze)
            {
                // Over the limit for half a minute, not for one moment.
                restartAt = plan.Due(now, worldStart, memoryOver >= 15);
                if (restartAt != null)
                {
                    restartTold.Clear();
                    RestartLog("Restart announced for " + restartAt.Value.ToString("HH:mm:ss") + " (" +
                        (plan.Mode == RestartPlan.AfterUptime ? "the server has run for " + RestartPlan.Span(now - worldStart)
                        : plan.Mode == RestartPlan.Daily ? "daily at " + plan.DailyText
                        : "the worldserver uses " + gb.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " GB") + ").");
                }
            }

            string running = "Worldserver: running for " + RestartPlan.Span(now - worldStart) +
                (worldMemory > 0 ? ", " + gb.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " GB of memory. " : ". ");
            if (restartAt != null)
            {
                var left = restartAt.Value - now;
                if (left <= TimeSpan.Zero)
                {
                    // Not in the middle of something else the user started.
                    if (Main.Enabled) DoScheduledRestart();
                    return;
                }
                // The smallest step that has been reached speaks for the larger ones: one announcement at a time.
                int reached = 0;
                foreach (int step in plan.Steps())
                    if (left.TotalSeconds <= step * 60 + 2) reached = step;
                if (reached > 0 && !restartTold.Contains(reached))
                {
                    foreach (int step in plan.Steps()) if (step >= reached) restartTold.Add(step);
                    int minutes = Math.Max(1, (int)Math.Round(left.TotalMinutes));
                    Announce("The server restarts in " + minutes + (minutes == 1 ? " minute" : " minutes") + ". It is back a few minutes later.",
                        reached <= 1 || reached == plan.WarnMinutes);
                }
                planPostpone.Visible = true;
                planState.ForeColor = Ui.Warn;
                planState.Text = running + "Restart in " + RestartPlan.Span(left) + (restartTold.Count > 0 ? " - players have been told." : ".");
                return;
            }

            planPostpone.Visible = false;
            planState.ForeColor = Ui.Muted;
            string next = "";
            if (now < restartSnooze) next = "The restart is postponed until " + restartSnooze.ToString("HH:mm") + ".";
            else if (plan.Mode == RestartPlan.AfterUptime)
                next = "Next restart at " + worldStart.AddHours(plan.Hours).ToString("HH:mm") + ", in " + RestartPlan.Span(worldStart.AddHours(plan.Hours) - now) + ".";
            else if (plan.Mode == RestartPlan.Daily)
            {
                var due = now.Date.AddMinutes(plan.DailyMinutes);
                if (now > due.AddHours(1) || worldStart > due.AddMinutes(-plan.WarnMinutes - 1)) due = due.AddDays(1);
                next = "Next restart " + (due.Date == now.Date ? "today" : "tomorrow") + " at " + plan.DailyText + ".";
            }
            else if (plan.Mode == RestartPlan.Memory && worldMemory <= 0)
            {
                next = "Windows does not tell how much memory the worldserver uses, so this rule cannot work here. Choose one of the others.";
                planState.ForeColor = Ui.Warn;
            }
            else if (plan.Mode == RestartPlan.Memory)
            {
                next = over && now - worldStart < TimeSpan.FromMinutes(20)
                    ? "That is over the limit of " + plan.MemoryGb.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " GB already, shortly after the start: the limit is too low for this server and is not applied before the world has run for 20 minutes."
                    : "Restart at " + plan.MemoryGb.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " GB.";
                if (over && now - worldStart < TimeSpan.FromMinutes(20)) planState.ForeColor = Ui.Warn;
            }
            planState.Text = running + next;
        }

        /// <summary>
        /// The restart itself: a clean stop of auth- and worldserver, then the same start as the button.
        /// It runs beside the window instead of locking it, because another window (settings, the
        /// database editor) may be open at that moment.
        /// </summary>
        void DoScheduledRestart()
        {
            autoRestarting = true;
            restartAt = null; restartTold.Clear(); memoryOver = 0;
            planPostpone.Visible = false;
            planState.ForeColor = Ui.Warn;
            planState.Text = "The scheduled restart is running ...";
            Action<string> st = t => { try { BeginInvoke((Action)(() => busy.Text = "Scheduled restart: " + t)); } catch { } };
            RestartLog("Restarting the server.");
            var thread = new System.Threading.Thread(() =>
            {
                string problem = null;
                try
                {
                    ctl.StopAll(st, false);
                    ctl.StartDatabase(st); ctl.StartAuth(st);
                    try { AdminLink.Prepare(inst); } catch { }
                    Process world = ctl.StartWorld(st);
                    st("the worldserver is loading.");
                    var done = new System.Threading.ManualResetEvent(false);
                    ctl.WatchWorld(world, (ready, why) => { if (!ready) problem = why; done.Set(); });
                    done.WaitOne();
                }
                catch (Exception ex) { problem = ex.Message; }
                RestartLog(problem == null ? "The server is running again." : "The restart failed: " + problem.Replace("\r", "").Replace("\n", " | "));
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        autoRestarting = false;
                        string when = DateTime.Now.ToString("HH:mm");
                        lastAutoRestart = problem == null ? "" : "The scheduled restart at " + when + " failed. Details are in logs\\scheduled-restart.log.";
                        busy.Text = problem == null ? "The server was restarted as scheduled at " + when + " and is running again."
                            : "The scheduled restart at " + when + " failed: " + problem.Split('\n')[0];
                        RefreshStatus();
                    }));
                }
                catch { autoRestarting = false; }
            }) { IsBackground = true, Name = "scheduled-restart" };
            thread.Start();
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
            try { realmName.Text = Accounts.GetRealmName(inst); } catch { }
        }
        void ApplyRealmName()
        {
            string n = realmName.Text.Trim();
            string err = Accounts.CheckRealmName(n);
            if (err != null) { Ui.Error(this, err); return; }
            Main.RunBusy(nameNote, st => { EnsureDatabase(st); st("Renaming the server ..."); Accounts.SetRealmName(inst, n); },
                ex =>
                {
                    if (ex != null) { nameNote.Text = ""; Ui.Error(this, ex.Message); return; }
                    nameNote.Text = "The server is now called \"" + n + "\". Restart the auth- and worldserver for it to take effect.";
                    RefreshStatus(); LoadRealm();
                });
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
    class MapDataDialog : AfkForm
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
