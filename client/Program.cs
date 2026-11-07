using System.Text.Json;

namespace AfkClient;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        string configPath = null;
        for (int i = 0; i + 1 < args.Length; i++)
            if (args[i] == "--config") configPath = args[i + 1];
        Application.Run(new MainForm(configPath));
    }
}

internal sealed class LauncherConfig
{
    public string Product { get; set; } = "AFK Client";
    public string Version { get; set; } = "";
    public string Realm { get; set; } = "";
    public string RealmAddress { get; set; } = "";
    public string PortalUrl { get; set; } = "";
    public string ManifestUrl { get; set; } = "";
    public string ClientDownloadUrl { get; set; } = "";
    public string BuiltAt { get; set; } = "";

    public static LauncherConfig Load(string explicitPath = null)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(explicitPath)) candidates.Add(explicitPath);
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "launcher.json"));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AFK Client", "launcher.json"));
        foreach (string file in candidates)
        {
            try
            {
                if (!File.Exists(file)) continue;
                var config = JsonSerializer.Deserialize<LauncherConfig>(File.ReadAllText(file), Json.Options);
                if (config != null && !string.IsNullOrWhiteSpace(config.ManifestUrl)) return config;
            }
            catch { }
        }
        return null;
    }
}

internal sealed class Manifest
{
    public string Product { get; set; } = "AFK Client";
    public string Realm { get; set; } = "";
    public string RealmAddress { get; set; } = "";
    public string Version { get; set; } = "";
    public string BuiltAt { get; set; } = "";
    public List<ManifestFile> Files { get; set; } = new();

    public long TotalBytes => Files.Sum(f => Math.Max(0, f.Size));
}

internal sealed class ManifestFile
{
    public string Path { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
    public string Url { get; set; } = "";
}

internal sealed class LocalState
{
    public string GameFolder { get; set; } = "";
    public string AccountName { get; set; } = "";
    public string LastManifest { get; set; } = "";
    public List<string> ManagedFiles { get; set; } = new();

    static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AFK Client", "state.json");

    public static LocalState Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<LocalState>(File.ReadAllText(FilePath), Json.Options) ?? new LocalState();
        }
        catch { }
        return new LocalState();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json.Options));
        }
        catch { }
    }
}

internal static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
}

internal sealed record UpdateProgress(string Status, int Percent = -1, string Detail = "");

internal sealed class UpdateResult
{
    public int Downloaded { get; set; }
    public int Skipped { get; set; }
    public int Removed { get; set; }
    public long Bytes { get; set; }
    public bool Changed { get; set; }
}
