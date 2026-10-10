# AFK Realm 0.8.1 – preview

Build, run and tweak your own Conquest of Azeroth server (AzerothCore fork with Playerbots) on Windows 10/11.

## New in 0.8.1

- **Updates are found again after many starts.** GitHub answers only 60 questions an hour to a program that does not sign in, and the update check used them all after a few starts of AFK Realm – then it showed nothing, although your modules had news. Now AFK Realm asks Git itself whether something is new, which has no such limit; GitHub only adds how many changes there are, and the banner says when it counts again.
- **Update notes you can read.** They stood right of the server status and ran out of the window when several parts had news. Now they sit under the server buttons: a short line (*CoA core, Playerbots and 3 more*), with the whole list in the tooltip and in the question before the update.

## New in 0.8.0

- **The classic classes are back.** A new module under *Modules by AFK Realm*, *mod-classic-classes*, lets you and the bots play Warrior, Paladin, Hunter, Rogue, Priest, Shaman, Mage, Warlock and Druid next to the 21 classes of Conquest of Azeroth. CoA already knows how to play them – the way Ascension's Warcraft Reborn realms did, with their own spells, trainers and talents – but only on a realm without its own classes; the module brings both together. The Death Knight stays out: his spells begin at 55. Which classes can be created is up to you on a new setup page (*Modules → Classic classes*).
- **Patches for the game client.** The character creation of the game only shows the CoA classes, so the module changes it a little. AFK Realm now builds such client patches itself, from *your* game client: it takes the files a module changes out of your game and changes them, so nothing of the game is handed out. The patch goes into your game folder with one click (*Modules → Game client*), is built anew when the game or a module changes, and comes along in *Export as ZIP* for the other players.
- **New bots when a module needs them.** The bots you have keep their class. After installing the classic classes, AFK Realm offers once to make the random bots anew, so they come in the classic classes too.
- Brand new and not yet played on a real server: the server side is built and compiled, the client patch was built from the CoA client and checked – how the class choice and the talent window look in the game still needs a test. Tell me what you see.

## New in 0.7.3

- **"Extra hard" for World Journey.** Since October 2026 CoA has creature multipliers of its own: in the open world creatures have two and a half times their health and hit twice as hard, in dungeons two and a half times the health and half again the damage. With World Journey they come on top of the journey's own difficulty, and that was far too much for us – so World Journey now switches them off. Who wants that harder world finds an *Extra hard* button on the World Journey setup page (*Modules → Set up World Journey → Difficulty*), right next to *Relaxed, Standard, Challenging, Hard*. The page shows the values CoA uses; they are set in `coa.conf`.

## New in 0.7.2

- **Clearer text in the World Journey setup.** The switch "Every character plays the scaled world" now explains what happens with the level scaling question at character creation: it still appears, but "off" no longer counts.

## New in 0.7.1

- **No more false Eluna warning.** The module manager said World Journey needs the Eluna module. It does not: its Lua file is the add-on for the game client, not a script for the server. Modules that bring an add-on are no longer mistaken for Eluna modules.

## New in 0.7.0

- **A new look.** AFK Realm got a proper interface: the server management is no longer one long page but a window with a side bar – *Server, Settings, Modules, Game master, Accounts, Database, Maintenance* – and the state of the server with *Start*, *Stop* and *Restart* always at the top. Buttons, switches, sliders and cards in AFK Realm's purple, every window with the same header, the installation steps on a clean sheet. Everything works as before; it is only easier to find and nicer to look at.
- **Setup pages for my modules.** If you use *World Journey* or the *Auction house bots* with AFK Realm, they now come with a setup page of their own (*Modules → Set up …*): sliders, switches and choices instead of numbers in a config file. World Journey shows where each part of the world begins as a picture of the journey from 1 to 60, lists every zone and dungeon with its new level and lets you move one by hand, previews the level window and offers difficulty presets. The auction bots get a page for trips, selling, prices, buying, crafting, chat and the Trading Post. The pages write the same `.conf` file as always, so whoever prefers the file can keep using it.
- **World Journey** – a new module under *Modules by AFK Realm*. The classes of CoA end at 60, so Outland and Northrend were out of their reach. World Journey turns the old world, Outland and Northrend into one journey from 1 to 60: the old world first, through the Dark Portal from about 30, Northrend from about 40, and the raids and heroics of both at 60. Creatures, quests, items, dungeons, the Dungeon Finder, battlegrounds and the random bots follow, and everything can be set in its config. Tick it, apply, done – AFK Realm brings its changes to the core and Playerbots along. Brand new and not yet played on a real server: back up, try it, tell me.
- **Add-ons for the game client.** Some modules need a small add-on in the game – World Journey uses one to show the new zone levels on the world map and the right values in tooltips. AFK Realm now takes care of that: after installing such a module it asks once for your game folder, puts the add-on into `Interface\AddOns` and clears the game's cache when the module needs it. When the module is updated, so is the add-on; when it is removed, the add-on goes too. Everything is under *Server management → Game client add-ons*.
- **Add-ons for your friends.** *Export as ZIP …* packs the add-ons into one file for the other players on your server. They unpack it into their game folder; a note inside tells them how.
- Good to know: close the game before installing add-ons – AFK Realm checks and asks you to.
- The new look was built and tried on a test system, not yet on many PCs. If a window looks wrong on yours (cut-off text, overlapping parts), send me a screenshot.

