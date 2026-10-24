using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CoAInstaller
{
    /// <summary>
    /// The main settings of Immersion Life (the AFK Realm continuation of City Life). The module has
    /// hundreds of options; this page exposes the ones a server owner changes first, and the file
    /// itself stays available for everything else. The database button applies the module's manual
    /// WORLD/CHARACTERS files once, in the order its README asks for.
    /// </summary>
    class ImmersionSetup : SetupDialog
    {
        const string Module = "mod-playerbots-immersion";

        public ImmersionSetup(Install i) : base(i, "mod_playerbots_immersion.conf", L.T("Immersion Life"),
            L.T("mod-playerbots-immersion  -  modules\\mod_playerbots_immersion.conf"), "accounts", "CityLife.Enable", L.T("Immersion Life on"))
        {
            Tab(L.T("City population"));
            var population = Card(L.T("City population"),
                L.T("These bots are borrowed from the normal playerbot pool and reserved while they stay. The limit is shared by city residents, questers, legendary characters and fishers; PvP has its own budget."), "accounts");
            AddSlider(population, "CityLife.Population.MaxTotal", L.T("At most at once"), 20, 1000, 10, v => ((int)v).ToString());
            AddToggle(population, "CityLife.Time.Enable", L.T("More people in the evening"),
                L.T("The population follows the time of day: fewer in the morning and at night, full in the evening."));
            var day = Card(L.T("Time of day"), null, "sun");
            AddSlider(day, "CityLife.Time.MorningMultiplierPct", L.T("Morning"), 0, 200, 5, Percent);
            AddSlider(day, "CityLife.Time.DayMultiplierPct", L.T("Day"), 0, 200, 5, Percent);
            AddSlider(day, "CityLife.Time.EveningMultiplierPct", L.T("Evening"), 0, 200, 5, Percent);
            AddSlider(day, "CityLife.Time.NightMultiplierPct", L.T("Night"), 0, 200, 5, Percent);

            var life = Card(L.T("Movement and life"), null, "globe");
            AddToggle(life, "CityLife.Behaviour.Relocate", L.T("Residents move between places"));
            AddPair(life, "CityLife.Behaviour.RelocateMinSeconds", "CityLife.Behaviour.RelocateMaxSeconds",
                L.T("Time between two moves"), 30, 3600, 30, Seconds);
            AddSlider(life, "CityLife.Behaviour.StaticServiceChancePct", L.T("Standing at auction house and bank"), 0, 100, 5, Percent);
            AddSlider(life, "CityLife.Behaviour.ServiceSpotWeightMultiplierPct", L.T("Pull toward service spots"), 0, 500, 10, Percent);
            AddSlider(life, "CityLife.Behaviour.LeashRadius", L.T("Distance from their place"), 10, 100, 5, v => L.F("{0} yd", (int)v));
            AddToggle(life, "CityLife.Behaviour.Emotes", L.T("Emotes"));
            AddSlider(life, "CityLife.Behaviour.EmoteChancePct", L.T("Chance for an emote"), 0, 100, 5, Percent);
            AddPair(life, "CityLife.Behaviour.EmoteMinSeconds", "CityLife.Behaviour.EmoteMaxSeconds",
                L.T("Time between two emotes"), 30, 3600, 30, Seconds);
            AddSlider(life, "CityLife.Behaviour.WalkChancePct", L.T("Residents that walk instead of running"), 0, 100, 5, Percent);
            AddChatter();

            Tab(L.T("Cities and towns"));
            AddHubGroups();

            Tab(L.T("Custom city characters"));
            var custom = Card(L.T("Custom city characters"), null, "accounts");
            AddToggle(custom, "CityLife.Custom.Enable", L.T("Custom city characters on"));
            AddToggle(custom, "CityLife.Custom.CreateMissing.Enable", L.T("Create missing bots"));
            AddSlider(custom, "CityLife.Custom.CreateMissing.MaxBots", L.T("At most at once"), 0, 100, 1);
            AddToggle(custom, "CityLife.Custom.Legendary.Enable", L.T("Legendary residents"));
            AddToggle(custom, "CityLife.Custom.Legendary.FixedIdentity", L.T("Fixed names and outfits"));
            AddToggle(custom, "CityLife.Custom.Beggar.Enable", L.T("Beggars"));
            AddToggle(custom, "CityLife.Custom.Buyer.Enable", L.T("Buyers"));
            AddToggle(custom, "CityLife.Custom.TrainingDummy.Enable", L.T("Training scenes"));
            AddCustomGroups();

            Tab(L.T("Fishing"));
            var fishing = Card(L.T("Fishing Life"), L.T("Fishing Life places fishers at the waterfronts. It is off by default and counts inside the city limit."), "journey");
            AddToggle(fishing, "FishingLife.Enable", L.T("Fishing Life on"));
            AddToggle(fishing, "FishingLife.CreateMissing.Enable", L.T("Create missing bots"));
            AddSlider(fishing, "FishingLife.CreateMissing.MaxBots", L.T("Fishers at most"), 0, 120, 1);
            AddFishingGroups();
            AddFishingChat();

            Tab(L.T("PvP"));
            var pvp = Card(L.T("PvP Life"), L.T("PvP Life brings duels, world PvP and faction campaigns. It is off by default for Conquest of Azeroth and uses its own budget."), "gamemaster");
            AddToggle(pvp, "PvPLife.Enable", L.T("PvP Life on"));
            AddSlider(pvp, "PvPLife.Bots.MaxPerSide", L.T("Bots per side"), 0, 200, 5);
            AddToggle(pvp, "PvPLife.Duel.Enable", L.T("Duel zones"));
            AddToggle(pvp, "PvPLife.World.AlwaysActive", L.T("World PvP"));
            AddToggle(pvp, "PvPLife.ForTheHorde.Enable", L.T("Horde"));
            AddToggle(pvp, "PvPLife.ForTheAlliance.Enable", L.T("Alliance"));
            AddPvpGroups();
            AddPvpChat();

            Tab(L.T("Database tables"));
            var database = Card(L.T("Database tables"),
                L.T("The module keeps its cities, custom characters, fishing spots and PvP locations in the world and characters databases. Install these tables once before the first start."), "database");
            var status = Small(L.T("Only needed once. Existing tables and rows are kept."), database.Inner - 40);
            var install = Ui.Secondary(L.T("Install the database tables ..."));
            install.Margin = new Padding(0, 2, 0, 6);
            database.Add(install);
            database.Add(status);
            install.Click += (s, e) => InstallDatabase(install, status);
            Note(L.T("Stop the worldserver before installing the tables. The changes show after the next start."));

            Start();
        }

        static string Seconds(double v)
        {
            return v >= 60 ? L.F("{0} min", (int)(v / 60)) : L.F("{0} s", (int)v);
        }

        static string Percent(double v) { return (int)v + " %"; }
        static string Yards(double v) { return L.F("{0} yd", (int)v); }

        /// <summary>ThunderBluffQuesting -> "Donnerfels – Quests"; names without a translation stay as they are.</summary>
        static string PlaceName(string key)
        {
            bool questing = key.EndsWith("Questing", StringComparison.Ordinal);
            if (questing) key = key.Substring(0, key.Length - "Questing".Length);
            string name = Regex.Replace(key, "(?<=[a-z])(?=[A-Z])", " ");
            string shown;
            switch (name)
            {
                case "Stormwind": shown = L.T("Stormwind"); break;
                case "Orgrimmar": shown = L.T("Orgrimmar"); break;
                case "Ironforge": shown = L.T("Ironforge"); break;
                case "Darnassus": shown = L.T("Darnassus"); break;
                case "Exodar": shown = L.T("Exodar"); break;
                case "Undercity": shown = L.T("Undercity"); break;
                case "Thunder Bluff": shown = L.T("Thunder Bluff"); break;
                case "Silvermoon": shown = L.T("Silvermoon"); break;
                case "Shattrath": shown = L.T("Shattrath"); break;
                case "Dalaran": shown = L.T("Dalaran"); break;
                case "Goldshire": shown = L.T("Goldshire"); break;
                default: shown = L.T(name); break;
            }
            return questing ? shown + " – " + L.T("Questing") : shown;
        }

        void AddHubGroups()
        {
            var prefix = "CityLife.Hub.";
            foreach (var group in conf.Options.Where(o => o.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                         .GroupBy(o => o.Key.Substring(prefix.Length).Split('.')[0]))
            {
                string root = prefix + group.Key + ".";
                // Quest hubs belong to the module's quest system. The setup page leaves it off:
                // the normal playerbots do the questing.
                if (conf.ByKey.ContainsKey(root + "Questing")) continue;
                var card = Card(PlaceName(group.Key), null, "globe");
                AddToggle(card, root + "Enable", L.T("On"));
                AddSlider(card, root + "Population", L.T("Population"), 0, 100, 1);
            }
        }

        void AddCustomGroups()
        {
            var prefix = "CityLife.Custom.";
            var roles = new[] { "Legendary", "Beggar", "Buyer", "TrainingDummy" };
            foreach (var role in roles)
            {
                var keys = conf.Options.Where(o => o.Key.StartsWith(prefix + role + ".", StringComparison.OrdinalIgnoreCase) &&
                                                   o.Key.EndsWith(".Population", StringComparison.OrdinalIgnoreCase)).ToList();
                if (keys.Count == 0) continue;
                string title = role == "Legendary" ? L.T("Legendary residents") :
                    role == "Beggar" ? L.T("Beggars") :
                    role == "Buyer" ? L.T("Buyers") : L.T("Training scenes");
                var card = Card(title, null, "accounts");
                foreach (var o in keys)
                {
                    string city = o.Key.Substring((prefix + role + ".").Length).Split('.')[0];
                    AddSlider(card, o.Key, PlaceName(city), 0, 100, 1);
                }
            }
        }

        void AddFishingGroups()
        {
            var prefix = "FishingLife.";
            foreach (var group in conf.Options.Where(o => o.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                                                          o.Key.EndsWith(".Population", StringComparison.OrdinalIgnoreCase))
                         .GroupBy(o => o.Key.Substring(prefix.Length).Split('.')[0]))
            {
                var card = Card(PlaceName(group.Key), null, "journey");
                string root = prefix + group.Key + ".";
                AddToggle(card, root + "Enable", L.T("On"));
                AddSlider(card, root + "Population", L.T("Population"), 0, 50, 1);
            }
        }

        void AddPvpGroups()
        {
            var prefix = "PvPLife.Zone.";
            foreach (var group in conf.Options.Where(o => o.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                         .GroupBy(o => o.Key.Substring(prefix.Length).Split('.')[0]))
            {
                var card = Card(group.Key.Replace('_', ' '), null, "gamemaster");
                string root = prefix + group.Key + ".";
                AddToggle(card, root + "Enable", L.T("On"));
                AddPair(card, root + "MinPopulation", root + "MaxPopulation", L.T("Population"), 0, 100, 1);
            }
        }

        void AddChatter()
        {
            var chatter = Card(L.T("Chatter"), null, "accounts");
            AddSlider(chatter, "CityLife.Custom.Chat.Range", L.T("Chat range"), 5, 100, 5, Yards);
            AddToggle(chatter, "CityLife.Custom.Conversations.Enable", L.T("Conversations between residents"));
            AddPair(chatter, "CityLife.Custom.Conversations.MinSeconds", "CityLife.Custom.Conversations.MaxSeconds",
                L.T("Time between two conversations"), 30, 3600, 30, Seconds);
            AddSlider(chatter, "CityLife.Custom.Conversations.MaxConcurrent", L.T("At most at once"), 1, 10, 1);
            AddSlider(chatter, "CityLife.Custom.Conversations.Range", L.T("Conversation range"), 1, 40, 1, Yards);
            AddSlider(chatter, "CityLife.Custom.Conversations.PartnerRange", L.T("Distance between speakers"), 1, 25, 1, Yards);
            AddPair(chatter, "CityLife.Custom.Conversations.ReplyMinSeconds", "CityLife.Custom.Conversations.ReplyMaxSeconds",
                L.T("Reply after"), 1, 30, 1, Seconds);
            AddToggle(chatter, "CityLife.Custom.PlayerReactions.Enable", L.T("React to players"));
            AddSlider(chatter, "CityLife.Custom.PlayerReactions.ChancePct", L.T("Chance for a reaction"), 0, 100, 5, Percent);
            AddSlider(chatter, "CityLife.Custom.PlayerReactions.CooldownSeconds", L.T("Reaction cooldown"), 0, 600, 5, Seconds);
            AddSlider(chatter, "CityLife.Custom.PlayerReactions.Range", L.T("Reaction range"), 1, 40, 1, Yards);

            AddRoleChat("CityLife.Custom.Legendary", L.T("Legendary residents"));
            AddRoleChat("CityLife.Custom.Beggar", L.T("Beggars"));
            AddRoleChat("CityLife.Custom.Buyer", L.T("Buyers"));
        }

        void AddRoleChat(string prefix, string title)
        {
            var card = Card(title, null, "accounts");
            AddPair(card, prefix + ".ChatMinSeconds", prefix + ".ChatMaxSeconds",
                L.T("Time between two lines"), 30, 3600, 30, Seconds);
            AddSlider(card, prefix + ".YellChancePct", L.T("Yell chance"), 0, 100, 5, Percent);
        }

        void AddFishingChat()
        {
            var card = Card(L.T("Fishing chatter"), null, "journey");
            AddSlider(card, "FishingLife.Chat.Range", L.T("Chat range"), 5, 100, 5, Yards);
            AddPair(card, "FishingLife.ChatMinSeconds", "FishingLife.ChatMaxSeconds",
                L.T("Time between two lines"), 30, 3600, 30, Seconds);
            AddSlider(card, "FishingLife.YellChancePct", L.T("Yell chance"), 0, 100, 5, Percent);
        }

        void AddPvpChat()
        {
            var card = Card(L.T("PvP chatter"), null, "gamemaster");
            AddToggle(card, "PvPLife.BotChat.Enable", L.T("Chatter"));
            AddSlider(card, "PvPLife.BotChat.WorldChance", L.T("World chat chance"), 0, 100, 5, Percent);
            AddSlider(card, "PvPLife.BotChat.YellChance", L.T("Yell chance"), 0, 100, 5, Percent);
        }

        void InstallDatabase(Button button, Label status)
        {
            if (!Ui.Confirm(this, L.T("Install the database tables now? This creates the module's tables in the world and characters databases. It is only needed once; existing tables and rows are kept."))) return;
            button.Enabled = false;
            status.Text = L.T("Installing the database tables ...");
            string done = null, error = null;
            var control = new ServerControl(inst);
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    // The module's tables can also be installed while the server is stopped.
                    // Start the portable database for it and leave it running for the next start.
                    if (control.Db == null && !Net.PortOpen(control.DbPort))
                    {
                        try { BeginInvoke((Action)(() => status.Text = L.T("Starting the database ..."))); } catch { }
                        control.StartDatabase(s => { });
                    }
                    ApplyDatabase();
                    done = L.T("The database tables are ready.");
                }
                catch (Exception ex) { error = ex.Message; }
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        button.Enabled = true;
                        status.Text = error == null ? done : L.T("The database tables could not be installed:") + " " + error;
                        if (error != null) Ui.Error(this, L.T("The database tables could not be installed:") + "\n\n" + error);
                    }));
                }
                catch { }
            });
        }

        void ApplyDatabase()
        {
            string folder = Path.Combine(ModuleCatalog.ModulesDir(inst), Module, "data", "sql", "manual");
            if (!Directory.Exists(folder))
                throw new InvalidOperationException(L.T("The module's database files were not found."));
            DbLogin login = DbLogin.FromConfig(inst);
            Func<string, string, bool> exists = (database, table) =>
            {
                var rows = MySql.Query(inst, login, "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = " +
                    MySql.Quote(database) + " AND TABLE_NAME = " + MySql.Quote(table) + ";");
                return rows.Count > 0 && rows[0].Trim() != "0";
            };
            Action<string, string> run = (database, name) =>
            {
                string file = Path.Combine(folder, name);
                if (!File.Exists(file)) throw new FileNotFoundException(L.T("The module's database files were not found."), file);
                MySql.RunFile(inst, login, database, file);
            };

            if (!exists("acore_world", "city_life_hub")) run("acore_world", "world_city_life.sql");
            run("acore_world", "world_city_life_quest_zones.sql");
            run("acore_world", "world_city_life_locations.sql");
            run("acore_world", "world_city_life_remove_questing.sql");
            run("acore_world", "world_city_life_custom.sql");
            run("acore_world", "world_city_life_fishing.sql");
            run("acore_world", "world_city_life_fishing_spacing.sql");
            run("acore_world", "world_city_life_legendary_stormwind.sql");
            run("acore_characters", "characters_city_life_custom.sql");
            run("acore_characters", "characters_city_life_custom_bots.sql");
            if (!exists("acore_world", "pvp_life_zone")) run("acore_world", "world_pvp_life.sql");
            run("acore_world", "world_pvp_life_locations.sql");
        }
    }
}
