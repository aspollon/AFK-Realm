using System;
using System.Drawing;
using System.Windows.Forms;

namespace CoAInstaller
{
    enum RaceNumberChoice { FreeNumber, MoveExisting, Cancel }

    /// <summary>The three answers when an imported race wants a number that another race already has.</summary>
    static class RaceNumberDialog
    {
        public static RaceNumberChoice Ask(IWin32Window owner, string text, string freeText, string moveText)
        {
            using (var form = new Form())
            {
                form.Text = Product.Name;
                form.StartPosition = FormStartPosition.CenterParent;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MaximizeBox = false;
                form.MinimizeBox = false;
                form.ShowInTaskbar = false;
                form.BackColor = Ui.Surface;
                form.ForeColor = Ui.Text;
                form.Font = Ui.Base;
                form.Padding = new Padding(22, 18, 22, 14);
                form.AutoScaleMode = AutoScaleMode.Dpi;

                var body = Ui.Column();
                body.Dock = DockStyle.Fill;
                body.MaximumSize = new Size(620, 0);
                var label = new Label
                {
                    Text = text,
                    AutoSize = true,
                    MaximumSize = new Size(620, 0),
                    Font = Ui.Base,
                    ForeColor = Ui.Text,
                    BackColor = Ui.Surface,
                    Margin = new Padding(0, 0, 0, 14),
                };
                body.Controls.Add(label);

                var buttons = new FlowLayoutPanel
                {
                    FlowDirection = FlowDirection.RightToLeft,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    Dock = DockStyle.Bottom,
                    Margin = new Padding(0),
                    Padding = new Padding(0, 12, 0, 0),
                    BackColor = Ui.Surface,
                };
                var cancel = Ui.Secondary(L.T("Cancel"));
                cancel.DialogResult = DialogResult.Cancel;
                var move = Ui.Secondary(moveText);
                if (moveText.Length == 0) move.Visible = false;
                var free = Ui.Primary(freeText);
                free.DialogResult = DialogResult.OK;
                buttons.Controls.Add(cancel);
                buttons.Controls.Add(move);
                buttons.Controls.Add(free);
                form.AcceptButton = free;
                form.CancelButton = cancel;
                form.Controls.Add(buttons);
                form.Controls.Add(body);
                form.ClientSize = new Size(680, Math.Min(560, 120 + label.PreferredHeight));
                form.Shown += (s, e) => { form.ClientSize = new Size(680, Math.Min(620, body.PreferredSize.Height + buttons.PreferredSize.Height + 70)); free.Focus(); };

                RaceNumberChoice answer = RaceNumberChoice.Cancel;
                move.Click += (s, e) => { answer = RaceNumberChoice.MoveExisting; form.DialogResult = DialogResult.OK; form.Close(); };
                free.Click += (s, e) => { answer = RaceNumberChoice.FreeNumber; form.DialogResult = DialogResult.OK; form.Close(); };
                cancel.Click += (s, e) => { answer = RaceNumberChoice.Cancel; form.DialogResult = DialogResult.Cancel; form.Close(); };
                form.ShowDialog(owner);
                return answer;
            }
        }
    }
}
