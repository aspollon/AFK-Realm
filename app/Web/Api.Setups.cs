using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Builder;

namespace CoAInstaller.Web
{
    /// <summary>
    /// The setup pages of the modules made for AFK Realm read and write the module's .conf file (and a few options of
    /// others). The page keeps the changes; these routes read a file as it is and write what the page changed.
    /// </summary>
    static partial class Api
    {
        /// <summary>A config file of the server by its name under configs ("modules/mod_world_journey.conf").</summary>
        static ConfigFile ConfOf(Install inst, string name)
        {
            name = (name ?? "").Replace('\\', '/').Trim('/');
            if (name.Length == 0 || name.Contains("..") || !name.EndsWith(".conf", StringComparison.OrdinalIgnoreCase)) throw new UserError("Invalid file: " + name);
            string path = Path.Combine(new[] { inst.ConfigDir }.Concat(name.Split('/')).ToArray());
            return ConfigFile.Load(path, name.Replace('/', '\\'));
        }

        static void MapSetups(WebApplication app)
        {
            Post(app, "/api/conf/read", a =>
            {
                var inst = Inst;
                var files = new List<object>();
                foreach (var name in a.List("files"))
                {
                    var f = ConfOf(inst, name);
                    bool exists = File.Exists(f.Path) || File.Exists(f.Path + ".dist");
                    var keys = new HashSet<string>(f.Options.Select(o => o.Key), StringComparer.OrdinalIgnoreCase);
                    var options = f.Options.Select(o => new
                    {
                        key = o.Key, has = f.Values.ContainsKey(o.Key), value = f.Values.ContainsKey(o.Key) ? ConfigOption.Unquote(f.Values[o.Key]) : null,
                        defaultValue = o.DefaultRaw == null ? null : ConfigOption.Unquote(o.DefaultRaw), quoted = o.Quoted
                    }).ToList<object>();
                    // values in the file without a description in the template (Journey.Zone.1234 and the like)
                    foreach (var kv in f.Values.Where(kv => !keys.Contains(kv.Key)))
                        options.Add(new { key = kv.Key, has = true, value = ConfigOption.Unquote(kv.Value), defaultValue = (string)null, quoted = ConfigOption.IsQuoted(kv.Value) });
                    files.Add(new { name, exists, path = f.Path, options });
                }
                return new { files, worldRunning = Ctl.World != null };
            });

            // files: [{name, set: {key: value}, unset: [key]}]; a value is quoted here when its option is a quoted one.
            Post(app, "/api/conf/save", a =>
            {
                var inst = Inst;
                var written = new List<string>();
                var loaded = new List<ConfigFile>();
                foreach (var e in Items(a.Raw("files")))
                {
                    var c = new Args(e);
                    var f = ConfOf(inst, c.Str("name"));
                    var set = c.Raw("set");
                    if (set.ValueKind == System.Text.Json.JsonValueKind.Object)
                        foreach (var p in set.EnumerateObject())
                        {
                            string value = p.Value.ValueKind == System.Text.Json.JsonValueKind.String ? p.Value.GetString() : p.Value.ToString();
                            if (value.IndexOfAny(new[] { '\r', '\n' }) >= 0) throw new UserError(T("The value must be on one line."));
                            ConfigOption o;
                            bool quoted = f.ByKey.TryGetValue(p.Name, out o) ? o.Quoted : (f.Values.ContainsKey(p.Name) && ConfigOption.IsQuoted(f.Values[p.Name]));
                            bool already = ConfigOption.IsQuoted(value);
                            f.Pending[p.Name] = quoted && !already ? "\"" + value.Replace("\"", "") + "\"" : value;
                        }
                    foreach (var k in c.List("unset")) if (f.Values.ContainsKey(k)) { f.Pending.Remove(k); f.Removed.Add(k); }
                    if (f.Pending.Count + f.Removed.Count > 0) { loaded.Add(f); written.Add(f.Name); }
                }
                try { foreach (var f in loaded) f.Save(); }
                catch (Exception ex) { throw new UserError(L.T("The settings could not be saved:") + "\n" + ex.Message); }
                Manager.CheckClient(false);
                return new
                {
                    written,
                    message = L.F("Saved in {0}.", string.Join(", ", written.Count > 0 ? written : new List<string> { "–" })) + "\n\n" + L.T("The changes take effect when the worldserver is started the next time (Restart server).")
                };
            });

            Post(app, "/api/setups/immersion/database", a =>
            {
                var inst = Inst;
                return Jobs.Run(L.T("Installing the database tables ..."), (job, st) =>
                {
                    var control = new ServerControl(inst);
                    if (control.Db == null && !Net.PortOpen(control.DbPort))
                    {
                        st(L.T("Starting the database ..."));
                        control.StartDatabase(s => { });
                    }
                    st(L.T("Installing the database tables ..."));
                    ImmersionDatabase(inst);
                    job.Message = L.T("The database tables are ready.");
                }).Snapshot();
            });

            // The zones and dungeons World Journey knows (data/journey-ui.json of the module).
            Get(app, "/api/setups/journey/places", c =>
            {
                string file = Path.Combine(ModuleCatalog.ModulesDir(Inst), "mod-world-journey", "data", "journey-ui.json");
                var places = new List<object>();
                try
                {
                    if (File.Exists(file))
                        using (var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file)))
                        {
                            Func<System.Text.Json.JsonElement, string, int> num = (e, k) => { System.Text.Json.JsonElement v; return e.TryGetProperty(k, out v) && v.ValueKind == System.Text.Json.JsonValueKind.Number ? v.GetInt32() : 0; };
                            Func<System.Text.Json.JsonElement, string, string> str = (e, k) => { System.Text.Json.JsonElement v; return e.TryGetProperty(k, out v) ? v.ToString() : ""; };
                            foreach (var z in doc.RootElement.GetProperty("zones").EnumerateArray())
                                places.Add(new { instance = false, id = num(z, "zone"), name = L.Outside(str(z, "name")), part = num(z, "part"), original = num(z, "originalEntry"), kind = 0, content = 0 });
                            foreach (var d in doc.RootElement.GetProperty("instances").EnumerateArray())
                                places.Add(new { instance = true, id = num(d, "map"), name = L.Outside(str(d, "name")), part = 0, original = num(d, "originalEntry"), kind = num(d, "kind"), content = num(d, "contentLevel") });
                        }
                }
                catch { places.Clear(); }
                return new { places };
            });

        }

        static string T(string s) { return L.T(s); }

        static void ImmersionDatabase(Install inst)
        {
            string folder = Path.Combine(ModuleCatalog.ModulesDir(inst), "mod-playerbots-immersion", "data", "sql", "manual");
            if (!Directory.Exists(folder)) throw new InvalidOperationException(L.T("The module's database files were not found."));
            DbLogin login = DbLogin.FromConfig(inst);
            Func<string, string, bool> exists = (database, table) =>
            {
                var rows = MySql.Query(inst, login, "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = " + MySql.Quote(database) + " AND TABLE_NAME = " + MySql.Quote(table) + ";");
                return rows.Count > 0 && rows[0].Trim() != "0";
            };
            Action<string, string> run = (database, name) =>
            {
                string file = Path.Combine(folder, name);
                if (!File.Exists(file)) throw new FileNotFoundException(L.T("The module's database files were not found."), file);
                MySql.RunFile(inst, login, database, file);
            };
            if (!exists("acore_world", "city_life_hub")) run("acore_world", "world_city_life.sql");
            run("acore_world", "world_city_life_quest_zones.sql");
            run("acore_world", "world_city_life_locations.sql");
            run("acore_world", "world_city_life_remove_questing.sql");
            run("acore_world", "world_city_life_custom.sql");
            run("acore_world", "world_city_life_fishing.sql");
            run("acore_world", "world_city_life_fishing_spacing.sql");
            run("acore_world", "world_city_life_legendary_stormwind.sql");
            run("acore_characters", "characters_city_life_custom.sql");
            run("acore_characters", "characters_city_life_custom_bots.sql");
            if (!exists("acore_world", "pvp_life_zone")) run("acore_world", "world_pvp_life.sql");
            run("acore_world", "world_pvp_life_locations.sql");
        }
    }
}
