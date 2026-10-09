using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace CoAInstaller
{
    /// <summary>
    /// Both server consoles in one window, worldserver above authserver: each shows what the server
    /// prints (its log file, read as it grows). The worldserver's has a command line that sends GM
    /// commands through <see cref="AdminLink"/> and prints the answer; the authserver takes no commands.
    /// </summary>
    class ConsolesDialog : AfkForm
    {
        readonly Install inst;
        readonly ServerControl ctl;
        // This window runs on a thread of its own, so it does not share font objects with the main window.
        readonly Font baseFont = new Font("Segoe UI", 10f), small = new Font("Segoe UI", 9f), bold = new Font("Segoe UI Semibold", 10f), mono = new Font("Consolas", 9f);
        readonly LogView world, auth;
        readonly TextBox input = new TextBox { Dock = DockStyle.Fill };
        readonly List<string> history = new List<string>();
        int historyAt;
        bool busy;

        public ConsolesDialog(Install i, ServerControl c)
        {
            inst = i; ctl = c;
            world = new LogView(mono) { Dock = DockStyle.Fill }; auth = new LogView(mono) { Dock = DockStyle.Fill };
            input.Font = mono;
            Text = Product.Name + " – Server consoles"; Font = baseFont; BackColor = Color.White; ShowInTaskbar = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { ShowIcon = false; }
            StartPosition = FormStartPosition.CenterScreen; ClientSize = new Size(980, 740); MinimumSize = new Size(640, 480);

            var send = Ui.Secondary("Send"); send.Dock = DockStyle.Right; send.Width = 80; send.AutoSize = false; send.Padding = new Padding(0); send.Font = small;
            var prompt = new Label { Text = "AC>", Dock = DockStyle.Left, Width = 36, Font = mono, TextAlign = ContentAlignment.MiddleLeft };
            var inputRow = new Panel { Dock = DockStyle.Bottom, Height = 32, Padding = new Padding(0, 6, 0, 0) };
            inputRow.Controls.Add(input); inputRow.Controls.Add(prompt); inputRow.Controls.Add(send);

            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 8, BackColor = Color.White };
            split.Panel1.Padding = new Padding(16, 10, 16, 4); split.Panel2.Padding = new Padding(16, 4, 16, 8);
            split.Panel1.Controls.Add(world); split.Panel1.Controls.Add(inputRow);
            split.Panel1.Controls.Add(new Label { Text = "Worldserver (game world)", Dock = DockStyle.Top, Height = 24, Font = bold });
            split.Panel2.Controls.Add(auth);
            split.Panel2.Controls.Add(new Label { Text = "The authserver takes no commands.", Dock = DockStyle.Bottom, Height = 22, Font = small, ForeColor = Ui.Muted, TextAlign = ContentAlignment.MiddleLeft });
            split.Panel2.Controls.Add(new Label { Text = "Authserver (login)", Dock = DockStyle.Top, Height = 24, Font = bold });

            var windows = new CheckBox { Text = "Also open the classic server windows at the next server start", AutoSize = true, Font = small, ForeColor = Ui.Muted,
                Checked = Settings.ShowServerWindows, Margin = new Padding(0, 10, 20, 0) };
            windows.CheckedChanged += (s, e) => Settings.ShowServerWindows = windows.Checked;
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 58, Padding = new Padding(14, 10, 14, 10), FlowDirection = FlowDirection.RightToLeft, BackColor = Ui.Panel, WrapContents = false };
            var close = Ui.Secondary("Close"); close.Font = baseFont; close.Click += (s, e) => Close();
            bottom.Controls.Add(close); bottom.Controls.Add(windows);

            Controls.Add(split); Controls.Add(bottom);

            send.Click += (s, e) => Send();
            input.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Send(); }
                else if (e.KeyCode == Keys.Up && history.Count > 0) { historyAt = Math.Max(0, historyAt - 1); input.Text = history[historyAt]; input.SelectionStart = input.TextLength; e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Down && history.Count > 0) { historyAt = Math.Min(history.Count, historyAt + 1); input.Text = historyAt < history.Count ? history[historyAt] : ""; input.SelectionStart = input.TextLength; e.SuppressKeyPress = true; }
            };
            Shown += (s, e) =>
            {
                split.SplitterDistance = (int)(split.Height * 0.66);
                world.Watch(Path.Combine(inst.ServerDir, "Server.log"));
                auth.Watch(Path.Combine(inst.ServerDir, "Auth.log"));
                input.Focus();
            };
        }

        void Send()
        {
            string command = input.Text.Trim();
            if (command.Length == 0 || busy) return;
            history.Remove(command); history.Add(command); historyAt = history.Count;
            input.Text = "";
            world.Print("AC> " + command);
            string problem = AdminLink.Problem(inst, ctl);
            if (problem != null) { world.Print("! " + problem); return; }
            busy = true;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                string answer;
                try { answer = AdminLink.Run(inst, command); if (answer.Length == 0) answer = "(done)"; }
                catch (Exception ex) { answer = "! " + ex.Message; }
                try { BeginInvoke((Action)(() => { busy = false; world.Print(answer); })); } catch { }
            });
        }
    }
}