## New in 0.6.6

- **Scheduled restart** (*Server management → Scheduled restart*): a world that has run for many hours can grow slow and use more and more memory, and a restart gives it back. AFK Realm can now do that on its own. Choose one of three rules: after the server has run for a number of hours, every day at a set time, or when the worldserver uses more than a set amount of memory.
- **Players are told first.** A few minutes before (you choose how many), then at five and at one minute, everybody in the game reads that the server is about to restart. Then it is stopped cleanly – all characters are saved – and started again.
- **You see what is going on.** The management page shows how long the worldserver has been running, how much memory it uses and when the next restart is due. A restart that has been announced can be postponed by an hour with one click, and every restart is noted in `logs\scheduled-restart.log`.
- **Good to know:** AFK Realm does the restart itself, so it only happens while AFK Realm is open. Close AFK Realm and the server simply keeps running.
- The memory rule could not be tried on a real server yet. If the status line shows no memory figure on your PC, use one of the other two rules and tell me.

## New in 0.6.5

- **Database editor** (*Server management → Database editor*): look into every table of the server, change values and run any SQL right in AFK Realm, without HeidiSQL or another program. Find a table by typing a part of its name, narrow the rows with a condition, edit cells, add and delete rows; nothing is written until you press *Save changes*. The SQL tab runs whatever you type and shows the results.
- **Import and export.** *Import SQL file …* runs an SQL file, for example one a module wants imported by hand: choose the file, read in one sentence what it will do, press *Import*. *Export …* writes the tables or whole databases you tick to one file, which can be imported again here or on another server.
- **Undo last change.** Whatever you change in the editor – a save in a table, SQL statements, an import – can be taken back with one button, also after you restarted the server and found it does not work. AFK Realm remembers the old rows, or keeps a copy of the tables concerned, before it changes anything. The last ten changes can be undone, one after the other.
- **Changing a running server asks first.** Changing the database while the server runs is risky: the server can overwrite what you changed, and it only reads most tables when it starts. AFK Realm now warns before every change and offers to stop the server first – one click stops it cleanly and then carries out the change.
- **Restart server**: a new button next to *Start* and *Stop* shuts the server down cleanly and starts it again – handy after changing settings or the database.
- **Safe until you say otherwise.** The editor only reads until you tick *Allow changes*. Doing so warns you plainly and offers to back up the whole server first; the backup can be restored under *Backups*.
- **Only for people who know what they are doing.** A wrong change in these tables can break the server. If you do not know what a table is for, leave it alone.
- Good to know: the worldserver reads most world tables only when it starts. If a change "does nothing", stop and start the server (or use the matching `reload` command), and for items delete the game client's `Cache` folder.

## New in 0.6.4

- **See a character's quest log.** In the game master tools, choosing a character now shows the quests it has in its log, with the progress of every objective (*Large Candle 3/8*). A player who is stuck only has to tell you his name: you see the quest that causes the trouble and can complete, reward or remove it right there. *Quest log* and *Refresh* read the log fresh from the running server.
- **New quests nearby.** The search around a character now leaves out what it already has in its log and lists only quests it could still take.

## New in 0.6.3

- **Reset random bots no longer reports a failure that was none.** On fast PCs the reset said the worldserver had started normally and nothing was deleted, although all bots were gone. If you saw that message: the bots were deleted, nothing needs to be repeated.

## New in 0.6.2

