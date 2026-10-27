using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;

namespace CoAInstaller.Web
{
    /// <summary>
    /// The module manager: the catalog with the installed modules. The page ticks and unticks; applying hands the
    /// choice to the engine (mode Modules), like the program on Windows did.
    /// </summary>
    static partial class Api
    {
        static List<ModuleEntry> modules = new List<ModuleEntry>();
        static readonly object modulesLock = new object();

        static string ModuleId(ModuleEntry m) { return (m.FullName.Length > 0 ? m.FullName : m.CloneUrl.Length > 0 ? m.CloneUrl : m.Name).ToLowerInvariant(); }

        static ModuleEntry FindModule(string id)
        {
            lock (modulesLock) return modules.FirstOrDefault(m => ModuleId(m) == (id ?? "").ToLowerInvariant());
        }

        static object ModuleJson(ModuleEntry m)
        {
            return new
            {
                id = ModuleId(m), name = m.Name, fullName = m.FullName, url = m.Url, cloneUrl = m.CloneUrl, description = L.Outside(m.Description), raw = m.Description,
                stars = m.InCatalog ? m.Stars : 0, pushed = m.Pushed > DateTime.MinValue ? m.Pushed.ToLocalTime().ToString("yyyy-MM-dd") : "",
                old = !(m.Pushed == DateTime.MinValue || m.Pushed > DateTime.UtcNow.AddYears(-2)),
                own = m.Own, state = m.State.ToString(), status = m.Status, installedOn = m.InstalledOn, installed = m.Installed, playerbots = m.IsPlayerbots,
                createdTables = m.CreatedTables, manualTables = m.ManualTables, inCatalog = m.InCatalog
            };
        }

