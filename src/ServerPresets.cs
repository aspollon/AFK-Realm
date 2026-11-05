using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Text.RegularExpressions;

namespace CoAInstaller
{
    /// <summary>One module of a server preset.</summary>
    class PresetModule
    {
        public string Name { get; set; } = "";
        public string FullName { get; set; } = "";
        public string CloneUrl { get; set; } = "";
    }

    /// <summary>One changed config value of a server preset.</summary>
    class PresetSetting
    {
        public string File { get; set; } = "";
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";
    }

    /// <summary>
    /// A named set of modules and changed settings. The built-in "AFK Realm" preset is made from the
    /// modules in this program; saved presets live below Dependencies\Presets and can be copied to
    /// another server. Passwords, database logins, local paths and ports are never stored.
    /// </summary>
    class ServerPreset
    {
        public int Format { get; set; } = 1;
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public bool RequiresCoaCustom { get; set; }
        public List<PresetModule> Modules { get; set; } = new List<PresetModule>();
        public List<PresetSetting> Settings { get; set; } = new List<PresetSetting>();

        internal bool BuiltIn { get; set; }
        internal string Id { get; set; } = "";
        internal string File { get; set; } = "";
    }

    class PresetPlan
    {
        public readonly List<string> Add = new List<string>();
        public readonly List<string> Remove = new List<string>();
        public readonly List<string> AddNames = new List<string>();
        public readonly List<string> RemoveNames = new List<string>();
        public readonly List<string> Warnings = new List<string>();
    }

    class PresetSettingsResult
    {
        public int Saved;
        public readonly List<string> Warnings = new List<string>();
    }

    static class ServerPresets
    {
        public const string BuiltInId = "builtin:afk-realm";
        const string BuiltInName = "AFK Realm";
        internal static string Serialize(ServerPreset preset) { return NewJson().Serialize(preset); }
        static JavaScriptSerializer NewJson() { return new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024 }; }

        /// <summary>Reads the JSON through dictionaries: the .NET 8 JavaScriptSerializer shim only returns
        /// dictionaries and arrays, while the .NET Framework one could map a type directly.</summary>
        internal static ServerPreset Deserialize(string text)
        {
            object raw = NewJson().DeserializeObject(text);
            var data = raw as System.Collections.IDictionary;
            if (data == null) return null;
            Func<System.Collections.IDictionary, string, object> get = (dict, key) =>
            {
                if (dict == null) return null;
                if (dict.Contains(key)) return dict[key];
                foreach (System.Collections.DictionaryEntry entry in dict)
                    if (string.Equals(Convert.ToString(entry.Key), key, StringComparison.OrdinalIgnoreCase)) return entry.Value;
                return null;
            };
            var preset = new ServerPreset
            {
                Format = Convert.ToInt32(get(data, "Format") ?? 1),
                Name = Convert.ToString(get(data, "Name") ?? ""),
                Description = Convert.ToString(get(data, "Description") ?? ""),
                RequiresCoaCustom = Convert.ToBoolean(get(data, "RequiresCoaCustom") ?? false)
            };
            foreach (object item in AsList(get(data, "Modules")))
            {
                var m = item as System.Collections.IDictionary;
                if (m == null) continue;
                preset.Modules.Add(new PresetModule
                {
                    Name = Convert.ToString(get(m, "Name") ?? ""),
                    FullName = Convert.ToString(get(m, "FullName") ?? ""),
                    CloneUrl = Convert.ToString(get(m, "CloneUrl") ?? "")
                });
            }
            foreach (object item in AsList(get(data, "Settings")))
            {
                var s = item as System.Collections.IDictionary;
                if (s == null) continue;
                preset.Settings.Add(new PresetSetting
                {
                    File = Convert.ToString(get(s, "File") ?? ""),
                    Key = Convert.ToString(get(s, "Key") ?? ""),
                    Value = Convert.ToString(get(s, "Value") ?? "")
                });
            }
            return preset;
        }

