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
        public readonly List<KeyValuePair<string, string>> Choices = new List<KeyValuePair<string, string>>();

        public string CurrentRaw { get { string v; return File.Values.TryGetValue(Key, out v) ? v : DefaultRaw; } }
        public string EditedRaw { get { string v; return File.Pending.TryGetValue(Key, out v) ? v : CurrentRaw; } }
        public bool IsPending { get { return File.Pending.ContainsKey(Key); } }
        public bool DiffersFromDefault { get { return DefaultRaw != null && Normalize(EditedRaw) != Normalize(DefaultRaw); } }

        public static string Unquote(string raw)
        {
            raw = (raw ?? "").Trim();
            return raw.Length >= 2 && raw[0] == '"' && raw[raw.Length - 1] == '"' ? raw.Substring(1, raw.Length - 2) : raw;
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
            if (Kind == OptionKind.Bool) return v == "1" ? "On" : v == "0" ? "Off" : v;
            if (Kind == OptionKind.Choice)
                foreach (var c in Choices) if (c.Key == v) return v + " – " + c.Value;
            return v;
        }
        /// <summary>Checks a value typed by the user and returns the raw text to store, or throws with a readable message.</summary>
        public string ToRaw(string input)
        {
            string v = (input ?? "").Trim();
            if (v.IndexOf('\n') >= 0 || v.IndexOf('\r') >= 0) throw new FormatException("The value must be on one line.");
            if (Kind == OptionKind.Bool && v != "0" && v != "1") throw new FormatException(Key + " can only be 0 (off) or 1 (on).");
            if (Kind == OptionKind.Number || Kind == OptionKind.Choice)
            {
                double d;
                if (!double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                    throw new FormatException(Key + " needs a number (use a dot for decimals, e.g. 1.5).");
            }
            if (Quoted)
            {
                if (v.IndexOf('"') >= 0) throw new FormatException("The value must not contain quotation marks.");
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
                    if (!m.Success || file.Values.ContainsKey(m.Groups[1].Value)) continue;
                    file.Values[m.Groups[1].Value] = m.Groups[2].Value;
                    if (!file.ByKey.ContainsKey(m.Groups[1].Value))
                        file.Add(new ConfigOption { Key = m.Groups[1].Value, Section = "Other (not in the template)", DefaultRaw = null,
                            Quoted = m.Groups[2].Value.StartsWith("\""), Description = LockedKeys.IsMatch(m.Groups[1].Value) ? "Written by " + Product.Name + " so the server can work." : "This option is not part of the current template. It may be outdated." });
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
                        // A blank line between comments starts the description of the next option.
                        if (blockUsed || (block.Count > 0 && previous.Length == 0)) { block = new List<string>(); blockUsed = false; }
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
                        o.Section = top.Length > 0 && sub.Length > 0 ? top + " › " + sub : (sub.Length > 0 ? sub : (top.Length > 0 ? top : "General"));
                        List<string> named;
                        Describe(o, byName.TryGetValue(o.Key, out named) ? named : block);
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
            return t.Replace(" And ", " and ").Replace(" Of ", " of ").Replace(" Or ", " or ").Replace("Pvp", "PvP").Replace("Gm", "GM")
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
                if (others.Count > 0) sb.AppendLine().Append("This description is shared with: ").AppendLine(string.Join(", ", others));
            }
            o.Description = Regex.Replace(sb.ToString().Trim(), @"\n\s{10,}", "\n    ");

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
            if (numeric && values.Count == 2 && values.Contains("0") && values.Contains("1") && !rangeHint) o.Kind = OptionKind.Bool;
            else if (numeric && values.Count >= 2 && values.Contains(def) && !rangeHint && o.Choices.All(c => !Regex.IsMatch(c.Value, @"^[A-Za-z][\w.]*\.[\w.]+$"))) o.Kind = OptionKind.Choice;
            else if (numeric && (def == "0" || def == "1") && values.Count == 0 &&
                     !Regex.IsMatch(o.Key, @"(Level|Weight|Count|Chance|Distance|Delay|Time|Interval|Min|Max|Rate|Radius|Limit|Size|Percent|Seconds|Cost|Multiplier|Factor|Amount|Number|Id)", RegexOptions.None) &&
                     (Regex.IsMatch(o.Description, @"\b(enable[sd]?|disable[sd]?|allow|toggle|turn (on|off))\b", RegexOptions.IgnoreCase) || Regex.IsMatch(o.Key, @"(Enable|Enabled|Allow|Disable|Disabled)", RegexOptions.None))
                     && !Regex.IsMatch(o.Description, @"\b[2-9]\d*\s*(-|=|:)", RegexOptions.None)) o.Kind = OptionKind.Bool;
            else if (numeric) o.Kind = OptionKind.Number;
            if (o.Kind != OptionKind.Choice) o.Choices.Clear();
        }

        /// <summary>Writes all pending changes into the .conf file. A one-time backup is kept next to it.</summary>
        public void Save()
        {
            if (Pending.Count == 0) return;
            string backup = Path + ".afk-backup";
            if (!System.IO.File.Exists(backup) && System.IO.File.Exists(Path)) System.IO.File.Copy(Path, backup);
            // A module whose .conf was never created starts from its complete template.
            if (!System.IO.File.Exists(Path) && System.IO.File.Exists(Path + ".dist")) System.IO.File.Copy(Path + ".dist", Path);
            string text = System.IO.File.Exists(Path) ? System.IO.File.ReadAllText(Path) : "";
            foreach (var p in Pending)
            {
                string line = p.Key + " = " + p.Value;
                var rx = new Regex(@"(?m)^[ \t]*" + Regex.Escape(p.Key) + @"[ \t]*=[^\r\n]*");
                if (rx.IsMatch(text)) text = rx.Replace(text, line.Replace("$", "$$"), 1);
                else text = text.TrimEnd('\r', '\n') + "\r\n" + line + "\r\n";
                Values[p.Key] = p.Value;
            }
            System.IO.File.WriteAllText(Path, text, new UTF8Encoding(false));
            Pending.Clear();
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
            new[] { "Experience and levels", "worldserver.conf", "Rate.XP.Global", "XP rate for everything (multiplier)" },
            new[] { "Experience and levels", "worldserver.conf", "Rate.XP.Kill", "XP from kills" },
            new[] { "Experience and levels", "worldserver.conf", "Rate.XP.Quest", "XP from quests" },
            new[] { "Experience and levels", "worldserver.conf", "Rate.XP.Explore", "XP from exploring" },
            new[] { "Experience and levels", "worldserver.conf", "Rate.Rest.InGame", "Rested XP gain while logged in" },
            new[] { "Experience and levels", "worldserver.conf", "MaxPlayerLevel", "Maximum player level" },
            new[] { "Experience and levels", "worldserver.conf", "StartPlayerLevel", "Start level of new characters" },
            new[] { "Experience and levels", "worldserver.conf", "StartPlayerMoney", "Start money of new characters (copper)" },
            new[] { "Loot, money and reputation", "worldserver.conf", "Rate.Drop.Money", "Money drop rate" },
            new[] { "Loot, money and reputation", "worldserver.conf", "Rate.Drop.Item.Uncommon", "Drop rate: uncommon (green) items" },
            new[] { "Loot, money and reputation", "worldserver.conf", "Rate.Drop.Item.Rare", "Drop rate: rare (blue) items" },
            new[] { "Loot, money and reputation", "worldserver.conf", "Rate.Drop.Item.Epic", "Drop rate: epic (purple) items" },
            new[] { "Loot, money and reputation", "worldserver.conf", "Rate.Reputation.Gain", "Reputation gain rate" },
            new[] { "Loot, money and reputation", "worldserver.conf", "Rate.Honor", "Honor rate" },
            new[] { "Loot, money and reputation", "worldserver.conf", "SkillGain.Crafting", "Crafting skill-up rate" },
            new[] { "Loot, money and reputation", "worldserver.conf", "SkillGain.Gathering", "Gathering skill-up rate" },
            new[] { "Convenience", "worldserver.conf", "InstantFlightPaths", "Instant flight paths" },
            new[] { "Convenience", "worldserver.conf", "AllFlightPaths", "New characters know all flight paths" },
            new[] { "Convenience", "worldserver.conf", "Instance.IgnoreLevel", "Enter dungeons at any level" },
            new[] { "Convenience", "worldserver.conf", "Instance.IgnoreRaid", "Enter raids without a raid group" },
            new[] { "Convenience", "worldserver.conf", "DurabilityLoss.OnDeath", "Durability loss on death (%)" },
            new[] { "Convenience", "worldserver.conf", "AllowTwoSide.Interaction.Group", "Groups with the other faction" },
            new[] { "Convenience", "worldserver.conf", "AllowTwoSide.Interaction.Guild", "Guilds with the other faction" },
            new[] { "Convenience", "worldserver.conf", "AllowTwoSide.Interaction.Chat", "Chat with the other faction" },
            new[] { "Convenience", "worldserver.conf", "PlayerLimit", "Player limit (0 = no limit)" },
            new[] { "Playerbots", "modules\\playerbots.conf", "AiPlayerbot.Enabled", "Playerbots on" },
            new[] { "Playerbots", "modules\\playerbots.conf", "AiPlayerbot.RandomBotAutologin", "Random bots populate the world" },
            new[] { "Playerbots", "modules\\playerbots.conf", "AiPlayerbot.MinRandomBots", "Random bots: minimum count" },
            new[] { "Playerbots", "modules\\playerbots.conf", "AiPlayerbot.MaxRandomBots", "Random bots: maximum count" },
            new[] { "Playerbots", "modules\\playerbots.conf", "AiPlayerbot.RandomBotMinLevel", "Random bots: lowest level" },
            new[] { "Playerbots", "modules\\playerbots.conf", "AiPlayerbot.RandomBotMaxLevel", "Random bots: highest level" },
            new[] { "Playerbots", "modules\\playerbots.conf", "AiPlayerbot.DisabledWithoutRealPlayer", "Random bots only while a real player is online" },
            new[] { "Playerbots", "modules\\playerbots.conf", "AiPlayerbot.SyncLevelWithPlayers", "Random bot levels follow the players" },
            new[] { "Playerbots", "modules\\playerbots.conf", "AiPlayerbot.MaxAddedBots", "Bots per player (party bots)" },
            new[] { "Playerbots", "modules\\playerbots.conf", "AiPlayerbot.AutoDoQuests", "Bots do quests on their own" },
            new[] { "Playerbots", "modules\\playerbots.conf", "AiPlayerbot.RandomBotJoinBG", "Bots join battlegrounds" },
            new[] { "Playerbots", "modules\\playerbots.conf", "AiPlayerbot.RandomBotTalk", "Bots chat" },
            new[] { "Playerbots", "modules\\playerbots.conf", "AiPlayerbot.BotActiveAlone", "Bots active without players nearby (%)" },
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
