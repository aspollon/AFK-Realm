using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace CoAInstaller
{
    /// <summary>Makes race packs for the race folder from another game client or repack (see RacePacker).</summary>
    class RacePackDialog : AfkForm
    {
        readonly string raceFolder, yourClient, serverDataFolder;
        readonly Label from, status;
        readonly CheckedListBox list;
        readonly Button choose, create;
        RacePacker.Archives source, yours;
        string sourceFolder;
        bool busy;
        public int Made;

        public RacePackDialog(string raceFolder, string yourClient, string serverDataFolder, string initialSource = null)
        {
            this.raceFolder = raceFolder;
            this.yourClient = yourClient;
            this.serverDataFolder = serverDataFolder;
            Text = Product.Name + " – " + L.T("Race packs from another client");
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(640, 600);
            MinimumSize = new Size(560, 480);
            var col = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 12, 20, 16), ColumnCount = 1, BackColor = Ui.Surface };
            col.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            col.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            col.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            col.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            col.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            col.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(590, 0), Font = Ui.Small, ForeColor = Ui.Muted, Margin = new Padding(0, 0, 0, 8),
                Text = L.F("Choose the game folder of another client or repack (or a folder with its patch archives, like the client folder of CoA Custom). " +
                       "Each race you tick gets a folder of its own in your race folder ({0}): its rows and the files your client does not have. " +
                       "Afterwards the other client is no longer needed.", raceFolder) +
                       (string.IsNullOrEmpty(serverDataFolder) ? "" : L.T(" The racial spells of your server data folder come along with the races, so your game client can show them.")) });
            var row = Ui.Row(); row.BackColor = Ui.Surface; row.AutoSize = true;
            choose = Ui.Secondary(L.T("Choose the other client …"));
            from = new Label { AutoSize = true, MaximumSize = new Size(380, 0), Font = Ui.Small, ForeColor = Ui.Text, Margin = new Padding(8, 8, 0, 0), Text = L.T("No client chosen.") };
            row.Controls.Add(choose); row.Controls.Add(from);
            col.Controls.Add(row);
            list = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false, Font = Ui.Base, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 10, 0, 8) };
            col.Controls.Add(list);
            status = new Label { AutoSize = true, MaximumSize = new Size(590, 0), Font = Ui.Small, ForeColor = Ui.Muted, Margin = new Padding(0, 0, 0, 8), Text = "" };
            col.Controls.Add(status);
            var buttons = Ui.Row(); buttons.BackColor = Ui.Surface; buttons.AutoSize = true;
            create = Ui.Primary(L.T("Create race packs"));
            create.Enabled = false;
            var close = Ui.Ghost(L.T("Close"));
            buttons.Controls.Add(create); buttons.Controls.Add(close);
            col.Controls.Add(buttons);
            Controls.Add(col);

            choose.Click += (s, e) => Choose();
            create.Click += (s, e) => Create();
            close.Click += (s, e) => Close();
            list.ItemCheck += (s, e) => BeginInvoke((Action)(() => create.Enabled = !busy && list.CheckedItems.Count > 0));
            FormClosing += (s, e) => { if (busy) { e.Cancel = true; return; } Release(); };
            if (!string.IsNullOrEmpty(initialSource)) Shown += (s, e) => UseSource(initialSource);
        }

        class Item
        {
            public RacePacker.Candidate Race;
            public override string ToString()
            {
                var notes = new List<string>();
                if (Race.MaleOnly) notes.Add(L.T("male only"));
                if (Race.FemaleOnly) notes.Add(L.T("female only"));
                if (Race.InYourClient) notes.Add(L.T("your client has it too"));
                if (Race.InRaceFolder) notes.Add(L.T("in your race folder already"));
                return Race.Name + (notes.Count > 0 ? "   (" + string.Join(", ", notes) + ")" : "");
            }
        }

        void Release()
        {
            if (source != null) source.Dispose();
            if (yours != null) yours.Dispose();
            source = yours = null;
        }

        void Busy(bool on, string text)
        {
            busy = on;
            choose.Enabled = !on;
            list.Enabled = !on;
            create.Enabled = !on && list.CheckedItems.Count > 0;
            status.Text = text;
            UseWaitCursor = on;
        }

        void Choose()
        {
            using (var d = new FolderBrowserDialog { Description = L.T("The game folder of the other client, or a folder with its patch archives"), SelectedPath = sourceFolder ?? "" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                UseSource(d.SelectedPath);
            }
        }

        /// <summary>Reads a chosen client or patch folder without asking for it again (the import button already
        /// knows the folder).</summary>
        public void UseSource(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
            if (yourClient != null && Path.GetFullPath(folder).TrimEnd('\\').Equals(Path.GetFullPath(yourClient).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            { Ui.Error(this, L.T("That is your own game client. Choose the folder of another client.")); return; }
            sourceFolder = folder;
            from.Text = sourceFolder;
            list.Items.Clear();
            Release();
            Busy(true, L.T("Reading the races of the other client …"));
            ThreadPool.QueueUserWorkItem(_ =>
            {
                RacePacker.Archives src = null, mine = null; List<RacePacker.Candidate> races = null; string error = null;
                try
                {
                    src = RacePacker.Open(folder, yourClient);
                    mine = new RacePacker.Archives(yourClient == null ? new List<string>() : ClientPatch.ClientArchives(yourClient));
                    races = RacePacker.Races(src, yourClient, raceFolder);
                }
                catch (Exception ex) { error = ex.Message; }
                BeginInvoke((Action)(() =>
                {
                    source = src; yours = mine;
                    if (error != null) { Busy(false, ""); Ui.Error(this, L.T("The races of this client could not be read:") + " " + error); return; }
                    foreach (var r in races) list.Items.Add(new Item { Race = r });
                    Busy(false, races.Count == 0 ? L.T("This client has no races beyond the stock ones.") :
                        yourClient != null ? L.F("{0} races. Tick the ones you want; a race your client has too can get the other client's looks.", races.Count)
                        : L.F("{0} races. Tick the ones you want.", races.Count));
                }));
            });
        }

        void Create()
        {
            var chosen = list.CheckedItems.Cast<Item>().Select(i => i.Race).ToList();
            if (chosen.Count == 0) return;
            var again = chosen.Where(r => r.InRaceFolder).Select(r => r.Name).ToList();
            if (again.Count > 0 && !Ui.Confirm(this, again.Count == 1
                ? L.F("Your race folder has {0} already. Replace it with the race of this client?", again[0])
                : L.F("Your race folder has {0} already. Replace them with the races of this client?", string.Join(", ", again)))) return;
            Busy(true, L.T("Starting …"));
            string label = RacePacker.SourceLabel(sourceFolder);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var lines = new List<string>();
                try { var kept = RacePacker.KeepRacePatch(sourceFolder, raceFolder); if (kept != null) { lines.Add(kept); Made++; } }
                catch (Exception ex) { lines.Add(L.T("The 64-race patch could not be kept:") + " " + ex.Message); }
                foreach (var race in chosen)
                {
                    try
                    {
                        Directory.CreateDirectory(raceFolder);
                        uint[] spells = ServerRacials.ClientSpells(serverDataFolder, race.Name, race.Id);
                        lines.Add(RacePacker.Pack(source, yours, race, raceFolder, label, spells, text => BeginInvoke((Action)(() => status.Text = text))));
                        race.InRaceFolder = true;
                        Made++;
                    }
                    catch (Exception ex) { lines.Add(L.F("{0}: not made - {1}", race.Name, ex.Message)); }
                }
                BeginInvoke((Action)(() =>
                {
                    Busy(false, string.Join(Environment.NewLine, lines));
                    for (int i = 0; i < list.Items.Count; i++) { list.SetItemChecked(i, false); list.Items[i] = list.Items[i]; }
                }));
            });
        }
    }
}