        static IEnumerable<object> AsList(object value)
        {
            var list = value as System.Collections.IEnumerable;
            if (list == null || value is string) yield break;
            foreach (object item in list) yield return item;
        }

        public static string Dir(Install inst) { return Path.Combine(inst.Root, "Dependencies", "Presets"); }

        /// <summary>The modules of the AFK Realm baseline. mod-azeroth-flying stays optional.</summary>
        static readonly string[] AfkModules =
        {
            "mod-playerbots-auctions",
            "mod-playerbots-immersion",
            "mod-world-journey",
            "mod-classic-classes",
            "mod-custom-races",
            "mod-subclass",
            "mod-afk-junk-to-gold",
            "mod-dungeon-clear"
        };

        public static ServerPreset BuiltInAfkRealm()
        {
            var preset = new ServerPreset
            {
                Name = BuiltInName,
                Description = L.T("The AFK Realm baseline: the modules made for AFK Realm, plus the CoA/WCR settings of the realm. CoA Custom is installed separately afterwards."),
                BuiltIn = true,
                Id = BuiltInId,
                RequiresCoaCustom = true
            };

            var own = ModuleCatalog.OwnModules().ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);
            foreach (string name in AfkModules)
            {
                ModuleEntry entry;
                if (!own.TryGetValue(name, out entry)) continue;
                preset.Modules.Add(new PresetModule { Name = entry.Name, FullName = entry.FullName, CloneUrl = entry.CloneUrl });
            }

            foreach (string[] setting in new[]
            {
                new[] { "modules\\coa.conf", "CoA.ClassModel", "coa" },
                new[] { "modules\\coa.conf", "CoA.RealmType", "live" },
                new[] { "modules\\coa.conf", "CoA.GameModeMask", "0" },
                new[] { "modules\\coa.conf", "CoA.MapClass10ToWarrior", "1" },
                new[] { "modules\\mod_classic_classes.conf", "ClassicClasses.Enable", "1" }
            })
                preset.Settings.Add(new PresetSetting { File = setting[0], Key = setting[1], Value = setting[2] });

