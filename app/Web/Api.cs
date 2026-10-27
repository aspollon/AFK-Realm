using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CoAInstaller.Web
{
    /// <summary>The body of a request: a JSON object with the values the page sends.</summary>
    class Args
    {
        readonly JsonElement root;
        public Args(JsonElement e) { root = e; }

        bool Get(string name, out JsonElement value)
        {
            value = default(JsonElement);
            return root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out value) && value.ValueKind != JsonValueKind.Null && value.ValueKind != JsonValueKind.Undefined;
        }
        public string Str(string name, string fallback = null)
        {
            JsonElement v;
            if (!Get(name, out v)) return fallback;
            return v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString();
        }
        public int Int(string name, int fallback = 0)
        {
            JsonElement v; int i;
            if (!Get(name, out v)) return fallback;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out i)) return i;
            return int.TryParse(v.ToString(), out i) ? i : fallback;
        }
        public long Long(string name, long fallback = 0)
        {
            JsonElement v; long i;
            if (!Get(name, out v)) return fallback;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out i)) return i;
            return long.TryParse(v.ToString(), out i) ? i : fallback;
        }
        public double Num(string name, double fallback = 0)
        {
            JsonElement v; double d;
            if (!Get(name, out v)) return fallback;
            if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
            return double.TryParse(v.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d) ? d : fallback;
        }
        public bool Bool(string name, bool fallback = false)
        {
            JsonElement v;
            if (!Get(name, out v)) return fallback;
            if (v.ValueKind == JsonValueKind.True) return true;
            if (v.ValueKind == JsonValueKind.False) return false;
            return v.ToString() == "1" || v.ToString().Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        public List<string> List(string name)
        {
            JsonElement v;
            var list = new List<string>();
            if (!Get(name, out v) || v.ValueKind != JsonValueKind.Array) return list;
            foreach (var x in v.EnumerateArray()) list.Add(x.ValueKind == JsonValueKind.String ? x.GetString() : x.ToString());
            return list;
        }
        public JsonElement Raw(string name) { JsonElement v; return Get(name, out v) ? v : default(JsonElement); }
        public bool Has(string name) { JsonElement v; return Get(name, out v); }
    }

    /// <summary>A refusal the page shows as it is (a message for the user, not an error of the program).</summary>
    class UserError : Exception { public UserError(string message) : base(message) { } }

    static partial class Api
    {
        static readonly JsonSerializerOptions Json = Events.Json;

        public static void Map(WebApplication app)
        {
            MapCore(app);
            MapSetup(app);
            MapServer(app);
            MapSettings(app);
            MapAccounts(app);
            MapGameMaster(app);
            MapDatabase(app);
            MapBackups(app);
            MapModules(app);
            MapClient(app);
            MapRaces(app);
            MapSetups(app);
            MapBots(app);
            MapFiles(app);
            MapRemote(app);
        }

        // ---- helpers ----

        /// <summary>A handler with the request's JSON body; what it returns is sent as JSON, an exception as {error}.</summary>
        public static void Post(IEndpointRouteBuilder app, string path, Func<Args, object> handler)
        {
            app.MapPost(path, async (HttpContext context) =>
            {
                JsonElement body = default(JsonElement);
                try
                {
                    if (context.Request.ContentLength != 0 && (context.Request.ContentType ?? "").Contains("json"))
                        using (var doc = await JsonDocument.ParseAsync(context.Request.Body)) body = doc.RootElement.Clone();
                }
                catch { }
                await Send(context, () => handler(new Args(body)));
            });
        }

        public static void Get(IEndpointRouteBuilder app, string path, Func<HttpContext, object> handler)
        {
            app.MapGet(path, async (HttpContext context) => await Send(context, () => handler(context)));
        }

        static async Task Send(HttpContext context, Func<object> handler)
        {
            object result;
            int code = 200;
            try { result = await Task.Run(handler); }
            catch (Exception ex)
            {
                while ((ex is AggregateException || ex is System.Reflection.TargetInvocationException) && ex.InnerException != null) ex = ex.InnerException;
                code = ex is UserError || ex is BusyException || ex is InvalidOperationException || ex is FormatException || ex is IOException || ex is UnauthorizedAccessException ? 400 : 500;
                if (ex is NeedsConfirmation) { code = 409; result = new { confirm = ex.Message }; }
                else result = new { error = ex.Message };
            }
            context.Response.StatusCode = code;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync(JsonSerializer.Serialize(result ?? new { ok = true }, Json));
        }

        public static Install Inst
        {
            get
            {
                var i = Manager.Inst;
                if (i == null) throw new UserError(L.T("No server is open."));
                return i;
            }
        }
        public static ServerControl Ctl { get { var c = Manager.Ctl; if (c == null) throw new UserError(L.T("No server is open.")); return c; } }

        public static void EnsureDatabase(Action<string> st) { if (Ctl.Db == null) Ctl.StartDatabase(st); }

        static readonly object ok = new { ok = true };
        public static object Ok { get { return ok; } }

        // ---- the basics: texts, look, events, the task ----

        static void MapCore(WebApplication app)
        {
            Get(app, "/api/meta", c => Meta());

            Post(app, "/api/language", a =>
            {
                if (Jobs.Busy) throw new UserError(L.T("The language can be changed once the current task is finished."));
                L.Choice = a.Str("code") ?? L.Auto;
                L.Init();
                if (Manager.Inst != null) L.AddModules(ModuleCatalog.ModulesDir(Manager.Inst));
                Manager.Poke();
                return Meta();
            });

            Post(app, "/api/language/add", a =>
            {
                string code = (a.Str("code") ?? "").Trim();
                if (!System.Text.RegularExpressions.Regex.IsMatch(code, "^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})?$")) throw new UserError(L.T("Please use a language code such as ja, ko or pt-PT."));
                if (L.Languages().Any(l => l.Code.Equals(code, StringComparison.OrdinalIgnoreCase) && l.BuiltIn)) throw new UserError(L.T("AFK Realm has this language already."));
                string file = L.WriteTemplate(code);
                Files.Reveal(Path.GetDirectoryName(file));
                return new { file };
            });

            Post(app, "/api/theme", a =>
            {
                string choice = a.Str("choice") ?? "auto";
                if (choice != "light" && choice != "dark") choice = "auto";
                try { Directory.CreateDirectory(Paths.SettingsDir); File.WriteAllText(Path.Combine(Paths.SettingsDir, "theme.txt"), choice); } catch { }
                return new { theme = choice };
            });

            app.MapGet("/api/events", (HttpContext context) => Events.Stream(context, () =>
            {
                var list = new List<KeyValuePair<string, object>>();
                list.Add(new KeyValuePair<string, object>("status", Manager.Status));
                if (Jobs.Current != null) list.Add(new KeyValuePair<string, object>("job", Jobs.Current.Snapshot()));
                return list;
            }));

            Get(app, "/api/state", c => new { status = Manager.Status, job = Jobs.Current == null ? null : Jobs.Current.Snapshot(), server = Manager.Inst == null ? null : Manager.Inst.Root });

            Get(app, "/api/job/log", c =>
            {
                var j = Jobs.Current;
                return new { id = j == null ? null : j.Id, lines = j == null ? new List<string>() : j.LogLines.ToList() };
            });
            Post(app, "/api/job/cancel", a => { Jobs.Cancel(); return Ok; });
            Post(app, "/api/job/dismiss", a => { var j = Jobs.Current; if (j != null && !j.Running) { Jobs.Current = null; Events.Send("job", null); } return Ok; });

            Post(app, "/api/quit", a =>
            {
                if (Jobs.Busy) throw new UserError(L.T("A task is still running."));
                System.Threading.ThreadPool.QueueUserWorkItem(_ => { System.Threading.Thread.Sleep(300); WebHost.Shutdown(); });
                return Ok;
            });
        }

        static object Meta()
        {
            string theme = "auto";
            try { var f = Path.Combine(Paths.SettingsDir, "theme.txt"); if (File.Exists(f)) theme = File.ReadAllText(f).Trim(); } catch { }
            return new
            {
                product = Product.Name, version = Product.Version, tagline = L.T(Product.Tagline), buildsFor = Product.BuildsFor,
                windows = Os.Windows, os = Os.Windows ? "windows" : "linux",
                remote = Program.Opts.Listen != null || WebHost.RequestRemote.Value,
                language = L.Code, languageChoice = L.Choice, font = L.Font,
                languages = L.Languages().Select(l => new { code = l.Code, name = l.Name, english = l.English, builtIn = l.BuiltIn }).ToArray(),
                theme, strings = L.All(),
                server = Manager.Inst == null ? null : Manager.Inst.Root,
                justUpdated = SelfUpdateWeb.TakeJustUpdated(),
                home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                pathSeparator = Path.DirectorySeparatorChar.ToString()
            };
        }
    }
}