        static void MapModules(WebApplication app)
        {
            Get(app, "/api/modules", c =>
            {
                var inst = Inst;
                bool refresh = c.Request.Query["refresh"] == "1";
                List<ModuleEntry> catalog = null, merged = new List<ModuleEntry>(); string warning = null, error = null;
                try { catalog = ModuleCatalog.LoadCatalog(inst, refresh, out warning); }
                catch (Exception ex) { error = ex.Message; }
                try { merged = ModuleCatalog.Merge(inst, catalog); }
                catch (Exception ex) { error = (error != null ? error + " " : "") + L.T("The installed modules could not be read:") + " " + ex.Message; }
                lock (modulesLock) modules = merged;
                return new
                {
                    modules = merged.Select(ModuleJson).ToArray(),
                    status = error ?? warning ?? L.F("{0} modules in the catalog.", merged.Count(m => m.InCatalog)), warn = error != null || warning != null,
                    token = Settings.GitHubToken ?? ""
                };
            });

            Post(app, "/api/modules/token", a =>
            {
                string want = (a.Str("token") ?? "").Trim();
                if (want.Length == 0) want = null;
                if (want != Settings.GitHubToken) Settings.GitHubToken = want;
                return Ok;
            });

            Post(app, "/api/modules/analyze", a =>
            {
                var m = FindModule(a.Str("id"));
                if (m == null) throw new UserError(L.T("No module was chosen."));
                try
                {
                    var r = ModuleCatalog.Analyze(m);
                    return new { checks = r.Checks.OrderByDescending(x => x.Key).Select(x => new { level = x.Key, text = x.Value }).ToArray(), readme = r.ReadmeText.Length > 0 };
                }
                catch (Exception ex) { return new { error = ex.Message }; }
            });

            Post(app, "/api/modules/readme", a =>
            {
                var m = FindModule(a.Str("id"));
                if (m == null) throw new UserError(L.T("No module was chosen."));
                string text; try { text = ModuleCatalog.Analyze(m).ReadmeText; } catch (Exception ex) { text = L.T("The README could not be loaded:") + " " + ex.Message; }
                return new { title = m.Name + " – README", text };
            });

            Post(app, "/api/modules/custom", a =>
            {
                string url = (a.Str("url") ?? "").Trim();
                if (!Regex.IsMatch(url, @"^https://[^\s""';]+$"))
                    throw new UserError(L.T("Please enter the https address of the module's Git repository, for example") + " https://github.com/azerothcore/mod-transmog");
                string name = ModuleCatalog.FolderName(url);
                if (!Regex.IsMatch(name, @"^[\w.\-]+$")) throw new UserError(L.T("This address does not end in a module name."));
                string full = ModuleCatalog.GitHubName(url);
                ModuleEntry known;
                lock (modulesLock)
                {
                    known = modules.FirstOrDefault(m => (full != null && m.FullName.Equals(full, StringComparison.OrdinalIgnoreCase)) || m.CloneUrl.Equals(url, StringComparison.OrdinalIgnoreCase));
                    if (known == null)
                    {
                        known = new ModuleEntry { Name = name, FullName = full ?? "", Url = full != null ? "https://github.com/" + full : url, CloneUrl = full != null ? "https://github.com/" + full + ".git" : url, Description = L.N("Added from an address") };
                        modules.Add(known);
                    }
                }
                return new { module = ModuleJson(known) };
            });

            // The summary shown before applying: what is installed and removed, the warnings of each, what happens.
            Post(app, "/api/modules/plan", a =>
            {
                var add = a.List("add").Select(FindModule).Where(m => m != null && m.State == ModuleState.Available).ToList();
                var remove = a.List("remove").Select(FindModule).Where(m => m != null && m.Installed).ToList();
                if (add.Count + remove.Count == 0) throw new UserError(L.T("No module was chosen."));
                var found = new Dictionary<ModuleEntry, ModuleAnalysis>();
                var failed = new Dictionary<ModuleEntry, string>();
                foreach (var m in add) { try { found[m] = ModuleCatalog.Analyze(m); } catch (Exception ex) { failed[m] = ex.Message; } }
                var b = new StringBuilder();
                bool blocking = false;
                if (add.Count > 0)
                {
                    b.AppendLine(L.T("INSTALL")); b.AppendLine();
                    foreach (var m in add)
                    {
                        b.AppendLine("  " + m.Name + (m.FullName.Length > 0 ? "  (" + m.FullName + ")" : "  (" + m.CloneUrl + ")"));
                        ModuleAnalysis an;
                        if (found.TryGetValue(m, out an))
                            foreach (var c in an.Checks.Where(c => c.Key > 0).OrderByDescending(c => c.Key)) { b.AppendLine("     " + (c.Key == 2 ? "✖ " : "⚠ ") + c.Value); if (c.Key == 2) blocking = true; }
                        else if (failed.ContainsKey(m)) b.AppendLine("     ⚠ " + L.T("Could not be checked:") + " " + failed[m]);
                        b.AppendLine();
                    }
                }
                if (remove.Count > 0)
                {
                    b.AppendLine(L.T("REMOVE")); b.AppendLine();
                    foreach (var m in remove)
                    {
                        b.AppendLine("  " + m.Name);
                        if (m.State == ModuleState.ByHand) b.AppendLine("     ⚠ " + L.T("Added by hand: its database changes (if any) stay in place."));
                        else
                        {
                            b.AppendLine("     ✔ " + L.T("Its recorded database changes are undone."));
                            if (m.CreatedTables.Length > 0) b.AppendLine("     ⚠ " + L.T("Its tables are deleted with their data:") + " " + string.Join(", ", m.CreatedTables));
                            if (m.ManualTables.Length > 0) b.AppendLine("     ⚠ " + L.F("Not undoable automatically: {0} (a backup restores them)", string.Join(", ", m.ManualTables)));
                        }
                        b.AppendLine("     " + L.T("Items or spells that players got from the module may disappear or stop working."));
                        b.AppendLine();
                    }
                }
                b.AppendLine(L.T("WHAT HAPPENS")); b.AppendLine();
                var steps = new List<string> { L.T("stops the server"), L.T("backs it up") };
                if (add.Count > 0) steps.Add(L.T("downloads the modules"));
                steps.Add(L.T("rebuilds it"));
                if (remove.Count > 0) steps.Add(L.T("undoes the database changes of the removed modules"));
                if (add.Count > 0) steps.Add(L.T("applies the new modules' database changes with a record of them"));
                b.AppendLine(L.F("{0}: {1}. This can take a while.", Product.Name, string.Join(", ", steps)));
                if (add.Count > 0) b.AppendLine(L.T("If the server cannot be built with a new module, the module is taken out again and the server stays as it is."));
                if (blocking) b.AppendLine("\n✖ " + L.T("At least one module will most likely not work. Install it anyway only if you know what you are doing."));
                return new
                {
                    text = b.ToString(), blocking,
                    add = add.Select(m => m.CloneUrl.Length > 0 ? m.CloneUrl : "https://github.com/" + m.FullName + ".git").ToArray(),
                    remove = remove.Select(m => m.Name).ToArray()
                };
            });
        }
    }
}
