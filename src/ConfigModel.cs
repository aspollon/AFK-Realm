using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CoAInstaller
{
    enum OptionKind { Text, Number, Bool, Choice }

    /// <summary>One "Key = Value" option, described by the comments of its .conf.dist template.</summary>
    class ConfigOption
    {
        public ConfigFile File;
        public string Key, Section = "", Description = "", DefaultRaw;
        public OptionKind Kind = OptionKind.Text;
        public bool Quoted;
        public bool Locked;               // managed by AFK Realm, shown read-only
        public string Label;              // friendly name in the popular list
        public string DescriptionSource = "";   // how the description was found (for checks)
        public string DescriptionBody;          // the description of the template without the note on shared options
        public string SharedWith = "";          // the other options the description of the template is for
        /// <summary>The description in the language shown: a module's lang file can carry its options' descriptions.</summary>
        public string ShownDescription
        {
            get
            {
                if (DescriptionBody == null) return L.Outside(Description);
                return L.Outside(DescriptionBody) + (SharedWith.Length > 0 ? "\n\n" + L.F("This description is shared with: {0}", SharedWith) : "");
            }
        }
        public readonly List<string> BlockKeys = new List<string>();
        public readonly List<KeyValuePair<string, string>> Choices = new List<KeyValuePair<string, string>>();

        public string CurrentRaw { get { string v; return File.Values.TryGetValue(Key, out v) ? v : DefaultRaw; } }
        public string EditedRaw { get { string v; return File.Pending.TryGetValue(Key, out v) ? v : CurrentRaw; } }
        public bool IsPending { get { return File.Pending.ContainsKey(Key); } }
        public bool DiffersFromDefault { get { return DefaultRaw != null && Normalize(EditedRaw) != Normalize(DefaultRaw); } }

        public static string Unquote(string raw)
        {
            raw = (raw ?? "").Trim();
            return IsQuoted(raw) ? raw.Substring(1, raw.Length - 2) : raw;
        }
        /// <summary>Whether a text is wrapped in the quotes a quoted option stores (both ends).</summary>
        public static bool IsQuoted(string text)
        {
            text = (text ?? "").Trim();
            return text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"';
        }
        static string Normalize(string raw)
        {
            string v = Unquote(raw);
            double d;
            return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d.ToString("R", CultureInfo.InvariantCulture) : v;
        }
        /// <summary>The text shown to the user (without quotes).</summary>
        public string Display(string raw)
        {
            string v = Unquote(raw);
            if (Kind == OptionKind.Bool) return v == "1" ? L.T("On") : v == "0" ? L.T("Off") : v;
            if (Kind == OptionKind.Choice)
                foreach (var c in Choices) if (c.Key == v) return v + " – " + L.Outside(c.Value);
            return v;
        }
        /// <summary>Checks a value typed by the user and returns the raw text to store, or throws with a readable message.</summary>
        public string ToRaw(string input)
        {
            string v = (input ?? "").Trim();
            if (v.IndexOf('\n') >= 0 || v.IndexOf('\r') >= 0) throw new FormatException(L.T("The value must be on one line."));
            if (Kind == OptionKind.Bool && v != "0" && v != "1") throw new FormatException(L.F("{0} can only be 0 (off) or 1 (on).", Key));
            if (Kind == OptionKind.Number || Kind == OptionKind.Choice)
            {
                double d;
                if (!double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                    throw new FormatException(L.F("{0} needs a number (use a dot for decimals, e.g. 1.5).", Key));
            }
            if (Quoted)
            {
                if (v.IndexOf('"') >= 0) throw new FormatException(L.T("The value must not contain quotation marks."));
                return "\"" + v + "\"";
            }
            return v;
        }
    }

    /// <summary>A server config file (.conf) together with its template (.conf.dist).</summary>
    class ConfigFile
    {
        public string Name;                // e.g. "worldserver.conf", "modules\playerbots.conf"
        public string Path;
        public readonly List<ConfigOption> Options = new List<ConfigOption>();
        public readonly Dictionary<string, ConfigOption> ByKey = new Dictionary<string, ConfigOption>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> Pending = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Options to take out of the file on Save (options without a fixed name, such as a level set by hand for one zone).</summary>
        public readonly HashSet<string> Removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Written by AFK Realm itself; changing them by hand breaks the installation.
        static readonly Regex LockedKeys = new Regex(@"^(\w+DatabaseInfo|MySQLExecutable|DataDir|SourceDirectory|LogsDir|TempDir|BindIP|CoA\.AllowRemoteClients|Updates\.EnableDatabases)$", RegexOptions.IgnoreCase);
        static readonly Regex OptionLine = new Regex(@"^[ \t]*([A-Za-z][\w.\-]*)[ \t]*=[ \t]*(.*?)[ \t]*$");
        static readonly Regex HeaderLine = new Regex(@"^#[ \t]*([A-Z][A-Z0-9 &/()'+,.\-]*[A-Z)])[ \t]*(#*)[ \t]*$");
        static readonly Regex SpacerLine = new Regex(@"^[#\s]*$");
        static readonly Regex ChoiceLine = new Regex(@"^(?:(?:Default|Values?|Options?)\s*:\s*)?(-?\d+)\s+-\s+\(?(.+?)\)?\s*$", RegexOptions.IgnoreCase);

        public static ConfigFile Load(string confPath, string name)
        {
            var file = new ConfigFile { Name = name, Path = confPath };
            string dist = confPath + ".dist";
            if (System.IO.File.Exists(dist)) file.ParseTemplate(System.IO.File.ReadAllLines(dist));
            if (System.IO.File.Exists(confPath))
            {
                foreach (var line in System.IO.File.ReadAllLines(confPath))
                {
                    var m = OptionLine.Match(line);
                    if (!m.Success) continue;
                    // The last line wins, as it does for the server; a hand-edited file may
                    // still contain the old default line above the new one.
                    file.Values[m.Groups[1].Value] = m.Groups[2].Value;
                    if (!file.ByKey.ContainsKey(m.Groups[1].Value))
                        file.Add(new ConfigOption { Key = m.Groups[1].Value, Section = L.T("Other (not in the template)"), DefaultRaw = null,
                            Quoted = m.Groups[2].Value.StartsWith("\""), Description = LockedKeys.IsMatch(m.Groups[1].Value) ? L.F("Written by {0} so the server can work.", Product.Name) : L.T("This option is not part of the current template. It may be outdated.") });
                }
            }
            return file;
        }

        void Add(ConfigOption o)
        {
            o.File = this;
            o.Locked = LockedKeys.IsMatch(o.Key);
            Options.Add(o); ByKey[o.Key] = o;
        }

        void ParseTemplate(string[] lines)
        {
            string top = "", sub = "";
            var block = new List<string>();        // comment lines right above the current option(s)
            var byName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var pendingNames = new List<string>();
            bool blockUsed = false;
            string previous = "";
            foreach (var raw in lines)
            {
                string line = raw.TrimEnd();
                if (line.StartsWith("#"))
                {
                    var h = HeaderLine.Match(line);
                    if (h.Success && SpacerLine.IsMatch(previous) && !line.StartsWith("##"))
                    {
                        string caps = h.Groups[1].Value.Trim();
                        bool end = caps.EndsWith(" END");
                        string title = Title(Regex.Replace(caps, @" (BEGIN|END)$", ""));
                        if (h.Groups[2].Value.Length > 0) { top = end ? "" : title; sub = ""; } else sub = title;
                        block = new List<string>(); blockUsed = false;
                    }
                    else if (line.StartsWith("###")) { block = new List<string>(); blockUsed = false; }
                    else
                    {
                        // A blank line between comments starts the description of the next option, and so does
                        // a key name of its own after a "#" line, when the block so far described a named option.
                        bool nextName = Regex.IsMatch(line, @"^#[ \t]{2,}[A-Za-z][\w.\-]*\.[\w.\-]+[ \t]*$") && SpacerLine.IsMatch(previous) &&
                                        block.Any(b => !SpacerLine.IsMatch(b) && !Regex.IsMatch(b, @"^#[ \t]{2,}[A-Za-z][\w.\-]*[ \t]*$")) &&
                                        byName.Values.Any(v => ReferenceEquals(v, block));
                        if (blockUsed || nextName || (block.Count > 0 && previous.Length == 0)) { block = new List<string>(); blockUsed = false; }
                        block.Add(line);
                        // AzerothCore style names the keys at the top of their block; the option
                        // lines can come much later, so the block is remembered per key name.
                        var k = Regex.Match(line, @"^#[ \t]{2,}([A-Za-z][\w.\-]*)[ \t]*$");
                        if (k.Success && block.Take(block.Count - 1).All(b => SpacerLine.IsMatch(b) || Regex.IsMatch(b, @"^#[ \t]{2,}[A-Za-z][\w.\-]*[ \t]*$")))
                        { pendingNames.Add(k.Groups[1].Value); byName[k.Groups[1].Value] = block; }
                        else if (pendingNames.Count > 0 && !k.Success) pendingNames.Clear();
                        // Options only named in a block's default list ("1 - (Rate.XP.Quest.DF)") belong to that block too.
                        foreach (Match d in Regex.Matches(line, @"-\s+\(([A-Za-z][\w\-]*\.[\w.\-]+)\)"))
                            if (!byName.ContainsKey(d.Groups[1].Value)) byName[d.Groups[1].Value] = block;
                    }
                }
                else if (line.Length > 0 && !line.StartsWith("["))
                {
                    var m = OptionLine.Match(line);
                    if (m.Success && !ByKey.ContainsKey(m.Groups[1].Value))
                    {
                        var o = new ConfigOption { Key = m.Groups[1].Value, DefaultRaw = m.Groups[2].Value, Quoted = m.Groups[2].Value.StartsWith("\"") };
                        o.Section = top.Length > 0 && sub.Length > 0 ? top + " › " + sub : (sub.Length > 0 ? sub : (top.Length > 0 ? top : L.T("General")));
                        List<string> named;
                        bool byKey = byName.TryGetValue(o.Key, out named);
                        o.DescriptionSource = byKey ? "named" : (block.Count > 0 ? "above" : "none");
                        Describe(o, byKey ? named : block);
                        Add(o);
                    }
                    blockUsed = true;
                }
                previous = line;
            }
        }

        static string Title(string caps)
        {
            var t = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(caps.ToLowerInvariant());
            return t.Replace(" And ", " and ").Replace(" Of ", " of ").Replace(" The ", " the ").Replace(" Or ", " or ").Replace("Pvp", "PvP").Replace("Gm", "GM")
                    .Replace("Lfg", "LFG").Replace("Ffa", "FFA").Replace("Npc", "NPC").Replace("Ai ", "AI ");
        }

        static void Describe(ConfigOption o, List<string> block)
        {
            // Plain text of the comment block, without the '#' and common indentation.
            var text = block.Select(l => l.TrimStart('#')).ToList();
            int indent = text.Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
            text = text.Select(l => l.Length >= indent ? l.Substring(indent) : l.TrimStart()).ToList();
            // AzerothCore style: the block starts with the key name(s) on their own lines.
            var keyLines = text.TakeWhile(l => l.Trim().Length == 0 || Regex.IsMatch(l.Trim(), @"^[A-Za-z][\w.\-]*$")).ToList();
            int sharedKeys = keyLines.Count(l => l.Trim().Length > 0);
            o.BlockKeys.AddRange(keyLines.Select(l => l.Trim()).Where(l => l.Length > 0));
            var body = text.Skip(keyLines.Count).Select(l => l.TrimEnd()).ToList();
            while (body.Count > 0 && body[body.Count - 1].Trim().Length == 0) body.RemoveAt(body.Count - 1);
            int bodyIndent = body.Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
            var sb = new StringBuilder();
            // One block can describe several options ("1 - (Rate.Drop.Item.Poor)", "1 - (Rate.Drop.Money)", ...).
            // Only the default of the option being shown is kept, so the list does not look like value choices.
            var perKey = new Regex(@"^(Default:\s*)?(\S+)\s+-\s+\(([A-Za-z][\w.\-]*)\)(.*)$");
            bool defaultWritten = false;
            foreach (var l in body)
            {
                string t = l.Length >= bodyIndent ? l.Substring(bodyIndent) : l.TrimStart();
                t = Regex.Replace(t, @"^Description:\s*", "");
                if (sharedKeys > 1)
                {
                    var pk = perKey.Match(t.Trim());
                    if (pk.Success && pk.Groups[3].Value.Contains("."))
                    {
                        if (!pk.Groups[3].Value.Equals(o.Key, StringComparison.OrdinalIgnoreCase) || defaultWritten) continue;
                        string note = pk.Groups[4].Value.Trim().TrimStart('-').Trim();
                        t = "Default:     " + pk.Groups[2].Value + (note.Length > 0 ? " (" + note + ")" : "");
                        defaultWritten = true;
                    }
                }
                sb.AppendLine(t);
            }
            if (sharedKeys > 1)
            {
                var others = keyLines.Select(k => k.Trim()).Where(k => k.Length > 0 && !k.Equals(o.Key, StringComparison.OrdinalIgnoreCase)).ToList();
                if (others.Count > 0) o.SharedWith = string.Join(", ", others);
            }
            o.DescriptionBody = Regex.Replace(sb.ToString().Trim(), @"\n\s{10,}", "\n    ");
            o.Description = o.DescriptionBody + (o.SharedWith.Length > 0 ? "\n\nThis description is shared with: " + o.SharedWith : "");

            // Value lists like "0 - (Disabled)  1 - (Enabled)" belong to a single option only.
            if (sharedKeys <= 1)
            {
                foreach (var l in body)
                {
                    var c = ChoiceLine.Match(l.Trim());
                    if (c.Success && !o.Choices.Any(x => x.Key == c.Groups[1].Value)) o.Choices.Add(new KeyValuePair<string, string>(c.Groups[1].Value, c.Groups[2].Value.Trim()));
                }
            }
            string def = ConfigOption.Unquote(o.DefaultRaw);
            double num;
            bool numeric = !o.Quoted && double.TryParse(def, NumberStyles.Float, CultureInfo.InvariantCulture, out num);
            var values = new HashSet<string>(o.Choices.Select(c => c.Key));
            bool rangeHint = Regex.IsMatch(o.Description, @"\d+\+\s+-|\bRange\b", RegexOptions.IgnoreCase);
            // 0/1 is only a switch when the two values mean off and on; otherwise they are two variants.
            var onOff = new Regex(@"^\(?\s*(disabled?|enabled?|off|on|no|yes|false|true)\b", RegexOptions.IgnoreCase);
            bool switchLabels = o.Choices.All(c => onOff.IsMatch(c.Value));
            if (numeric && values.Count == 2 && values.Contains("0") && values.Contains("1") && !rangeHint && switchLabels) o.Kind = OptionKind.Bool;
            else if (numeric && values.Count >= 2 && values.Contains(def) && !rangeHint && o.Choices.All(c => !Regex.IsMatch(c.Value, @"^[A-Za-z][\w.]*\.[\w.]+$"))) o.Kind = OptionKind.Choice;
            else if (numeric && (def == "0" || def == "1") && values.Count == 0 &&
                     !Regex.IsMatch(o.Key, @"(Level|Weight|(?<!Ac)Count|Chance|Distance|Delay|Time|Interval|Min|Max|Rate|Radius|Limit|Size|Percent|Seconds|Cost|Multiplier|Factor|Amount|Number|Id)", RegexOptions.None) &&
                     (Regex.IsMatch(o.Description, @"\b(enable[sd]?|disable[sd]?|allow|toggle|turn (on|off))\b", RegexOptions.IgnoreCase) || Regex.IsMatch(o.Key, @"(Enable|Enabled|Allow|Disable|Disabled|^[^.]*\.?Delete|\.Delete)", RegexOptions.None))
                     && !Regex.IsMatch(o.Description, @"\b[2-9]\d*\s*(-|=|:)", RegexOptions.None)) o.Kind = OptionKind.Bool;
            else if (numeric) o.Kind = OptionKind.Number;
            if (o.Kind != OptionKind.Choice) o.Choices.Clear();
        }

        /// <summary>Writes all pending changes into the .conf file. A one-time backup is kept next to it.</summary>
        public void Save()
        {
            if (Pending.Count == 0 && Removed.Count == 0) return;
            string backup = Path + ".afk-backup";
            if (!System.IO.File.Exists(backup) && System.IO.File.Exists(Path)) System.IO.File.Copy(Path, backup);
            // A module whose .conf was never created starts from its complete template.
            if (!System.IO.File.Exists(Path) && System.IO.File.Exists(Path + ".dist")) System.IO.File.Copy(Path + ".dist", Path);
            string text = System.IO.File.Exists(Path) ? System.IO.File.ReadAllText(Path) : "";
            foreach (var p in Pending)
            {
                string line = p.Key + " = " + p.Value;
                var rx = new Regex(@"(?m)^[ \t]*" + Regex.Escape(p.Key) + @"[ \t]*=[^\r\n]*");
                if (rx.IsMatch(text)) text = rx.Replace(text, line.Replace("$", "$$"));
                else text = text.TrimEnd('\r', '\n') + "\r\n" + line + "\r\n";
                Values[p.Key] = p.Value;
            }
            foreach (var key in Removed)
            {
                if (Pending.ContainsKey(key)) continue;
                text = new Regex(@"(?m)^[ \t]*" + Regex.Escape(key) + @"[ \t]*=[^\r\n]*(\r?\n)?").Replace(text, "");
                Values.Remove(key);
            }
            System.IO.File.WriteAllText(Path, text, new UTF8Encoding(false));
            Pending.Clear(); Removed.Clear();
        }
    }

    /// <summary>All config files of an installation, plus the hand-picked list of popular settings.</summary>
    class ConfigSet
    {
        public readonly List<ConfigFile> Files = new List<ConfigFile>();
        public readonly List<ConfigOption> Popular = new List<ConfigOption>();

        // file, key, friendly label - grouped by the section shown in the popular list
        static readonly string[][] PopularDefs =
        {
            new[] { L.T("Experience and levels"), "worldserver.conf", "Rate.XP.Global", L.T("XP rate for everything (multiplier)") },
            new[] { L.T("Experience and levels"), "worldserver.conf", "Rate.XP.Kill", L.T("XP from kills") },
            new[] { L.T("Experience and levels"), "worldserver.conf", "Rate.XP.Quest", L.T("XP from quests") },
            new[] { L.T("Experience and levels"), "worldserver.conf", "Rate.XP.Explore", L.T("XP from exploring") },
            new[] { L.T("Experience and levels"), "worldserver.conf", "Rate.Rest.InGame", L.T("Rested XP gain while logged in") },
            new[] { L.T("Experience and levels"), "worldserver.conf", "MaxPlayerLevel", L.T("Maximum player level") },
            new[] { L.T("Experience and levels"), "worldserver.conf", "StartPlayerLevel", L.T("Start level of new characters") },
            new[] { L.T("Experience and levels"), "worldserver.conf", "StartPlayerMoney", L.T("Start money of new characters (copper)") },
            new[] { L.T("Loot, money and reputation"), "worldserver.conf", "Rate.Drop.Money", L.T("Money drop rate") },
            new[] { L.T("Loot, money and reputation"), "worldserver.conf", "Rate.Drop.Item.Uncommon", L.T("Drop rate: uncommon (green) items") },
            new[] { L.T("Loot, money and reputation"), "worldserver.conf", "Rate.Drop.Item.Rare", L.T("Drop rate: rare (blue) items") },
            new[] { L.T("Loot, money and reputation"), "worldserver.conf", "Rate.Drop.Item.Epic", L.T("Drop rate: epic (purple) items") },
            new[] { L.T("Loot, money and reputation"), "worldserver.conf", "Rate.Reputation.Gain", L.T("Reputation gain rate") },
            new[] { L.T("Loot, money and reputation"), "worldserver.conf", "Rate.Honor", L.T("Honor rate") },
            new[] { L.T("Loot, money and reputation"), "worldserver.conf", "SkillGain.Crafting", L.T("Crafting skill-up rate") },
            new[] { L.T("Loot, money and reputation"), "worldserver.conf", "SkillGain.Gathering", L.T("Gathering skill-up rate") },
            new[] { L.T("Convenience"), "worldserver.conf", "InstantFlightPaths", L.T("Instant flight paths") },
            new[] { L.T("Convenience"), "worldserver.conf", "AllFlightPaths", L.T("New characters know all flight paths") },
            new[] { L.T("Convenience"), "worldserver.conf", "Instance.IgnoreLevel", L.T("Enter dungeons at any level") },
            new[] { L.T("Convenience"), "worldserver.conf", "Instance.IgnoreRaid", L.T("Enter raids without a raid group") },
            new[] { L.T("Convenience"), "worldserver.conf", "DurabilityLoss.OnDeath", L.T("Durability loss on death (%)") },
            new[] { L.T("Convenience"), "worldserver.conf", "AllowTwoSide.Interaction.Group", L.T("Groups with the other faction") },
            new[] { L.T("Convenience"), "worldserver.conf", "AllowTwoSide.Interaction.Guild", L.T("Guilds with the other faction") },
            new[] { L.T("Convenience"), "worldserver.conf", "AllowTwoSide.Interaction.Chat", L.T("Chat with the other faction") },
            new[] { L.T("Convenience"), "worldserver.conf", "PlayerLimit", L.T("Player limit (0 = no limit)") },
            new[] { L.T("Playerbots"), "modules\\playerbots.conf", "AiPlayerbot.Enabled", L.T("Playerbots on") },
            new[] { L.T("Playerbots"), "modules\\playerbots.conf", "AiPlayerbot.RandomBotAutologin", L.T("Random bots populate the world") },
            new[] { L.T("Playerbots"), "modules\\playerbots.conf", "AiPlayerbot.MinRandomBots", L.T("Random bots: minimum count") },
            new[] { L.T("Playerbots"), "modules\\playerbots.conf", "AiPlayerbot.MaxRandomBots", L.T("Random bots: maximum count") },
            new[] { L.T("Playerbots"), "modules\\playerbots.conf", "AiPlayerbot.RandomBotMinLevel", L.T("Random bots: lowest level") },
            new[] { L.T("Playerbots"), "modules\\playerbots.conf", "AiPlayerbot.RandomBotMaxLevel", L.T("Random bots: highest level") },
            new[] { L.T("Playerbots"), "modules\\playerbots.conf", "AiPlayerbot.DisabledWithoutRealPlayer", L.T("Random bots only while a real player is online") },
            new[] { L.T("Playerbots"), "modules\\playerbots.conf", "AiPlayerbot.SyncLevelWithPlayers", L.T("Random bot levels follow the players") },
            new[] { L.T("Playerbots"), "modules\\playerbots.conf", "AiPlayerbot.MaxAddedBots", L.T("Bots per player (party bots)") },
            new[] { L.T("Playerbots"), "modules\\playerbots.conf", "AiPlayerbot.AutoDoQuests", L.T("Bots do quests on their own") },
            new[] { L.T("Playerbots"), "modules\\playerbots.conf", "AiPlayerbot.RandomBotJoinBG", L.T("Bots join battlegrounds") },
            new[] { L.T("Playerbots"), "modules\\playerbots.conf", "AiPlayerbot.RandomBotTalk", L.T("Bots chat") },
            new[] { L.T("Playerbots"), "modules\\playerbots.conf", "AiPlayerbot.BotActiveAlone", L.T("Bots active without players nearby (%)") },
        };

        public static ConfigSet Load(Install inst)
        {
            var set = new ConfigSet();
            var names = new List<string> { "worldserver.conf", "modules\\playerbots.conf" };
            string modules = Path.Combine(inst.ConfigDir, "modules");
            if (Directory.Exists(modules))
                names.AddRange(Directory.GetFiles(modules, "*.conf.dist").Select(f => "modules\\" + Path.GetFileName(f).Replace(".conf.dist", ".conf"))
                    .Where(n => !names.Contains(n, StringComparer.OrdinalIgnoreCase)).OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
            names.Add("authserver.conf");
            foreach (var n in names)
            {
                string path = Path.Combine(inst.ConfigDir, n.Replace('\\', Path.DirectorySeparatorChar));
                if (File.Exists(path) || File.Exists(path + ".dist")) set.Files.Add(ConfigFile.Load(path, n));
            }
            foreach (var d in PopularDefs)
            {
                var file = set.Files.FirstOrDefault(f => f.Name.Equals(d[1], StringComparison.OrdinalIgnoreCase));
                ConfigOption o;
                if (file == null || !file.ByKey.TryGetValue(d[2], out o)) continue;
                o.Label = d[3];
                set.Popular.Add(o);
                set.PopularSection[o] = d[0];
            }
            return set;
        }
        public readonly Dictionary<ConfigOption, string> PopularSection = new Dictionary<ConfigOption, string>();

        public int PendingCount { get { return Files.Sum(f => f.Pending.Count); } }
        public void SaveAll() { foreach (var f in Files) f.Save(); }
        public void DiscardAll() { foreach (var f in Files) f.Pending.Clear(); }
    }
}
