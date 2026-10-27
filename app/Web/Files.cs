using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace CoAInstaller.Web
{
    /// <summary>
    /// Files and folders on the computer the server runs on. The page cannot open a folder dialog of the system, so it
    /// browses with these: a folder's subfolders (and files of a kind), creating a folder, opening one in the file manager
    /// (on this computer only), handing a file out to the browser and taking one from it.
    /// </summary>
    static class Files
    {
        /// <summary>Shows a folder in the file manager (Explorer, or what xdg-open opens); nothing on a server without a desktop.</summary>
        public static bool Reveal(string path)
        {
            if (Program.Opts.Listen != null || string.IsNullOrEmpty(path)) return false;
            try
            {
                if (Os.Windows) Process.Start(new ProcessStartInfo("explorer.exe", MySql.Arg(path)) { UseShellExecute = true });
                else
                {
                    if (!AppWindow.CanShow) return false;
                    Process.Start(new ProcessStartInfo("xdg-open", MySql.Arg(path)) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true });
                }
                return true;
            }
            catch { return false; }
        }

        /// <summary>Opens a text file with the system's editor or viewer.</summary>
        public static bool OpenFile(string path)
        {
            if (Program.Opts.Listen != null || !File.Exists(path)) return false;
            try
            {
                if (Os.Windows) Process.Start(new ProcessStartInfo("notepad.exe", MySql.Arg(path)) { UseShellExecute = true });
                else
                {
                    if (!AppWindow.CanShow) return false;
                    Process.Start(new ProcessStartInfo("xdg-open", MySql.Arg(path)) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true });
                }
                return true;
            }
            catch { return false; }
        }

        // Files handed out once to the browser: a key that is good for one download.
        static readonly ConcurrentDictionary<string, Tuple<string, string, bool>> downloads = new ConcurrentDictionary<string, Tuple<string, string, bool>>();

        /// <summary>A link for the page to download a file under a name; <paramref name="deleteAfter"/> for a temporary file.</summary>
        public static string Offer(string file, string name, bool deleteAfter)
        {
            string key = Guid.NewGuid().ToString("N");
            downloads[key] = Tuple.Create(file, name ?? Path.GetFileName(file), deleteAfter);
            return "/api/files/download?k=" + key;
        }

        /// <summary>A temporary folder for uploads and exports.</summary>
        public static string Temp
        {
            get
            {
                string dir = Path.Combine(Path.GetTempPath(), "afk-realm-web");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static void Map(WebApplication app)
        {
            Api.Get(app, "/api/fs/list", context =>
            {
                string path = context.Request.Query["path"];
                string filter = context.Request.Query["files"];          // e.g. ".sql,.afkaccount" or "*"
                return List(path, filter);
            });

            Api.Post(app, "/api/fs/mkdir", a =>
            {
                string parent = a.Str("path") ?? ""; string name = (a.Str("name") ?? "").Trim();
                if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name == "." || name == "..") throw new UserError(L.T("This is not a valid folder name."));
                string full = Path.Combine(parent, name);
                Directory.CreateDirectory(full);
                return new { path = full };
            });

            Api.Post(app, "/api/fs/reveal", a => new { ok = Reveal(a.Str("path")) });

            app.MapGet("/api/files/download", async (HttpContext context) =>
            {
                Tuple<string, string, bool> entry;
                if (!downloads.TryRemove(context.Request.Query["k"].ToString(), out entry) || !File.Exists(entry.Item1)) { context.Response.StatusCode = 404; return; }
                context.Response.ContentType = "application/octet-stream";
                context.Response.Headers["Content-Disposition"] = "attachment; filename*=UTF-8''" + Uri.EscapeDataString(entry.Item2);
                using (var s = File.OpenRead(entry.Item1))
                {
                    context.Response.ContentLength = s.Length;
                    await s.CopyToAsync(context.Response.Body);
                }
                if (entry.Item3) { try { File.Delete(entry.Item1); } catch { } }
            });

            // A file from the browser; the answer is the path it was put at (a temporary folder), for the action that follows.
            app.MapPost("/api/files/upload", async (HttpContext context) =>
            {
                string name = Path.GetFileName(context.Request.Query["name"].ToString());
                if (string.IsNullOrEmpty(name)) name = "upload";
                string dir = Path.Combine(Temp, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, name);
                using (var f = File.Create(file)) await context.Request.Body.CopyToAsync(f);
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(new { path = file }));
            });
        }

        static object List(string path, string filter)
        {
            var exts = string.IsNullOrEmpty(filter) ? null : filter.Split(',').Select(e => e.Trim().ToLowerInvariant()).Where(e => e.Length > 0).ToList();
            if (string.IsNullOrEmpty(path))
            {
                // The places to start from: drives on Windows, the home folder and / on Linux.
                var roots = new List<object>();
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                roots.Add(new { name = L.T("Home folder"), path = home });
                string downloadsDir = Path.Combine(home, "Downloads");
                if (Directory.Exists(downloadsDir)) roots.Add(new { name = L.T("Downloads"), path = downloadsDir });
                if (Os.Windows)
                    foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable || d.DriveType == DriveType.Network)))
                        roots.Add(new { name = d.Name + (string.IsNullOrEmpty(d.VolumeLabel) ? "" : "  (" + d.VolumeLabel + ")"), path = d.RootDirectory.FullName });
                else
                {
                    roots.Add(new { name = "/", path = "/" });
                    foreach (var m in new[] { "/mnt", "/media", "/run/media" }) if (Directory.Exists(m)) roots.Add(new { name = m, path = m });
                }
                return new { path = "", parent = (string)null, dirs = roots, files = new object[0] };
            }
            string full = Path.GetFullPath(path);
            if (!Directory.Exists(full)) throw new UserError(L.F("The folder {0} does not exist.", full));
            var di = new DirectoryInfo(full);
            var dirs = new List<object>(); var files = new List<object>();
            try
            {
                foreach (var d in di.EnumerateDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
                {
                    if ((d.Attributes & FileAttributes.Hidden) != 0 || (!Os.Windows && d.Name.StartsWith("."))) continue;
                    dirs.Add(new { name = d.Name, path = d.FullName });
                }
                if (exts != null)
                    foreach (var f in di.EnumerateFiles().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        if (!Os.Windows && f.Name.StartsWith(".")) continue;
                        if (!exts.Contains("*") && !exts.Contains(f.Extension.ToLowerInvariant())) continue;
                        files.Add(new { name = f.Name, path = f.FullName, size = f.Length, modified = f.LastWriteTime.ToString("yyyy-MM-dd HH:mm") });
                    }
            }
            catch (UnauthorizedAccessException) { throw new UserError(L.F("You cannot open {0}.", full)); }
            return new { path = full, parent = di.Parent == null ? "" : di.Parent.FullName, dirs, files };
        }
    }

    static partial class Api
    {
        static void MapFiles(WebApplication app)
        {
            Files.Map(app);
            Post(app, "/api/open", a =>
            {
                var inst = Inst;
                string what = a.Str("what");
                string path = what == "logs" ? inst.ServerDir : what == "install-log" ? inst.InstallLog : what == "lang" ? L.UserFolder : inst.Root;
                bool opened = what == "install-log" ? Files.OpenFile(path) : Files.Reveal(path);
                return new { ok = opened, path };
            });
            // The new part of a log since the offset the page has (the console windows follow the logs this way).
            Get(app, "/api/log/tail", c =>
            {
                var inst = Inst;
                string what = c.Request.Query["what"];
                string path = what == "auth" ? Path.Combine(inst.ServerDir, "Auth.log") : Path.Combine(inst.ServerDir, "Server.log");
                long from; long.TryParse(c.Request.Query["from"], out from);
                if (!File.Exists(path)) return new { offset = 0L, text = "", reset = from > 0 };
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    bool reset = from > fs.Length || from < 0;
                    if (reset) from = 0;
                    long start = from == 0 ? Math.Max(0, fs.Length - 200000) : from;
                    if (fs.Length - start > 1000000) start = fs.Length - 1000000;
                    fs.Seek(start, SeekOrigin.Begin);
                    var buffer = new byte[fs.Length - start];
                    int n = 0, got;
                    while (n < buffer.Length && (got = fs.Read(buffer, n, buffer.Length - n)) > 0) n += got;
                    // only whole lines; the rest comes with the next call
                    int end = n;
                    while (end > 0 && buffer[end - 1] != (byte)'\n') end--;
                    if (end == 0 && n > 64000) end = n;
                    return new { offset = start + end, text = System.Text.Encoding.UTF8.GetString(buffer, 0, end), reset = reset || from == 0 };
                }
            });

            Get(app, "/api/text", c =>
            {
                // A log or text file of the server, for showing it in the page (its last part when large).
                var inst = Inst;
                string what = c.Request.Query["what"];
                string path = what == "install-log" ? inst.InstallLog : what == "server-log" ? Path.Combine(inst.ServerDir, "Server.log")
                    : what == "auth-log" ? Path.Combine(inst.ServerDir, "Auth.log") : what == "errors-log" ? Path.Combine(inst.ServerDir, "Errors.log")
                    : what == "restart-log" ? Path.Combine(inst.LogDir, "scheduled-restart.log") : null;
                if (path == null || !File.Exists(path)) return new { path, text = "" };
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    long start = Math.Max(0, fs.Length - 400000);
                    fs.Seek(start, SeekOrigin.Begin);
                    using (var r = new StreamReader(fs)) return new { path, text = r.ReadToEnd() };
                }
            });
        }
    }
}
