using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;

namespace CoAInstaller.Web
{
    /// <summary>One line of the worldserver's chat log, already split into speaker, channel and text.</summary>
    class BotChatLine
    {
        public string At { get; set; } = "";
        public string Speaker { get; set; } = "";
        public string Kind { get; set; } = "other";
        public string Channel { get; set; } = "";
        public string Target { get; set; } = "";
        public string Text { get; set; } = "";
    }

    /// <summary>One rare item a bot looted, from BotLoot.log.</summary>
    class BotLootLine
    {
        public string At { get; set; } = "";
        public string Bot { get; set; } = "";
        public int ClassId { get; set; }
        public int Level { get; set; }
        public string Item { get; set; } = "";
        public int ItemId { get; set; }
        public int Quality { get; set; }
        public int ItemLevel { get; set; }
        public int Count { get; set; }
    }

    /// <summary>The chat and loot journals: read-only tails of the files worldserver.conf appenders write.</summary>
    static class BotActivity
    {
        const int ChatRead = 2 * 1024 * 1024;
        const int LootRead = 2 * 1024 * 1024;

        static readonly Regex ChatLine = new Regex(@"^(?:(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d)\s+)?(?:Leader player|Player) (.+?) (?=(?:says|yells|emotes|whisper|tells|sends)\b)(.+)$", RegexOptions.Compiled);
        static readonly Regex LootLine = new Regex(@"^(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d)\s+(.+?) \(class (\d+) level (\d+)\) looted (.+?) \((\d+)\) quality (\d+) ilvl (\d+) x(\d+)$", RegexOptions.Compiled);
        static readonly Regex ChatCodes = new Regex(@"\|c[0-9a-fA-F]{8}|\|r|\|H[^|]*\|h|\|h", RegexOptions.Compiled);

        static readonly (string Pattern, string Kind, string Channel)[] ChatForms =
        {
            (@"^says to ([^:]+?): (.*)$", "offscreen", ""),
            (@"^says \(language \d+\): (.*)$", "say", ""),
            (@"^yells \(language \d+\): (.*)$", "yell", ""),
            (@"^emotes \(language \d+\): (.*)$", "other", "emote"),
            (@"^whisper ([^:]+?): (.*)$", "whisper", ""),
            (@"^tells (party|raid|bg) with leader [^:]+?: (.*)$", "party", ""),
            (@"^sends raid warning raid with leader [^:]+?: (.*)$", "party", "raid warning"),
            (@"^tells guild ""(.*?)"": (.*)$", "guild", ""),
            (@"^tells guild\.officer ""(.*?)"": (.*)$", "guild", ""),
            (@"^tells channel ([^:]+): (.*)$", "channel", ""),
            (@"^tells ([^:]+?): (.*)$", "whisper", "")
        };
        static readonly Regex[] ChatFormRegex = ChatForms.Select(f => new Regex(f.Pattern, RegexOptions.Compiled)).ToArray();

        public static string Find(Install inst, string name)
        {
            string[] candidates =
            {
                Path.Combine(inst.ServerDir, name),
                Path.Combine(inst.LogDir, name),
                Path.Combine(inst.ServerDir, "logs", name),
                Path.Combine(inst.Root, name)
            };
            return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
        }

