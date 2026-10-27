using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Builder;

namespace CoAInstaller.Web
{
    /// <summary>The server settings: every option of every config file with the description of its template.</summary>
    static partial class Api
    {
        static void MapSettings(WebApplication app)
        {
            Get(app, "/api/settings", c =>
            {
                var inst = Inst;
                if (!File.Exists(inst.WorldConf)) throw new UserError(L.T("worldserver.conf was not found. Run \"Repair setup\" first."));
                var set = ConfigSet.Load(inst);
                var files = set.Files.Select(f => new
                {
                    name = f.Name.Replace('\\', '/'),
                    options = f.Options.Select(o => OptionJson(o, null)).ToArray()
                }).ToArray();
                var popular = set.Popular.Select(o => new { file = o.File.Name.Replace('\\', '/'), key = o.Key, label = o.Label, section = set.PopularSection[o] }).ToArray();
                return new { files, popular, worldRunning = Ctl.World != null };
            });

            Post(app, "/api/settings/save", a =>
            {
                var inst = Inst;
                var set = ConfigSet.Load(inst);
                var errors = new List<string>();
                var raw = a.Raw("changes");
                if (raw.ValueKind == System.Text.Json.JsonValueKind.Array)
                    foreach (var ch in raw.EnumerateArray())
                    {
                        var c = new Args(ch);
                        string fileName = (c.Str("file") ?? "").Replace('/', '\\');
                        var file = set.Files.FirstOrDefault(f => f.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));
                        ConfigOption o;
                        if (file == null || !file.ByKey.TryGetValue(c.Str("key") ?? "", out o)) { errors.Add((c.Str("key") ?? "?") + ": " + L.T("No setting found")); continue; }
                        if (o.Locked) continue;
                        try
                        {
                            string value = o.ToRaw(c.Str("value") ?? "");
                            if (ConfigOption.Unquote(value) == ConfigOption.Unquote(o.CurrentRaw) && o.File.Values.ContainsKey(o.Key)) continue;
                            file.Pending[o.Key] = value;
                        }
                        catch (FormatException ex) { errors.Add(ex.Message); }
                    }
                if (errors.Count > 0) throw new UserError(string.Join("\n", errors));
                int count = set.PendingCount;
                try { set.SaveAll(); }
                catch (Exception ex) { throw new UserError(L.T("The settings could not be saved:") + "\n" + ex.Message); }
                Manager.CheckClient(false);
                return new { saved = count, worldRunning = Ctl.World != null };
            });
        }

        public static object OptionJson(ConfigOption o, string label)
        {
            return new
            {
                key = o.Key, label = label ?? o.Label, section = L.Outside(o.Section),
                description = o.Description.Length > 0 ? o.ShownDescription : "",
                kind = o.Kind.ToString().ToLowerInvariant(), quoted = o.Quoted, locked = o.Locked,
                value = ConfigOption.Unquote(o.CurrentRaw), raw = o.CurrentRaw, defaultValue = o.DefaultRaw == null ? null : ConfigOption.Unquote(o.DefaultRaw),
                choices = o.Choices.Select(ch => new { value = ch.Key, text = L.Outside(ch.Value) }).ToArray()
            };
        }
    }
}
