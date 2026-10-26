using System;
using System.Drawing;
using System.Windows.Forms;

namespace CoAInstaller
{
    /// <summary>A read-only, copyable race-number table: source row in ChrRaces.dbc, assigned race number, base race
    /// and whether the race came from the client or from the race folder.</summary>
    class RaceIdDialog : AfkForm
    {
        public RaceIdDialog(string text)
        {
            Text = Product.Name + " - " + L.T("Race IDs");
            Font = Ui.Base; BackColor = Ui.Surface; ShowIcon = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(780, 520); MinimumSize = new Size(620, 380);

            var top = new Panel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(16, 12, 16, 6), BackColor = Ui.Surface };
            top.Controls.Add(new Label { Text = L.T("What number belongs to which race"), AutoSize = true, Font = Ui.Bold, ForeColor = Ui.Text });
            top.Controls.Add(Ui.Para(L.T("This is read from your client and the current race settings. Copy it when you need to compare setups in Discord or a ticket."), 740));

            var box = new TextBox
            {
                Text = text, ReadOnly = true, Multiline = true, WordWrap = false, ScrollBars = ScrollBars.Both,
                Dock = DockStyle.Fill, Font = new Font("Consolas", 9f), BackColor = Ui.Surface, ForeColor = Ui.Text,
                BorderStyle = BorderStyle.FixedSingle
            };
            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16, 0, 16, 8), BackColor = Ui.Surface };
            body.Controls.Add(box);

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(14, 8, 14, 8), FlowDirection = FlowDirection.RightToLeft, BackColor = Ui.Panel, WrapContents = false };
            var close = Ui.Secondary(L.T("Close")); close.Click += (s, e) => Close();
            var copy = Ui.Primary(L.T("Copy table"));
            copy.Click += (s, e) =>
            {
                try { Clipboard.SetText(text); } catch { }
            };
            bottom.Controls.Add(close); bottom.Controls.Add(copy);

            Controls.Add(body); Controls.Add(top); Controls.Add(bottom);
        }
    }
}
