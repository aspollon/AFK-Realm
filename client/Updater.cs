using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AfkClient;

internal static class ManifestClient
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    public static async Task<Manifest> FetchAsync(string manifestUrl, CancellationToken cancel)
    {
        if (!Uri.TryCreate(manifestUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("The launcher has no valid manifest address. Ask the server owner for a new player package.");
        using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancel);
        response.EnsureSuccessStatusCode();
        string json = await response.Content.ReadAsStringAsync(cancel);
        var manifest = JsonSerializer.Deserialize<Manifest>(json, Json.Options)
            ?? throw new InvalidOperationException("The server sent an empty manifest.");
        manifest.Files ??= new List<ManifestFile>();
        return manifest;
    }
}

internal static class Updater
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };
    static readonly Regex RealmLine = new(@"^\s*set\s+realmlist\s", RegexOptions.IgnoreCase);
    static readonly Regex ConfigRealm = new(@"^\s*SET\s+realmList\s", RegexOptions.IgnoreCase);
    static readonly Regex ConfigName = new(@"^\s*SET\s+realmName\s", RegexOptions.IgnoreCase);

    public static async Task<UpdateResult> ApplyAsync(string gameFolder, LauncherConfig config, Manifest manifest,
        IReadOnlyCollection<string> previousManaged, bool force, IProgress<UpdateProgress> progress, CancellationToken cancel)
    {
        if (string.IsNullOrWhiteSpace(gameFolder) || !Game.IsClientFolder(gameFolder))
            throw new InvalidOperationException("Choose the Conquest of Azeroth game folder (the one with the Data folder).");
        Directory.CreateDirectory(gameFolder);
        string root = Path.GetFullPath(gameFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var result = new UpdateResult();
        int done = 0, total = manifest.Files.Count;
        foreach (var file in manifest.Files)
        {
            cancel.ThrowIfCancellationRequested();
            string target = SafeTarget(root, file.Path);
            bool current = !force && File.Exists(target) && new FileInfo(target).Length == file.Size &&
                string.Equals(await HashAsync(target, cancel), file.Sha256, StringComparison.OrdinalIgnoreCase);
            if (current)
            {
                result.Skipped++;
                progress.Report(new UpdateProgress("Checking the game files ...", Percent(done++, total), file.Path));
                continue;
            }

            string uri = ResolveUrl(config.PortalUrl, file.Url);
            string temp = target + ".afk-download";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                progress.Report(new UpdateProgress("Downloading " + file.Path, Percent(done, total), ""));
                await DownloadAsync(uri, temp, cancel);
                string hash = await HashAsync(temp, cancel);
                if (!string.Equals(hash, file.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The downloaded file is incomplete: " + file.Path);
                if (File.Exists(target) && IsProtectedFile(target))
                {
                    try { File.Copy(target, target + ".afk-backup", true); } catch { }
                }
                File.Move(temp, target, true);
                result.Downloaded++;
                result.Bytes += new FileInfo(target).Length;
                result.Changed = true;
                progress.Report(new UpdateProgress("Updating " + file.Path, Percent(done++, total), ""));
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }

        var newFiles = new HashSet<string>(manifest.Files.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
        foreach (string old in (previousManaged ?? Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancel.ThrowIfCancellationRequested();
            if (newFiles.Contains(old) || old.StartsWith("/") || old.StartsWith("\\") || old.Contains(":") || old.Contains("..")) continue;
            // Never remove the game's own exe or a root DLL; only the files AFK puts under Interface and Data are ours.
            if (!(old.StartsWith("Interface/", StringComparison.OrdinalIgnoreCase) ||
                  old.StartsWith("Data/", StringComparison.OrdinalIgnoreCase))) continue;
            try
            {
                string stale = SafeTarget(root, old);
                if (File.Exists(stale)) { File.Delete(stale); result.Removed++; }
                string dir = Path.GetDirectoryName(stale);
                while (!string.IsNullOrEmpty(dir) && dir.Length > root.Length && Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir);
                    dir = Path.GetDirectoryName(dir);
                }
            }
            catch { }
        }

        progress.Report(new UpdateProgress("Writing the realm address ...", 100, ""));
        WriteRealm(gameFolder, manifest.RealmAddress, manifest.Realm);
        return result;
    }

    static int Percent(int done, int total) => total <= 0 ? 100 : Math.Clamp((int)(done * 100L / total), 0, 100);

    static bool IsProtectedFile(string path)
    {
        string ext = Path.GetExtension(path);
        return ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) || ext.Equals(".dll", StringComparison.OrdinalIgnoreCase);
    }

    static string SafeTarget(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.StartsWith("/") || relative.StartsWith("\\") ||
            relative.Contains(":") || relative.Contains(".."))
            throw new InvalidOperationException("The manifest contains an unsafe path: " + relative);
        string full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The manifest contains a path outside the game folder: " + relative);
        return full;
    }

    static string ResolveUrl(string portalUrl, string fileUrl)
    {
        if (Uri.TryCreate(fileUrl, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
            return absolute.ToString();
        if (!Uri.TryCreate((portalUrl ?? "").TrimEnd('/') + "/", UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("The launcher has no valid portal address.");
        return new Uri(baseUri, fileUrl.TrimStart('/')).ToString();
    }

    static async Task DownloadAsync(string url, string target, CancellationToken cancel)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(cancel);
        await using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 131072, true);
        await input.CopyToAsync(output, 131072, cancel);
    }

    static async Task<string> HashAsync(string path, CancellationToken cancel)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true);
        using var sha = SHA256.Create();
        byte[] hash = await sha.ComputeHashAsync(stream, cancel);
        return Convert.ToHexString(hash);
    }

    static void WriteRealm(string gameFolder, string address, string realm)
    {
        address = (address ?? "").Trim();
        if (address.Length == 0) throw new InvalidOperationException("The server did not send a realm address.");
        string data = Path.Combine(gameFolder, "Data");
        var lists = new List<string>();
        if (Directory.Exists(data))
        {
            foreach (string dir in Directory.GetDirectories(data))
                if (Regex.IsMatch(Path.GetFileName(dir), "^[a-z]{2}[A-Z]{2}$"))
                    lists.Add(Path.Combine(dir, "realmlist.wtf"));
            foreach (string file in Directory.GetFiles(data, "realmlist.wtf", SearchOption.AllDirectories))
                lists.Add(file);
        }
        if (lists.Count == 0)
        {
            Directory.CreateDirectory(Path.Combine(data, "enUS"));
            lists.Add(Path.Combine(data, "enUS", "realmlist.wtf"));
        }
        foreach (string file in lists.Distinct(StringComparer.OrdinalIgnoreCase))
            WriteRealmlist(file, address);

        string wtf = Path.Combine(gameFolder, "WTF");
        Directory.CreateDirectory(wtf);
        string config = Path.Combine(wtf, "Config.wtf");
        WriteConfig(config, address, realm);
    }

    static void WriteRealmlist(string file, string address)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        string old = File.Exists(file) ? File.ReadAllText(file) : "";
        var lines = old.Replace("\r\n", "\n").Split('\n').Where(l => l.Trim().Length > 0).ToList();
        string want = "set realmlist " + address;
        bool found = false;
        for (int i = 0; i < lines.Count; i++)
        {
            if (!RealmLine.IsMatch(lines[i])) continue;
            if (found) { lines.RemoveAt(i--); continue; }
            lines[i] = want;
            found = true;
        }
        if (!found) lines.Insert(0, want);
        File.WriteAllText(file, string.Join("\r\n", lines) + "\r\n", new UTF8Encoding(false));
    }

    static void WriteConfig(string file, string address, string realm)
    {
        string old = File.Exists(file) ? File.ReadAllText(file) : "";
        var lines = old.Replace("\r\n", "\n").Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        string want = "SET realmList \"" + address + "\"";
        bool found = false;
        for (int i = 0; i < lines.Count; i++)
        {
            if (!ConfigRealm.IsMatch(lines[i])) continue;
            if (found) { lines.RemoveAt(i--); continue; }
            lines[i] = want;
            found = true;
        }
        if (!found) lines.Add(want);
        if (!string.IsNullOrWhiteSpace(realm))
        {
            string name = "SET realmName \"" + realm.Replace("\"", "") + "\"";
            bool hasName = false;
            for (int i = 0; i < lines.Count; i++)
            {
                if (!ConfigName.IsMatch(lines[i])) continue;
                if (hasName) { lines.RemoveAt(i--); continue; }
                lines[i] = name;
                hasName = true;
            }
            if (!hasName) lines.Add(name);
        }
        File.WriteAllText(file, string.Join("\r\n", lines) + "\r\n", new UTF8Encoding(false));
    }
}
