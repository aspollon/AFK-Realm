using System.IO;
using System.Reflection;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace AfkClient;

internal sealed class MainForm : Form
{
    readonly string _explicitConfig;
    readonly WebView2 _web;
    LauncherConfig _config;
    LocalState _state;
    Manifest _manifest;
    bool _busy;
    string _status = "Checking the server ...";
    string _statusKind = "";
    string _detail = "";
    int _progress;

    public MainForm(string explicitConfig)
    {
        _explicitConfig = explicitConfig;
        _config = LauncherConfig.Load(explicitConfig);
        _state = LocalState.Load();
        Text = "AFK Client";
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(1100, 720);
        MinimumSize = new Size(900, 600);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Font;
        try { Icon = Theme.AppIcon; } catch { }
        Theme.DarkTitleBar(this);
        _web = new WebView2 { Dock = DockStyle.Fill };
        Controls.Add(_web);
        Shown += async (_, _) => await InitializeAsync();
    }

    async Task InitializeAsync()
    {
        try
        {
            string dataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AFK Client", "WebView2");
            Directory.CreateDirectory(dataFolder);
            var environment = await CoreWebView2Environment.CreateAsync(null, dataFolder);
            await _web.EnsureCoreWebView2Async(environment);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show(this,
                "AFK Client needs the Microsoft Edge WebView2 Runtime.\n\nDownload it from:\nhttps://go.microsoft.com/fwlink/p/?LinkId=2124703",
                "AFK Client", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
            return;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "AFK Client could not start its interface:\n\n" + ex.Message,
                "AFK Client", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
            return;
        }

        var core = _web.CoreWebView2;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.WebMessageReceived += OnWebMessage;
        _web.DefaultBackgroundColor = Color.FromArgb(14, 11, 22);
        string ui = ExtractUi();
        core.SetVirtualHostNameToFolderMapping("afk.client", ui, CoreWebView2HostResourceAccessKind.Allow);
        core.Navigate("https://afk.client/index.html");
    }

    static string ExtractUi()
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AFK Client", "ui");
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        Write("AfkClient.ui.index.html", Path.Combine(root, "index.html"));
        Write("AfkClient.ui.app.css", Path.Combine(root, "app.css"));
        Write("AfkClient.ui.app.js", Path.Combine(root, "app.js"));
        Write("AfkClient.background.jpg", Path.Combine(root, "assets", "afk-realm-background.jpg"));
        return root;

        static void Write(string resource, string target)
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
            if (stream == null) return;
            using var output = File.Create(target);
            stream.CopyTo(output);
        }
    }

    void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            string action = doc.RootElement.TryGetProperty("action", out var a) ? a.GetString() ?? "" : "";
            _ = HandleAsync(action, doc.RootElement);
        }
        catch { }
    }

    async Task HandleAsync(string action, JsonElement data)
    {
        switch (action)
        {
            case "ready":
                SendState();
                await LoadManifestAsync();
                return;
            case "close":
                Close();
                return;
            case "choose":
                ChooseFolder();
                SendState();
                return;
            case "update":
                await CheckUpdates(false);
                return;
            case "repair":
                await CheckUpdates(true);
                return;
            case "play":
                await Play();
                return;
            case "portal":
                Game.OpenUrl(_config?.PortalUrl);
                return;
            case "client":
                Game.OpenUrl(_config?.ClientDownloadUrl);
                return;
            case "register":
            case "login":
                await AccountAsync(action == "register", data);
                return;
        }
    }

    async Task LoadManifestAsync()
    {
        if (_config == null || string.IsNullOrWhiteSpace(_config.ManifestUrl)) return;
        try
        {
            _manifest = await ManifestClient.FetchAsync(_config.ManifestUrl, CancellationToken.None);
            if (_status == "Checking the server ...")
            {
                if (string.IsNullOrWhiteSpace(_state.AccountName))
                    SetStatus("Welcome. Create your game account to get started.", "");
                else if (!Game.IsClientFolder(_state.GameFolder))
                    SetStatus("One more step: choose your game folder.", "");
                else
                    SetStatus("Ready. Press Play to check for updates.", "");
            }
        }
        catch (Exception ex)
        {
            SetStatus("Server not reachable: " + ex.Message, "bad");
        }
        SendState();
    }

    bool ChooseFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose your Conquest of Azeroth game folder (the one containing the Data folder).",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        if (Directory.Exists(_state.GameFolder)) dialog.SelectedPath = _state.GameFolder;
        if (dialog.ShowDialog(this) != DialogResult.OK) return false;
        if (!Game.IsClientFolder(dialog.SelectedPath))
        {
            SetStatus("This folder has no Data folder. Choose the game folder itself.", "bad");
            return false;
        }
        _state.GameFolder = dialog.SelectedPath;
        _state.Save();
        SetStatus(string.IsNullOrWhiteSpace(_state.AccountName)
            ? "Game folder saved. Create your game account to play."
            : "Game folder saved. Press Play.", "ok");
        return true;
    }

    bool EnsureGameFolder()
    {
        if (Game.IsClientFolder(_state.GameFolder)) return true;
        if (ChooseFolder()) return true;
        SetStatus("Choose your game folder first.", "warn");
        SendState();
        return false;
    }

    async Task CheckUpdates(bool repair)
    {
        if (_busy) return;
        if (!EnsureGameFolder()) return;
        if (Game.IsRunning(_state.GameFolder))
        {
            SetStatus("Close the game before updating the client files.", "warn");
            SendState();
            return;
        }
        SetBusy(true);
        SetStatus(repair ? "Repairing the game files ..." : "Checking the game files ...", "");
        SendState();
        try
        {
            if (_manifest == null) await LoadManifestAsync();
            if (_manifest == null) throw new InvalidOperationException("The server manifest could not be loaded.");
            var result = await Updater.ApplyAsync(_state.GameFolder, _config, _manifest, _state.ManagedFiles, repair,
                new InlineProgress(ShowProgress), CancellationToken.None);
            _state.LastManifest = _manifest.Version;
            _state.ManagedFiles = _manifest.Files.Select(f => f.Path).ToList();
            _state.Save();
            _progress = 100;
            SetStatus(result.Downloaded == 0
                ? "Everything is up to date."
                : "Client updated: " + result.Downloaded + " files, " + FormatBytes(result.Bytes) +
                  (result.Removed > 0 ? ", " + result.Removed + " old files removed" : "") + ".", "ok");
            _detail = "";
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, "bad");
            _progress = 0;
        }
        finally
        {
            _busy = false;
            SendState();
        }
    }

    async Task Play()
    {
        if (_busy) return;
        if (string.IsNullOrWhiteSpace(_state.AccountName))
        {
            SetStatus("Create your game account first.", "warn");
            SendState();
            Post(new { type = "accountOpen", mode = "register" });
            return;
        }
        if (!EnsureGameFolder()) return;
        if (Game.IsRunning(_state.GameFolder)) { SetStatus("The game is already running.", "warn"); SendState(); return; }
        SetBusy(true);
        SetStatus("Checking the game files ...", "");
        SendState();
        try
        {
            if (_manifest == null) await LoadManifestAsync();
            if (_manifest == null) throw new InvalidOperationException("The server manifest could not be loaded.");
            var result = await Updater.ApplyAsync(_state.GameFolder, _config, _manifest, _state.ManagedFiles, false,
                new InlineProgress(ShowProgress), CancellationToken.None);
            _state.LastManifest = _manifest.Version;
            _state.ManagedFiles = _manifest.Files.Select(f => f.Path).ToList();
            _state.Save();
            _progress = 100;
            SetStatus("Starting the game ...", "ok");
            SendState();
            Game.Launch(_state.GameFolder);
            _detail = "Game started.";
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, "bad");
        }
        finally
        {
            _busy = false;
            SendState();
        }
    }

    async Task AccountAsync(bool register, JsonElement data)
    {
        string name = data.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        string password = data.TryGetProperty("password", out var p) ? p.GetString() ?? "" : "";
        string repeat = data.TryGetProperty("password2", out var p2) ? p2.GetString() ?? "" : "";
        AccountResult result;
        if (register && password != repeat)
            result = new AccountResult(false, "", "The two passwords do not match.");
        else
            result = register
                ? await AccountClient.RegisterAsync(_config, name, password, repeat)
                : await AccountClient.LoginAsync(_config, name, password);
        if (result.Ok)
        {
            _state.AccountName = result.Name;
            _state.Save();
            SetStatus(Game.IsClientFolder(_state.GameFolder)
                ? "Account ready. Press Play."
                : "Account ready. Now choose your game folder.", "ok");
            SendState();
        }
        SendAccount(result);
    }

    void SendAccount(AccountResult result)
    {
        Post(new { type = "account", ok = result.Ok, name = result.Name, message = result.Message });
    }

    void ShowProgress(UpdateProgress progress)
    {
        _progress = progress.Percent < 0 ? 0 : Math.Clamp(progress.Percent, 0, 100);
        if (!string.IsNullOrWhiteSpace(progress.Status)) _status = progress.Status;
        _detail = progress.Detail ?? "";
        SendState();
    }

    void SetBusy(bool busy) => _busy = busy;

    void SetStatus(string text, string kind)
    {
        _status = text;
        _statusKind = kind;
    }

    void SendState()
    {
        try
        {
            Post(new
            {
                type = "state",
                missing = _config == null,
                realm = string.IsNullOrWhiteSpace(_config?.Realm) ? "AFK Realm" : _config.Realm,
                folder = _state.GameFolder ?? "",
                account = _state.AccountName ?? "",
                busy = _busy,
                status = _status,
                statusKind = _statusKind,
                progress = _progress,
                detail = _detail,
                version = _manifest?.Version ?? _config?.Version ?? "",
                portalUrl = _config?.PortalUrl ?? "",
                manifestUrl = _config?.ManifestUrl ?? ""
            });
        }
        catch { }
    }

    void Post(object payload)
    {
        try { _web.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(payload, Json.Options)); } catch { }
    }

    static string FormatBytes(long n)
    {
        if (n < 1024) return n + " B";
        if (n < 1048576) return (n / 1024.0).ToString("0") + " KB";
        if (n < 1073741824) return (n / 1048576.0).ToString("0.0") + " MB";
        return (n / 1073741824.0).ToString("0.0") + " GB";
    }

    sealed class InlineProgress : IProgress<UpdateProgress>
    {
        readonly Action<UpdateProgress> _report;
        public InlineProgress(Action<UpdateProgress> report) { _report = report; }
        public void Report(UpdateProgress value) { _report(value); }
    }
}