- **Modules by AFK Realm**: the module manager now starts with a section of modules made for this server. The first one is **mod-playerbots-auctions**: the bots use the auction house like players. They travel to a city when they have enough to sell, price their loot, buy and bid on what they need, gather and craft with their professions, and sell their junk to vendors. Tick it, apply, done.
- **Modules can bring what they need.** Until now a module that needed a change in the core or in Playerbots could not be installed through AFK Realm. A module can now carry those changes itself: AFK Realm applies them to the freshly downloaded source with every build, leaves a change out when it no longer fits or when the original has it already, and takes everything back when the module is removed. The auction module uses this for two small changes to Playerbots (bots keep what is in their bags; no crash on level-scaled loot) and one setting.
- **Updates are much faster.** Because of a mistake, AFK Realm threw its build folder away before every build, so each update and each module change compiled the whole server again. Now only the changed parts are compiled. The first update after installing this version still takes the full time once.
- **Module updates are announced.** The notice at the top of the server management now also says when an installed module has new changes on GitHub; *Check for updates and install* gets them, as before.
- This is new and was tested on one server only. As always: AFK Realm backs up the server before it changes anything.

## New in 0.6.1

- **Change a character's name**: *Game master tools → Player → Change name* marks a character so that its player chooses a new name at the next login – next to *Customize appearance*, *Change race* and *Change faction*.

## New in 0.6.0

- **AFK Realm updates itself.** When a newer release exists, the banner at the top of the server management now says *click to update*: AFK Realm downloads the new version from GitHub, checks it against GitHub's checksum, replaces itself and restarts. A running server keeps running. This works from this version on; to get here from an older version, download the exe once by hand.

## New in 0.5.0

- **Server name**: when you install a new server, AFK Realm asks what it should be called – the name players see in the realm list, instead of always "AzerothCore". An existing server is renamed under *Server management → Server name*.

## Fixed in 0.4.1

- Game master actions (quests, customize, change race, …) failed with "character does not exist" for characters with a first and last name. They work now, and long names are shown in full in the character list.

## What's new since 0.3.0

- **Game master tools** (*Server management → Game master tools*): AFK Realm now talks to the running server. Starting the server through AFK Realm sets up the connection automatically (only reachable from your own PC).
- **Quest helper**: type an NPC name, a quest title or a quest id and see the matching quests with who gives them and who takes them – read from your server's own database, so it fits CoA's changed world. Or let AFK Realm list the open quests around a character. Then *Give quest*, *Complete*, *Reward*, *Remove* or *Check* with one click, also for characters that are offline. Handy when a quest giver cannot be clicked or a quest item does not drop.
- **Player actions**: unstuck (to the inn), revive, kick, set level, send gold or a mail, let a player rename or customize the character or change its race or faction at the next login, and an announcement to everyone.
- **Server console**: every other GM command can be typed right in the window; the server's answer is shown below.
- **No more console windows**: authserver and worldserver now run in the background. *Open server consoles* shows both in one window, one above the other: what they print, and a command line for GM commands to the worldserver. The classic windows can be switched back on with one tick.
- **User guide**: [docs/MANUAL.md](docs/MANUAL.md) explains every window – what it is for and how to use it.
- **Fix**: the installation no longer stops at *Prepare the build (CMake)* with "the version field is not 4 integer components" on PCs that have more than one Visual Studio 2022 installation (for example one left over from an earlier attempt in another folder).

## What it does

- Installs every build tool automatically and compiles the newest CoA core and Playerbots from source
- Sets up a portable MySQL, imports the CoA world data and prepares all databases before the first start
- Extracts maps, vmaps, mmaps and the CoA client DBC tables from your game client
- Edits XP and drop rates, Playerbots and every other server setting, each with its description
- Resets all random bots with one click so fresh ones are generated
- Starts and stops the server cleanly, creates and manages player accounts and sets up play over VPN or LAN
- Moves accounts with their characters from one server to another via an export file
- Checks for updates and rebuilds only when something changed
- Backs up the server before every update and rolls it back with one click
- Installs and removes AzerothCore modules, including their database changes
- Game master tools for the running server: quest helper, player actions and a console
- Database editor: look into the server's tables, change values and run SQL, read-only until you allow changes

## Please note

- **Work in progress and highly experimental**: AFK Realm is unfinished and changes all the time; every release and every new function has to be seen as experimental. It is very likely that one thing or another does not work properly yet. Please report problems with `logs\install.log` attached.
- Many AzerothCore modules are written for the regular core and may not compile or work with CoA. Installing one is safe to try: a module that does not compile is taken out again.
- The exe is not code-signed, so Windows SmartScreen may warn about an unknown publisher.
- You need your own CoA game client. The first installation may take a long time and needs about 40 GB of disk space.
