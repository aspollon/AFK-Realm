using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace AfkClient;

internal sealed class AccountForm : Form
{
    readonly LauncherConfig _config;
    readonly TextBox _name, _password, _repeat;
    readonly Label _status;
    readonly Button _create, _login, _cancel;
    public string AccountName { get; private set; } = "";

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public AccountForm(LauncherConfig config)
    {
        _config = config;
        Text = "AFK Client - Account";
        ClientSize = new Size(500, 430);
        MinimumSize = new Size(500, 430);
        MaximumSize = new Size(500, 430);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Font;
        try { Icon = Theme.AppIcon; } catch { }
        Theme.DarkTitleBar(this);

        var title = Theme.Label("Your account", Theme.Heading);
        var intro = Theme.Label("Create a game account here, or log in with an account you already have on this server.", Theme.Small, Theme.Muted);
        intro.MaximumSize = new Size(440, 0);

        _name = Theme.Input();
        _password = Theme.Input();
        _password.UseSystemPasswordChar = true;
        _repeat = Theme.Input();
        _repeat.UseSystemPasswordChar = true;

        _status = Theme.Label("", Theme.Small, Theme.Muted);
        _status.MaximumSize = new Size(440, 0);

        _create = Theme.Button("Create account", async (_, _) => await Register(), true);
        _login = Theme.Button("Log in", async (_, _) => await Login());
        _cancel = Theme.Button("Cancel", (_, _) => { DialogResult = DialogResult.Cancel; Close(); });

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            Padding = new Padding(0, 16, 0, 8)
        };
        AddField(fields, "Account name", _name);
        AddField(fields, "Password", _password);
        AddField(fields, "Repeat password (new account)", _repeat);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0)
        };
        buttons.Controls.Add(_create);
        buttons.Controls.Add(_login);
        buttons.Controls.Add(_cancel);

        Controls.AddRange(new Control[] { _status, buttons, fields, intro, title });
        title.Location = new Point(28, 24);
        intro.Location = new Point(28, 62);
        fields.Location = new Point(28, 108);
        buttons.Location = new Point(28, 310);
        _status.Location = new Point(28, 366);
        AcceptButton = _login;
        CancelButton = _cancel;
    }

    static void AddField(TableLayoutPanel table, string label, TextBox input)
    {
        var l = Theme.Label(label, Theme.Small, Theme.Muted);
        l.Margin = new Padding(0, 0, 0, 2);
        table.Controls.Add(l);
        input.Dock = DockStyle.Top;
        table.Controls.Add(input);
    }

    async Task Register()
    {
        if (_password.Text != _repeat.Text)
        {
            SetStatus("The two passwords do not match.", false);
            return;
        }
        await Send("api/public/register", true);
    }

    async Task Login() => await Send("api/public/downloads", false);

    async Task Send(string path, bool register)
    {
        string portal = (_config?.PortalUrl ?? "").TrimEnd('/');
        if (portal.Length == 0)
        {
            SetStatus("This launcher has no portal address. Ask the server owner for a new package.", false);
            return;
        }
        if (string.IsNullOrWhiteSpace(_name.Text) || string.IsNullOrEmpty(_password.Text))
        {
            SetStatus("Enter your account name and password.", false);
            return;
        }
        SetBusy(true);
        try
        {
            var body = new
            {
                name = _name.Text.Trim(),
                password = _password.Text,
                password2 = register ? _repeat.Text : _password.Text
            };
            using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var response = await Http.PostAsync(portal + "/" + path.TrimStart('/'), content);
            string text = await response.Content.ReadAsStringAsync();
            string error = null, name = null;
            try
            {
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("name", out var n)) name = n.GetString();
                if (doc.RootElement.TryGetProperty("error", out var e)) error = e.GetString();
                if (doc.RootElement.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False)
                    error ??= "The server rejected the request.";
            }
            catch { }
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                    ? "The server could not complete the request (" + (int)response.StatusCode + ")."
                    : error);
            AccountName = string.IsNullOrWhiteSpace(name) ? _name.Text.Trim().ToUpperInvariant() : name;
            SetStatus(register ? "Account created. You can play now." : "Logged in. You can play now.", true);
            DialogResult = DialogResult.OK;
            await Task.Delay(350);
            Close();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, false);
        }
        finally { SetBusy(false); }
    }

    void SetBusy(bool busy)
    {
        _create.Enabled = _login.Enabled = !busy;
        _name.Enabled = _password.Enabled = _repeat.Enabled = !busy;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
    }

    void SetStatus(string text, bool ok)
    {
        _status.Text = text;
        _status.ForeColor = ok ? Theme.Ok : Theme.Bad;
    }
}
