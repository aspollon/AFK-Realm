using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace CoAInstaller
{
    /// <summary>
    /// Game master tools for the running server: the characters on the left, quest helper and player
    /// actions on the right, and a console that sends any GM command and shows the server's answer.
    /// Lookups read the server's databases; actions go through <see cref="AdminLink"/>.
    /// </summary>
    class GameMasterDialog : Form
    {
        readonly Install inst;
        readonly ServerControl ctl;
        readonly Label link = new Label { AutoSize = true, Font = Ui.Bold, Margin = new Padding(0, 0, 0, 0) };
        readonly ListView people = new ListView { View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, Dock = DockStyle.Fill, Font = Ui.Base, BorderStyle = BorderStyle.FixedSingle };
        readonly CheckBox offline = new CheckBox { Text = "Offline too", AutoSize = true, Font = Ui.Small, Margin = new Padding(0, 4, 10, 0) };
        readonly CheckBox bots = new CheckBox { Text = "Bots too", AutoSize = true, Font = Ui.Small, Margin = new Padding(0, 4, 10, 0) };
        readonly TextBox questText = new TextBox { Font = Ui.Base, Width = 250, Margin = new Padding(0, 3, 8, 3) };
        readonly NumericUpDown radius = new NumericUpDown { Minimum = 20, Maximum = 2000, Value = 150, Increment = 50, Width = 64, Font = Ui.Base, Margin = new Padding(0, 4, 4, 0) };
        readonly CheckBox levelOnly = new CheckBox { Text = "Only quests it can take now", AutoSize = true, Checked = true, Font = Ui.Small, Margin = new Padding(10, 6, 0, 0) };
        readonly ListView quests = new ListView { View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, Dock = DockStyle.Fill, Font = Ui.Base, BorderStyle = BorderStyle.FixedSingle };
        readonly Label questHint = new Label { AutoSize = true, Font = Ui.Small, ForeColor = Ui.Muted, Margin = new Padding(0, 6, 0, 0) };
        readonly TextBox log = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = Ui.Mono, Dock = DockStyle.Fill, BackColor = Color.FromArgb(24, 22, 32), ForeColor = Color.FromArgb(215, 215, 225), WordWrap = true };
        readonly TextBox input = new TextBox { Font = Ui.Mono, Dock = DockStyle.Fill };
        readonly TextBox announce = new TextBox { Font = Ui.Base, Width = 380, Margin = new Padding(0, 3, 8, 3) };
        readonly List<Button> actions = new List<Button>();
        readonly List<string> history = new List<string>();
        int historyAt;
        bool busy;

        public GameMasterDialog(Install i, ServerControl c)
        {
            inst = i; ctl = c;
            Text = Product.Name + " – Game master"; Font = Ui.Base; BackColor = Color.White; ShowIcon = false;
            StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(1160, 720); MinimumSize = new Size(980, 600);

            var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(16, 12, 16, 6), WrapContents = false };
            top.Controls.Add(link);

            // ---- characters
            people.Columns.Add("Character", 130);
            people.Columns.Add("Lvl", 38, HorizontalAlignment.Right);
            people.Columns.Add("Zone", 130);
            var peopleTop = Ui.Row(); peopleTop.Dock = DockStyle.Top; peopleTop.Margin = new Padding(0);
            peopleTop.Controls.Add(new Label { Text = "Characters", AutoSize = true, Font = Ui.H2, Margin = new Padding(0, 0, 12, 4) });
            var peopleFilter = Ui.Row(); peopleFilter.Dock = DockStyle.Top; peopleFilter.Margin = new Padding(0);
            var refresh = Ui.Secondary("Refresh"); refresh.Font = Ui.Small; refresh.Padding = new Padding(6, 1, 6, 1);
            peopleFilter.Controls.Add(offline); peopleFilter.Controls.Add(bots); peopleFilter.Controls.Add(refresh);
            var left = new Panel { Dock = DockStyle.Left, Width = 340, Padding = new Padding(16, 0, 8, 10) };
            left.Controls.Add(people); left.Controls.Add(peopleFilter); left.Controls.Add(peopleTop);

            // ---- quest helper
            quests.Columns.Add("Id", 54, HorizontalAlignment.Right);
            quests.Columns.Add("Quest", 220);
            quests.Columns.Add("Lvl", 38, HorizontalAlignment.Right);
            quests.Columns.Add("Given by", 150);
            quests.Columns.Add("Handed in to", 150);
            quests.Columns.Add("Yards", 50, HorizontalAlignment.Right);
            quests.Columns.Add("Status", 82);
            var search = Ui.Row(); search.Dock = DockStyle.Top; search.Margin = new Padding(0);
            search.Controls.Add(new Label { Text = "NPC name, quest title or id", AutoSize = true, Font = Ui.Base, Margin = new Padding(0, 6, 6, 0) });
            search.Controls.Add(questText);
            var find = Ui.Primary("Search"); find.Padding = new Padding(10, 3, 10, 3);
            search.Controls.Add(find);
            var near = Ui.Row(); near.Dock = DockStyle.Top; near.Margin = new Padding(0);
            var nearBtn = Ui.Secondary("Quests near the character"); nearBtn.Padding = new Padding(10, 3, 10, 3);
            near.Controls.Add(nearBtn);
            near.Controls.Add(new Label { Text = "within", AutoSize = true, Font = Ui.Base, Margin = new Padding(8, 6, 4, 0) });
            near.Controls.Add(radius);
            near.Controls.Add(new Label { Text = "yards", AutoSize = true, Font = Ui.Base, Margin = new Padding(0, 6, 0, 0) });
            near.Controls.Add(levelOnly);
            var questButtons = Ui.Row(); questButtons.Dock = DockStyle.Bottom; questButtons.Margin = new Padding(0);
            questButtons.Controls.Add(Action("Give quest", () => Quest("add")));
            questButtons.Controls.Add(Action("Complete", () => Quest("complete")));
            questButtons.Controls.Add(Action("Reward", () => Quest("reward")));
            questButtons.Controls.Add(Action("Remove", () => Quest("remove")));
            questButtons.Controls.Add(Action("Check", () => Quest("status")));
            var questNote = new Label { Dock = DockStyle.Bottom, Height = 50, Font = Ui.Small, ForeColor = Ui.Muted,
                Text = "Give puts the quest into the character's log. Complete marks every objective as done and adds missing quest items. " +
                       "Reward hands a completed quest in: experience, money and the reward (the first one, if there is a choice). Works for offline characters too." };
            var questTab = new TabPage("Quests") { BackColor = Color.White, Padding = new Padding(10) };
            questTab.Controls.Add(quests); questTab.Controls.Add(questHint); questTab.Controls.Add(near); questTab.Controls.Add(search);
            questTab.Controls.Add(questButtons); questTab.Controls.Add(questNote);
            questHint.Dock = DockStyle.Top;

            // ---- player actions
            var player = Ui.Column(); player.Padding = new Padding(4);
            player.Controls.Add(Ui.Hint("These act on the character chosen on the left.", 700));
            var p1 = Ui.Row();
            p1.Controls.Add(Action("Unstuck (to its inn)", () => Player("unstuck {0} inn", null)));
            p1.Controls.Add(Action("Revive", () => Player("revive {0}", null)));
            p1.Controls.Add(Action("Kick …", () => { string r = Prompt.Ask(this, "Kick", "Reason shown to the player (optional)", ""); if (r != null) Player("kick {0} " + Clean(r), null); }));
            player.Controls.Add(p1);
            var p2 = Ui.Row();
            p2.Controls.Add(Action("Set level …", () =>
            {
                string v = Prompt.Ask(this, "Set level", "New level", ""); int n;
                if (v == null) return;
                if (!int.TryParse(v.Trim(), out n) || n < 1 || n > 255) { Ui.Error(this, "Please enter a level as a number."); return; }
                Player("character level {0} " + n, null);
            }));
            p2.Controls.Add(Action("Send gold …", () =>
            {
                string v = Prompt.Ask(this, "Send gold", "Gold (arrives by in-game mail)", ""); int n;
                if (v == null) return;
                if (!int.TryParse(v.Trim(), out n) || n < 1 || n > 200000) { Ui.Error(this, "Please enter the gold as a whole number."); return; }
                Player("send money {0} \"Gold\" \"From your game master.\" " + ((long)n * 10000), null);
            }));
            p2.Controls.Add(Action("Send mail …", () =>
            {
                string[] v = Prompt.Ask2(this, "Send mail", "Subject", "Text");
                if (v == null || v[0].Trim().Length == 0) return;
                Player("send mail {0} \"" + Clean(v[0]) + "\" \"" + Clean(v[1]) + "\"", null);
            }));
            player.Controls.Add(p2);
            player.Controls.Add(Ui.Heading("Message to everyone"));
            var p3 = Ui.Row(); p3.Controls.Add(announce);
            p3.Controls.Add(Action("Announce", () => { string t = Clean(announce.Text); if (t.Length > 0) { Send("announce " + t, null); announce.Text = ""; } }));
            player.Controls.Add(p3);
            player.Controls.Add(Ui.Hint("Everything else works in the console below: type any GM command the server window accepts, for example \"server info\" or \"lookup item sword\".", 700));
            var playerTab = new TabPage("Player") { BackColor = Color.White, Padding = new Padding(10), AutoScroll = true };
            playerTab.Controls.Add(player);

            var tabs = new TabControl { Dock = DockStyle.Fill, Font = Ui.Base };
            tabs.TabPages.Add(questTab); tabs.TabPages.Add(playerTab);

            // ---- console
            var send = Ui.Secondary("Send"); send.Dock = DockStyle.Right; send.Width = 80; send.AutoSize = false; send.Padding = new Padding(0); send.Font = Ui.Small;
            var inputRow = new Panel { Dock = DockStyle.Bottom, Height = 32, Padding = new Padding(0, 6, 0, 0) };
            inputRow.Controls.Add(input); inputRow.Controls.Add(send);
            var console = new Panel { Dock = DockStyle.Bottom, Height = 210, Padding = new Padding(0, 6, 0, 0) };
            console.Controls.Add(log); console.Controls.Add(inputRow);
            console.Controls.Add(new Label { Text = "Server console", Dock = DockStyle.Top, Height = 22, Font = Ui.Bold });

            var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 16, 12) };
            right.Controls.Add(tabs); right.Controls.Add(console);

            Controls.Add(right); Controls.Add(left); Controls.Add(top);

            refresh.Click += (s, e) => LoadPeople();
            offline.CheckedChanged += (s, e) => LoadPeople();
            bots.CheckedChanged += (s, e) => LoadPeople();
            people.SelectedIndexChanged += (s, e) => { if (quests.Items.Count > 0 && quests.Tag as string == "search") SearchQuests(); };
            find.Click += (s, e) => SearchQuests();
            questText.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SearchQuests(); } };
            nearBtn.Click += (s, e) => NearQuests();
            send.Click += (s, e) => SendInput();
            input.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SendInput(); }
                else if (e.KeyCode == Keys.Up && history.Count > 0) { historyAt = Math.Max(0, historyAt - 1); input.Text = history[historyAt]; input.SelectionStart = input.TextLength; e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Down && history.Count > 0) { historyAt = Math.Min(history.Count, historyAt + 1); input.Text = historyAt < history.Count ? history[historyAt] : ""; input.SelectionStart = input.TextLength; e.SuppressKeyPress = true; }
            };
            Shown += (s, e) => { ShowLink(); LoadPeople(); };
        }

        Button Action(string text, Action run)
        {
            var b = Ui.Secondary(text); b.Padding = new Padding(10, 3, 10, 3); b.Margin = new Padding(0, 2, 8, 2);
            b.Click += (s, e) => run();
            actions.Add(b);
            return b;
        }

        /// <summary>Quotes end a text argument and line breaks end the command, so both are taken out.</summary>
        static string Clean(string text) { return (text ?? "").Replace("\"", "'").Replace("\r", " ").Replace("\n", " ").Trim(); }

        CharacterInfo Who { get { return people.SelectedItems.Count == 1 ? people.SelectedItems[0].Tag as CharacterInfo : null; } }
        QuestInfo Which { get { return quests.SelectedItems.Count == 1 ? quests.SelectedItems[0].Tag as QuestInfo : null; } }

        void UI(Action a) { try { if (IsHandleCreated && !IsDisposed) BeginInvoke(a); } catch { } }
        void Background(Action work) { System.Threading.ThreadPool.QueueUserWorkItem(_ => { try { work(); } catch (Exception ex) { UI(() => Print("! " + ex.Message)); } }); }

        void Print(string text)
        {
            if (log.TextLength > 200000) log.Text = log.Text.Substring(100000);
            log.AppendText(text.Replace("\r\n", "\n").Replace("\n", Environment.NewLine) + Environment.NewLine);
        }

        void ShowLink()
        {
            Background(() =>
            {
                string problem = AdminLink.Problem(inst, ctl);
                UI(() =>
                {
                    link.Text = problem ?? "●  Connected to the running server.";
                    link.ForeColor = problem == null ? Ui.Ok : Ui.Warn;
                });
            });
        }

        // ---------------------------------------------------------------- characters

        void LoadPeople()
        {
            bool off = offline.Checked, bot = bots.Checked;
            int keep = Who != null ? Who.Guid : 0;
            Background(() =>
            {
                List<CharacterInfo> list;
                try { list = GameData.Characters(inst, off, bot); }
                catch (Exception ex) { UI(() => Print("! The characters could not be read: " + ex.Message)); return; }
                UI(() =>
                {
                    people.BeginUpdate();
                    people.Items.Clear();
                    foreach (var c in list)
                    {
                        var it = new ListViewItem((c.Online ? "● " : "○ ") + c.Name) { Tag = c };
                        it.SubItems.Add(c.Level.ToString());
                        it.SubItems.Add(GameData.ZoneName(inst, c.Zone));
                        if (!c.Online) it.ForeColor = Ui.Muted;
                        it.ToolTipText = "Account " + c.Account + (c.Bot ? " (random bot)" : "");
                        people.Items.Add(it);
                        if (c.Guid == keep) it.Selected = true;
                    }
                    people.ShowItemToolTips = true;
                    people.EndUpdate();
                    if (people.SelectedItems.Count == 0 && people.Items.Count == 1) people.Items[0].Selected = true;
                    if (list.Count == 0) Print(off ? "No characters found." : "Nobody is online. Tick \"Offline too\" to see all characters.");
                });
            });
        }

        // ---------------------------------------------------------------- commands

        /// <summary>Sends a command and prints the answer; <paramref name="done"/> runs after success.</summary>
        void Send(string command, Action done)
        {
            if (busy) return;
            string problem = AdminLink.Problem(inst, ctl);
            if (problem != null) { ShowLink(); Print("! " + problem); return; }
            busy = true; foreach (var b in actions) b.Enabled = false;
            Print("> " + command);
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                string answer = null, error = null;
                try { answer = AdminLink.Run(inst, command); }
                catch (Exception ex) { error = ex.Message; }
                UI(() =>
                {
                    busy = false; foreach (var b in actions) b.Enabled = true;
                    if (error != null) { Print("! " + error); return; }
                    Print(answer.Length > 0 ? answer : "(done)");
                    if (done != null) done();
                });
            });
        }

        void SendInput()
        {
            string command = input.Text.Trim();
            if (command.Length == 0) return;
            history.Remove(command); history.Add(command); historyAt = history.Count;
            input.Text = "";
            Send(command, null);
        }

        void Player(string format, Action done)
        {
            var who = Who;
            if (who == null) { Ui.Error(this, "Choose a character on the left first."); return; }
            Send(string.Format(format, who.Name), done);
        }

        // ---------------------------------------------------------------- quests

        void Fill(List<QuestInfo> list, string kind, string hint)
        {
            quests.BeginUpdate();
            quests.Items.Clear();
            foreach (var q in list)
            {
                var it = new ListViewItem(q.Id.ToString()) { Tag = q };
                it.SubItems.Add(q.Title);
                it.SubItems.Add(q.Level > 0 ? q.Level.ToString() : "");
                it.SubItems.Add(q.Givers);
                it.SubItems.Add(q.Takers);
                it.SubItems.Add(q.Distance >= 0 ? Math.Round(q.Distance).ToString() : "");
                it.SubItems.Add(q.StatusText);
                quests.Items.Add(it);
            }
            quests.EndUpdate();
            quests.Tag = kind;
            questHint.Text = hint;
        }

        void SearchQuests()
        {
            string text = questText.Text.Trim();
            if (text.Length == 0) return;
            var who = Who;
            Background(() =>
            {
                var list = GameData.SearchQuests(inst, text, who != null ? who.Guid : 0);
                UI(() => Fill(list, "search", list.Count == 0 ? "Nothing found for \"" + text + "\"."
                    : list.Count + " quest(s)" + (who != null ? ". Status for " + who.Name + (who.Online ? " as of the server's last save." : ".") : ". Choose a character to see its status.")));
            });
        }

        void NearQuests()
        {
            var who = Who;
            if (who == null) { Ui.Error(this, "Choose a character on the left first."); return; }
            int r = (int)radius.Value; bool level = levelOnly.Checked;
            bool live = who.Online && AdminLink.Problem(inst, ctl) == null;
            questHint.Text = "Looking around " + who.Name + " …";
            Background(() =>
            {
                // The position in the database is the one of the last save; "saveall" makes it current.
                if (live) { try { AdminLink.Run(inst, "saveall"); System.Threading.Thread.Sleep(1500); } catch { live = false; } }
                var now = GameData.Character(inst, who.Guid);
                if (now == null) { UI(() => questHint.Text = "The character was not found."); return; }
                var list = GameData.QuestsNear(inst, now, r, level);
                UI(() => Fill(list, "near", (list.Count == 0 ? "No open quests" : list.Count + " open quest(s)") + " within " + r + " yards of " + now.Name +
                    " in " + GameData.ZoneName(inst, now.Zone) + (live ? "." : " (position as last saved by the server).") +
                    (level ? "" : " Quests it cannot take yet are included; \"Check\" tells why.")));
            });
        }

        void Quest(string verb)
        {
            var who = Who; var quest = Which; var item = quests.SelectedItems.Count == 1 ? quests.SelectedItems[0] : null;
            if (who == null) { Ui.Error(this, "Choose a character on the left first."); return; }
            if (quest == null) { Ui.Error(this, "Choose a quest in the list first."); return; }
            if (verb == "reward" && quest.Status != 1) Print("Note: Reward only works for a completed quest. If the server refuses, use Complete first.");
            Send("quest " + verb + " " + quest.Id.ToString(CultureInfo.InvariantCulture) + " " + who.Name, () =>
            {
                if (verb == "add") quest.Status = 3;
                else if (verb == "complete") quest.Status = 1;
                else if (verb == "reward") quest.Status = 100;
                else if (verb == "remove") quest.Status = -1;
                if (item != null && verb != "status") item.SubItems[6].Text = quest.StatusText;
            });
        }
    }

    /// <summary>Small input windows with one or two text fields.</summary>
    static class Prompt
    {
        public static string Ask(IWin32Window owner, string title, string label, string value)
        {
            var r = Show(owner, title, new[] { label }, new[] { value });
            return r == null ? null : r[0];
        }
        public static string[] Ask2(IWin32Window owner, string title, string label1, string label2)
        {
            return Show(owner, title, new[] { label1, label2 }, new[] { "", "" });
        }
        static string[] Show(IWin32Window owner, string title, string[] labels, string[] values)
        {
            using (var f = new Form { Text = title, Font = Ui.Base, BackColor = Color.White, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
                ShowIcon = false, StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(460, 80 + labels.Length * 62) })
            {
                var col = Ui.Column(); col.Padding = new Padding(18, 14, 18, 8); col.Dock = DockStyle.Fill;
                var boxes = new List<TextBox>();
                for (int i = 0; i < labels.Length; i++)
                {
                    col.Controls.Add(new Label { Text = labels[i], AutoSize = true, Font = Ui.Base, Margin = new Padding(0, 4, 0, 2) });
                    var box = Ui.Input(420); box.Text = values[i];
                    boxes.Add(box); col.Controls.Add(box);
                }
                var row = Ui.Row(); row.Margin = new Padding(0, 10, 0, 0);
                var ok = Ui.Primary("OK"); var cancel = Ui.Secondary("Cancel");
                ok.Click += (s, e) => { f.DialogResult = DialogResult.OK; f.Close(); };
                cancel.Click += (s, e) => { f.DialogResult = DialogResult.Cancel; f.Close(); };
                row.Controls.Add(ok); row.Controls.Add(cancel);
                col.Controls.Add(row);
                f.Controls.Add(col);
                f.AcceptButton = ok; f.CancelButton = cancel;
                return f.ShowDialog(owner) == DialogResult.OK ? boxes.Select(b => b.Text).ToArray() : null;
            }
        }
    }
}
