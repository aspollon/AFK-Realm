using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace CoAInstaller
{
    /// <summary>
    /// Modules that change what the random bots are made of - classes, races - say so in their afk-realm.json
    /// ("bots": {"reset": true, "why": "..."}). The bots that exist keep what they are, so AFK Realm offers once,
    /// after such a module came, to make the random bots anew. Asked modules are kept in Dependencies\bot-reset-offered.txt.
    /// </summary>
    static class ModuleBots
    {
        static string RecordFile(Install inst) { return Path.Combine(inst.Root, "Dependencies", "bot-reset-offered.txt"); }

        static HashSet<string> Offered(Install inst)
        {
            try { return new HashSet<string>(File.Exists(RecordFile(inst)) ? File.ReadAllLines(RecordFile(inst)).Select(l => l.Trim()).Where(l => l.Length > 0) : new string[0], StringComparer.OrdinalIgnoreCase); }
            catch { return new HashSet<string>(StringComparer.OrdinalIgnoreCase); }
        }

        /// <summary>
        /// Installed modules that ask for new bots and were not asked about yet: module -> why. Only modules the
        /// worldserver was built with count (its exe is newer than the module's folder): before that, new bots would
        /// not have what the module brings.
        /// </summary>
        public static List<KeyValuePair<string, string>> NotOffered(Install inst, DateTime serverBuilt)
        {
            var result = new List<KeyValuePair<string, string>>();
            string modules = ModuleCatalog.ModulesDir(inst);
            if (!Directory.Exists(modules)) return result;
            var offered = Offered(inst);
            var json = new JavaScriptSerializer();
            foreach (var dir in Directory.GetDirectories(modules).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    string name = Path.GetFileName(dir), file = Path.Combine(dir, ModuleCatalog.ManifestName);
                    if (offered.Contains(name) || !File.Exists(file) || Directory.GetCreationTimeUtc(dir) > serverBuilt) continue;
                    object v;
                    var data = json.DeserializeObject(File.ReadAllText(file, Encoding.UTF8)) as Dictionary<string, object>;
                    var bots = data != null && data.TryGetValue("bots", out v) ? v as Dictionary<string, object> : null;
                    if (bots == null || !(bots.TryGetValue("reset", out v) && v is bool && (bool)v)) continue;
                    result.Add(new KeyValuePair<string, string>(name, bots.TryGetValue("why", out v) ? Convert.ToString(v) : ""));
                }
                catch { }
            }
            return result;
        }

        public static void MarkOffered(Install inst, IEnumerable<string> modules)
        {
            try
            {
                var all = Offered(inst);
                foreach (var m in modules) all.Add(m);
                Directory.CreateDirectory(Path.GetDirectoryName(RecordFile(inst)));
                File.WriteAllLines(RecordFile(inst), all.OrderBy(m => m, StringComparer.OrdinalIgnoreCase), new UTF8Encoding(false));
            }
            catch { }
        }
    }
}
