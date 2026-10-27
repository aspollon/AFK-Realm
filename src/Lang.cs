using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

namespace CoAInstaller
{
    /// <summary>
    /// The language of the window. Every text is written in English in the code and looked up by that English text
    /// in the file of the chosen language; a text a file lacks stays English. Files:
    ///   - built in: lang\&lt;code&gt;.json of the repo, embedded in the exe;
    ///   - the user's own: lang\&lt;code&gt;.json next to the exe or in %APPDATA%\AFK-Realm\lang (new languages, or
    ///     corrections of a built-in one: their texts win);
    ///   - the modules': lang\&lt;code&gt;.json in a module's folder, for the texts the module brings (its afk-realm.json,
    ///     its options). They only add texts.
    /// A file: {"code": "ja", "name": "日本語", "english": "Japanese", "font": "", "strings": {"English": "translation"}}
    /// </summary>
    static class L
    {
        public const string Auto = "auto";

        /// <summary>A language AFK Realm can show.</summary>
        public class Language
        {
            public string Code, Name, English, Font;
            public bool BuiltIn;
            public override string ToString() { return Name; }
        }

        static Dictionary<string, string> strings = new Dictionary<string, string>(StringComparer.Ordinal);
        static readonly Dictionary<string, string> moduleStrings = new Dictionary<string, string>(StringComparer.Ordinal);
        static readonly HashSet<string> moduleDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The code of the language shown ("en" when English).</summary>
        public static string Code { get; private set; }
        /// <summary>The font family the language asks for; null for the default (Segoe UI).</summary>
        public static string Font { get; private set; }

        static L() { Code = "en"; }

        /// <summary>The culture of the language shown, for names of months and the like.</summary>
        public static CultureInfo Culture
        {
            get
            {
                if (Code == "en") return CultureInfo.InvariantCulture;
                try { return CultureInfo.GetCultureInfo(Code == "zh-Hans" ? "zh-CN" : Code == "zh-Hant" ? "zh-TW" : Code); }
                catch { return CultureInfo.InvariantCulture; }
            }
        }

        static string UserDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Product.FileStem, "lang"); } }
        static string ExeDir
        {
#if AFK_WEB
            get { try { return Path.Combine(AppContext.BaseDirectory, "lang"); } catch { return null; } }
#else
            get { try { return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(System.Windows.Forms.Application.ExecutablePath)), "lang"); } catch { return null; } }
