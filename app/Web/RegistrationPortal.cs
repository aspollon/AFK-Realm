using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
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
    public sealed class RegistrationSettings
    {
        public bool Enabled { get; set; }
        public int Port { get; set; } = 47820;
        public string Address { get; set; } = "";
        public string PublicUrl { get; set; } = "";
        public string ClientUrl { get; set; } = "";
        public int MaxAccountsPerIp { get; set; } = 10;
        public int WindowMinutes { get; set; } = 60;
    }

    /// <summary>
    /// The public registration site. It is deliberately a second web server with its own small set of routes:
    /// it must never expose the admin API of the local AFK Realm page. Players only need a browser.
    /// </summary>
    static class RegistrationPortal
    {
        public const int DefaultPort = 47820;
        static readonly object gate = new object();
        static readonly ConcurrentDictionary<string, List<DateTime>> registrations = new ConcurrentDictionary<string, List<DateTime>>();
        static WebApplication app;
        static Install current;
        static RegistrationSettings settings = new RegistrationSettings();
        public static string LastError;

        public static bool Running { get { lock (gate) return app != null; } }
        public static int Port { get { lock (gate) return settings.Port; } }

        static string ConfigPath(Install inst) { return Path.Combine(inst.Root, "Dependencies", "registration.json"); }

        public static RegistrationSettings Load(Install inst)
        {
            var s = new RegistrationSettings();
            try
            {
                string file = ConfigPath(inst);
                if (File.Exists(file))
                {
                    var loaded = JsonSerializer.Deserialize<RegistrationSettings>(File.ReadAllText(file));
                    if (loaded != null) s = loaded;
                }
            }
            catch { }
            if (s.Port < 1024 || s.Port > 65535) s.Port = DefaultPort;
            if (s.MaxAccountsPerIp < 0) s.MaxAccountsPerIp = 0;
            if (s.MaxAccountsPerIp > 100) s.MaxAccountsPerIp = 100;
            if (s.WindowMinutes < 1) s.WindowMinutes = 60;
            if (s.WindowMinutes > 10080) s.WindowMinutes = 10080;
            return s;
        }

        static void Save(Install inst, RegistrationSettings s)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath(inst)));
            File.WriteAllText(ConfigPath(inst), JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
        }

        public static object Status(Install inst)
        {
            if (inst == null) return new { enabled = false, running = false, port = DefaultPort, addresses = new object[0] };
            RegistrationSettings s = Load(inst);
            bool running;
            lock (gate) running = app != null && current != null && string.Equals(current.Root, inst.Root, StringComparison.OrdinalIgnoreCase);
            string realm = "", address = "";
            try { realm = Accounts.GetRealmName(inst); } catch { }
            try { address = AddressOf(inst, s); } catch { }
            bool patch = PatchFiles().Count > 0;
            return new
            {
                enabled = s.Enabled,
                running,
                port = s.Port,
                address,
                publicUrl = s.PublicUrl ?? "",
                clientUrl = s.ClientUrl ?? "",
                realm = realm,
                patch,
                maxAccountsPerIp = s.MaxAccountsPerIp,
                windowMinutes = s.WindowMinutes,
                error = LastError,
                addresses = Addresses(s.Port)
            };
        }

        public static void Apply(Install inst, bool enabled, int port, string address, string publicUrl, string clientUrl, int maxPerIp, int windowMinutes)
        {
            if (inst == null) throw new InvalidOperationException(L.T("Open a server first."));
            if (port < 1024 || port > 65535) throw new InvalidOperationException(L.T("Please choose a port between 1024 and 65535."));
            if (maxPerIp < 0 || maxPerIp > 100) throw new InvalidOperationException(L.T("The registration limit must be between 0 and 100."));
            if (windowMinutes < 1 || windowMinutes > 10080) throw new InvalidOperationException(L.T("The time window must be between 1 minute and 7 days."));
            string pu = (publicUrl ?? "").Trim(), cu = (clientUrl ?? "").Trim();
            if (pu.Length > 0 && !WebUrl(pu)) throw new InvalidOperationException(L.T("The public address must start with http:// or https://."));
            if (cu.Length > 0 && !WebUrl(cu)) throw new InvalidOperationException(L.T("The client download link must start with http:// or https://."));
            if (enabled) CheckPort(inst, port);
            var s = new RegistrationSettings
            {
                Enabled = enabled, Port = port, Address = (address ?? "").Trim(),
                PublicUrl = pu, ClientUrl = cu,
                MaxAccountsPerIp = maxPerIp, WindowMinutes = windowMinutes
            };
            lock (gate)
            {
                Stop();
                settings = s;
                current = inst;
                Save(inst, s);
                if (enabled) Start(inst, s);
            }
        }

        /// <summary>Follows the open server: starts the site when its config says so, and stops an old server's site.</summary>
        public static void StartIfEnabled(Install inst)
        {
            lock (gate)
            {
                if (inst == null) { Stop(); return; }
                var s = Load(inst);
                if (!s.Enabled) { Stop(); return; }
                if (app != null && current != null && string.Equals(current.Root, inst.Root, StringComparison.OrdinalIgnoreCase) && settings.Port == s.Port) return;
                Stop();
                current = inst;
                settings = s;
                Start(inst, s);
            }
        }

        public static void Stop()
        {
            var a = app;
            app = null;
            current = null;
            LastError = null;
            CloseFirewall();
            if (a == null) return;
            try { a.StopAsync().Wait(3000); } catch { }
            try { ((IDisposable)a).Dispose(); } catch { }
        }

        static void Start(Install inst, RegistrationSettings s)
        {
            try
            {
                CheckPort(inst, s.Port);
                OpenFirewall(s.Port);
                var a = Build(inst, s);
                a.StartAsync().Wait();
                app = a;
                LastError = null;
            }
            catch (Exception ex)
            {
                CloseFirewall();
                while (ex.InnerException != null) ex = ex.InnerException;
                LastError = ex is System.Net.Sockets.SocketException || ex is IOException
                    ? L.F("Port {0} is used by another program. Choose another port.", s.Port)
                    : ex.Message;
                throw new InvalidOperationException(LastError);
            }
        }

        static WebApplication Build(Install inst, RegistrationSettings s)
        {
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = Array.Empty<string>(), ContentRootPath = Paths.ExeDir });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(k =>
            {
                k.Limits.MaxRequestBodySize = 64 * 1024;
                k.Listen(IPAddress.Any, s.Port);
            });
            builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(o =>
            {
                o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            });
            var a = builder.Build();

            a.Use(async (ctx, next) =>
            {
                ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
                ctx.Response.Headers["X-Frame-Options"] = "DENY";
                ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
                await next();
            });
            a.MapGet("/", ctx => Page(ctx, inst, s));
            a.MapGet("/register", ctx => Page(ctx, inst, s));
            a.MapGet("/api/public/info", ctx => Info(ctx, inst, s));
            a.MapPost("/api/public/register", ctx => Register(ctx, inst, s));
            a.MapGet("/download/realmlist.wtf", ctx => TextFile(ctx, "realmlist.wtf", "set realmlist " + Clean(AddressOf(inst, s)) + "\r\n"));
            a.MapGet("/download/Config.wtf", ctx => TextFile(ctx, "Config.wtf", ConfigText(inst, s)));
            a.MapGet("/download/client-patch", ctx => ServeClientPatch(ctx));
            a.MapGet("/favicon.ico", ctx => { ctx.Response.StatusCode = 204; return Task.CompletedTask; });
            a.MapFallback(ctx => { ctx.Response.StatusCode = 404; return Task.CompletedTask; });
            return a;
        }

        static async Task Page(HttpContext ctx, Install inst, RegistrationSettings s)
        {
            string realm = "";
            try { realm = Accounts.GetRealmName(inst); } catch { }
            string address = "";
            try { address = AddressOf(inst, s); } catch { }
            string body = PageHtml(realm, address, PatchFiles().Count > 0, s.ClientUrl);
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.Headers["Cache-Control"] = "no-store";
            ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
            ctx.Response.Headers["X-Frame-Options"] = "DENY";
            ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
            await ctx.Response.WriteAsync(body);
        }

        static async Task Info(HttpContext ctx, Install inst, RegistrationSettings s)
        {
            string realm = "";
            try { realm = Accounts.GetRealmName(inst); } catch { }
            await Json(ctx, new
            {
                open = s.Enabled,
                realm,
                address = AddressOf(inst, s),
                patch = PatchFiles().Count > 0,
                clientUrl = ClientUrlOf(s.ClientUrl),
                realmlist = "/download/realmlist.wtf",
                config = "/download/Config.wtf",
                clientPatch = "/download/client-patch"
            });
        }

        static async Task Register(HttpContext ctx, Install inst, RegistrationSettings s)
        {
            if (!s.Enabled) { await Error(ctx, 403, L.T("Registration is closed.")); return; }
            string ip = ctx.Connection.RemoteIpAddress == null ? "" : ctx.Connection.RemoteIpAddress.ToString();
            string name = "", pw = "", pw2 = "";
            try
            {
                using (var doc = await JsonDocument.ParseAsync(ctx.Request.Body))
                {
                    var root = doc.RootElement;
                    name = root.TryGetProperty("name", out var a) ? (a.GetString() ?? "") : "";
                    pw = root.TryGetProperty("password", out var b) ? (b.GetString() ?? "") : "";
                    pw2 = root.TryGetProperty("password2", out var c) ? (c.GetString() ?? "") : "";
                }
            }
            catch { await Error(ctx, 400, L.T("The request could not be read.")); return; }
            name = name.Trim();
            string invalid = Accounts.Validate(name, pw, pw2);
            if (invalid != null) { await Error(ctx, 400, invalid); return; }
            if (!Reserve(ip, s)) { await Error(ctx, 429, L.T("Too many registration attempts from this address. Try again later.")); return; }
            try
            {
                EnsureDatabase(inst);
                Accounts.Create(inst, name, pw, 0);
                string realm = "";
                try { realm = Accounts.GetRealmName(inst); } catch { }
                await Json(ctx, new
                {
                    ok = true,
                    name = name.ToUpperInvariant(),
                    realm,
                    address = AddressOf(inst, s),
                    realmlist = "/download/realmlist.wtf",
                    config = "/download/Config.wtf",
                    clientPatch = PatchFiles().Count > 0 ? "/download/client-patch" : null
                });
            }
            catch (Exception ex)
            {
                try { Program.StartLog("public registration failed for " + name + ": " + ex.Message); } catch { }
                await Error(ctx, 400, L.T("The account could not be created. Please try again, choose another name or ask the server owner."));
            }
        }

        static void EnsureDatabase(Install inst)
        {
            ServerControl ctl = Manager.Ctl;
            if (ctl == null || Manager.Inst == null || !string.Equals(Manager.Inst.Root, inst.Root, StringComparison.OrdinalIgnoreCase))
                ctl = new ServerControl(inst);
            if (ctl.Db == null && !Net.PortOpen(ctl.DbPort)) ctl.StartDatabase(_ => { });
        }

        static string AddressOf(Install inst, RegistrationSettings s)
        {
            if (!string.IsNullOrWhiteSpace(s.Address))
            {
                string own = Clean(s.Address);
                if (own.Length > 0 && own != "127.0.0.1" && !own.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return own;
            }
            try
            {
                string a = GameLaunch.SharedAddress(inst);
                if (!string.IsNullOrWhiteSpace(a) && a != "127.0.0.1" && !a.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return Clean(a);
            }
            catch { }
            try
            {
                string a = Accounts.GetRealmAddress(inst);
                if (!string.IsNullOrWhiteSpace(a) && a != "127.0.0.1" && !a.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return Clean(a);
            }
            catch { }
            return "";
        }

        static string Clean(string value)
        {
            return (value ?? "").Replace("\r", " ").Replace("\n", " ").Replace("\"", "").Trim();
        }

        /// <summary>The portal must not fight the admin page or a server process for a port.</summary>
        static void CheckPort(Install inst, int port)
        {
            if (WebHost.Port > 0 && port == WebHost.Port)
                throw new InvalidOperationException(L.F("Port {0} is used by the AFK Realm admin page. Choose another port.", port));
            if (RemoteAccess.Running && port == RemoteAccess.Port)
                throw new InvalidOperationException(L.F("Port {0} is used by the remote access to AFK Realm. Choose another port.", port));
            try
            {
                ServerControl ctl = Manager.Ctl;
                if (ctl == null || Manager.Inst == null || !string.Equals(Manager.Inst.Root, inst.Root, StringComparison.OrdinalIgnoreCase))
                    ctl = new ServerControl(inst);
                if (port == ctl.AuthPort || port == ctl.WorldPort || port == ctl.DbPort)
                    throw new InvalidOperationException(L.F("Port {0} is used by the server. Choose another port.", port));
            }
            catch (InvalidOperationException) { throw; }
            catch { }
        }

        static bool WebUrl(string url)
        {
            Uri uri;
            return Uri.TryCreate((url ?? "").Trim(), UriKind.Absolute, out uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }

        static string ClientUrlOf(string url)
        {
            url = (url ?? "").Trim();
            return WebUrl(url) ? url : "";
        }

        static string ConfigText(Install inst, RegistrationSettings s)
        {
            string realm = "";
            try { realm = Accounts.GetRealmName(inst); } catch { }
            string address = Clean(AddressOf(inst, s));
            var b = new StringBuilder();
            b.Append("SET realmList \"" + address + "\"\r\n");
            if (!string.IsNullOrWhiteSpace(realm)) b.Append("SET realmName \"" + Clean(realm) + "\"\r\n");
            return b.ToString();
        }

        static bool Reserve(string ip, RegistrationSettings s)
        {
            if (s.MaxAccountsPerIp <= 0) return true;
            var list = registrations.GetOrAdd(ip, _ => new List<DateTime>());
            lock (list)
            {
                DateTime since = DateTime.UtcNow.AddMinutes(-s.WindowMinutes);
                list.RemoveAll(t => t < since);
                if (list.Count >= s.MaxAccountsPerIp) return false;
                list.Add(DateTime.UtcNow);
                return true;
            }
        }

        static List<string> PatchFiles()
        {
            var result = new List<string>();
            try
            {
                string client = ClientAddons.ClientFolder;
                if (string.IsNullOrWhiteSpace(client) || !Directory.Exists(client)) return result;
                foreach (string name in new[] { ClientPatch.ArchiveName, "patch-ZZZZZY.MPQ", "patch-ZZZZY.MPQ" })
                {
                    string file = Path.Combine(client, "Data", name);
                    if (File.Exists(file)) result.Add(file);
                }
            }
            catch { }
            return result;
        }

        static async Task ServeClientPatch(HttpContext ctx)
        {
            var files = PatchFiles();
            if (files.Count == 0) { ctx.Response.StatusCode = 404; return; }
            string zip = PatchZip(files);
            await FileDownload(ctx, zip, "AFK-client-patch.zip");
        }

        static readonly object patchGate = new object();
        static string patchZipPath, patchZipKey;

        static string PatchZip(List<string> files)
        {
            string key = string.Join("|", files.Select(f =>
            {
                var info = new FileInfo(f);
                return Path.GetFileName(f) + ":" + info.Length + ":" + info.LastWriteTimeUtc.Ticks;
            }));
            lock (patchGate)
            {
                if (patchZipPath != null && patchZipKey == key && File.Exists(patchZipPath)) return patchZipPath;
                string folder = Path.Combine(Paths.DataDir, "public-registration");
                Directory.CreateDirectory(folder);
                string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(key))).Substring(0, 12);
                string target = Path.Combine(folder, "client-patch-" + hash + ".zip");
                string temp = target + ".tmp";
                if (File.Exists(temp)) File.Delete(temp);
                using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
                    foreach (string file in files)
                        zip.CreateEntryFromFile(file, "Data/" + Path.GetFileName(file), CompressionLevel.Optimal);
                if (File.Exists(target)) File.Delete(target);
                File.Move(temp, target);
                patchZipPath = target;
                patchZipKey = key;
                return target;
            }
        }

        static async Task FileDownload(HttpContext ctx, string path, string name)
        {
            ctx.Response.ContentType = name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? "application/zip" : "application/octet-stream";
            ctx.Response.Headers["Cache-Control"] = "no-store";
            ctx.Response.Headers["Content-Disposition"] = "attachment; filename*=UTF-8''" + Uri.EscapeDataString(name);
            using (var stream = File.OpenRead(path))
            {
                ctx.Response.ContentLength = stream.Length;
                await stream.CopyToAsync(ctx.Response.Body);
            }
        }

        static async Task TextFile(HttpContext ctx, string name, string text)
        {
            if (text.Contains("realmlist \r\n") || text.Contains("realmList \"\""))
            {
                ctx.Response.StatusCode = 404;
                await ctx.Response.WriteAsync("The realm address is not set yet. Set it in AFK Realm under Settings > Play with others.");
                return;
            }
            ctx.Response.ContentType = "text/plain; charset=utf-8";
            ctx.Response.Headers["Cache-Control"] = "no-store";
            ctx.Response.Headers["Content-Disposition"] = "attachment; filename*=UTF-8''" + Uri.EscapeDataString(name);
            await ctx.Response.WriteAsync(text);
        }

        static async Task Json(HttpContext ctx, object value)
        {
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.Headers["Cache-Control"] = "no-store";
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        }

        static async Task Error(HttpContext ctx, int code, string message)
        {
            ctx.Response.StatusCode = code;
            await Json(ctx, new { ok = false, error = message });
        }

        static List<object> Addresses(int port)
        {
            var list = new List<object>();
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var u in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (u.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(u.Address)) continue;
                        string ip = u.Address.ToString();
                        if (ip.StartsWith("169.254.")) continue;
                        list.Add(new { url = "http://" + ip + ":" + port + "/", network = nic.Name });
                    }
                }
            }
            catch { }
            return list;
        }

        const string FirewallRule = "AFK Realm public registration";

        /// <summary>Windows: a port rule for players outside this PC. The portal is meant to be reachable from the
        /// internet, so unlike the admin page's remote access rule it is not limited to the local subnet.</summary>
        static void OpenFirewall(int port)
        {
            if (!Os.Windows) return;
            try
            {
                Run("netsh", "advfirewall firewall delete rule name=\"" + FirewallRule + "\"");
                Run("netsh", "advfirewall firewall add rule name=\"" + FirewallRule + "\" dir=in action=allow protocol=TCP localport=" + port + " remoteip=any profile=any");
            }
            catch { }
        }

        static void CloseFirewall()
        {
            if (!Os.Windows) return;
            try { Run("netsh", "advfirewall firewall delete rule name=\"" + FirewallRule + "\""); } catch { }
        }

        static void Run(string file, string args)
        {
            using (var p = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }))
            {
                p.StandardOutput.ReadToEnd();
                p.WaitForExit(30000);
            }
        }

        static string PageHtml(string realm, string address, bool patch, string clientUrl)
        {
            string title = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(realm) ? Product.Name : realm);
            string shownAddress = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(address) ? "not set yet" : address);
            string addressNote = string.IsNullOrWhiteSpace(address)
                ? "<div class=\"small bad\" style=\"margin-top:10px\">The server address is not set yet. Ask the server owner to set it under Settings &gt; Play with others.</div>"
                : "";
            string patchLink = patch ? "<a class=\"button secondary\" href=\"/download/client-patch\">Client patch ZIP</a>" : "";
            string realmLinks = string.IsNullOrWhiteSpace(address) ? "" :
                "<a class=\"button secondary\" href=\"/download/realmlist.wtf\">realmlist.wtf</a>" +
                "<a class=\"button secondary\" href=\"/download/Config.wtf\">Config.wtf</a>";
            string client = ClientUrlOf(clientUrl);
            string clientLink = client.Length > 0 ? "<a class=\"button secondary\" href=\"" + WebUtility.HtmlEncode(client) + "\" target=\"_blank\" rel=\"noopener\">Game client</a>" : "";
            return "<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">" +
                "<title>" + title + " - Account</title><style>" +
                "*{box-sizing:border-box}body{margin:0;background:#15131e;color:#e9e7f2;font:16px/1.5 'Segoe UI',system-ui,sans-serif;display:grid;place-items:center;min-height:100vh;padding:18px}" +
                ".box{width:min(560px,100%);background:#211e2c;border:1px solid #343140;border-radius:16px;padding:28px;box-shadow:0 20px 60px #0008}" +
                "h1{margin:0 0 4px;font-size:28px}.muted{color:#aaa5bb;font-size:14px}.bad{color:#ff8c8c}.ok{color:#8ee6a8}" +
                "label{display:block;margin:14px 0 0;font-size:14px}input{width:100%;margin-top:6px;padding:11px;border-radius:9px;border:1px solid #545064;background:#15131e;color:inherit;font:inherit}" +
                "button,.button{display:inline-flex;align-items:center;justify-content:center;margin:16px 8px 0 0;padding:11px 18px;border:0;border-radius:9px;background:#6c4cc4;color:#fff;font:inherit;font-weight:650;text-decoration:none;cursor:pointer}" +
                ".secondary{background:#343140;color:#e9e7f2}" +
                ".downloads{display:flex;flex-wrap:wrap;margin-top:14px;gap:8px}.done{margin-top:18px;padding:14px;border:1px solid #426b51;background:#1b2b22;border-radius:10px}" +
                "code{background:#15131e;border:1px solid #343140;border-radius:5px;padding:2px 5px}.small{font-size:13px}</style></head><body><main class=\"box\">" +
                "<h1>" + title + "</h1><div class=\"muted\">Create an account for this server. It is free and needs no email address.</div>" +
                "<form id=\"form\"><label>Account name<input id=\"name\" autocomplete=\"username\" maxlength=\"16\" required></label>" +
                "<label>Password<input id=\"password\" type=\"password\" autocomplete=\"new-password\" maxlength=\"16\" required></label>" +
                "<label>Repeat password<input id=\"password2\" type=\"password\" autocomplete=\"new-password\" maxlength=\"16\" required></label>" +
                "<div class=\"small muted\" style=\"margin-top:8px\">Name: 3-16 letters or digits. Password: 4-16 characters.</div>" +
                "<button id=\"submit\">Create account</button></form><div id=\"msg\" class=\"small\"></div>" +
                addressNote +
                "<div class=\"small muted\" style=\"margin-top:18px\">Server address: <code>" + shownAddress + "</code></div>" +
                "<div id=\"done\" class=\"done\" style=\"display:none\"><b>Account created.</b><div class=\"small\">You can now log in with this account in the game.</div>" +
                "<div class=\"downloads\">" + realmLinks + patchLink + clientLink + "</div>" +
                "<div class=\"small muted\" style=\"margin-top:10px\">Put <code>realmlist.wtf</code> in the game folder (or <code>Data\\enUS</code>) and merge <code>Config.wtf</code> into <code>WTF\\Config.wtf</code>. The client patch ZIP belongs into the game folder.</div></div>" +
                "</main><script>const f=document.getElementById('form'),m=document.getElementById('msg'),d=document.getElementById('done'),b=document.getElementById('submit');" +
                "f.addEventListener('submit',async e=>{e.preventDefault();m.textContent='';b.disabled=true;try{const r=await fetch('/api/public/register',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({name:document.getElementById('name').value,password:document.getElementById('password').value,password2:document.getElementById('password2').value})});const j=await r.json();if(!r.ok||!j.ok)throw new Error(j.error||'Registration failed.');f.style.display='none';d.style.display='block';m.textContent='Welcome, '+j.name+'.';m.className='small ok'}catch(err){m.textContent=err.message;m.className='small bad'}finally{b.disabled=false}});" +
                "</script></body></html>";
        }
    }
}