            return preset;
        }

        public static List<ServerPreset> All(Install inst)
        {
            var list = new List<ServerPreset> { BuiltInAfkRealm() };
            string dir = Dir(inst);
            if (!Directory.Exists(dir)) return list;

            foreach (string file in Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var preset = Deserialize(File.ReadAllText(file));
                    if (preset == null || preset.Format != 1 || preset.Name.Length == 0) continue;
                    preset.BuiltIn = false;
                    preset.File = file;
                    preset.Id = "user:" + Path.GetFileNameWithoutExtension(file);
                    list.Add(preset);
                }
                catch { }
            }
            return list;
        }

        public static ServerPreset Find(Install inst, string id)
        {
            if (string.Equals(id, BuiltInId, StringComparison.OrdinalIgnoreCase)) return BuiltInAfkRealm();
            string key = (id ?? "").Trim();
            if (!key.StartsWith("user:", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException(L.T("The preset was not found."));
            string name = key.Substring(5);
            if (name.Length == 0 || !Regex.IsMatch(name, @"^[\w .\-]+$")) throw new InvalidOperationException(L.T("The preset was not found."));
            string file = Path.Combine(Dir(inst), name + ".json");
            if (!File.Exists(file)) throw new InvalidOperationException(L.T("The preset was not found."));
            var preset = Deserialize(File.ReadAllText(file));
            if (preset == null || preset.Format != 1) throw new InvalidOperationException(L.T("The preset was not found."));
            preset.BuiltIn = false;
            preset.File = file;
            preset.Id = "user:" + name;
            return preset;
        }

        /// <summary>Captures the installed modules and the config values that differ from the .conf.dist defaults.</summary>
        public static ServerPreset Capture(Install inst, string name, string description)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) throw new InvalidOperationException(L.T("Please enter a name for the preset."));
            if (name.Length > 80) name = name.Substring(0, 80);

            var preset = new ServerPreset { Name = name, Description = (description ?? "").Trim() };
            var installed = ModuleCatalog.Merge(inst, null)
                .Where(m => m.Installed && !m.IsPlayerbots && m.State != ModuleState.PartOfCoA)
                .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (ModuleEntry module in installed)
            {
                string url = module.CloneUrl;
                if (url.Length == 0 && module.FullName.Length > 0) url = "https://github.com/" + module.FullName + ".git";
                if (url.Length == 0) continue;
                preset.Modules.Add(new PresetModule { Name = module.Name, FullName = module.FullName, CloneUrl = url });
                if (module.Name.Equals("mod-custom-races", StringComparison.OrdinalIgnoreCase) ||
                    module.Name.Equals("mod-subclass", StringComparison.OrdinalIgnoreCase))
                    preset.RequiresCoaCustom = true;
            }

            var set = ConfigSet.Load(inst);
            foreach (ConfigFile file in set.Files)
            {
                if (file.Name.Equals("authserver.conf", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (ConfigOption option in file.Options)
                {
                    if (option.Locked || !PresetKeySafe(option.Key)) continue;
                    if (option.CurrentRaw == null) continue;
                    if (option.DefaultRaw != null &&
                        string.Equals(ConfigOption.Unquote(option.CurrentRaw), ConfigOption.Unquote(option.DefaultRaw), StringComparison.Ordinal))
                        continue;
                    preset.Settings.Add(new PresetSetting
                    {
                        File = file.Name,
                        Key = option.Key,
                        Value = ConfigOption.Unquote(option.CurrentRaw)
                    });
                }
            }

            preset.Settings = preset.Settings.OrderBy(s => s.File, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.Key, StringComparer.OrdinalIgnoreCase).ToList();
            return preset;
        }

        public static string Save(Install inst, ServerPreset preset, bool overwrite)
        {
            string name = (preset.Name ?? "").Trim();
            if (name.Length == 0) throw new InvalidOperationException(L.T("Please enter a name for the preset."));
            string file = Path.Combine(Dir(inst), SafeName(name) + ".json");
            if (File.Exists(file) && !overwrite) throw new InvalidOperationException(L.T("A preset with this name already exists."));
            Directory.CreateDirectory(Dir(inst));
            preset.Format = 1;
            preset.Name = name;
            preset.Description = (preset.Description ?? "").Trim();
            preset.Modules = (preset.Modules ?? new List<PresetModule>()).Where(m => m != null && m.Name.Length > 0).ToList();
            preset.Settings = (preset.Settings ?? new List<PresetSetting>()).Where(s => s != null && s.Key.Length > 0).ToList();
            File.WriteAllText(file, Serialize(preset), new UTF8Encoding(false));
            return "user:" + Path.GetFileNameWithoutExtension(file);
        }

        public static void Delete(Install inst, string id)
        {
            ServerPreset preset = Find(inst, id);
            if (preset.BuiltIn) throw new InvalidOperationException(L.T("The built-in preset cannot be deleted."));
            File.Delete(preset.File);
        }

        public static string Rename(Install inst, string id, string newName)
        {
            ServerPreset preset = Find(inst, id);
            if (preset.BuiltIn) throw new InvalidOperationException(L.T("The built-in preset cannot be renamed."));
            newName = (newName ?? "").Trim();
            if (newName.Length == 0) throw new InvalidOperationException(L.T("Please enter a name for the preset."));
            if (newName.Length > 80) newName = newName.Substring(0, 80);
            string file = Path.Combine(Dir(inst), SafeName(newName) + ".json");
            if (File.Exists(file) && !file.Equals(preset.File, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(L.T("A preset with this name already exists."));
            preset.Name = newName;
            File.WriteAllText(file, Serialize(preset), new UTF8Encoding(false));
            if (!file.Equals(preset.File, StringComparison.OrdinalIgnoreCase)) File.Delete(preset.File);
            return "user:" + Path.GetFileNameWithoutExtension(file);
        }

        public static PresetPlan Plan(Install inst, ServerPreset preset, bool exact)
        {
            var plan = new PresetPlan();
            var installed = ModuleCatalog.Merge(inst, null)
                .Where(m => m.Installed && !m.IsPlayerbots)
                .ToList();

            Func<PresetModule, ModuleEntry, bool> same = (a, b) =>
                a.Name.Equals(b.Name, StringComparison.OrdinalIgnoreCase) ||
                (a.CloneUrl.Length > 0 && a.CloneUrl.Equals(b.CloneUrl, StringComparison.OrdinalIgnoreCase)) ||
                (a.FullName.Length > 0 && a.FullName.Equals(b.FullName, StringComparison.OrdinalIgnoreCase));

            var add = new List<PresetModule>();
            foreach (PresetModule module in preset.Modules)
            {
                if (installed.Any(m => m.State != ModuleState.PartOfCoA && same(module, m))) continue;
                if (module.CloneUrl.Length == 0 && module.FullName.Length > 0)
                    module.CloneUrl = "https://github.com/" + module.FullName + ".git";
                if (!Regex.IsMatch(module.CloneUrl ?? "", @"^https://[^\s""';]+$"))
                {
                    plan.Warnings.Add(L.F("{0} has no usable Git address and is not installed.", module.Name));
                    continue;
                }
                add.Add(module);
            }
            foreach (PresetModule module in add)
            {
                plan.Add.Add(module.CloneUrl);
                plan.AddNames.Add(module.Name);
            }

            if (exact)
            {
                foreach (ModuleEntry module in installed)
                {
                    if (module.State == ModuleState.PartOfCoA) continue;
                    if (preset.Modules.Any(p => same(p, module))) continue;
                    plan.Remove.Add(module.Name);
                    plan.RemoveNames.Add(module.Name);
                }
            }
            return plan;
        }

        public static PresetSettingsResult ApplySettings(Install inst, ServerPreset preset)
        {
            var result = new PresetSettingsResult();
            var set = ConfigSet.Load(inst);
            foreach (PresetSetting setting in preset.Settings)
            {
                string fileName = (setting.File ?? "").Replace('/', '\\');
                ConfigFile file = set.Files.FirstOrDefault(f => f.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));
                ConfigOption option;
                if (file == null || !file.ByKey.TryGetValue(setting.Key ?? "", out option))
                {
                    result.Warnings.Add(L.F("{0}: {1} was not found.", setting.File, setting.Key));
                    continue;
                }
                if (option.Locked) continue;
                try
                {
                    file.Pending[option.Key] = option.ToRaw(setting.Value ?? "");
                    result.Saved++;
                }
                catch (FormatException ex)
                {
                    result.Warnings.Add(L.F("{0}: {1}", option.Key, ex.Message));
                }
            }
            if (result.Saved > 0) set.SaveAll();
            return result;
        }

        static bool PresetKeySafe(string key)
        {
            if (Regex.IsMatch(key ?? "", @"(DatabaseInfo|MySQL|DataDir|SourceDirectory|LogsDir|TempDir|BindIP|SOAP|Updates\.EnableDatabases|AllowRemoteClients|ClientDbcDirectory|Password|Passwd|Secret|Token|AccountPrefix|AccountId|ClientRootFolder|ImportFolder|SourceFolder|BackupFolder|RealmAddress|RealmName)", RegexOptions.IgnoreCase))
                return false;
            if (Regex.IsMatch(key ?? "", @"(^|\.)Port$", RegexOptions.IgnoreCase)) return false;
            if (Regex.IsMatch(key ?? "", @"(File|Path|Folder)$", RegexOptions.IgnoreCase)) return false;
            return true;
        }

        static string SafeName(string name)
        {
            string s = Regex.Replace(name.Trim(), @"[^\w .\-]", "_").Trim(' ', '.');
            if (s.Length == 0) s = "preset";
            return s.Length > 80 ? s.Substring(0, 80) : s;
        }
    }
}
