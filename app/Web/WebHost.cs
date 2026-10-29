using System;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CoAInstaller.Web
{
    /// <summary>
    /// The web server: the page (from the program itself), its interface (/api/...) and the events. Only this computer
    /// can reach it, and only with the key the window was opened with (a cookie): another user of the computer or a web
    /// page in a browser cannot. With --listen it is reachable from elsewhere, behind a password.
    /// </summary>
    static class WebHost
    {
        public static string Token;
        /// <summary>True while a request of another computer is answered.</summary>
        public static readonly System.Threading.AsyncLocal<bool> RequestRemote = new System.Threading.AsyncLocal<bool>();
        public static int Port;
        static WebApplication app;
        static readonly ConcurrentDictionary<string, DateTime> sessions = new ConcurrentDictionary<string, DateTime>();
        public static readonly CancellationTokenSource Stopping = new CancellationTokenSource();

        public static int Run()
        {
            Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(20)).ToLowerInvariant();
            bool remote = Program.Opts.Listen != null;
            Port = Program.Opts.Port > 0 ? Program.Opts.Port : FreePort(remote ? 47800 : 47810);

            app = Build(k =>
            {
                if (remote) k.Listen(IPAddress.Parse(Program.Opts.Listen == "*" ? "0.0.0.0" : Program.Opts.Listen), Port);
                else k.Listen(IPAddress.Loopback, Port);
            }, remote);

            var instance = new Instance { Pid = Environment.ProcessId, Port = Port, Token = Token, Host = remote ? null : "127.0.0.1" };
            instance.Save();
            AppDomain.CurrentDomain.ProcessExit += (s, e) => Instance.Remove();
            Console.CancelKeyPress += (s, e) => { e.Cancel = true; Shutdown(); };

            app.Lifetime.ApplicationStopping.Register(Shutdown);     // SIGTERM, Ctrl+C
            app.StartAsync().Wait();
            RemoteAccess.StartIfEnabled();

            // The last server opened, or the one asked for.
            try
            {
                string root = Program.Opts.Server ?? Settings.LastInstall;
                if (!string.IsNullOrEmpty(root) && new Install(root).IsInstalled) Manager.Open(new Install(root));
            }
            catch { }

            string localUrl = "http://127.0.0.1:" + Port + "/login?t=" + Token;
            if (remote)
            {
                Console.WriteLine(L.F("{0} is running at {1} (log in with the password).", Product.Name, "http://" + (Program.Opts.Listen == "0.0.0.0" || Program.Opts.Listen == "*" ? Dns.GetHostName() : Program.Opts.Listen) + ":" + Port + "/"));
            }
            else if (Program.Opts.NoWindow || !AppWindow.CanShow)
            {
                Console.WriteLine(L.F("{0} is running. Open this address in a browser on this computer:", Product.Name));
                Console.WriteLine("  " + localUrl);
                if (!AppWindow.CanShow && !Program.Opts.NoWindow)
                    Console.WriteLine(L.T("(No desktop was found. On a server without a screen, start with --listen 0.0.0.0 --password <password> to manage it from another computer, or use an SSH tunnel.)"));
            }
            else
            {
                bool opened = AppWindow.Open(localUrl, true);
                Program.StartLog("window: " + (AppWindow.Current != null ? "app window (" + AppWindow.Browser + ")" : opened ? "default browser" : "could not be opened"));
            }

            Watch(remote, localUrl);
            RemoteAccess.Stop();
            try { app.StopAsync().Wait(5000); } catch { }
            Instance.Remove();
            return 0;
        }

        /// <summary>A web server with the page and its interface: the local one of the window, or (remote) the one for other computers.</summary>
        public static WebApplication Build(Action<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions> listen, bool remote)
        {
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = new string[0], ContentRootPath = Paths.ExeDir });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(k =>
            {
                k.Limits.MaxRequestBodySize = 2L * 1024 * 1024 * 1024;     // account files, SQL imports, race packs
                listen(k);
            });
            builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(o =>
            {
                o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
                o.SerializerOptions.DictionaryKeyPolicy = null;
            });
            var app = builder.Build();

            app.Use(async (context, next) =>
            {
                var path = context.Request.Path.Value ?? "/";
                RequestRemote.Value = remote;
                if (path == "/ping") { await context.Response.WriteAsync("AFK Realm"); return; }
                if (!remote && !LocalHost(context.Request.Host.Host))
                {
                    // A page of another site that made its name point here (DNS rebinding) gets nothing.
                    context.Response.StatusCode = 403; return;
                }
                if (path == "/login") { await Login(context, remote); return; }
                if (!Authorized(context, remote))
                {
                    if (path.StartsWith("/api/")) { context.Response.StatusCode = 401; return; }
                    if (remote) { context.Response.Redirect("/login"); return; }
                    context.Response.StatusCode = 401;
                    context.Response.ContentType = "text/html; charset=utf-8";
                    await context.Response.WriteAsync("<!doctype html><meta charset=utf-8><title>AFK Realm</title><body style=\"font-family:sans-serif;padding:40px\">" +
                        WebUtility.HtmlEncode(L.F("Open {0} from its own window (start the program again).", Product.Name)) + "</body>");
                    return;
                }
                if (path.StartsWith("/api/") && context.Request.Method != "GET" && context.Request.Method != "HEAD")
                {
                    // Changes only from the page itself: a form on another site cannot set this header.
                    if (context.Request.Headers["X-AFK"] != "1") { context.Response.StatusCode = 403; return; }
                }
                await next();
            });

            Api.Map(app);
            app.MapFallback("{**path}", ServeStatic);
            return app;
        }

        /// <summary>
        /// Runs until the program should end: its window was closed (and nothing runs any more), or - for a page in a
        /// normal browser - no page has been open for ten minutes. A server that is reachable from elsewhere and the
        /// command without a window run until they are stopped.
        /// </summary>
        static void Watch(bool remote, string localUrl = null)
        {
            bool tracked = AppWindow.Current != null;
            bool everSeen = false, fallback = false, retried = false;
            DateTime started = DateTime.UtcNow;
            while (!Stopping.IsCancellationRequested)
            {
                Stopping.Token.WaitHandle.WaitOne(2000);
                if (Stopping.IsCancellationRequested) break;
                if (remote || Program.Opts.NoWindow || RemoteAccess.Running) continue;     // others may still come
                if (Jobs.Busy || Manager.AutoRestarting) continue;
                if (Events.Count > 0 && !everSeen) { everSeen = true; Program.StartLog("the page connected"); }
                if (tracked)
                {
                    var w = AppWindow.Current;
                    bool closed = w == null || w.HasExited;
                    // The app window closed before its page ever reached this program (the browser failed to start, an
                    // antivirus stopped it, a firewall keeps it from 127.0.0.1): AFK Realm used to end here without a word.
                    // The page goes to the default browser once, and a note says where it is.
                    if (closed && !everSeen && !retried && localUrl != null && (DateTime.UtcNow - started).TotalSeconds > 6)
                    {
                        // first once more in a new profile folder: a browser process left over from an earlier window can
                        // hold the usual one, take the window over and never show it
                        retried = true;
                        Program.StartLog("the app window closed before its page connected (exit code " + SafeExit(w) + "); trying once more with a new browser profile");
                        AppWindow.Current = null;
                        AppWindow.Open(localUrl, true, true);
                        started = DateTime.UtcNow;
                        Events.LastSeen = DateTime.UtcNow;
                        if (AppWindow.Current != null) continue;
                    }
                    w = AppWindow.Current;
                    closed = w == null || w.HasExited;
                    if (closed && !everSeen && !fallback && localUrl != null && (DateTime.UtcNow - started).TotalSeconds > 6)
                    {
                        fallback = true;
                        tracked = false;
                        Program.StartLog("the app window closed before its page connected (exit code " + SafeExit(w) + "); opening the default browser");
                        try { Process.Start(new ProcessStartInfo(localUrl) { UseShellExecute = true }); } catch (Exception ex) { Program.StartLog("default browser: " + ex.Message); }
                        Events.LastSeen = DateTime.UtcNow;
                        continue;
                    }
                    // Some browsers hand the window to a process that already runs and end at once: then the pages count.
                    if (closed && Events.Count == 0 && (DateTime.UtcNow - Events.LastSeen).TotalSeconds > 8) break;
                }
                else if (Events.Count == 0 && (DateTime.UtcNow - Events.LastSeen).TotalMinutes > 10) break;
            }
        }

        static string SafeExit(Process p) { try { return p == null ? "-" : p.ExitCode.ToString(); } catch { return "?"; } }

        public static void Shutdown() { Stopping.Cancel(); }

        static bool LocalHost(string host)
        {
            return host == "127.0.0.1" || host == "localhost" || host == "[::1]";
        }

        static bool Authorized(HttpContext context, bool remote)
        {
            string cookie = context.Request.Cookies["afk"];
            if (cookie == null) return false;
            if (!remote) return FixedEquals(cookie, Token);
            DateTime until;
            return sessions.TryGetValue(cookie, out until) && until > DateTime.UtcNow;
        }

        static bool FixedEquals(string a, string b)
        {
            return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a ?? ""), Encoding.UTF8.GetBytes(b ?? ""));
        }

        static readonly ConcurrentDictionary<string, int> failures = new ConcurrentDictionary<string, int>();

        static async Task Login(HttpContext context, bool remote)
        {
            var options = new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/", IsEssential = true };
            if (!remote)
            {
                string t = context.Request.Query["t"];
                if (t != null && FixedEquals(t, Token))
                {
                    context.Response.Cookies.Append("afk", Token, options);
                    context.Response.Redirect("/");
                    return;
                }
                context.Response.StatusCode = 401;
                return;
            }
            string error = "";
            if (context.Request.Method == "POST")
            {
                string ip = context.Connection.RemoteIpAddress == null ? "" : context.Connection.RemoteIpAddress.ToString();
                int failed;
                failures.TryGetValue(ip, out failed);
                if (failed >= 10) { await Task.Delay(3000); }
                var form = await context.Request.ReadFormAsync();
                if (Program.Opts.Password != null ? FixedEquals(form["password"], Program.Opts.Password) : RemoteAccess.Verify(form["password"]))
                {
                    string session = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
                    sessions[session] = DateTime.UtcNow.AddDays(14);
                    failures.TryRemove(ip, out failed);
                    options.Expires = DateTimeOffset.UtcNow.AddDays(14);
                    context.Response.Cookies.Append("afk", session, options);
                    context.Response.Redirect("/");
                    return;
                }
                failures[ip] = failed + 1;
                await Task.Delay(800);
                error = "<p style=\"color:#c83232\">" + WebUtility.HtmlEncode(L.T("Wrong password.")) + "</p>";
            }
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync("<!doctype html><html><head><meta charset=utf-8><meta name=viewport content=\"width=device-width,initial-scale=1\"><title>AFK Realm</title>" +
                "<style>body{font-family:'Segoe UI',system-ui,sans-serif;background:#15131e;color:#e9e7f2;display:grid;place-items:center;min-height:100vh;margin:0}" +
                "form{background:#211e2c;padding:32px;border-radius:14px;border:1px solid #343140;width:min(340px,90vw)}input{width:100%;box-sizing:border-box;padding:10px;margin:12px 0;border-radius:8px;border:1px solid #545064;background:#15131e;color:inherit}" +
                "button{background:#6c4cc4;color:#fff;border:0;border-radius:8px;padding:10px 18px;font-weight:600;cursor:pointer}</style></head><body>" +
                "<form method=post><h2 style=\"margin-top:0\">AFK Realm</h2><label>" + WebUtility.HtmlEncode(L.T("Password")) + "</label><input type=password name=password autofocus>" + error +
                "<button>" + WebUtility.HtmlEncode(L.T("Log in")) + "</button></form></body></html>");
        }

        static int FreePort(int preferred)
        {
            for (int p = preferred; p < preferred + 40; p++)
            {
                try { var l = new TcpListener(IPAddress.Loopback, p); l.Start(); l.Stop(); return p; } catch { }
            }
            var any = new TcpListener(IPAddress.Loopback, 0); any.Start(); int port = ((IPEndPoint)any.LocalEndpoint).Port; any.Stop();
            return port;
        }

        // ---- the page ----

        static async Task ServeStatic(HttpContext context)
        {
            string path = (context.Request.Path.Value ?? "/").TrimStart('/');
            if (path.Length == 0 || !path.Contains('.')) path = "index.html";
            if (path.Contains("..")) { context.Response.StatusCode = 400; return; }
            byte[] data = null;
            if (!string.IsNullOrEmpty(Program.Opts.WebRoot))
            {
                string file = Path.Combine(Program.Opts.WebRoot, path.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(file)) data = await File.ReadAllBytesAsync(file);
            }
            if (data == null)
            {
                // a build on Windows names the resources of subfolders with a backslash (web/js\app.js)
                var asm = Assembly.GetExecutingAssembly();
                using (var s = asm.GetManifestResourceStream("web/" + path) ?? asm.GetManifestResourceStream("web/" + path.Replace('/', '\\')))
                {
                    if (s != null) { var ms = new MemoryStream(); await s.CopyToAsync(ms); data = ms.ToArray(); }
                }
            }
            if (data == null) { context.Response.StatusCode = 404; return; }
            context.Response.ContentType = Mime(path);
            context.Response.Headers["Cache-Control"] = "no-cache";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            if (path == "index.html")
                context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; img-src 'self' data: blob:; style-src 'self' 'unsafe-inline'; script-src 'self'; connect-src 'self'; frame-ancestors 'none'";
            await context.Response.Body.WriteAsync(data);
        }

        static string Mime(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            switch (ext)
            {
                case ".html": return "text/html; charset=utf-8";
                case ".js": return "text/javascript; charset=utf-8";
                case ".css": return "text/css; charset=utf-8";
                case ".svg": return "image/svg+xml";
                case ".png": return "image/png";
                case ".ico": return "image/x-icon";
                case ".json": return "application/json; charset=utf-8";
                case ".woff2": return "font/woff2";
                default: return "application/octet-stream";
            }
        }
    }
}
