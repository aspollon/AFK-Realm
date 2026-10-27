using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.Builder;

namespace CoAInstaller.Web
{
    /// <summary>Game master tools: characters, quests, GM commands to the running worldserver.</summary>
    static partial class Api
    {
        static object CharJson(Install inst, CharacterInfo c)
        {
            return new { guid = c.Guid, name = c.Name, level = c.Level, zone = GameData.ZoneName(inst, c.Zone), online = c.Online, bot = c.Bot, account = c.Account };
        }

        static object QuestJson(QuestInfo q)
        {
            return new { id = q.Id, title = q.Title, level = q.Level, status = q.Status, statusText = q.StatusText, progress = q.Progress, givers = q.Givers, takers = q.Takers, distance = q.Distance, canTake = q.CanTake };
        }

        static void MapGameMaster(WebApplication app)
        {
            Get(app, "/api/gm/link", c => new { problem = AdminLink.Problem(Inst, Ctl) });

            Get(app, "/api/gm/characters", c =>
            {
                bool offline = c.Request.Query["offline"] == "1", bots = c.Request.Query["bots"] == "1";
                var inst = Inst;
                try { return new { characters = GameData.Characters(inst, offline, bots).Select(x => CharJson(inst, x)).ToArray() }; }
                catch (Exception ex) { throw new UserError(L.T("The characters could not be read:") + " " + ex.Message); }
            });

            Post(app, "/api/gm/command", a =>
            {
                var inst = Inst;
                string command = (a.Str("command") ?? "").Trim();
                if (command.Length == 0) return new { answer = "" };
                string problem = AdminLink.Problem(inst, Ctl);
                if (problem != null) return new { problem };
                try
                {
                    string answer = AdminLink.Run(inst, command);
                    return new { answer = answer.Length > 0 ? answer : L.T("(done)") };
                }
                catch (Exception ex) { return new { error = ex.Message }; }
            });

            Post(app, "/api/gm/search", a =>
            {
                var inst = Inst;
                return new { quests = GameData.SearchQuests(inst, (a.Str("text") ?? "").Trim(), a.Int("guid")).Select(QuestJson).ToArray() };
            });

            Post(app, "/api/gm/questlog", a =>
            {
                var inst = Inst;
                bool live = a.Bool("live");
                if (live && AdminLink.Problem(inst, Ctl) == null) { try { AdminLink.Run(inst, "saveall"); Thread.Sleep(1500); } catch { live = false; } }
                else live = false;
                return new { live, quests = GameData.QuestLog(inst, a.Int("guid")).Select(QuestJson).ToArray() };
            });

            Post(app, "/api/gm/near", a =>
            {
                var inst = Inst;
                bool live = a.Bool("online") && AdminLink.Problem(inst, Ctl) == null;
                if (live) { try { AdminLink.Run(inst, "saveall"); Thread.Sleep(1500); } catch { live = false; } }
                var now = GameData.Character(inst, a.Int("guid"));
                if (now == null) throw new UserError(L.T("The character was not found."));
                int radius = Math.Max(20, Math.Min(2000, a.Int("radius", 150)));
                var list = GameData.QuestsNear(inst, now, radius, a.Bool("takeable", true));
                return new { live, name = now.Name, zone = GameData.ZoneName(inst, now.Zone), radius, quests = list.Select(QuestJson).ToArray() };
            });
        }
    }
}
