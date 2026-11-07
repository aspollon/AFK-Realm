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
        public bool AllowInternet { get; set; }
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
        public const string DefaultClientUrl = "https://coa-development.org/download";
        static readonly object gate = new object();
        static readonly ConcurrentDictionary<string, List<DateTime>> registrations = new ConcurrentDictionary<string, List<DateTime>>();
        static readonly ConcurrentDictionary<string, List<DateTime>> loginAttempts = new ConcurrentDictionary<string, List<DateTime>>();
        static WebApplication app;
        static Install current;
        static RegistrationSettings settings = new RegistrationSettings();
        public static string LastError;

        public static bool Running { get { lock (gate) return app != null; } }
        public static int Port { get { lock (gate) return settings.Port; } }

        static string ConfigPath(Install inst) { return Path.Combine(inst.Root, "Dependencies", "registration.json"); }

        /// <summary>The optional complete player ZIP, built once from the owner's own client and kept for the portal.</summary>
        public static string ClientPackFile(Install inst)
        {
            string key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(inst.Root))).Substring(0, 12);
            return Path.Combine(Paths.DataDir, "public-registration", "client-pack-" + key + ".zip");
        }

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
            string pack = ClientPackFile(inst);
            bool clientPack = File.Exists(pack);
            return new
            {
                enabled = s.Enabled,
                running,
                port = s.Port,
                address,
                publicUrl = s.PublicUrl ?? "",
                clientUrl = s.ClientUrl ?? "",
                allowInternet = s.AllowInternet,
                realm = realm,
                patch,
                clientPack,
                clientPackAt = clientPack ? File.GetLastWriteTimeUtc(pack).ToString("yyyy-MM-dd HH:mm 'UTC'") : null,
                clientPackSize = clientPack ? new FileInfo(pack).Length : 0,
                maxAccountsPerIp = s.MaxAccountsPerIp,
                windowMinutes = s.WindowMinutes,
                error = LastError,
                addresses = Addresses(s.Port)
            };
        }

        public static void Apply(Install inst, bool enabled, int port, string address, string publicUrl, string clientUrl, bool allowInternet, int maxPerIp, int windowMinutes)
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
                PublicUrl = pu, ClientUrl = cu, AllowInternet = allowInternet,
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

        /// <summary>First-run defaults: make the portal LAN-ready instead of leaving every field empty.</summary>
        public static void EnsureDefaults(Install inst)
        {
            string lan = "";
            if (!File.Exists(ConfigPath(inst)))
            {
                lan = DefaultAddress(inst);
                var s = new RegistrationSettings
                {
                    Enabled = true, Port = DefaultPort, Address = lan,
                    PublicUrl = lan.Length > 0 ? "http://" + lan + ":" + DefaultPort + "/" : "",
                    AllowInternet = false
                };
                Save(inst, s);
                return;
            }

            // An older config may still have empty fields. Fill them once, but never overwrite
            // an address, port or limit the owner already chose.
            var current = Load(inst);
            bool changed = false;
            if (string.IsNullOrWhiteSpace(current.Address))
            {
                lan = DefaultAddress(inst);
                if (lan.Length > 0) { current.Address = lan; changed = true; }
            }
            if (string.IsNullOrWhiteSpace(current.PublicUrl) && !string.IsNullOrWhiteSpace(current.Address))
            {
                current.PublicUrl = "http://" + current.Address + ":" + current.Port + "/";
                changed = true;
            }
            if (changed) Save(inst, current);
        }

        /// <summary>When the realm still points at this PC, use the local network address so LAN players can log in.</summary>
        public static bool EnsureRealmDefaults(Install inst)
        {
            try
            {
                string currentAddress = (Accounts.GetRealmAddress(inst) ?? "").Trim();
                if (currentAddress.Length == 0 || currentAddress == "127.0.0.1" || currentAddress.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                {
                    string lan = LanAddress();
                    if (lan.Length > 0) Accounts.SetRealmAddress(inst, lan);
                }
                return true;
            }
            catch { return false; }
        }

        /// <summary>The address players already use, or the detected LAN address when it is still localhost.</summary>
        static string DefaultAddress(Install inst)
        {
            try
            {
                string address = (Accounts.GetRealmAddress(inst) ?? "").Trim();
                if (address.Length > 0 && address != "127.0.0.1" && !address.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                    return address;
            }
            catch { }
            return LanAddress();
        }

        /// <summary>The private IPv4 address of this PC, preferring a real LAN interface over VPN adapters.</summary>
        public static string LanAddress()
        {
            string fallback = "";
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    var props = nic.GetIPProperties();
                    bool gateway = props.GatewayAddresses.Any(g => g.Address != null && g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
                    foreach (var u in props.UnicastAddresses)
                    {
                        if (u.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(u.Address)) continue;
                        string ip = u.Address.ToString();
                        if (ip.StartsWith("169.254.")) continue;
                        if (privateAddress(ip))
                        {
                            if (gateway) return ip;
                            if (fallback.Length == 0) fallback = ip;
                        }
                        else if (fallback.Length == 0) fallback = ip;
                    }
                }
            }
            catch { }
            return fallback;
        }

        static bool privateAddress(string ip)
        {
            IPAddress address;
            if (!IPAddress.TryParse(ip, out address)) return false;
            byte[] b = address.GetAddressBytes();
            return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168);
        }

        static void Start(Install inst, RegistrationSettings s)
        {
            try
            {
                CheckPort(inst, s.Port);
                OpenFirewall(s.Port, s.AllowInternet);
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
            a.MapPost("/api/public/downloads", ctx => Downloads(ctx, inst, s));
            a.MapGet("/download/realmlist.wtf", ctx => TextFile(ctx, "realmlist.wtf", "set realmlist " + Clean(AddressOf(inst, s)) + "\r\n"));
            a.MapGet("/download/Config.wtf", ctx => TextFile(ctx, "Config.wtf", ConfigText(inst, s)));
            a.MapGet("/download/client-patch", ctx => ServeClientPatch(ctx));
            a.MapGet("/download/client-pack", ctx => ServeClientPack(ctx, inst));
            a.MapGet("/api/public/manifest", ctx => LauncherBuilder.Manifest(ctx, inst));
            a.MapGet("/download/launcher-file", ctx => LauncherBuilder.ServeFile(ctx, inst));
            a.MapGet("/download/launcher.json", ctx => LauncherBuilder.Config(ctx, inst));
            a.MapGet("/download/afk-client", ctx => LauncherBuilder.Package(ctx, inst));
            a.MapGet("/assets/afk-realm-background.jpg", ctx => Background(ctx));
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
            var patchFiles = PatchFiles();
            bool patch = patchFiles.Count > 0;
            string pack = ClientPackFile(inst);
            bool clientPack = File.Exists(pack);
            string packAt = clientPack
                ? File.GetLastWriteTimeUtc(pack).ToString("o")
                : patch ? patchFiles.Max(f => File.GetLastWriteTimeUtc(f)).ToString("o") : "";
            string body = PageHtml(realm, address, patch, clientPack, packAt, s.ClientUrl, LauncherBuilder.PackageAvailable(inst));
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
                clientPack = File.Exists(ClientPackFile(inst)),
                clientPackAt = File.Exists(ClientPackFile(inst))
                    ? File.GetLastWriteTimeUtc(ClientPackFile(inst)).ToString("o")
                    : PatchFiles().Count > 0 ? PatchFiles().Max(f => File.GetLastWriteTimeUtc(f)).ToString("o") : null,
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

        static async Task Downloads(HttpContext ctx, Install inst, RegistrationSettings s)
        {
            if (!s.Enabled) { await Error(ctx, 403, L.T("Registration is closed.")); return; }
            string ip = ctx.Connection.RemoteIpAddress == null ? "" : ctx.Connection.RemoteIpAddress.ToString();
            string name = "", pw = "";
            try
            {
                using (var doc = await JsonDocument.ParseAsync(ctx.Request.Body))
                {
                    var root = doc.RootElement;
                    name = root.TryGetProperty("name", out var a) ? (a.GetString() ?? "") : "";
                    pw = root.TryGetProperty("password", out var b) ? (b.GetString() ?? "") : "";
                }
            }
            catch { await Error(ctx, 400, L.T("The request could not be read.")); return; }
            name = name.Trim();
            if (!ReserveLogin(ip)) { await Error(ctx, 429, L.T("Too many login attempts from this address. Try again later.")); return; }
            bool valid = false;
            try { valid = VerifyAccount(inst, name, pw); }
            catch (Exception ex)
            {
                try { Program.StartLog("public downloads login failed: " + ex.Message); } catch { }
            }
            if (!valid) { await Error(ctx, 401, L.T("Wrong account name or password.")); return; }
            await Json(ctx, new
            {
                ok = true,
                name = name.ToUpperInvariant(),
                realm = RealmName(inst),
                address = AddressOf(inst, s),
                realmlist = "/download/realmlist.wtf",
                config = "/download/Config.wtf",
                clientPatch = PatchFiles().Count > 0 ? "/download/client-patch" : null,
                clientPack = File.Exists(ClientPackFile(inst)) ? "/download/client-pack" : null
            });
        }

        static string RealmName(Install inst)
        {
            try { return Accounts.GetRealmName(inst); } catch { return ""; }
        }

        /// <summary>Checks an existing account's SRP6 salt/verifier without changing it.</summary>
        static bool VerifyAccount(Install inst, string name, string pass)
        {
            if (Accounts.Validate(name, pass, pass) != null) return false;
            EnsureDatabase(inst);
            string sql = "SELECT HEX(salt), HEX(verifier) FROM acore_auth.account WHERE username=" +
                MySql.Quote(name.ToUpperInvariant()) + " LIMIT 1;";
            var rows = MySql.Query(inst, DbLogin.FromConfig(inst), sql);
            if (rows.Count == 0) return false;
            string[] parts = rows[0].Split('\t');
            if (parts.Length < 2 || parts[0].Length == 0 || parts[1].Length == 0) return false;
            byte[] salt = Convert.FromHexString(parts[0]);
            byte[] expected = Convert.FromHexString(parts[1]);
            byte[] actual = Srp6.Verifier(name, pass, salt);
            return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(actual, expected);
        }

        static bool ReserveLogin(string ip)
        {
            var list = loginAttempts.GetOrAdd(ip, _ => new List<DateTime>());
            lock (list)
            {
                DateTime since = DateTime.UtcNow.AddMinutes(-10);
                list.RemoveAll(t => t < since);
                if (list.Count >= 20) return false;
                list.Add(DateTime.UtcNow);
                return true;
            }
        }

        static void EnsureDatabase(Install inst)
        {
            ServerControl ctl = Manager.Ctl;
            if (ctl == null || Manager.Inst == null || !string.Equals(Manager.Inst.Root, inst.Root, StringComparison.OrdinalIgnoreCase))
                ctl = new ServerControl(inst);
            if (ctl.Db == null && !Net.PortOpen(ctl.DbPort)) ctl.StartDatabase(_ => { });
            try { EnsureRealmDefaults(inst); } catch { }
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
            return WebUrl(url) ? url : DefaultClientUrl;
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

        /// <summary>The public base URL of the registration page, without a trailing slash.</summary>
        public static string PublicBaseUrl(Install inst)
        {
            var s = Load(inst);
            string configured = (s.PublicUrl ?? "").Trim().TrimEnd('/');
            if (WebUrl(configured)) return configured;
            string address = Clean(AddressOf(inst, s));
            if (address.Length == 0) address = LanAddress();
            return address.Length > 0 ? "http://" + address + ":" + s.Port : "";
        }

        public static string PublicRealmAddress(Install inst)
        {
            var s = Load(inst);
            string address = Clean(AddressOf(inst, s));
            return address.Length > 0 ? address : LanAddress();
        }

        public static List<string> ClientPatchFiles()
        {
            return PatchFiles();
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

        static async Task ServeClientPack(HttpContext ctx, Install inst)
        {
            string file = ClientPackFile(inst);
            if (!File.Exists(file)) { ctx.Response.StatusCode = 404; return; }
            await FileDownload(ctx, file, "AFK-client-pack.zip");
        }

        /// <summary>Builds the complete player ZIP from the owner's own client: module add-ons, generated
        /// client patch, the extra subclass patches and a realmlist. It is cached and served by the portal.</summary>
        public static void BuildClientPack(Install inst, Action<string, int, int> progress)
        {
            string client = ClientAddons.ClientFolder;
            if (string.IsNullOrWhiteSpace(client) || !ClientAddons.IsClientFolder(client))
                throw new InvalidOperationException(L.T("Choose your game folder under Client build first. The client pack is built from your own client."));
            if (ClientAddons.ClientRunning(client))
                throw new InvalidOperationException(L.T("The game or its launcher is running from this folder. Close it before building the player client pack."));
            var addons = ClientAddons.Find(inst, client);
            var patch = ClientPatch.Collect(inst);
            if (addons.Count == 0 && patch.Empty)
                throw new InvalidOperationException(L.T("None of the installed modules brings an add-on or a client patch for the game client."));
            string target = ClientPackFile(inst);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            string temp = target + ".tmp";
            try
            {
                if (File.Exists(temp)) File.Delete(temp);
                ClientAddons.Export(temp, addons, inst, client, patch, progress);
                using (var zip = ZipFile.Open(temp, ZipArchiveMode.Update))
                {
                    string address = Clean(AddressOf(inst, Load(inst)));
                    if (address.Length > 0)
                    {
                        var old = zip.GetEntry("Data/enUS/realmlist.wtf");
                        if (old != null) old.Delete();
                        var entry = zip.CreateEntry("Data/enUS/realmlist.wtf");
                        using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
                            writer.Write("set realmlist " + address + "\r\n");
                    }
                    foreach (string file in PatchFiles())
                    {
                        string entry = "Data/" + Path.GetFileName(file);
                        if (zip.GetEntry(entry) == null)
                            zip.CreateEntryFromFile(file, entry, CompressionLevel.Optimal);
                    }
                }
                File.Move(temp, target, true);
            }
            catch
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                throw;
            }
        }

        static async Task Background(HttpContext ctx)
        {
            using (var stream = typeof(RegistrationPortal).Assembly.GetManifestResourceStream("web/assets/afk-realm-background.jpg") ??
                typeof(RegistrationPortal).Assembly.GetManifestResourceStream("web/assets\\afk-realm-background.jpg") ??
                typeof(RegistrationPortal).Assembly.GetManifestResourceStream("web\\assets\\afk-realm-background.jpg"))
            {
                if (stream == null) { ctx.Response.StatusCode = 404; return; }
                ctx.Response.ContentType = "image/jpeg";
                ctx.Response.Headers["Cache-Control"] = "public, max-age=86400";
                await stream.CopyToAsync(ctx.Response.Body);
            }
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
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                long length = stream.Length;
                long start = 0, end = length - 1;
                string range = ctx.Request.Headers["Range"];
                ctx.Response.Headers["Accept-Ranges"] = "bytes";
                if (!string.IsNullOrWhiteSpace(range) && range.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
                {
                    string part = range.Substring(6).Split(',')[0].Trim();
                    int dash = part.IndexOf('-');
                    if (dash < 0 || !long.TryParse(part.Substring(0, dash), out start) || start < 0 || start >= length)
                    {
                        ctx.Response.StatusCode = 416;
                        ctx.Response.Headers["Content-Range"] = "bytes */" + length;
                        return;
                    }
                    if (dash + 1 < part.Length && long.TryParse(part.Substring(dash + 1), out end)) end = Math.Min(end, length - 1);
                    else end = length - 1;
                    if (end < start)
                    {
                        ctx.Response.StatusCode = 416;
                        ctx.Response.Headers["Content-Range"] = "bytes */" + length;
                        return;
                    }
                    ctx.Response.StatusCode = 206;
                    ctx.Response.Headers["Content-Range"] = "bytes " + start + "-" + end + "/" + length;
                }
                ctx.Response.ContentLength = end - start + 1;
                stream.Position = start;
                byte[] buffer = new byte[81920];
                long left = end - start + 1;
                while (left > 0)
                {
                    int read = await stream.ReadAsync(buffer, 0, (int)Math.Min(buffer.Length, left));
                    if (read <= 0) break;
                    await ctx.Response.Body.WriteAsync(buffer, 0, read);
                    left -= read;
                }
            }
        }

        static async Task TextFile(HttpContext ctx, string name, string text)
        {
            if (text.Contains("realmlist \r\n") || text.Contains("realmList \"\""))
            {
                ctx.Response.StatusCode = 404;
                await ctx.Response.WriteAsync("The realm address is not set yet. Set it in AFK Realm under Access > Server network.");
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

        /// <summary>Windows: a port rule for the public page. It starts limited to the local network; internet
        /// access is an explicit opt-in, so an accidentally enabled portal does not open registration to the world.</summary>
        static void OpenFirewall(int port, bool internet)
        {
            if (!Os.Windows) return;
            try
            {
                Run("netsh", "advfirewall firewall delete rule name=\"" + FirewallRule + "\"");
                Run("netsh", "advfirewall firewall add rule name=\"" + FirewallRule + "\" dir=in action=allow protocol=TCP localport=" + port + " remoteip=" + (internet ? "any" : "localsubnet") + " profile=any");
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

        static string PageHtml(string realm, string address, bool patch, bool clientPack, string clientPackAt, string clientUrl, bool launcherPack)
        {
            string title = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(realm) ? Product.Name : realm);
            string shownAddress = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(address) ? "not set yet" : address);
            string addressNote = string.IsNullOrWhiteSpace(address)
                ? "<div class=\"small bad\" style=\"margin-top:10px\">The server address is not set yet. Ask the server owner to set it under Access &gt; Server network.</div>"
                : "";
            string clientFiles = launcherPack ? "<a class=\"button\" href=\"/download/afk-client\">AFK Client (recommended)</a>" : "";
            clientFiles += clientPack
                ? "<a class=\"button secondary\" id=\"clientUpdateLink\" href=\"/download/client-pack\">AFK client pack (add-ons + patches)</a>"
                : patch ? "<a class=\"button secondary\" id=\"clientUpdateLink\" href=\"/download/client-patch\">Client patch ZIP</a>" : "";
            string clientLabel = clientPack ? "AFK client pack" : "AFK client patch";
            string realmLinks = string.IsNullOrWhiteSpace(address) ? "" :
                "<a class=\"button secondary\" href=\"/download/realmlist.wtf\">realmlist.wtf</a>" +
                "<a class=\"button secondary\" href=\"/download/Config.wtf\">Config.wtf</a>";
            string client = ClientUrlOf(clientUrl);
            string clientLink = "<a class=\"button secondary\" href=\"" + WebUtility.HtmlEncode(client) + "\" target=\"_blank\" rel=\"noopener\">Game client / launcher</a>";
            string instructions = launcherPack
                ? "Download the <code>AFK Client</code>, unzip it into a folder of its own and start <code>AFK-Client.exe</code>. It chooses the game folder once and keeps add-ons, patches and realmlist up to date."
                : clientPack
                ? "Unzip the <code>AFK client pack</code> into the game folder. It contains the module add-ons and the client patches. Then merge <code>Config.wtf</code> into <code>WTF\\Config.wtf</code>."
                : patch
                    ? "Unzip the <code>client patch ZIP</code> into the game folder, then merge <code>Config.wtf</code> into <code>WTF\\Config.wtf</code>. For custom races, also copy the matching add-ons from the pack."
                    : "Use <code>realmlist.wtf</code> and <code>Config.wtf</code>, and ask the server owner for the module patches or add-ons.";
            string page = """
<!doctype html>
<html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>__TITLE__ - Account</title>
<style>
*{box-sizing:border-box}
html{background:#08070d}
body{margin:0;min-height:100dvh;background:#08070d;color:#e9e7f2;font:16px/1.5 'Segoe UI',system-ui,sans-serif;overflow-x:hidden}
body::before{content:"";position:fixed;inset:0;background:#08070d url('/assets/afk-realm-background.jpg') center top/cover no-repeat;z-index:0}
body::after{content:"";position:fixed;inset:0;background:linear-gradient(180deg,rgba(8,6,14,.04) 0%,rgba(8,6,14,.14) 30%,rgba(8,6,14,.82) 100%);z-index:0;pointer-events:none}
.stage{position:relative;z-index:1;min-height:100dvh;display:flex;flex-direction:column;align-items:center;padding:clamp(18px,3vw,36px)}
.logo-space{height:clamp(190px,38vh,440px);flex:0 0 auto;width:100%}
.box{width:min(540px,100%);background:rgba(24,21,34,.88);border:1px solid rgba(151,130,216,.36);border-radius:18px;padding:28px;box-shadow:0 30px 80px #000b;backdrop-filter:blur(16px);-webkit-backdrop-filter:blur(16px);margin-bottom:28px;text-align:center}
h1{margin:0 0 4px;font-size:28px;letter-spacing:.2px}.muted{color:#c2bcd4;font-size:14px}.bad{color:#ff9a9a}.ok{color:#9ef0b4}.warn{color:#ffd479}
label{display:block;margin:14px 0 0;font-size:14px}input{width:100%;margin-top:6px;padding:11px;border-radius:9px;border:1px solid #625a7b;background:#15131e;color:inherit;font:inherit;text-align:center}
input:focus{outline:2px solid #9b7bea;outline-offset:1px;border-color:#9b7bea}
button,.button{display:inline-flex;align-items:center;justify-content:center;margin:16px 4px 0;padding:11px 18px;border:0;border-radius:9px;background:#7b57d6;color:#fff;font:inherit;font-weight:650;text-decoration:none;cursor:pointer}
#submit,#loginSubmit{display:flex;margin:18px auto 0}
button:hover,.button:hover{background:#8d67ec}.secondary{background:#3a344b;color:#e9e7f2}.secondary:hover{background:#4a4360}
.downloads{display:flex;flex-wrap:wrap;justify-content:center;margin-top:14px;gap:8px}.done{margin-top:18px;padding:14px;border:1px solid #426b51;background:#182b20;border-radius:10px}
code{background:#15131e;border:1px solid #3d374d;border-radius:5px;padding:2px 5px}.small{font-size:13px}
@media(max-aspect-ratio:4/3){body::before{background-size:100% auto}.logo-space{height:clamp(210px,32vh,320px)}}
@media(max-width:560px){.box{padding:22px}.downloads .button{width:100%}}
</style></head>
<body><div class="stage"><div class="logo-space"></div><main class="box">
<h1>__TITLE__</h1><div class="muted">Create an account for this server. It is free and needs no email address.</div>
<div id="updateBanner" class="small warn" style="display:none;margin-top:8px"></div>
<form id="form"><label>Account name<input id="name" autocomplete="username" maxlength="16" required></label>
<label>Password<input id="password" type="password" autocomplete="new-password" maxlength="16" required></label>
<label>Repeat password<input id="password2" type="password" autocomplete="new-password" maxlength="16" required></label>
<div class="small muted" style="margin-top:8px">Name: 3-16 letters or digits. Password: 4-16 characters.</div>
<button id="submit">Create account</button></form><div id="msg" class="small"></div>
<div id="loginBox" style="margin-top:18px;padding-top:16px;border-top:1px solid #3d374d">
<div class="small muted">Already have an account? Log in to show the downloads again.</div>
<form id="loginForm" style="margin-top:4px"><label>Account name<input id="loginName" autocomplete="username" maxlength="16" required></label>
<label>Password<input id="loginPassword" type="password" autocomplete="current-password" maxlength="16" required></label>
<button id="loginSubmit" class="secondary">Show downloads</button></form><div id="loginMsg" class="small"></div>
</div>
__ADDRESS_NOTE__
<div class="small muted" style="margin-top:18px">Server address: <code>__ADDRESS__</code></div>
<div id="done" class="done" style="display:none"><b id="doneTitle">Account created.</b><div class="small">You can now log in with this account in the game.</div>
<div id="updateNotice" class="small warn" style="display:none;margin-top:10px"></div>
<div class="downloads">__DOWNLOADS__</div>
<div class="small muted" style="margin-top:10px">__INSTRUCTIONS__</div></div>
</main></div>
<script>
const f=document.getElementById('form'),m=document.getElementById('msg'),d=document.getElementById('done'),b=document.getElementById('submit');
const lf=document.getElementById('loginForm'),lm=document.getElementById('loginMsg'),lb=document.getElementById('loginBox'),dt=document.getElementById('doneTitle');
const packAt=__PACK_AT__,packLabel=__CLIENT_LABEL__,ub=document.getElementById('updateBanner'),un=document.getElementById('updateNotice');
function packKey(){return 'afk-pack-at:'+location.host}
function seenPack(){try{return localStorage.getItem(packKey())}catch{return null}}
function updatePackNotice(done){
  if(!packAt){if(ub)ub.style.display='none';if(un)un.style.display='none';return}
  const seen=seenPack(),when=new Date(packAt).toLocaleString();
  const now=Date.parse(packAt)||0,seenAt=Date.parse(seen)||0;
  const newer=seen && now>seenAt;
  if(done){
    if(un){
      if(newer){un.textContent='A new '+packLabel+' is available ('+when+'). Download it again.';un.className='small warn';un.style.display='block'}
      else if(!seen){un.textContent=packLabel+' ready ('+when+'). Download it once.';un.className='small muted';un.style.display='block'}
      else un.style.display='none'
    }
    if(ub)ub.style.display='none';
  }else{
    if(ub){
      if(newer){ub.textContent='A new '+packLabel+' is available ('+when+'). Log in to download it again.';ub.className='small warn';ub.style.display='block'}
      else if(!seen){ub.textContent=packLabel+' is available ('+when+'). Register or log in to download it.';ub.className='small muted';ub.style.display='block'}
      else ub.style.display='none'
    }
  }
}
function showDone(heading,text){f.style.display='none';lb.style.display='none';d.style.display='block';dt.textContent=heading;m.textContent=text;m.className='small ok';updatePackNotice(true)}
const packLink=document.getElementById('clientUpdateLink');
if(packLink)packLink.addEventListener('click',()=>{try{localStorage.setItem(packKey(),packAt)}catch{}});
f.addEventListener('submit',async e=>{e.preventDefault();m.textContent='';b.disabled=true;try{const r=await fetch('/api/public/register',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({name:document.getElementById('name').value,password:document.getElementById('password').value,password2:document.getElementById('password2').value})});const j=await r.json();if(!r.ok||!j.ok)throw new Error(j.error||'Registration failed.');showDone('Account created.','Welcome, '+j.name+'.')}catch(err){m.textContent=err.message;m.className='small bad'}finally{b.disabled=false}});
lf.addEventListener('submit',async e=>{e.preventDefault();lm.textContent='';const lbn=document.getElementById('loginSubmit');lbn.disabled=true;try{const r=await fetch('/api/public/downloads',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({name:document.getElementById('loginName').value,password:document.getElementById('loginPassword').value})});const j=await r.json();if(!r.ok||!j.ok)throw new Error(j.error||'Login failed.');showDone('Your downloads.','Welcome back, '+j.name+'.')}catch(err){lm.textContent=err.message;lm.className='small bad'}finally{lbn.disabled=false}});
updatePackNotice(false);
</script></body></html>
""";
            return page.Replace("__TITLE__", title)
                .Replace("__PACK_AT__", JsonSerializer.Serialize(clientPackAt ?? ""))
                .Replace("__CLIENT_LABEL__", JsonSerializer.Serialize(clientLabel))
                .Replace("__ADDRESS_NOTE__", addressNote)
                .Replace("__ADDRESS__", shownAddress)
                .Replace("__DOWNLOADS__", clientLink + clientFiles + realmLinks)
                .Replace("__INSTRUCTIONS__", instructions);
        }
    }
}