        static string Tail(string path, int max)
        {
            if (!File.Exists(path)) return "";
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    long start = Math.Max(0, fs.Length - max);
                    fs.Seek(start, SeekOrigin.Begin);
                    using (var reader = new StreamReader(fs, Encoding.UTF8, true))
                    {
                        string text = reader.ReadToEnd();
                        if (start > 0)
                        {
                            int newline = text.IndexOf('\n');
                            if (newline >= 0) text = text.Substring(newline + 1);
                        }
                        return text;
                    }
                }
            }
            catch { return ""; }
        }

        public static object Chat(Install inst, string kind, string query, string bot, int limit)
        {
            limit = Math.Max(1, Math.Min(500, limit));
            string file = Find(inst, "Chat.log");
            var kinds = string.IsNullOrWhiteSpace(kind)
                ? null
                : new HashSet<string>(kind.Split(',').Select(x => x.Trim().ToLowerInvariant()).Where(x => x.Length > 0));
            string wanted = (query ?? "").Trim().ToLowerInvariant();
            string who = (bot ?? "").Trim().ToLowerInvariant();
            var lines = new List<BotChatLine>();

            foreach (var raw in Tail(file, ChatRead).Split('\n'))
            {
                var line = ParseChat(raw.Trim());
                if (line == null) continue;
                if (kinds != null && !kinds.Contains(line.Kind)) continue;
                if (who.Length > 0 && !line.Speaker.Equals(who, StringComparison.OrdinalIgnoreCase) &&
                    !line.Target.Equals(who, StringComparison.OrdinalIgnoreCase)) continue;
                if (wanted.Length > 0 &&
                    !line.Speaker.ToLowerInvariant().Contains(wanted) &&
                    !line.Target.ToLowerInvariant().Contains(wanted) &&
                    !line.Text.ToLowerInvariant().Contains(wanted)) continue;
                lines.Add(line);
            }
            lines.Reverse();
            return new
            {
                lines = lines.Take(limit).ToArray(),
                file,
                present = File.Exists(file),
                note = File.Exists(file) ? "" : L.T("No Chat.log yet. Turn on the bot chat journal to fill this page.")
            };
        }

        static BotChatLine ParseChat(string text)
        {
            var line = ChatLine.Match(text);
            if (!line.Success) return null;
            string rest = line.Groups[3].Value;
            var entry = new BotChatLine { At = line.Groups[1].Value, Speaker = line.Groups[2].Value, Text = rest };
            for (int i = 0; i < ChatFormRegex.Length; i++)
            {
                var form = ChatFormRegex[i].Match(rest);
                if (!form.Success) continue;
                var shape = ChatForms[i];
                entry.Kind = shape.Kind;
                if (shape.Kind == "offscreen" || shape.Kind == "whisper") entry.Target = form.Groups[1].Value;
                if (form.Groups.Count > 2) entry.Text = form.Groups[form.Groups.Count - 1].Value;
                else entry.Text = form.Groups[1].Value;
                if (shape.Kind == "party" && shape.Channel.Length == 0) entry.Channel = form.Groups[1].Value;
                else entry.Channel = shape.Channel;
                if (shape.Kind == "guild")
                {
                    entry.Channel = form.Groups[1].Value + (rest.StartsWith("tells guild.officer", StringComparison.Ordinal) ? " officers" : "");
                    entry.Text = form.Groups[2].Value;
                }
                if (shape.Kind == "channel") entry.Channel = form.Groups[1].Value;
                break;
            }
            entry.Text = ChatCodes.Replace(entry.Text ?? "", "");
            return entry;
        }

        public static object Loot(Install inst, int limit)
        {
            limit = Math.Max(1, Math.Min(100, limit));
            string file = Find(inst, "BotLoot.log");
            var rows = new List<BotLootLine>();
            DateTime since = DateTime.Now.AddHours(-48);
            foreach (var raw in Tail(file, LootRead).Split('\n'))
            {
                var m = LootLine.Match(raw.Trim());
                if (!m.Success) continue;
                DateTime at;
                if (!DateTime.TryParseExact(m.Groups[1].Value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out at)) continue;
                if (at < since) continue;
                int cls, level, itemId, quality, ilvl, count;
                if (!int.TryParse(m.Groups[3].Value, out cls) || !int.TryParse(m.Groups[4].Value, out level) ||
                    !int.TryParse(m.Groups[6].Value, out itemId) || !int.TryParse(m.Groups[7].Value, out quality) ||
                    !int.TryParse(m.Groups[8].Value, out ilvl) || !int.TryParse(m.Groups[9].Value, out count)) continue;
                rows.Add(new BotLootLine
                {
                    At = m.Groups[1].Value, Bot = m.Groups[2].Value, ClassId = cls, Level = level,
                    Item = m.Groups[5].Value, ItemId = itemId, Quality = quality, ItemLevel = ilvl, Count = count
                });
            }
            rows.Reverse();
            return new
            {
                rows = rows.Take(limit).ToArray(), total = rows.Count, file,
                present = File.Exists(file),
                note = File.Exists(file) ? "" : L.T("No BotLoot.log yet. Turn on the bot loot journal to fill this page.")
            };
        }

        public static object LogState(Install inst)
        {
            string file = inst.WorldConf;
            var missing = new List<string>();
            foreach (var kv in BotLogs.Required)
                if (Conf.Get(file, kv.Key) == null) missing.Add(kv.Key);
            return new
            {
                file,
                present = File.Exists(file),
                missing,
                chat = Find(inst, "Chat.log"),
                loot = Find(inst, "BotLoot.log"),
                chatPresent = File.Exists(Find(inst, "Chat.log")),
                lootPresent = File.Exists(Find(inst, "BotLoot.log"))
            };
        }
    }

    static class BotLogs
    {
        public static readonly KeyValuePair<string, string>[] Required =
        {
            new KeyValuePair<string, string>("Appender.Chat", "2,4,1,Chat.log,a"),
            new KeyValuePair<string, string>("Logger.playerbots.chat", "4,Chat"),
            new KeyValuePair<string, string>("Appender.BotLoot", "2,4,1,BotLoot.log,a"),
            new KeyValuePair<string, string>("Logger.playerbots.loot", "4,BotLoot"),
            new KeyValuePair<string, string>("Appender.CoaBots", "2,4,1,CoaBots.log,a"),
            new KeyValuePair<string, string>("Logger.playerbots.coa", "4,CoaBots"),
            new KeyValuePair<string, string>("Battleground.StoreStatistics.Enable", "1")
        };

        static readonly KeyValuePair<string, string>[] PlayerChat =
        {
            new KeyValuePair<string, string>("ChatLog.Enable", "1"),
            new KeyValuePair<string, string>("Logger.chat.say", "4,Chat"),
            new KeyValuePair<string, string>("Logger.chat.yell", "4,Chat"),
            new KeyValuePair<string, string>("Logger.chat.channel", "4,Chat"),
            new KeyValuePair<string, string>("Logger.chat.whisper", "4,Chat"),
            new KeyValuePair<string, string>("Logger.chat.party", "4,Chat"),
            new KeyValuePair<string, string>("Logger.chat.guild", "4,Chat")
        };

        public static object Enable(Install inst, bool players)
        {
            string file = inst.WorldConf;
            if (!File.Exists(file)) throw new UserError(L.T("worldserver.conf was not found."));
            string backup = file + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            try { File.Copy(file, backup, true); } catch { }
            var changed = new List<string>();
            foreach (var kv in Required)
                if (Conf.Get(file, kv.Key) != kv.Value) { Conf.Set(file, kv.Key, kv.Value); changed.Add(kv.Key); }
            if (players)
                foreach (var kv in PlayerChat)
                    if (Conf.Get(file, kv.Key) != kv.Value) { Conf.Set(file, kv.Key, kv.Value); changed.Add(kv.Key); }
            return new
            {
                ok = true, backup, changed,
                note = L.T("The journals are switched on. Restart the worldserver; the pages fill as the bots write.")
            };
        }
    }

    static partial class Api
    {
        static void MapBotActivity(WebApplication app)
        {
            Get(app, "/api/bots/chat", c =>
            {
                int limit = 100;
                int.TryParse(c.Request.Query["limit"], out limit);
                return BotActivity.Chat(Inst, c.Request.Query["kind"], c.Request.Query["q"], c.Request.Query["bot"], limit);
            });

            Get(app, "/api/bots/loot", c =>
            {
                int limit = 30;
                int.TryParse(c.Request.Query["limit"], out limit);
                return BotActivity.Loot(Inst, limit);
            });

            Get(app, "/api/bots/logs", c => BotActivity.LogState(Inst));

            Post(app, "/api/bots/logs", a =>
            {
                var result = BotLogs.Enable(Inst, a.Bool("players"));
                return result;
            });
        }
    }
}
