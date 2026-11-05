using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;

namespace CoAInstaller.Web
{
    /// <summary>Server presets: a module list and changed settings that can be applied to another installation.</summary>
    static partial class Api
    {
        static void MapPresets(WebApplication app)
        {
            Get(app, "/api/presets", c =>
            {
                var inst = Inst;
                return new
                {
                    folder = ServerPresets.Dir(inst),
                    presets = ServerPresets.All(inst).Select(p => new
                    {
                        id = p.Id,
                        name = p.Name,
                        description = p.Description,
                        builtin = p.BuiltIn,
                        modules = p.Modules.Count,
                        settings = p.Settings.Count,
                        requiresCoaCustom = p.RequiresCoaCustom,
                        moduleNames = p.Modules.Select(m => m.Name).ToArray()
                    }).ToArray()
                };
            });

            // The state of this server, used as the starting point of the guided "create preset" flow.
            Get(app, "/api/presets/current", c =>
            {
                var preset = ServerPresets.Capture(Inst, "Current server", "");
                return new
                {
                    modules = preset.Modules.ToArray(),
                    settings = preset.Settings.ToArray(),
                    requiresCoaCustom = preset.RequiresCoaCustom
                };
            });

            Post(app, "/api/presets/save", a =>
            {
                var inst = Inst;
                ServerPreset preset;
                if (a.Has("preset"))
                {
                    JsonElement raw = a.Raw("preset");
                    try { preset = ServerPresets.Deserialize(raw.GetRawText()); }
                    catch (Exception ex) { throw new UserError(L.T("The preset could not be read:") + " " + ex.Message); }
                    if (preset == null) throw new UserError(L.T("The preset could not be read."));
                    ValidatePreset(preset);
                }
                else
                {
                    preset = ServerPresets.Capture(inst, a.Str("name") ?? "", a.Str("description") ?? "");
                }
                string id = ServerPresets.Save(inst, preset, a.Bool("overwrite"));
                return new { id, name = preset.Name, message = L.F("The preset \"{0}\" was saved.", preset.Name) };
            });

            Post(app, "/api/presets/delete", a =>
            {
                ServerPresets.Delete(Inst, a.Str("id") ?? "");
                return Ok;
            });

            Post(app, "/api/presets/rename", a =>
            {
                string id = ServerPresets.Rename(Inst, a.Str("id") ?? "", a.Str("name") ?? "");
                return new { id, message = L.F("The preset was renamed to \"{0}\".", a.Str("name") ?? "") };
            });

            // Share a preset as a small JSON file. It contains modules and changed settings only.
            Post(app, "/api/presets/export", a =>
            {
                var preset = ServerPresets.Find(Inst, a.Str("id") ?? "");
                string file = Path.Combine(Files.Temp, Guid.NewGuid().ToString("N") + ".json");
                File.WriteAllText(file, ServerPresets.Serialize(preset), new System.Text.UTF8Encoding(false));
                string name = System.Text.RegularExpressions.Regex.Replace(preset.Name, @"[^\w .\-]", "_").Trim() + ".afk-preset.json";
                return new { url = Files.Offer(file, name, true), name };
            });

            Post(app, "/api/presets/import", a =>
            {
                string path = a.Str("path") ?? "";
                string temp = Path.GetFullPath(Files.Temp).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string full;
                try { full = Path.GetFullPath(path); } catch { throw new UserError(L.T("The preset file could not be read.")); }
                if (!full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
                    throw new UserError(L.T("The preset file could not be read."));
                ServerPreset preset;
                try { preset = ServerPresets.Deserialize(File.ReadAllText(full)); }
                catch (Exception ex) { throw new UserError(L.T("The preset could not be read:") + " " + ex.Message); }
                if (preset == null || preset.Format != 1) throw new UserError(L.T("The preset could not be read."));
                ValidatePreset(preset);
                string id = ServerPresets.Save(Inst, preset, a.Bool("overwrite"));
                return new { id, name = preset.Name, message = L.F("The preset \"{0}\" was imported.", preset.Name) };
            });

            Post(app, "/api/presets/plan", a =>
            {
                var preset = ServerPresets.Find(Inst, a.Str("id") ?? "");
                var plan = ServerPresets.Plan(Inst, preset, a.Bool("exact"));
                return new
                {
                    add = plan.Add.ToArray(),
                    remove = plan.Remove.ToArray(),
                    addNames = plan.AddNames.ToArray(),
                    removeNames = plan.RemoveNames.ToArray(),
                    settings = preset.Settings.Count,
                    requiresCoaCustom = preset.RequiresCoaCustom,
                    warnings = plan.Warnings.ToArray()
                };
            });

            // Called after the module build. The settings are written last so a module's own defaults
            // cannot overwrite the values the preset brings.
            Post(app, "/api/presets/apply-settings", a =>
            {
                var inst = Inst;
                var preset = ServerPresets.Find(inst, a.Str("id") ?? "");
                var result = ServerPresets.ApplySettings(inst, preset);
                Manager.CheckClient(false);
                return new
                {
                    saved = result.Saved,
                    warnings = result.Warnings.ToArray(),
                    worldRunning = Ctl.World != null,
                    requiresCoaCustom = preset.RequiresCoaCustom
                };
            });
        }

        static void ValidatePreset(ServerPreset preset)
        {
            if ((preset.Name ?? "").Trim().Length == 0) throw new UserError(L.T("Please enter a name for the preset."));
            preset.Modules = preset.Modules ?? new List<PresetModule>();
            preset.Settings = preset.Settings ?? new List<PresetSetting>();
            foreach (PresetModule module in preset.Modules)
            {
                if (module == null || !System.Text.RegularExpressions.Regex.IsMatch(module.Name ?? "", @"^[\w.\-]+$"))
                    throw new UserError(L.T("The preset contains an invalid module."));
                if (module.CloneUrl.Length == 0 && module.FullName.Length > 0)
                    module.CloneUrl = "https://github.com/" + module.FullName + ".git";
                if (!System.Text.RegularExpressions.Regex.IsMatch(module.CloneUrl ?? "", @"^https://[^\s""';]+$"))
                    throw new UserError(L.T("The preset contains a module without a valid Git address."));
            }
            foreach (PresetSetting setting in preset.Settings)
            {
                if (setting == null || !System.Text.RegularExpressions.Regex.IsMatch(setting.File ?? "", @"^(?:modules[/\\])?[\w.\-]+\.conf$") ||
                    !System.Text.RegularExpressions.Regex.IsMatch(setting.Key ?? "", @"^[A-Za-z][\w.\-]*$") ||
                    (setting.Value ?? "").IndexOfAny(new[] { '\r', '\n' }) >= 0)
                    throw new UserError(L.T("The preset contains an invalid setting."));
            }
        }
    }
}
