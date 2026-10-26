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
    partial class ManagerPage : Page
    {
        readonly Install inst;
        readonly ServerControl ctl;
                readonly Label busy = Ui.Hint("");
        readonly Label mapState = Ui.Hint("");
        readonly Button start = Ui.Primary(L.T("Start server"));
        readonly Button stop = Ui.Secondary(L.T("Stop server"));
        readonly Button restart = Ui.Secondary(L.T("Restart server"));
        readonly TextBox accName = Ui.Input(200), accPw1 = Ui.Input(200, true), accPw2 = Ui.Input(200, true);
        readonly ComboBox accLevel = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, Font = Ui.Base };
        readonly TextBox realm = Ui.Input(200);
        readonly TextBox realmName = Ui.Input(200);
        readonly Label nameNote = Ui.Hint("");
        readonly LinkLabel serverUpdate = UpdateLink(), toolUpdate = UpdateLink();
        readonly LinkLabel clientNews = UpdateLink(), coaNews = UpdateLink();
        Button clientButton, racesButton;
        Panel serverBanner, toolBanner;
        List<string> patchWarnings = new List<string>();
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
                BackColor = Ui.AccentSoft, Padding = new Padding(10, 7, 10, 7), Margin = new Padding(0, 8, 0, 0) };
            p.Controls.Add(link);
            return p;
        }
        readonly System.Windows.Forms.Timer poll = new System.Windows.Forms.Timer { Interval = 2000 };

        // ---- scheduled restart (see RestartPlan)
        RestartPlan plan;
        readonly ComboBox planMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 290, Font = Ui.Base, Margin = new Padding(0, 2, 8, 2) };
        readonly TextBox planValue = Ui.Input(70), planWarn = Ui.Input(44);
        readonly Label planUnit = new Label { AutoSize = true, Font = Ui.Base, ForeColor = Ui.Text, Margin = new Padding(0, 6, 14, 0) };
        readonly Label planWarnLead = new Label { Text = L.T("Tell players"), AutoSize = true, Font = Ui.Base, ForeColor = Ui.Text, Margin = new Padding(0, 6, 6, 0) };
        readonly Label planWarnTail = new Label { Text = L.T("minutes before"), AutoSize = true, Font = Ui.Base, ForeColor = Ui.Text, Margin = new Padding(0, 6, 14, 0) };
        readonly Button planApply = Ui.Secondary(L.T("Apply"));
        readonly Button planPostpone = Ui.Secondary(L.T("Postpone by an hour"));
        readonly Label planState = Ui.Hint("");
        DateTime? restartAt;                    // the announced restart, once players have to be told
        readonly HashSet<int> restartTold = new HashSet<int>();
        DateTime restartSnooze = DateTime.MinValue;
        int memoryOver;                         // status checks in a row with the worldserver over its limit
        bool autoRestarting;
        string lastAutoRestart = "";
        readonly Button consoles = Ui.Secondary(L.T("Open server consoles …"));
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
        /// It reads the game client (and may build the client patch to compare), so it runs in the background; then() gets
        /// what is open.
        void ShowClientState(Action<string> then = null)
        {
            clientState.Text = L.T("Checking the game client …");
            clientState.ForeColor = Ui.Muted;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                string pending = ClientAddons.Pending(inst);
                Action apply = () =>
                {
                    clientState.Text = pending.Length == 0 ? "" : L.T("Not everything the installed modules bring for the game client is in your game yet.");
                    clientState.ForeColor = Ui.Warn;
                    NavItem item;
                    if (nav.TryGetValue("modules", out item)) { item.Badge = pending.Length == 0 ? null : "!"; item.Invalidate(); }
                    if (then != null) then(pending);
                };
                // When AFK Realm starts, the page is shown before its window exists: the answer waits for the window.
                try
                {
                    if (IsHandleCreated) BeginInvoke(apply);
                    else
                    {
                        EventHandler once = null;
                        once = (s, e) => { HandleCreated -= once; BeginInvoke(apply); };
                        HandleCreated += once;
                        if (IsHandleCreated) { HandleCreated -= once; BeginInvoke(apply); }
                    }
                }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            });
        }

        /// <summary>After a module brought, changed or lost an add-on, the game client dialog opens by itself (once for each new state).</summary>
        void OfferClient() { ShowClientState(Offer); }

        void Offer(string pending)
        {
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
                    using (var d = new ClientDialog(inst, L.T("A module brings a new or changed add-on or client patch for the game client (or one is no longer needed).")))
                        d.ShowDialog(this);
                    ShowClientState();
                }
                if (bots.Count > 0) OfferBotReset(bots);
            };
            BeginInvoke(offer);
        }

        // ---- the frame: side bar, status bar and the section shown
        readonly Panel sidebar = new Panel { Dock = DockStyle.Left, Width = 230, BackColor = Ui.Sidebar };
        readonly Panel statusBar = new Panel { Dock = DockStyle.Top, Height = 122, BackColor = Ui.Canvas };
        readonly Panel scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Ui.Canvas };
        readonly FlowLayoutPanel shown = Ui.Column();
        readonly Dictionary<string, List<Control>> sections = new Dictionary<string, List<Control>>();
        readonly Dictionary<string, NavItem> nav = new Dictionary<string, NavItem>();
        readonly StatusPill dbPill = new StatusPill(L.T("Database")), authPill = new StatusPill(L.T("Login (authserver)")), worldPill = new StatusPill(L.T("Game world (worldserver)"));
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
            if (key == "bots") RefreshBots();
        }

        public ManagerPage(MainForm main) : base(main)
        {
            inst = main.Target; ctl = new ServerControl(inst);
            L.AddModules(ModuleCatalog.ModulesDir(inst));   // the texts the modules bring in the language shown
            AutoScroll = false; Padding = new Padding(0); BackColor = Ui.Canvas;
            Body.Visible = false;

            // --- side bar
            AddNav("server", L.T("Server"), "server");
            AddNav("bots", L.T("Bots"), "bots");
            AddNav("settings", L.T("Settings"), "settings");
            AddNav("modules", L.T("Modules"), "modules");
            AddNav("gm", L.T("Game master"), "gamemaster");
            AddNav("accounts", L.T("Accounts"), "accounts");
            AddNav("database", L.T("Database"), "database");
            AddNav("maintenance", L.T("Maintenance"), "maintenance");
            var navList = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Fill, BackColor = Ui.Sidebar, Padding = new Padding(0, 16, 0, 0) };
            foreach (var n in nav.Values) navList.Controls.Add(n);
            var foot = new Label { Dock = DockStyle.Bottom, Height = 54, ForeColor = Color.FromArgb(150, 145, 175), Font = Ui.Small, BackColor = Ui.Sidebar,
                Padding = new Padding(20, 0, 10, 14), TextAlign = ContentAlignment.BottomLeft, Text = Product.Name + " " + Product.Version + "\n" + inst.Root, AutoEllipsis = true };
            serverPick = new ServerPicker(inst);
            serverPick.Click += (s, e) => ServerMenu();
            sidebar.Controls.Add(navList); sidebar.Controls.Add(foot); sidebar.Controls.Add(serverPick);

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
            serverUpdate.LinkClicked += (s, e) => RunEngine("Update", L.T("Install the server update now?") + "\n\n" +
                (patchWarnings.Count > 0 ? "⚠ " + (patchWarnings.Count == 1 ? L.T("Checked beforehand: a patch of a module does not fit the new code:") : L.T("Checked beforehand: some patches of modules do not fit the new code:")) +
                    "\n  " + string.Join("\n  ", patchWarnings) + "\n" +
                    (patchWarnings.Count == 1 ? L.T("You can update anyway - the server then runs without that change - or wait until the module is adapted.")
                        : L.T("You can update anyway - the server then runs without these changes - or wait until the module is adapted.")) + "\n\n" : "") +
                (serverNews.Count > 0 ? L.T("New:") + "\n  " + string.Join("\n  ", serverNews) + "\n" +
                    (serverNewsLimited != null ? L.F("(GitHub counts the changes again from {0})", serverNewsLimited.Value.ToString("HH:mm")) + "\n" : "") + "\n" : "") +
                L.T("The current server is backed up first, then rebuilt with the newest code, which can take a while. A running server is stopped cleanly first."));
            toolUpdate.LinkClicked += (s, e) => UpdateTool();

            shown.Location = new Point(28, 24); shown.BackColor = Ui.Canvas; shown.Margin = new Padding(0, 0, 0, 30);
            scroller.Controls.Add(shown);
            Controls.Add(scroller); Controls.Add(statusBar); Controls.Add(sidebar);

            // --- Server
            var restartCard = Section("server", L.T("Scheduled restart"), L.F("A world that has run for many hours can grow slow and use more and more memory; a restart gives it back. {0} can do that on its own: players are told in the game beforehand, then the server is stopped cleanly and started again. This works while the server runs and {0} stays open.", Product.Name), "server");
            planMode.Items.AddRange(new object[] { L.T("Off"), L.T("After the server has run for"), L.T("Every day at"), L.T("When the worldserver uses more than") });
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

            var mapCard = Section("server", L.T("Map data"), L.T("The worldserver needs the maps, vmaps and mmaps of the game and CoA's own DBC tables. They are made once from your game client."), "globe");
            mapCard.Add(mapState);
            var mapBtn = mapCard.Add(Ui.Secondary(L.T("Create map data from the game client …")));
            mapBtn.Click += (s, e) => { using (var d = new MapDataDialog(inst)) d.ShowDialog(this); };

            // --- Settings
            var settingsCard = Section("settings", L.T("Server settings"), L.T("XP and drop rates, Playerbots, convenience options and every other setting of the worldserver and its modules, each with its description."), "settings");
            var settingsBtn = settingsCard.Add(Ui.Primary(L.T("Open server settings …")));
            settingsBtn.Click += (s, e) =>
            {
                if (!File.Exists(inst.WorldConf)) { Ui.Error(this, L.T("worldserver.conf was not found. Run \"Repair setup\" first.")); return; }
                using (var d = new SettingsDialog(inst, ctl.World != null)) d.ShowDialog(this);
            };

            var nameCard = Section("settings", L.T("Server name"), L.T("The name players see in the realm list when they log in. The game client keeps each character's interface settings in a folder named after the server (WTF\\Account\\<account>\\<server name>), so after renaming it starts with fresh ones unless you rename that folder too."), "server");
            var nameRow = Ui.Row(); nameRow.Controls.Add(Ui.FieldLabel(L.T("Server name"))); nameRow.Controls.Add(realmName);
            var rename = Ui.Secondary(L.T("Rename")); nameRow.Controls.Add(rename);
            rename.Click += (s, e) => ApplyRealmName();
            realmName.MaxLength = 32;
            nameCard.Add(nameRow);
            nameCard.Add(nameNote);

            var realmCard = Section("settings", L.T("Play with others"), L.F("Enter the address other players use to reach your PC, for example your Radmin VPN or Hamachi IP (26.x.x.x / 25.x.x.x) or your LAN IP. {0} then adjusts everything needed and allows the servers through the Windows Firewall. You keep playing via 127.0.0.1 yourself. Other players put this address into their realmlist.wtf.", Product.Name), "globe");
            var realmRow = Ui.Row(); realmRow.Controls.Add(Ui.FieldLabel(L.T("Realm address"))); realmRow.Controls.Add(realm);
            var apply = Ui.Secondary(L.T("Apply")); realmRow.Controls.Add(apply);
            apply.Click += (s, e) => ApplyRealm();
            realmCard.Add(realmRow);

            // --- Modules
            var modulesCard = Section("modules", L.T("Manage modules"), L.T("Add or remove modules: those made for this server and the AzerothCore module catalog. The server is backed up and rebuilt; database changes of modules installed here are recorded, so removing a module undoes them."), "modules");
            var modulesBtn = modulesCard.Add(Ui.Primary(L.T("Manage modules …")));
            modulesBtn.Click += (s, e) => ManageModules();

            journeyCard = Section("modules", L.T("World Journey"), L.T("The old world, Outland and Northrend as one journey from 1 to 60: where each part begins, how the level window works, how hard creatures are and what follows the journey."), "journey");
            journeyCard.Stripe = Ui.Accent;
            var journeyBtn = journeyCard.Add(Ui.Primary(L.T("Set up World Journey …")));
            journeyBtn.Click += (s, e) => OpenSetup(new JourneySetup(inst));

            auctionCard = Section("modules", L.T("Auction house bots"), L.T("How the bots of mod-playerbots-auctions trade: how often they come, what they sell and buy, their prices, crafting, deals by chat, the Trading Post and their chatter."), "auction");
            auctionCard.Stripe = Ui.Accent;
            var auctionBtn = auctionCard.Add(Ui.Primary(L.T("Set up the auction house bots …")));
            auctionBtn.Click += (s, e) => OpenSetup(new AuctionSetup(inst));

            classicCard = Section("modules", L.T("Classic classes"), L.T("Which classic classes - Warrior to Druid - can be played next to the classes of Conquest of Azeroth, by players and bots."), "gamemaster");
            classicCard.Stripe = Ui.Accent;
            var classicBtn = classicCard.Add(Ui.Primary(L.T("Set up the classic classes …")));
            classicBtn.Click += (s, e) => OpenSetup(new ClassicSetup(inst));

            racesCard = Section("modules", L.T("Custom races"), L.T("Which races of the game client - Goblin, Worgen, Pandaren, Vrykul and more - can be played, and the stock race each leans on: where it starts, its quests, racial abilities and faction."), "accounts");
            racesCard.Stripe = Ui.Accent;
            coaNews.Visible = false; coaNews.Margin = new Padding(0, 2, 0, 6);
            coaNews.LinkClicked += (s, e) => OpenRaces();
            racesCard.Add(coaNews);
            racesButton = racesCard.Add(Ui.Primary(L.T("Set up the races …")));
            racesButton.Click += (s, e) => OpenRaces();

            flyingCard = Section("modules", L.T("Azeroth flying"), L.T("Optional flying mounts in Eastern Kingdoms and Kalimdor: off by default, with a configurable no-fly list for the old capitals. Old Azeroth was not built for flying; use it at your own risk."), "journey");
            flyingCard.Stripe = Ui.Accent;
            var flyingBtn = flyingCard.Add(Ui.Primary(L.T("Set up Azeroth flying …")));
            flyingBtn.Click += (s, e) => OpenSetup(new AzerothFlyingSetup(inst));

            immersionCard = Section("modules", L.T("Immersion Life"), L.T("The population of cities and towns, questing, legendary residents, PvP Life and Fishing Life for mod-playerbots-immersion. PvP Life and Fishing Life are off by default; the legendary residents are on."), "accounts");
            immersionCard.Stripe = Ui.Accent;
            var immersionBtn = immersionCard.Add(Ui.Primary(L.T("Set up Immersion Life ...")));
            immersionBtn.Click += (s, e) => OpenSetup(new ImmersionSetup(inst));

            var clientCard = Section("modules", L.T("Game client"), L.F("Some modules bring an add-on or a patch for the game client (for example World Journey's map levels and tooltips, or the class choice of the classic classes). {0} puts them into your game and clears its cache when a module asks for it; for other players it packs them into a ZIP.", Product.Name), "client");
            clientCard.Add(clientState);
            clientNews.Visible = false; clientNews.Margin = new Padding(0, 2, 0, 6);
            clientNews.LinkClicked += (s, e) => OpenClient();
            clientCard.Add(clientNews);
            clientButton = clientCard.Add(Ui.Secondary(L.T("Game client …")));
            clientButton.Click += (s, e) => OpenClient();

            // --- Game master
            BuildBotsSection();

            var gmCard = Section("gm", L.T("Game master tools"), L.T("Help players on the running server: give, complete and reward quests (found by NPC name or around a character), unstuck, revive, level, gold and mail, announcements, and a console for every other GM command."), "gamemaster");
            var gmBtn = gmCard.Add(Ui.Primary(L.T("Open game master tools …")));
            gmBtn.Click += (s, e) =>
            {
                if (ctl.Db == null) { Ui.Error(this, L.T("The database is not running. Start the server first.")); return; }
                using (var d = new GameMasterDialog(inst, ctl)) d.ShowDialog(this);
            };

            // --- Accounts
            var accCard = Section("accounts", L.T("Create account"), L.T("A new login for the game, with the access level you choose."), "accounts");
            accLevel.Items.AddRange(new object[] { L.T("Player"), L.T("Moderator (GM 1)"), L.T("Game Master (GM 2)"), L.T("Administrator (GM 3)") });
            accLevel.SelectedIndex = 0;
            accCard.Add(Field(L.T("Account name"), accName));
            accCard.Add(Field(L.T("Password"), accPw1));
            accCard.Add(Field(L.T("Repeat password"), accPw2));
            accCard.Add(Field(L.T("Access level"), accLevel));
            var create = accCard.Add(Ui.Primary(L.T("Create account")));
            create.Click += (s, e) => CreateAccount();
            var manageCard = Section("accounts", L.T("Player accounts"), L.T("The accounts of real players with their characters: a new password, the access level, deleting, and moving an account to another server (export and import)."), "accounts");
            var manage = manageCard.Add(Ui.Secondary(L.T("Manage player accounts …")));
            manage.Click += (s, e) => { using (var d = new AccountsDialog(inst, ctl)) d.ShowDialog(this); };

            // --- Database
            var dbCard = Section("database", L.T("Database editor"), L.T("Look into the server's tables, change values and run SQL without a separate program. Read-only until you allow changes; meant for people who know what the tables are for - a wrong change can break the server, so back it up first."), "database");
            var dbBtn = dbCard.Add(Ui.Primary(L.T("Open the database editor …")));
            dbBtn.Click += (s, e) =>
            {
                if (ctl.Db == null) { Ui.Error(this, L.T("The database is not running. Start the server first.")); return; }
                using (var d = new DatabaseDialog(inst, ctl))
                {
                    d.ShowDialog(this);
                    if (d.BackUpNow) RunEngine("Backup", null, false);
                }
            };
            var backupCard = Section("database", L.T("Backups"), L.F("Before every update and module change {0} saves the whole server: programs, settings, all databases. Restore one when something went wrong.", Product.Name), "database");
            var backups = backupCard.Add(Ui.Secondary(L.T("Backups …")));
            backups.Click += (s, e) =>
            {
                using (var d = new BackupsDialog(inst))
                {
                    d.ShowDialog(this);
                    if (d.BackUpNow) RunEngine("Backup", null, false);
                    else if (d.RestoreName != null) RunEngine("Restore", null, true, "-Snapshot \"" + d.RestoreName + "\"");
                    else if (d.Parts != null) RunEngine("RestoreParts", null, true, "-Snapshot \"" + d.PartsFolder.TrimEnd('\\') + "\" -Parts \"" + d.Parts + "\"");
                }
            };

            // --- Maintenance
            var updCard = Section("maintenance", L.T("Updates"), L.T("Checks the CoA core, Playerbots and your modules for new versions; the server is backed up first and rebuilt only when something changed."), "maintenance");
            var upd = updCard.Add(Ui.Primary(L.T("Check for updates and install")));
            upd.Click += (s, e) => RunEngine("Update", L.T("Check for updates?\n\nIf there are new versions of CoA or Playerbots, the current server is backed up first and then recompiled, which can take a while. A running server is stopped cleanly first."));
            var repairCard = Section("maintenance", L.T("Repair and reset"), L.T("\"Repair setup\" sets up the database and configuration again without recompiling; characters and accounts are kept. \"Reset random bots\" deletes every random bot and lets the server create new ones."), "maintenance");
            var r = Ui.Row();
            var repair = Ui.Secondary(L.T("Repair setup"));
            repair.Click += (s, e) => RunEngine("Setup", L.T("Set up the database and configuration again (without recompiling)?\n\nCharacters and accounts are kept."));
            var botReset = Ui.Secondary(L.T("Reset random bots …"));
            botReset.Click += (s, e) => ResetBots();
            r.Controls.Add(repair); r.Controls.Add(botReset);
            repairCard.Add(r);
            var filesCard = Section("maintenance", L.T("Files"), L.T("The server's folder and its logs, or another server."), "server");
            var r2 = Ui.Row();
            var openDir = Ui.Secondary(L.T("Open folder")); openDir.Click += (s, e) => Process.Start("explorer.exe", "\"" + inst.Root + "\"");
            var openLogs = Ui.Secondary(L.T("Open server logs")); openLogs.Click += (s, e) => Process.Start("explorer.exe", "\"" + inst.ServerDir + "\"");
            var other = Ui.Secondary(L.T("Other server / new install")); other.Click += (s, e) => Main.Navigate(new WelcomePage(Main), true);
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

        Card journeyCard, auctionCard, classicCard, racesCard, flyingCard, immersionCard;
        ServerPicker serverPick;

        /// <summary>The servers AFK Realm knows, to switch between them, and the ways to add one.</summary>
        void ServerMenu()
        {
            var menu = new ContextMenuStrip { Font = Ui.Base, ShowImageMargin = false };
            foreach (var k in Settings.KnownServers)
            {
                var other = new Install(k.Root);
                bool here = k.Root.Equals(inst.Root, StringComparison.OrdinalIgnoreCase);
                var item = new ToolStripMenuItem((here ? "✓  " : "     ") + ServerPicker.NameOf(k) + "   —   " + k.Root) { Enabled = !here && other.IsInstalled };
                if (!other.IsInstalled && !here) item.Text += "   " + L.T("(not found)");
                string root = k.Root;
                item.Click += (s, e) => SwitchTo(root);
                menu.Items.Add(item);
            }
            menu.Items.Add(new ToolStripSeparator());
            var add = new ToolStripMenuItem(L.T("Add an existing server …"));
            add.Click += (s, e) =>
            {
                using (var d = new FolderBrowserDialog { Description = L.T("Choose the server folder (the one containing \"Server\", \"DB\" and \"Dependencies\").") })
                {
                    if (d.ShowDialog(this) != DialogResult.OK) return;
                    if (!new Install(d.SelectedPath).IsInstalled) { Ui.Error(this, L.T("No finished server was found in this folder.")); return; }
                    Settings.Remember(new Install(d.SelectedPath).Root, null);
                    SwitchTo(new Install(d.SelectedPath).Root);
                }
            };
            var fresh = new ToolStripMenuItem(L.T("Install a new server …"));
            fresh.Click += (s, e) => Main.Navigate(new FolderPage(Main), true);
            var forget = new ToolStripMenuItem(L.T("Remove from this list"));
            foreach (var k in Settings.KnownServers.Where(k => !k.Root.Equals(inst.Root, StringComparison.OrdinalIgnoreCase)))
            {
                string root = k.Root;
                var f = new ToolStripMenuItem(ServerPicker.NameOf(k) + "   —   " + k.Root);
                f.Click += (s, e) => Settings.Forget(root);
                forget.DropDownItems.Add(f);
            }
            forget.Enabled = forget.DropDownItems.Count > 0;
            menu.Items.Add(add); menu.Items.Add(fresh); menu.Items.Add(forget);
            menu.Show(serverPick, new Point(0, serverPick.Height));
        }

        void SwitchTo(string root)
        {
            var target = new Install(root);
            if (!target.IsInstalled) { Ui.Error(this, L.F("No finished server was found in {0}.", root)); return; }
            if (ctl.World != null || ctl.Auth != null)
            {
                if (!Ui.Confirm(this, L.F("This server is running and keeps running. {0} then looks after the other server: the scheduled restart of this one " +
                    "pauses until you switch back.", Product.Name) + "\n\n" + L.T("Two servers cannot run at the same time on the same ports: stop this one before you start the other.") + "\n\n" + L.T("Switch anyway?"))) return;
            }
            Main.Target = target;
            Settings.LastInstall = target.Root;
            Main.Navigate(new ManagerPage(Main), true);
        }

        /// <summary>The setup pages of the modules made for AFK Realm are only there when the module is installed.</summary>
        void ShowModuleCards()
        {
            string dir = ModuleCatalog.ModulesDir(inst);
            journeyCard.Tag = Directory.Exists(Path.Combine(dir, "mod-world-journey")) ? null : "hidden";
            auctionCard.Tag = Directory.Exists(Path.Combine(dir, "mod-playerbots-auctions")) ? null : "hidden";
            classicCard.Tag = Directory.Exists(Path.Combine(dir, "mod-classic-classes")) ? null : "hidden";
            racesCard.Tag = Directory.Exists(Path.Combine(dir, "mod-custom-races")) ? null : "hidden";
            flyingCard.Tag = Directory.Exists(Path.Combine(dir, "mod-azeroth-flying")) ? null : "hidden";
            immersionCard.Tag = Directory.Exists(Path.Combine(dir, "mod-playerbots-immersion")) ? null : "hidden";
            if (current == "modules") Show("modules");
        }

        void OpenClient()
        {
            using (var d = new ClientDialog(inst)) d.ShowDialog(this);
            ShowClientState();
            CheckDownloadUpdates();
        }

        void OpenRaces()
        {
            Busy.Run(this, L.T("Reading the races of your game client and your race folder …"), report => RacesSetup.Preload(inst));
            var before = PlayableCustomRaces();
            OpenSetup(new RacesSetup(inst));
            OfferBotsForRaces(before);
            CheckDownloadUpdates();
        }

        void OpenSetup(Form setup)
        {
            bool races = setup is RacesSetup;
            using (setup)
                if (setup.ShowDialog(this) == DialogResult.OK)
                    ShowClientState(pending => AfterSetup(pending, races));
        }

        string RaceFolderForUpdate()
        {
            string configured = ConfigOption.Unquote(ClientPatch.ConfValue(inst, "mod_custom_races.conf", "CustomRaces.ImportFolder") ?? "");
            return configured.Length > 0 ? configured :
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "AFK-Rassen");
        }

        /// <summary>Background notices for the game client and the CoA Custom package, next to their cards and on
        /// their buttons. They only say that something is newer; the actual download still asks first.</summary>
        void CheckDownloadUpdates()
        {
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                string clientNewsText = null, coaNewsText = null;
                string clientVersion = null, installedClient = null, coaVersion = null, installedCoa = null;
                try
                {
                    string client = ClientAddons.ClientFolder;
                    if (client != null && ClientAddons.IsClientFolder(client))
                    {
                        var manifest = ClientDownload.Latest();
                        clientVersion = manifest.Version;
                        installedClient = ClientDownload.InstalledVersion(client);
                        if (installedClient != clientVersion)
                            clientNewsText = installedClient == null
                                ? L.F("Game client update available: {0} — click to update or repair", clientVersion)
                                : L.F("Game client update available: {0} (installed {1}) — click to update", clientVersion, installedClient);
                    }
                }
                catch { }
                try
                {
                    var release = CoaCustom.Latest();
                    coaVersion = release.Version;
                    installedCoa = CoaCustom.Installed(RaceFolderForUpdate());
                    if (installedCoa != coaVersion)
                        coaNewsText = installedCoa == null
                            ? L.F("CoA Custom {0} can be downloaded — click to open", coaVersion)
                            : L.F("CoA Custom update available: {0} (installed {1}) — click to update", coaVersion, installedCoa);
                }
                catch { }
                Action show = () =>
                {
                    if (IsDisposed) return;
                    clientNews.Text = clientNewsText ?? "";
                    clientNews.Visible = clientNewsText != null;
                    coaNews.Text = coaNewsText ?? "";
                    coaNews.Visible = coaNewsText != null;
                    clientButton.Text = installedClient == null
                        ? L.T("Download / update game client …")
                        : installedClient != clientVersion ? L.F("Update game client to {0} …", clientVersion)
                        : L.T("Game client …");
                    racesButton.Text = installedCoa == null
                        ? L.F("Download CoA Custom {0} …", coaVersion ?? "")
                        : installedCoa != coaVersion ? L.F("Update custom races to {0} …", coaVersion)
                        : L.T("Set up the races …");
                };
                try { BeginInvoke(show); }
                catch { try { show(); } catch { } }
            });
        }

        /// <summary>
        /// After a setup page was saved: when the game client needs the new state (new races, other classes, the map levels),
        /// the game client window opens right away and says what to do - every time, not only once like the offer at the start.
        /// </summary>
        void AfterSetup(string pending, bool races)
        {
            if (pending.Length == 0) return;
            try { File.WriteAllText(Path.Combine(inst.Root, "Dependencies", "client-offered.txt"), pending); } catch { }
            bool running = ctl.World != null;
            string why = (races ? L.T("Your races are saved, but the game does not show them yet.") : L.T("Your change is saved, but the game client does not have it yet.")) + "\n" +
                L.T("1. Click \"Install\" below: it builds the client patch into your game.") + "\n" +
                (running ? L.T("2. Restart the server so it reads the new settings.") : L.T("2. Start the server.")) + "\n" +
                L.T("3. Give the other players the new ZIP (\"Export as ZIP …\" below): without it they do not see the change.");
            using (var d = new ClientDialog(inst, why)) d.ShowDialog(this);
            ShowClientState();
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
            if (SelfUpdate.JustUpdated) { SelfUpdate.JustUpdated = false; busy.Text = L.F("{0} was updated to version {1}.", Product.Name, Product.Version); }
            CheckForUpdates();
            CheckDownloadUpdates();
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
            var parts = news.ConvertAll(n => n.Split(':', '：')[0]);
            if (parts.Count <= 3) return string.Join(", ", parts);
            int modules = parts.Count - 2;
            return L.F("{0}, {1} and {2} more", parts[0], parts[1], modules);
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
                            serverNews = r.Server; serverNewsLimited = r.LimitedUntil; patchWarnings = r.PatchWarnings;
                            serverUpdate.Text = L.F("Server update available: {0}  –  click to install", NewsSummary(r.Server)) +
                                (r.PatchWarnings.Count > 0 ? "  ·  ⚠ " + (r.PatchWarnings.Count == 1 ? L.T("1 module patch would not fit") : L.F("{0} module patches would not fit", r.PatchWarnings.Count)) : "");
                            bannerTip.SetToolTip(serverUpdate, string.Join("\n", r.Server) +
                                (r.PatchWarnings.Count > 0 ? "\n\n" + string.Join("\n", r.PatchWarnings) : "") +
                                (r.LimitedUntil != null ? "\n" + L.F("(GitHub counts the changes again from {0})", r.LimitedUntil.Value.ToString("HH:mm")) : ""));
                            serverBanner.Visible = true;
                            nav["maintenance"].Badge = "new"; nav["maintenance"].Invalidate();
                        }
                        if (r.ToolVersion != null)
                        {
                            toolUrl = r.ToolUrl; toolVersion = r.ToolVersion; toolDownload = r.ToolDownload; toolSha256 = r.ToolSha256;
                            toolUpdate.Text = toolDownload != null ? L.F("New: {0} {1} is available  –  click to update", Product.Name, r.ToolVersion) : L.F("New: {0} {1} is available  –  click to download", Product.Name, r.ToolVersion);
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
            if (!Ui.Confirm(this, L.F("Update {0} to version {1} now?", Product.Name, toolVersion) + "\n\n" +
                L.F("The new version is downloaded from GitHub, then {0} restarts. A running server keeps running.", Product.Name))) return;
            string copy = Path.Combine(inst.Root, "Builder", Product.FileStem + ".exe");
            Main.RunBusy(busy, st => { st(L.T("Downloading the update ...")); SelfUpdate.Download(toolDownload, toolSha256, st); st(L.T("Installing the update ...")); SelfUpdate.Replace(copy); },
                ex =>
                {
                    if (ex == null) { SelfUpdate.Restart(); return; }
                    busy.Text = "";
                    if (Ui.Confirm(this, L.T("The update could not be installed:") + "\n\n" + ex.Message + "\n\n" + L.T("Open the download page instead?"))) Process.Start(toolUrl);
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
                        uptime.Text = worldReady && worldStart != DateTime.MinValue ? L.F("Running for {0}", RestartPlan.Span(DateTime.Now - worldStart)) + (worldMemory > 0 ? "  ·  " + L.F("{0} GB of memory", (worldMemory / 1073741824.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)) : "") : "";
                        start.Enabled = !(auth && world);
                        stop.Enabled = db || auth || world;
                        restart.Enabled = auth || world;
                        Schedule(worldReady, worldStart, worldMemory);
                        mapState.ForeColor = maps && dbc ? Ui.Ok : Ui.Warn;
                        mapState.Text = !maps ? L.T("Map data is still missing. The worldserver cannot run without it.")
                            : !dbc ? L.T("The CoA DBC tables are missing. Use \"Create map data\" and tick \"Only refresh the CoA DBC tables\".")
                            : L.T("Map data is present.");
                    }));
                }
                catch { polling = false; }
            });
        }
        static void ShowState(StatusPill p, bool running, bool ready)
        {
            p.Set(!running ? L.T("stopped") : ready ? L.T("running") : L.T("loading …"), !running ? Ui.Muted : ready ? Ui.Ok : Ui.Warn);
        }

        void StartServer()
        {
            if (RestartInProgress()) return;
            if (!inst.HasMapData && !Ui.Confirm(this, L.T("Map data is missing. The worldserver will not run properly without it.\n\nStart anyway?"))) return;
            if (inst.HasMapData && !inst.HasClientDbc)
            { Ui.Error(this, L.T("The CoA DBC tables are missing, so the worldserver would stop right away.\n\nOpen \"Create map data\", choose your CoA game folder and tick \"Only refresh the CoA DBC tables\".")); return; }
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
                    busy.Text = L.F("The worldserver is loading. You can keep using {0} meanwhile.", Product.Name);
                    ctl.WatchWorld(world, (ready, problem) =>
                    {
                        try
                        {
                            BeginInvoke((Action)(() =>
                            {
                                RefreshStatus();
                                if (ready) busy.Text = L.T("The server is running. You can log in now.");
                                else { busy.Text = ""; Ui.Error(this, problem); }
                            }));
                        }
                        catch { }
                    });
                });
        }
        /// <summary>A module that changes what the random bots are made of asks once, after it came, for new bots.</summary>
        /// <summary>The races mod-custom-races makes playable now (its settings), by key.</summary>
        HashSet<string> PlayableCustomRaces()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string enable = ClientPatch.ConfValue(inst, "mod_custom_races.conf", "CustomRaces.Enable") ?? "1";
            if (enable.Trim() == "0") return set;
            foreach (var key in (ClientPatch.ConfValue(inst, "mod_custom_races.conf", "CustomRaces.Races") ?? "").Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries))
                set.Add(key);
            return set;
        }

        /// <summary>Races just added on the races page: the random bots keep the races they have, so new races only
        /// come with new bots. Offers to make the random bots anew.</summary>
        void OfferBotsForRaces(HashSet<string> before)
        {
            var added = PlayableCustomRaces().Where(r => !before.Contains(r)).ToList();
            if (added.Count == 0 || !File.Exists(Path.Combine(inst.ConfigDir, "modules", "playerbots.conf"))) return;
            var names = added.Select(RaceName).ToList();
            ResetBots((added.Count == 1 ? L.F("A new race is playable: {0}.", names[0]) : L.F("New races are playable: {0}.", string.Join(", ", names))) + "\n\n" +
                (added.Count == 1 ? L.T("The random bots keep the races they were made with; only new bots can be of the new race. To see them among the bots soon, make the random bots anew.")
                    : L.T("The random bots keep the races they were made with; only new bots can be of the new races. To see them among the bots soon, make the random bots anew.")) + "\n\n");
        }

        void OfferBotReset(List<KeyValuePair<string, string>> modules)
        {
            ModuleBots.MarkOffered(inst, modules.Select(m => m.Key));
            if (!File.Exists(Path.Combine(inst.ConfigDir, "modules", "playerbots.conf"))) return;
            string why = string.Join("\n", modules.Select(m => "• " + m.Key + (m.Value.Length > 0 ? ": " + m.Value : "")));
            ResetBots(L.T("A new module changes what the random bots are made of:") + "\n\n" + why + "\n\n");
        }

        void ResetBots(string reason = null)
        {
            if (!Ui.Confirm(this, (reason ?? "") + L.T("Delete all random bots and create new ones?") + "\n\n" +
                L.T("This removes every random bot account with its characters, guilds, arena teams and mail. " +
                "Your own accounts and characters are kept, including bots you created on your own accounts.") + "\n\n" +
                L.T("The server is stopped first. Deleting can take a while; the new bots are created at the next server start.") +
                (reason != null ? "\n\n" + L.T("Not now? \"Reset random bots …\" under Maintenance does the same later.") : ""))) return;
            Main.RunBusy(busy, st => ctl.ResetRandomBots(st),
                err =>
                {
                    RefreshStatus();
                    if (err != null) { busy.Text = ""; Ui.Error(this, err.Message); return; }
                    busy.Text = L.T("All random bots were deleted.");
                    if (Ui.Confirm(this, L.T("All random bots were deleted.\n\nStart the server now? The new bots are created while it starts, which takes a bit longer than usual.")))
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
            if (!Ui.Confirm(this, L.T("Stop the server now? All players are saved and logged out."))) return;
            Main.RunBusy(busy, st => { ctl.StopAll(st, true); st(L.T("Server is stopped.")); },
                err => { if (err != null) { busy.Text = ""; Ui.Error(this, err.Message); } RefreshStatus(); });
        }

        /// <summary>Stops auth- and worldserver cleanly and starts them again; the database keeps running.</summary>
        void RestartServer()
        {
            if (RestartInProgress()) return;
            if (!Ui.Confirm(this, L.T("Restart the server now? All players are saved and logged out; they can log in again once the world has loaded."))) return;
            Main.RunBusy(busy, st => { ctl.StopAll(st, false); st(L.T("Server is stopped. Starting it again ...")); },
                err =>
                {
                    RefreshStatus();
                    if (err != null) { busy.Text = ""; Ui.Error(this, err.Message); return; }
                    StartServer();
                });
        }

        // ---- scheduled restart -----------------------------------------------------------------

        /// <summary>The name of a race of mod-custom-races (its key, "ZandalariTroll"), as the game says it, in the language shown.</summary>
        static string RaceName(string key) { return L.Outside(System.Text.RegularExpressions.Regex.Replace(key, "(?<=[a-z])(?=[A-Z])", " ")); }

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
            planUnit.Text = mode == RestartPlan.AfterUptime ? L.T("hours") : mode == RestartPlan.Daily ? L.T("(24-hour clock)") : mode == RestartPlan.Memory ? L.T("GB of memory") : "";
            planWarn.Text = plan.WarnMinutes.ToString();
            planApply.Margin = new Padding(8, 0, 0, 0);
        }

        void ApplyPlan()
        {
            int mode = planMode.SelectedIndex;
            double hours = plan.Hours, gb = plan.MemoryGb; int daily = plan.DailyMinutes, warn = plan.WarnMinutes;
            if (mode == RestartPlan.AfterUptime && !RestartPlan.ParseHours(planValue.Text, out hours))
            { Ui.Error(this, L.T("Enter after how many hours the server is restarted, for example 6 or 5.5 (at least half an hour).")); return; }
            if (mode == RestartPlan.Daily && !RestartPlan.ParseTime(planValue.Text, out daily))
            { Ui.Error(this, L.T("Enter the time of day on the 24-hour clock, for example 05:00 or 17:30.")); return; }
            if (mode == RestartPlan.Memory && !RestartPlan.ParseGb(planValue.Text, out gb))
            { Ui.Error(this, L.T("Enter the memory limit in GB, for example 8 (at least 1).")); return; }
            if (mode != RestartPlan.Off && (!int.TryParse(planWarn.Text.Trim(), out warn) || warn < 0 || warn > 60))
            { Ui.Error(this, L.T("Enter how many minutes before the restart players are told: 0 to 60.")); return; }
            bool announced = restartAt != null && restartTold.Count > 0;
            plan.Mode = mode; plan.Hours = hours; plan.DailyMinutes = daily; plan.MemoryGb = gb; plan.WarnMinutes = warn;
            try { plan.Save(inst); } catch (Exception ex) { Ui.Error(this, L.T("The setting could not be saved:") + "\n" + ex.Message); return; }
            restartAt = null; restartTold.Clear(); restartSnooze = DateTime.MinValue; memoryOver = 0;
            if (announced) Announce("The announced restart is off.", false);      // in the game: in English, like the game client
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
            Ui.Error(this, L.T("The scheduled restart is running right now. Wait until the server is up again."));
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

            string running = (worldMemory > 0 ? L.F("Worldserver: running for {0}, {1} GB of memory.", RestartPlan.Span(now - worldStart), gb.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))
                : L.F("Worldserver: running for {0}.", RestartPlan.Span(now - worldStart))) + " ";
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
                planState.Text = running + (restartTold.Count > 0 ? L.F("Restart in {0} - players have been told.", RestartPlan.Span(left)) : L.F("Restart in {0}.", RestartPlan.Span(left)));
                return;
            }

            planPostpone.Visible = false;
            planState.ForeColor = Ui.Muted;
            string next = "";
            if (now < restartSnooze) next = L.F("The restart is postponed until {0}.", restartSnooze.ToString("HH:mm"));
            else if (plan.Mode == RestartPlan.AfterUptime)
                next = L.F("Next restart at {0}, in {1}.", worldStart.AddHours(plan.Hours).ToString("HH:mm"), RestartPlan.Span(worldStart.AddHours(plan.Hours) - now));
            else if (plan.Mode == RestartPlan.Daily)
            {
                var due = now.Date.AddMinutes(plan.DailyMinutes);
                if (now > due.AddHours(1) || worldStart > due.AddMinutes(-plan.WarnMinutes - 1)) due = due.AddDays(1);
                next = due.Date == now.Date ? L.F("Next restart today at {0}.", plan.DailyText) : L.F("Next restart tomorrow at {0}.", plan.DailyText);
            }
            else if (plan.Mode == RestartPlan.Memory && worldMemory <= 0)
            {
                next = L.T("Windows does not tell how much memory the worldserver uses, so this rule cannot work here. Choose one of the others.");
                planState.ForeColor = Ui.Warn;
            }
            else if (plan.Mode == RestartPlan.Memory)
            {
                next = over && now - worldStart < TimeSpan.FromMinutes(20)
                    ? L.F("That is over the limit of {0} GB already, shortly after the start: the limit is too low for this server and is not applied before the world has run for 20 minutes.", plan.MemoryGb.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))
                    : L.F("Restart at {0} GB.", plan.MemoryGb.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
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
            planState.Text = L.T("The scheduled restart is running ...");
            Action<string> st = t => { try { BeginInvoke((Action)(() => busy.Text = L.T("Scheduled restart:") + " " + t)); } catch { } };
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
                    st(L.T("the worldserver is loading."));
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
                        lastAutoRestart = problem == null ? "" : L.F("The scheduled restart at {0} failed. Details are in logs\\scheduled-restart.log.", when);
                        busy.Text = problem == null ? L.F("The server was restarted as scheduled at {0} and is running again.", when)
                            : L.F("The scheduled restart at {0} failed:", when) + " " + problem.Split('\n')[0];
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
            Main.RunBusy(busy, st => { EnsureDatabase(st); st(L.T("Creating account ...")); Accounts.Create(inst, name, pw, level); },
                ex =>
                {
                    if (ex != null) { busy.Text = ""; Ui.Error(this, L.T("The account could not be created:") + "\n" + ex.Message); return; }
                    busy.Text = L.F("Account \"{0}\" was created.", name.ToUpperInvariant());
                    accPw1.Text = accPw2.Text = "";
                    RefreshStatus();
                });
        }

        void LoadRealm()
        {
            if (ctl.Db == null) { realm.Text = realm.Text.Length > 0 ? realm.Text : ""; return; }
            try { realm.Text = Accounts.GetRealmAddress(inst); } catch { }
            try { realmName.Text = Accounts.GetRealmName(inst); Settings.Remember(inst.Root, realmName.Text); serverPick.Invalidate(); } catch { }
        }
        void ApplyRealmName()
        {
            string n = realmName.Text.Trim();
            string err = Accounts.CheckRealmName(n);
            if (err != null) { Ui.Error(this, err); return; }
            Main.RunBusy(nameNote, st => { EnsureDatabase(st); st(L.T("Renaming the server ...")); Accounts.SetRealmName(inst, n); },
                ex =>
                {
                    if (ex != null) { nameNote.Text = ""; Ui.Error(this, ex.Message); return; }
                    nameNote.Text = L.F("The server is now called \"{0}\". Restart the auth- and worldserver for it to take effect.", n);
                    RefreshStatus(); LoadRealm();
                });
        }
        void ApplyRealm()
        {
            string a = realm.Text.Trim();
            System.Net.IPAddress ip;
            if (!System.Net.IPAddress.TryParse(a, out ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            { Ui.Error(this, L.T("Please enter an IPv4 address, for example 26.66.211.210 or 192.168.1.20.")); return; }
            Main.RunBusy(busy, st => { EnsureDatabase(st); st(L.T("Setting the realm address ...")); Accounts.SetRealmAddress(inst, a); },
                ex =>
                {
                    if (ex != null) { busy.Text = ""; Ui.Error(this, ex.Message); return; }
                    busy.Text = L.F("Realm address set: {0}. Restart the auth- and worldserver for it to take effect.", a);
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
        readonly CheckBox mmaps = new CheckBox { Text = L.T("Also create mmaps (pathfinding for bots and creatures, highly recommended; may take a long time)"), Checked = true, AutoSize = true, MaximumSize = new Size(560, 0) };
        readonly CheckBox dbcOnly = new CheckBox { Text = L.T("Only refresh the CoA DBC tables (for a server that already has maps)"), Checked = false, AutoSize = true, MaximumSize = new Size(560, 0) };

        public MapDataDialog(Install i)
        {
            inst = i;
            Text = L.T("Create map data"); Font = Ui.Base; BackColor = Ui.Surface;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(620, 370);
            var col = Ui.Column(); col.Padding = new Padding(22); col.Dock = DockStyle.Fill;
            col.Controls.Add(Ui.Heading(L.T("Map data from the game client")));
            col.Controls.Add(Ui.Para(L.T("Choose the folder of your CoA game client (the one with the \"Data\" folder). The extraction runs in its own window. " +
                "When it finishes, the data is moved into the server automatically. Keep the game and its launcher closed meanwhile."), 570));
            var row = Ui.Row(); row.Controls.Add(wow);
            var browse = Ui.Secondary(L.T("Browse …"));
            browse.Click += (s, e) => { using (var d = new FolderBrowserDialog { Description = L.T("Choose the game folder (it contains the \"Data\" folder)") }) if (d.ShowDialog(this) == DialogResult.OK) wow.Text = ClientAddons.GameRoot(d.SelectedPath) ?? d.SelectedPath; };
            row.Controls.Add(browse); col.Controls.Add(row);
            col.Controls.Add(mmaps);
            if (inst.NeedsClientDbc) col.Controls.Add(dbcOnly);
            dbcOnly.CheckedChanged += (s, e) => mmaps.Enabled = !dbcOnly.Checked;
            var go = Ui.Primary(L.T("Start extraction")); go.Margin = new Padding(0, 16, 0, 0);
            go.Click += (s, e) => Run();
            col.Controls.Add(go);
            Controls.Add(col);
        }

        static string Q(string path) { return "\"" + path + "\""; }

        void Run()
        {
            string dir = ClientAddons.GameRoot(wow.Text);
            if (dir == null)
            { Ui.Error(this, L.T("This is not a game folder: it needs a \"Data\" folder with the game's .MPQ archives.") + " " + L.T("You can also choose the \"Data\" folder itself.")); return; }
            wow.Text = dir;
            string clientData = Path.Combine(dir, "Data");
            bool coaDbc = inst.NeedsClientDbc;
            bool onlyDbc = coaDbc && dbcOnly.Checked;
            if (coaDbc && (!File.Exists(inst.PythonExe) || !File.Exists(inst.MpqCliExe)))
            { Ui.Error(this, L.T("The tools for the CoA DBC extraction are missing. Run \"Repair\" once, then try again.")); return; }

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
            catch (Exception ex) { Ui.Error(this, L.T("The tools could not be copied into the game folder:") + "\n" + ex.Message); return; }

            string data = inst.DataDir;
            // The window of the extraction speaks English: the console's code page cannot show every language.
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
            MessageBox.Show(this, L.T("The extraction is now running in a new window. Keep it open until it says DONE."), Product.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
    }
}
