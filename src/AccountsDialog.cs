using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace CoAInstaller
{
    /// <summary>Lists the accounts of real players (bot accounts are left out) and lets you delete them, reset passwords and change access levels.</summary>
    class AccountsDialog : Form
    {
        static readonly string[] Levels = { "Player", "Moderator (GM 1)", "Game Master (GM 2)", "Administrator (GM 3)" };

        readonly Install inst;
        readonly ServerControl ctl;
        readonly ListView list = new ListView { View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, Dock = DockStyle.Fill, Font = Ui.Base, BorderStyle = BorderStyle.FixedSingle };
        readonly Label status = new Label { AutoSize = true, Font = Ui.Base, ForeColor = Ui.Muted, Margin = new Padding(0, 10, 16, 0) };
        readonly Button delete = Ui.Secondary("Delete account …");
        readonly Button password = Ui.Secondary("New password …");
        readonly ComboBox level = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, Font = Ui.Base, Margin = new Padding(16, 4, 4, 0) };
        readonly Button applyLevel = Ui.Secondary("Set access level");
        readonly List<Control> actions = new List<Control>();

        public AccountsDialog(Install i, ServerControl c)
        {
            inst = i; ctl = c;
            Text = Product.Name + " – Player accounts"; Font = Ui.Base; BackColor = Color.White; ShowIcon = false;
            StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(900, 560); MinimumSize = new Size(760, 420);

            var top = new Panel { Dock = DockStyle.Top, Height = 64, Padding = new Padding(16, 12, 16, 0) };
            top.Controls.Add(new Label { Dock = DockStyle.Fill, Font = Ui.Base, ForeColor = Ui.Muted,
                Text = "Accounts of real players. The Playerbots accounts are not listed and are managed with \"Reset random bots\".\r\n" +
                       "Select an account to delete it, give it a new password or change its access level." });

            list.Columns.Add("Account", 150);
            list.Columns.Add("Access level", 170);
            list.Columns.Add("Characters", 320);
            list.Columns.Add("Last login", 140);
            list.Columns.Add("Online", 70);
            var listPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 4, 16, 8) };
            listPanel.Controls.Add(list);

            var actionRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(14, 6, 14, 6), WrapContents = false };
            foreach (var l in Levels) level.Items.Add(l);
            actionRow.Controls.Add(delete);
            actionRow.Controls.Add(password);
            actionRow.Controls.Add(level);
            actionRow.Controls.Add(applyLevel);
            actions.AddRange(new Control[] { delete, password, level, applyLevel });

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58, Padding = new Padding(14, 10, 14, 10), FlowDirection = FlowDirection.RightToLeft, BackColor = Ui.Panel, WrapContents = false };
            var close = Ui.Secondary("Close"); close.Click += (s, e) => Close();
            var refresh = Ui.Secondary("Refresh"); refresh.Click += (s, e) => Reload(null);
            bottom.Controls.Add(close); bottom.Controls.Add(refresh); bottom.Controls.Add(status);

            Controls.Add(listPanel);
            Controls.Add(actionRow);
            Controls.Add(bottom);
            Controls.Add(top);

            list.SelectedIndexChanged += (s, e) => UpdateActions();
            delete.Click += (s, e) => DeleteSelected();
            password.Click += (s, e) => NewPassword();
            applyLevel.Click += (s, e) => SetLevel();
            Shown += (s, e) => Reload(null);
            UpdateActions();
        }

        Accounts.Info Selected { get { return list.SelectedItems.Count > 0 ? (Accounts.Info)list.SelectedItems[0].Tag : null; } }

        void UpdateActions()
        {
            var a = Selected;
            foreach (var c in actions) c.Enabled = a != null;
            if (a != null) level.SelectedIndex = Math.Max(0, Math.Min(3, a.Level));
        }

        /// <summary>Runs database work in the background; the window stays responsive.</summary>
        void Run(string what, Action work, Action<Exception> done)
        {
            UseWaitCursor = true; Enabled = false; status.Text = what; status.ForeColor = Ui.Muted;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Exception error = null;
                try { if (ctl.Db == null) ctl.StartDatabase(t => { }); work(); } catch (Exception ex) { error = ex; }
                try { BeginInvoke((Action)(() => { Enabled = true; UseWaitCursor = false; done(error); })); } catch { }
            });
        }

        void Reload(string message)
        {
            List<Accounts.Info> accounts = null;
            Run("Loading accounts …", () => accounts = Accounts.ListPlayers(inst), err =>
            {
                if (err != null) { status.Text = ""; Ui.Error(this, "The accounts could not be loaded:\n" + err.Message); return; }
                int keep = Selected != null ? Selected.Id : -1;
                list.BeginUpdate(); list.Items.Clear();
                foreach (var a in accounts)
                {
                    var item = new ListViewItem(new[] { a.Name, Levels[Math.Max(0, Math.Min(3, a.Level))],
                        a.CharacterCount == 0 ? "–" : a.Characters + (a.CharacterCount > 1 ? "  (" + a.CharacterCount + ")" : ""),
                        a.LastLogin.Length > 0 ? a.LastLogin : "never", a.Online ? "yes" : "" }) { Tag = a };
                    if (a.Online) item.ForeColor = Ui.Ok;
                    list.Items.Add(item);
                    if (a.Id == keep) item.Selected = true;
                }
                list.EndUpdate();
                status.ForeColor = message != null ? Ui.Ok : Ui.Muted;
                status.Text = message ?? (accounts.Count == 1 ? "1 player account" : accounts.Count + " player accounts");
                UpdateActions();
            });
        }

        void DeleteSelected()
        {
            var a = Selected; if (a == null) return;
            if (ctl.World != null) { Ui.Error(this, "Stop the server first. Accounts can only be deleted while the worldserver is not running, so no character is still in use."); return; }
            string chars = a.CharacterCount == 0 ? "It has no characters." :
                "Its " + (a.CharacterCount == 1 ? "character" : a.CharacterCount + " characters") + " (" + a.Characters + ") will be deleted as well.";
            string purge = Accounts.PurgesDeletedCharacters(inst)
                ? "The worldserver removes the characters' remaining data (items, mail, guild membership …) at its next start."
                : "Note: CharDelete.KeepDays is 0 in your settings, so the worldserver never purges deleted characters. They stay hidden in the database.";
            if (!Ui.Confirm(this, "Delete the account \"" + a.Name + "\"?\n\n" + chars + "\n" + purge + "\n\nThis cannot be undone.")) return;
            Run("Deleting " + a.Name + " …", () => Accounts.Delete(inst, a.Id), err =>
            {
                if (err != null) { status.Text = ""; Ui.Error(this, "The account could not be deleted:\n" + err.Message); return; }
                Reload("Account " + a.Name + " was deleted.");
            });
        }

        void NewPassword()
        {
            var a = Selected; if (a == null) return;
            string pw;
            using (var d = new PasswordPrompt(a.Name)) { if (d.ShowDialog(this) != DialogResult.OK) return; pw = d.Password; }
            Run("Setting the password …", () => Accounts.SetPassword(inst, a.Id, a.Name, pw), err =>
            {
                if (err != null) { status.Text = ""; Ui.Error(this, "The password could not be changed:\n" + err.Message); return; }
                Reload("New password set for " + a.Name + ".");
            });
        }

        void SetLevel()
        {
            var a = Selected; if (a == null) return;
            int lvl = level.SelectedIndex;
            Run("Changing the access level …", () => Accounts.SetLevel(inst, a.Id, lvl), err =>
            {
                if (err != null) { status.Text = ""; Ui.Error(this, "The access level could not be changed:\n" + err.Message); return; }
                Reload(a.Name + " is now: " + Levels[lvl] + (a.Online ? " (applies at the next login)" : "") + ".");
            });
        }

        /// <summary>Asks for a new password twice, with the same rules as account creation.</summary>
        class PasswordPrompt : Form
        {
            readonly TextBox pw1 = Ui.Input(220, true), pw2 = Ui.Input(220, true);
            readonly string user;
            public string Password { get { return pw1.Text; } }
            public PasswordPrompt(string account)
            {
                user = account;
                Text = "New password for " + account; Font = Ui.Base; BackColor = Color.White; ShowIcon = false;
                FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
                StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(440, 190);
                var col = Ui.Column(); col.Padding = new Padding(20); col.Dock = DockStyle.Fill;
                var r1 = Ui.Row(); r1.Controls.Add(Ui.FieldLabel("New password", 150)); r1.Controls.Add(pw1); col.Controls.Add(r1);
                var r2 = Ui.Row(); r2.Controls.Add(Ui.FieldLabel("Repeat password", 150)); r2.Controls.Add(pw2); col.Controls.Add(r2);
                var ok = Ui.Primary("Set password"); ok.Margin = new Padding(0, 14, 0, 0);
                ok.Click += (s, e) =>
                {
                    string err = Accounts.Validate("account", pw1.Text, pw2.Text);  // only the password rules matter here
                    if (err != null) { Ui.Error(this, err); return; }
                    DialogResult = DialogResult.OK;
                };
                col.Controls.Add(ok);
                AcceptButton = ok;
                Controls.Add(col);
            }
        }
    }
}