#endif
        }
        static string ChoiceFile { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Product.FileStem, "language.txt"); } }

        /// <summary>What the user chose: "auto" (the language of Windows) or a code.</summary>
        public static string Choice
        {
            get { try { var c = File.Exists(ChoiceFile) ? File.ReadAllText(ChoiceFile).Trim() : ""; return c.Length > 0 ? c : Auto; } catch { return Auto; } }
            set { try { Directory.CreateDirectory(Path.GetDirectoryName(ChoiceFile)); File.WriteAllText(ChoiceFile, value ?? Auto); } catch { } }
        }

        /// <summary>Loads the chosen language. Called once at the start, before any window is made.</summary>
        public static void Init()
        {
            try
            {
                string choice = Choice;
                string code = choice == Auto ? FromWindows() : choice;
                Load(code);
            }
            catch { strings = new Dictionary<string, string>(StringComparer.Ordinal); Code = "en"; Font = null; }
        }

        /// <summary>The language of Windows, as one of the languages there are; "en" when none fits.</summary>
        public static string FromWindows()
        {
            return Match(CultureInfo.CurrentUICulture.Name) ?? "en";
        }

        /// <summary>The best language there is for a culture name such as "de-AT", "zh-CN" or "pt-PT"; null when none.</summary>
        public static string Match(string culture)
        {
            if (string.IsNullOrEmpty(culture)) return null;
            var codes = Languages().Select(l => l.Code).ToList();
            string exact = codes.FirstOrDefault(c => c.Equals(culture, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
            string lower = culture.ToLowerInvariant();
            if (lower.StartsWith("zh"))
            {
                bool traditional = lower.Contains("hant") || lower.EndsWith("-tw") || lower.EndsWith("-hk") || lower.EndsWith("-mo");
                string want = traditional ? "zh-Hant" : "zh-Hans";
                string hit = codes.FirstOrDefault(c => c.Equals(want, StringComparison.OrdinalIgnoreCase))
                    ?? codes.FirstOrDefault(c => c.ToLowerInvariant().StartsWith("zh"));
                if (hit != null) return hit;
            }
            string lang = lower.Split('-')[0];
            return codes.FirstOrDefault(c => c.ToLowerInvariant() == lang)
                ?? codes.FirstOrDefault(c => c.ToLowerInvariant().Split('-')[0] == lang);
        }

        // ---- files ----

        class LangFile { public string Code, Name, English, Font; public Dictionary<string, string> Strings; }

        static LangFile Parse(string json)
        {
            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var d = ser.Deserialize<Dictionary<string, object>>(json);
            Func<string, string> text = k => { object v; return d.TryGetValue(k, out v) && v != null ? Convert.ToString(v) : ""; };
            var f = new LangFile { Code = text("code"), Name = text("name"), English = text("english"), Font = text("font"), Strings = new Dictionary<string, string>(StringComparer.Ordinal) };
            object s;
            if (d.TryGetValue("strings", out s) && s is Dictionary<string, object>)
                foreach (var kv in (Dictionary<string, object>)s)
                {
                    string value = kv.Value == null ? "" : Convert.ToString(kv.Value);
                    if (value.Length > 0) f.Strings[Key(kv.Key)] = value;
                }
            return f;
        }

        static IEnumerable<string> Embedded()
        {
            var asm = Assembly.GetExecutingAssembly();
            foreach (var name in asm.GetManifestResourceNames())
                if (name.StartsWith("CoAInstaller.lang.", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal))
                    yield return name;
        }

        static string ReadEmbedded(string resource)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            using (var r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
        }

        static IEnumerable<string> UserFiles()
        {
            foreach (var dir in new[] { ExeDir, UserDir })
            {
                if (dir == null || !Directory.Exists(dir)) continue;
                string[] files;
                try { files = Directory.GetFiles(dir, "*.json"); } catch { continue; }
                foreach (var f in files.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) yield return f;
            }
        }

        /// <summary>Every language there is: English, the built-in ones and those of the user's lang folders.</summary>
        public static List<Language> Languages()
        {
            var list = new List<Language> { new Language { Code = "en", Name = "English", English = "English", BuiltIn = true } };
            foreach (var res in Embedded())
                try
                {
                    var f = Parse(ReadEmbedded(res));
                    if (f.Code.Length == 0 || f.Code == "en" || f.Code == "template" || list.Any(l => l.Code.Equals(f.Code, StringComparison.OrdinalIgnoreCase))) continue;
                    list.Add(new Language { Code = f.Code, Name = f.Name.Length > 0 ? f.Name : f.Code, English = f.English, Font = f.Font, BuiltIn = true });
                }
                catch { }
            foreach (var file in UserFiles())
                try
                {
                    var f = Parse(File.ReadAllText(file, Encoding.UTF8));
                    if (f.Code.Length == 0 || f.Code == "en" || f.Code == "template" || list.Any(l => l.Code.Equals(f.Code, StringComparison.OrdinalIgnoreCase))) continue;
                    list.Add(new Language { Code = f.Code, Name = f.Name.Length > 0 ? f.Name : f.Code, English = f.English, Font = f.Font });
                }
                catch { }
            return list;
        }

        static void Load(string code)
        {
            var merged = new Dictionary<string, string>(StringComparer.Ordinal);
            string font = null;
            if (!string.IsNullOrEmpty(code) && code != "en")
            {
                foreach (var res in Embedded())
                    try
                    {
                        var f = Parse(ReadEmbedded(res));
                        if (!f.Code.Equals(code, StringComparison.OrdinalIgnoreCase)) continue;
                        foreach (var kv in f.Strings) merged[kv.Key] = kv.Value;
                        if (f.Font.Length > 0) font = f.Font;
                    }
                    catch { }
                foreach (var file in UserFiles())
                    try
                    {
                        var f = Parse(File.ReadAllText(file, Encoding.UTF8));
                        if (!f.Code.Equals(code, StringComparison.OrdinalIgnoreCase)) continue;
                        foreach (var kv in f.Strings) merged[kv.Key] = kv.Value;
                        if (f.Font.Length > 0) font = f.Font;
                    }
                    catch { }
            }
            strings = merged;
            Code = string.IsNullOrEmpty(code) ? "en" : code;
            Font = font;
            moduleStrings.Clear();
            var dirs = moduleDirs.ToList();
            moduleDirs.Clear();
            foreach (var d in dirs) AddModule(d);
        }

        /// <summary>Adds the texts a module brings for the language shown (lang\&lt;code&gt;.json in its folder).</summary>
        public static void AddModule(string moduleDir)
        {
            if (string.IsNullOrEmpty(moduleDir)) return;
            moduleDirs.Add(moduleDir);
            if (Code == "en") return;
            try
            {
                string dir = Path.Combine(moduleDir, "lang");
                if (!Directory.Exists(dir)) return;
                foreach (var file in Directory.GetFiles(dir, "*.json"))
                    try
                    {
                        var f = Parse(File.ReadAllText(file, Encoding.UTF8));
                        if (!f.Code.Equals(Code, StringComparison.OrdinalIgnoreCase)) continue;
                        foreach (var kv in f.Strings) if (!moduleStrings.ContainsKey(kv.Key)) moduleStrings[kv.Key] = kv.Value;
                    }
                    catch { }
            }
            catch { }
        }

        /// <summary>Adds the texts of every module in a server's module folder.</summary>
        public static void AddModules(string modulesDir)
        {
            try
            {
                if (!Directory.Exists(modulesDir)) return;
                foreach (var d in Directory.GetDirectories(modulesDir)) AddModule(d);
            }
            catch { }
        }

        // ---- lookups ----

        /// <summary>Line ends as \n, no spaces around: how a text is looked up.</summary>
        static string Key(string s) { return (s ?? "").Replace("\r\n", "\n").Trim(); }

        /// <summary>The text in the language shown.</summary>
        public static string T(string english)
        {
            if (string.IsNullOrEmpty(english) || Code == "en") return english;
            string v;
            if (strings.TryGetValue(english, out v) || moduleStrings.TryGetValue(english, out v)) return v;
            string k = Key(english);
            if (k.Length == english.Length) return english;
            if (strings.TryGetValue(k, out v) || moduleStrings.TryGetValue(k, out v))
            {
                // Keep the spaces and line ends around the text.
                int lead = english.Length - english.TrimStart().Length;
                int trail = english.Length - english.TrimEnd().Length;
                return english.Substring(0, lead) + v + english.Substring(english.Length - trail);
            }
            return english;
        }

        /// <summary>A text with places for values ({0}, {1} ...), in the language shown.</summary>
        public static string F(string english, params object[] args)
        {
            string t = T(english);
            try { return string.Format(CultureInfo.InvariantCulture, t, args); }
            catch (FormatException)
            {
                try { return string.Format(CultureInfo.InvariantCulture, english, args); } catch { return english; }
            }
        }

        /// <summary>
        /// A short text that means different things in different places ("Rare" of an item's quality and of a
        /// creature): looked up as "context|text", shown as the text in English.
        /// </summary>
        public static string C(string context, string english)
        {
            if (Code == "en") return english;
            string key = context + "|" + english, v;
            if (strings.TryGetValue(key, out v) || moduleStrings.TryGetValue(key, out v)) return v;
            return english;
        }

        /// <summary>Marks an English text to translate where it is shown (with Outside or T); returns it unchanged.</summary>
        public static string N(string english) { return english; }

        /// <summary>A text from outside (a module's afk-realm.json, a .conf description): translated when a file has it.</summary>
        public static string Outside(string english) { return T(english); }

        // ---- texts of the engine ----

        static readonly System.Text.RegularExpressions.Regex Marked = new System.Text.RegularExpressions.Regex("\u0001([^\u0001\u0003]*)\u0003");

        /// <summary>
        /// A text of the PowerShell engine: English with places and its values between marks (Tr in engine.ps1),
        /// innermost first, so a value can be such a text itself. Shown in the language of the window.
        /// </summary>
        public static string Engine(string text) { return Unmark(text, true); }

        /// <summary>The same text in English (for the log).</summary>
        public static string Plain(string text) { return Unmark(text, false); }

        static string Unmark(string text, bool translate)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('\u0001') < 0) return text;
            for (int i = 0; i < 10 && Marked.IsMatch(text); i++)
                text = Marked.Replace(text, m =>
                {
                    var p = m.Groups[1].Value.Split('\u0002');
                    var values = p.Skip(1).Cast<object>().ToArray();
                    if (!translate) { try { return string.Format(CultureInfo.InvariantCulture, p[0], values); } catch { return string.Join(" ", p); } }
                    return values.Length > 0 ? F(p[0], values) : T(p[0]);
                });
            return text.Replace("\u0001", "").Replace("\u0002", " ").Replace("\u0003", "");
        }

        // ---- a new language ----

        /// <summary>Writes a file with every text of AFK Realm for a new language into the user's lang folder; returns its path.</summary>
        public static string WriteTemplate(string code)
        {
            Directory.CreateDirectory(UserDir);
            string file = Path.Combine(UserDir, code + ".json");
            if (File.Exists(file)) return file;
            var keys = new List<string>();
            foreach (var res in Embedded())
                if (res.EndsWith(".template.json", StringComparison.Ordinal))
                    try { keys.AddRange(Parse(ReadEmbedded(res)).Strings.Keys); } catch { }
            var sb = new StringBuilder();
            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            sb.Append("{\n  \"code\": ").Append(ser.Serialize(code))
              .Append(",\n  \"name\": ").Append(ser.Serialize(code))
              .Append(",\n  \"english\": \"\",\n  \"font\": \"\",\n  \"strings\": {\n");
            for (int i = 0; i < keys.Count; i++)
                sb.Append("    ").Append(ser.Serialize(keys[i])).Append(": \"\"").Append(i < keys.Count - 1 ? ",\n" : "\n");
            sb.Append("  }\n}\n");
            File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
            return file;
        }

        public static string UserFolder { get { return UserDir; } }

        /// <summary>Every text of the language shown with its translation, the modules' included (for the page of the web version).</summary>
        public static Dictionary<string, string> All()
        {
            var all = new Dictionary<string, string>(moduleStrings, StringComparer.Ordinal);
            foreach (var kv in strings) all[kv.Key] = kv.Value;
            return all;
        }
    }
}
