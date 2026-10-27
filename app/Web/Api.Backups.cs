using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Builder;

namespace CoAInstaller.Web
{
    /// <summary>The server snapshots in &lt;root&gt;/Backups; making, restoring and restoring parts run as engine tasks.</summary>
    static partial class Api
    {
        static Dictionary<string, string> Manifest(string dir)
        {
            string manifest = Path.Combine(dir, "manifest.txt");
            if (!File.Exists(manifest)) return null;
            var v = new Dictionary<string, string>();
            foreach (var line in File.ReadAllLines(manifest)) { int eq = line.IndexOf('='); if (eq > 0) v[line.Substring(0, eq)] = line.Substring(eq + 1); }
            return v;
        }

        static string Short(string rev) { return rev.Length >= 8 ? rev.Substring(0, 8) : (rev.Length > 0 ? rev : "–"); }

        static string DescribeBackup(string reason)
        {
            if (reason == "manual") return L.T("by hand");
            if (reason == "before update") return L.T("automatically, before update");
            if (reason == "before rebuild") return L.T("automatically, before rebuild");
            if (reason == "before module change") return L.T("automatically, before module change");
            return reason;
        }

        static object BackupJson(string dir, Dictionary<string, string> v)
        {
            Func<string, string> get = k => v.ContainsKey(k) ? v[k] : "";
            long bytes; long.TryParse(get("bytes"), out bytes);
            return new
            {
                name = Path.GetFileName(dir), created = get("created"), made = DescribeBackup(get("reason")), core = Short(get("core")), bots = Short(get("playerbots")),
                size = (bytes / 1048576.0).ToString("N0", CultureInfo.InvariantCulture) + " MB"
            };
        }

        static void MapBackups(WebApplication app)
        {
            Get(app, "/api/backups", c =>
            {
                var inst = Inst;
                string folder = Path.Combine(inst.Root, "Backups");
                var list = new List<object>();
                if (Directory.Exists(folder))
                    foreach (var dir in Directory.GetDirectories(folder).OrderByDescending(d => d, StringComparer.Ordinal))
                    {
                        var v = Manifest(dir);
                        if (v != null) list.Add(BackupJson(dir, v));
                    }
                return new { folder, keep = BackupSettings.Load(inst.Root), min = BackupSettings.Min, max = BackupSettings.Max, list };
            });

            Post(app, "/api/backups/keep", a =>
            {
                try { BackupSettings.Save(Inst.Root, a.Int("keep", BackupSettings.Default)); }
                catch (Exception ex) { throw new UserError(L.T("The setting could not be saved:") + "\n" + ex.Message); }
                return new { keep = BackupSettings.Load(Inst.Root) };
            });

            Post(app, "/api/backups/delete", a =>
            {
                string name = a.Str("name") ?? "";
                if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[\w\-]+$")) throw new UserError(L.T("No backup was chosen."));
                string dir = Path.Combine(Inst.Root, "Backups", name);
                if (Manifest(dir) == null) throw new UserError(L.T("No backup was chosen."));
                try { Directory.Delete(dir, true); }
                catch (Exception ex) { throw new UserError(L.T("The backup could not be deleted:") + "\n" + ex.Message); }
                return Ok;
            });

            // A backup folder of another server, for taking parts of it.
            Post(app, "/api/backups/other", a =>
            {
                string dir = (a.Str("folder") ?? "").TrimEnd('\\', '/');
                var v = Directory.Exists(dir) ? Manifest(dir) : null;
                if (v == null || !File.Exists(Path.Combine(dir, "server.zip")))
                    throw new UserError(L.T("This folder is not a backup of AFK Realm. Choose the folder with the date as its name, the one that holds manifest.txt."));
                return new { folder = dir, created = v.ContainsKey("created") ? v["created"] : Path.GetFileName(dir) };
            });
        }
    }
}
