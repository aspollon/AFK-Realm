# AFK Realm 1.0.40

Build, run and tweak your own Conquest of Azeroth server (AzerothCore fork with Playerbots) on Windows 10/11.

## New in 1.0.40

- **Race numbers work again.** The page no longer says "all 21 free race numbers are taken" while most of them are free.
- **CoA Custom races get a fallback number.** If a race's own CoA number is taken, it can still take a normal free number; numbers outside CoA Custom's race table are preferred.
- **The status line and the free buttons agree.** IDs above the free slots no longer count against the free-number limit.

## New in 1.0.39

- **The auction bots use a price band.** What an item really sold for gives a low, a usual and a high price. Bots ask within it, undercut only down to its low, and cannot ask more than its cap.
- **No external scan.** Until there are enough real sales, a band around the usual vendor-based price is used. The band is learned from the server's own bot-involved sales, so a realm with very different prices cannot poison it.
- **On the auction page.** The Prices tab has the band's switch, minimum sales, fallback low and high, and the hard cap.
- **Old price memory keeps working.** The two new columns are added to the existing table automatically.

## New in 1.0.38

- **Immersion Life no longer has its own quest system.** The quest code, settings and quest hubs are gone; the normal Playerbots do the questing. Old quest hub rows are ignored and cleaned from existing databases.
- **City levels follow World Journey.** A hub takes the `journey_zone.bot_min/bot_max` bracket of the zone it stands in, so capitals, towns, Outland and Northrend have the same levels as the journey around them.
- **Fallback without World Journey.** Without the journey tables, or for a zone without a bracket, the hub keeps its original level range.
- **Never above the realm's maximum.** Every hub, custom, fishing and old quest range is clamped to the server's maximum level.

## New in 1.0.37

- **The module catalog can be sorted.** Choose Recommended, Name A-Z, Name Z-A, newest first, oldest first or most stars; the choice stays while you search and reload.
- **The new texts speak all six languages again.** Some translations of 1.0.35 and 1.0.36 had lost their accents or non-Latin characters; they are repaired.

## New in 1.0.36

- **Chatter is on the City population tab.** Chat range, conversations between residents, reactions to players and the chat timings of legendary residents, beggars and buyers are together in one place.
- **Fishing and PvP chatter** have their own cards on the fishing and PvP tabs.
- **No quest hubs on the page.** The quest system and its hubs are out of the page; the normal playerbots do the questing. The module's own questing stays off.
- **Custom characters keep every location** the module ships with, per role.
- **Fixed the legendary yell chance.** Its config line was broken and is a proper option again.
- **Defaults stay default.** The page does not change any module setting by itself.

## New in 1.0.35

- **The Immersion Life page is now a proper settings page.** Six clear tabs: city population and time of day, movement and life, every city and town, custom city characters, fishing and PvP.
- **Every city and town has its own controls.** The switch and population per hub; quest hubs also get their questing switch and level range.
- **Custom residents, fishers and PvP zones are per place.** Legendary residents, beggars, buyers and training scenes per city; every fishing city and PvP zone with its own switch and population.
- **No raw settings list.** The *All settings* tab is gone; uncommon internals stay in the config file. All module defaults are unchanged.
- **Six languages.** Every new label is translated.

## New in 1.0.34

- **Every Immersion setting is on the module page.** A new *All settings* tab reads the module's own config template: grouped by area, searchable, each option with its description and the right switch, choice or value field.
- **Defaults stay default.** The page does not rewrite values by itself; use it to build the population, routines and chat behaviour you want.

## New in 1.0.33

- **Only hand-written chatter.** The generated line variants and conversations are removed. The auction bots use the 17,721 hand-written lines again; Immersion keeps its hand-written conversations and level 60 buyer lines.
- **Clean databases.** The generated chat rows are deleted from the world and characters databases.
- **No experimental content in the installer.** The installer only applies the hand-written module data again.

## New in 1.0.32

- **The auction bots have much more to say.** The spoken catalog is now 40,000 lines, including 1,000 short conversations between two or three nearby bots.
- **Immersion Life has a large voice.** 6,000 new ambient lines for legendary residents, beggars, buyers and fishers, plus 800 three-step conversations between nearby residents.
- **No more level 70-80 advertisements.** Buyer lines now offer level 55-60 greens and endgame goods, fitting the level 60 realm.
- **The Immersion installer brings the new catalogs.** The database button applies both new chat files automatically.

## New in 1.0.31

- **The auction bots' loot filter is on their setup page.** A new *Loot filter* tab has the switch, the lowest quality, food and drink, other consumables, excluded item ids and excluded name parts.
- **The legendary residents' guild is called "AFK Realm".** New installations get that name; existing configurations that still say "The GOATS" are moved over, and an existing owned "The GOATS" guild is renamed rather than duplicated.

## New in 1.0.30

- **Module patches with Windows line endings no longer break the build.** AFK Realm normalizes every module patch to LF before `git apply`, in the build and in the update check. The former "corrupt patch" at the flying module is gone even on a fresh checkout.
- **Azeroth flying's patch stays LF in Git.** The module now marks `.patch` files with `eol=lf`, so updates do not turn the file into CRLF again.

## New in 1.0.29

- **Immersion Life installs its tables with the server stopped.** The database button starts the portable database itself, applies the WORLD and CHARACTERS files and leaves the database running. A status message says when it is starting; no manual start is needed.

## New in 1.0.28

- **Azeroth flying builds again.** The current CoA core rejected the module with `IsNoFlyArea` not found. The core header now declares the no-fly check, so the build finishes and flying can be tested in Kalimdor and the Eastern Kingdoms.
- **Store and achievement mounts fly too.** Mount spells marked "flying areas only" were refused in the old world before the module ever saw them. The core's location check now accepts Eastern Kingdoms and Kalimdor while `AzerothFlying.Enable` is on.
- **Immersion Life installs every quest hub.** The database button now also applies `world_city_life_quest_zones.sql`, which older installations need before the optional locations.
- **Immersion Life is out of testing.** The module card says what users get: PvP Life and Fishing Life off by default, legendary residents on.

## New in 1.0.27

- **The database editor is dark in dark mode.** The tab strip, the tree of databases, the input fields, the table and row headers and the scrollbars now follow the look; no white controls with unreadable text are left.
- **Immersion Life is here.** AFK Realm's continuation of City Life is now one of its own modules: city population, questing, PvP Life, Fishing Life and legendary residents for playerbots. PvP Life and Fishing Life are off by default; the legendary residents stay on.
- **A setup page for Immersion Life.** The shared city budget, the main cities, questing, legendary residents, PvP Life and Fishing Life are on one page, with a one-time installer for the module's database tables.
- **Azeroth flying fits the new core.** The module uses the core's normal mount rules and adds Eastern Kingdoms and Kalimdor only. Leave `CoA.LegacyMountFlight = 0`; then the no-fly list and the dismount still work.
- **CoA 2.7 builds again.** A compatibility patch in the auction house bots keeps the Warcraft Reborn Mystic Enchants and lets the Zyth45 Playerbots 2.7 branch compile with the current CoA core.

## New in 1.0.25

- **Long client paths: the real cause is named.** `Ascension.exe` is not long-path aware, so custom textures can go missing even when every patch is installed correctly. Move the client to a short path such as `C:\CoA`; AFK Realm warns when the game folder path is longer than 100 characters.

## New in 1.0.24

- **Race numbers stay with their race.** An imported Gnoll can no longer take 63: in CoA Custom's table that is Nightborne, while Gnoll is 83. An old Gnoll setting saved under 63 is moved to a free number, and the client patch refuses to overwrite Nightborne.
- **Long client paths warn.** Haranir and Esteria Vulpera load many files below the game folder. AFK Realm now warns when the game folder path is longer than 100 characters and recommends a short path such as `C:\CoA`.

## New in 1.0.23

- **One less folder to choose.** The separate “Server data” card is gone from Custom races. CoA Custom and imported race packs bring their racial rows without another folder; the setup page now only asks for the CoA Custom pack and the module switch.

## New in 1.0.22

- **No more file pickers for the CoA Custom pack.** The separate `Ascension.exe`/`Ascension-b.exe` and `dinput8.dll` buttons are gone. Pick the CoA Custom folder once (or let AFK Realm find `Downloads\CoA-Custom\CoA-Custom`), and the exe and 64-race DLL are taken from that pack automatically. The module switch is all that is left.

## New in 1.0.21

- **One folder is enough.** Choosing the client_root of CoA Custom now detects `Ascension.exe` or `Ascension-b.exe` and the 64-race `dinput8.dll` from the same pack automatically. The separate file pickers remain only as a fallback for unusual layouts.

## New in 1.0.20

- **CoA Custom 1.8.3's `Ascension.exe` works too.** The races setup page now accepts both `Ascension.exe` and `Ascension-b.exe` as the separate game exe and copies the chosen one under its own name. If the client-root folder has neither, it warns and offers the picker. The 64-race `dinput8.dll` still comes from `files\client` of the same CoA Custom pack.

## New in 1.0.19

- **Ascension-b.exe is no longer lost when the client-root folder has only the Esteria files.** The races setup page warns when the chosen folder does not contain `Ascension-b.exe` and offers a separate picker for it (`CustomRaces.ClientExe`). The client patch copies that exe as `Ascension-b.exe` next to the game, and the ZIP for the players gets it too.

## New in 1.0.18

- **The module catalog stays visible.** The "Modules by AFK Realm" section now grows in its own resizable, scrollable area. With five or six modules of our own, the AzerothCore catalog below no longer shrinks to nothing.
- **Azeroth flying has a setup page.** The master switch, the configurable no-fly area list and the warning that old Azeroth was not built for flying are now on one page under *Modules*.

## New in 1.0.17

- **Optional flying in old Azeroth.** *Modules by AFK Realm* now lists **mod-azeroth-flying**. It lets mounts with a flying effect fly in Eastern Kingdoms and Kalimdor when `AzerothFlying.Enable` is on. Off by default. The old-world capitals and Teldrassil are a configurable no-fly list; flying into one dismounts the player. Outland, Northrend, battlegrounds and instances keep their normal rules.
- **Use it at your own risk.** Azeroth was not built for flying: expect clipping, stuck spots, skipped quest paths, unintended shortcuts and PvP advantages. The module's README and config say so.

## New in 1.0.16

- **The client patch installs again with the CoA client.** The game client carries 31 class rows for Troll 8, while a module may add at most 30 rows per race. AFK Realm now applies that limit only to the rows the module really adds, so the stock Troll rows stay and *Modules → Game client → Install* no longer stops with "CharBaseInfo.dbc would have 31 rows for 8".
- **Readable logs.** Long lines in the installation/update details and in the two server consoles now wrap instead of running off the right edge. The horizontal scrollbar is gone.

## New in 1.0.15

- **Grummle and other imported races get all their files.** The client archive of a race is now built from the files that are actually in its race folder. A race.info.json written while the pack was still being copied can no longer leave an almost empty archive; Grummle no longer shows a blue cube. Click *Modules → Game client → Install* once to rebuild the archives.
- **The race list matches packs reliably.** Only player races are offered, races sharing a name (Vulpera Esteria, Earthen Alliance/Horde) are kept apart, existing packs are recognised by source number, file and name, and icons are found under more names. Old compositor-only packs need *Custom races → Create race packs from a client* once more; the fallback now picks a donor on the same model by prefix, name and side.
- **No more phantom module updates.** The update check no longer offers an update that the engine would always skip because the module has local changes. When the local changes are exactly the upstream commit's contents, it moves only the recorded HEAD forward.

## New in 1.0.14

- **Imported races get their skin even from old packs.** The importer now borrows skin layers from a matching race in the target client when a pack has compositor-only sections, so races like Vulpera Esteria show their skin even if the pack was created before the fallback was added.

## New in 1.0.13

- **Two Vulpera show up separately.** The setup page now lists Vulpera 19 and the Esteria Vulpera 74 as two distinct races instead of one, so both can be switched on and configured on their own.

## New in 1.0.12

- **Imported Esteria races get their skin.** Races of CoA Custom whose looks are painted by EsteriaAppearance.dll (Vulpera Esteria and others) now keep their own skin, face and hair in AFK Realm instead of neutral placeholders. Make their race packs once more (*Custom races -> Create race packs from a client*), then *Modules -> Game client -> Install*.
- **Two Vulpera stay two.** Vulpera 19 and the Esteria Vulpera 74 no longer collapse into one pack; both can be offered.

## New in 1.0.11

- **Drakonid and Drakkin get their skin.** CoA Custom's EsteriaAppearance.dll paints them only under CoA Custom's race numbers (98 and 99), and AFK Realm gave them other ones. On the race page switch each of them off, click *Free its number*, switch it on again, save, then *Game client → Install*.

## New in 1.0.10

- **Helmets for every race.** A race without helmet models of its own now wears the helmets of the race it leans on, the way CoA Custom does it, instead of checkered boxes. Needs mod-custom-races updated (*Check for updates and install*), then *Save* on the race page and *Game client → Install*.
- **Imported races look complete.** Race packs now also take the race's helmets (no more checkered boxes on their heads), the textures of its horns, eyes or blindfold, every file of its own model folder, and its own voice. Make your race packs once more (*Custom races → Create race packs from a client …*), then *Modules → Game client → Install*.

## New in 1.0.9

- **Big races fit again.** The archives AFK Realm writes for imported races are now compressed like the game's own, often to half their size. A race with many textures could make its archive larger than the 2 GB the game can read, and *Install* stopped with "would be larger than 2 GB". Just click *Install* again.

## New in 1.0.8

- **Servers with many custom races start again.** A racial ability whose description had a line break broke the file AFK Realm writes for its race, and the worldserver stopped while starting, right after "voices of their base races lent". Fixed in AFK Realm and in mod-custom-races (*Check for updates and install* brings the module up to date).
- **The real reason when the server stops.** If the worldserver stops while starting, the message now also shows the end of Errors.log, where the server writes why.

## New in 1.0.7

- **All the looks of CoA Custom's races.** Highmountain Tauren, Earthen, Haranir, Mechagnome, Vulpera, Skyborne and the other Esteria races of CoA Custom only showed their standard choices on an AFK Realm server: CoA Custom's EsteriaAppearance.dll knows them only by the race numbers CoA Custom gave them, and AFK Realm gave them other ones. *Custom races → More than 31 races → Races of CoA Custom's game keep their own numbers* fixes that, and the server now stores the extra looks of Highmountain, Earthen and Haranir like CoA Custom's server does. Needs CoA Custom's folder (*Files of CoA Custom next to the game*), mod-custom-races updated, *Update server*, and every player starting the game with `Ascension-b.exe`.

## New in 1.0.6

- **Fewer crashes with imported races.** The game could crash when you quit (or when changing characters) with imported races: their skin and hair rows were spread over the client's table, and the game counts them in groups. AFK Realm now keeps every group together. Run *Modules → Game client → Install* once and give your players the new ZIP.
- **Archives stay readable.** An archive AFK Realm writes may now be 2 GB at most: the game cannot read past 2 GB.
- **A note for players with CoA Custom.** The READ ME of the ZIP says that the game folder must be writable (EsteriaAppearance.dll keeps a cache there); a game under C:\Program Files is better moved.

## New in 1.0.5

- **Racial abilities for CoA Custom's races.** CoA Custom keeps its racials inside its own server program, so AFK Realm could not find them in the pack. mod-custom-races now carries CoA Custom's racial table, and every race gets its racials again. For races from your race folder, make their packs again from CoA Custom's client (*Create race packs from a client …*), so they carry the spells. Their special effects – charges, scaled damage and healing, Mole Machine, Holo, the Dracthyr bloods and more – come along too: mod-custom-races now carries CoA Custom's own code for them. Update AFK Realm and mod-custom-races, open *Custom races* and save once, then *Modules → Game client → Install*.
- **CoA Custom's exe is copied too.** *Files of CoA Custom next to the game* copied everything except the exe: CoA Custom 1.8 brings its own `Ascension.exe`, and AFK Realm never replaced the game itself. It does now for this folder; your old exe is kept as `Ascension.exe.afk-backup` and comes back with *Copy nothing*. Close the game and its launcher, then *Modules → Game client → Install* again. Thanks to Feles Noctis for the report.
- **One ZIP for your players, CoA Custom included.** *Export as ZIP …* now also packs CoA Custom's files that go next to the game (its `Ascension.exe`, `EsteriaAppearance.dll` and the rest of `client_root`). Your players unpack the one ZIP into their game folder and everything lands in the right place. Needs the mod-custom-races update of today.
- **Restore only parts of a backup – even from another server.** *Database → Backups → Restore only parts …* takes just the accounts, the characters (with the Playerbots data) or the settings and leaves everything else as it is. *From another server …* does the same with a backup folder copied from another AFK Realm server. After a broken update you can install a fresh server and take your accounts and characters over. Thanks for the suggestion!
- **Long install folders work.** Installs in long folders failed at Boost or while compiling: Windows' old 260-character limit, which some build tools hit even with long paths switched on. AFK Realm now builds through a short path by itself (`<drive>\afk\…`), and the folder may be up to 120 characters long.
- **World Journey: dungeons alone.** The World Journey page has a new card *Dungeons alone* (Difficulty tab): switch it on and set how strong the creatures of a solo dungeon are. Needs the World Journey update of today; players tick *Go alone* in the Dungeon Finder.
- **Manastorm loads again.** On a fresh server Manastorm did not start at all ("Manastorm datastore failed to load"). The cause is in the CoA core itself: the teleport pad that brings Horde players back to Orgrimmar after a death or wipe shares its number with the new Basalthane raid, and the raid's update deletes it. A server update with AFK Realm now puts a missing pad back by itself. As soon as the CoA core fixes this, AFK Realm notices and takes its own pad out again. Update AFK Realm, run *Update server*, then start the server.
- **Removing World Journey puts the world back.** World Journey changes the levels of items, creatures and quests while the server runs. Removing it in AFK Realm now puts the original values back before its folder is deleted. Before, the changed levels stayed in the database.

## New in 1.0.4

- **Tall races fit the character creation.** A race much taller than its base race – the Highmountain Tauren next to the Tauren – stood half out of the picture, because the character creation shows it with the camera of its base race. AFK Realm now measures how tall every race's model stands, and mod-custom-races moves the camera back and up as far as needed, in the choice of race and class and in the customization. Update AFK Realm and mod-custom-races, then *Modules → Game client → Install* and give the other players the new ZIP.

## New in 1.0.3

- **Much faster server updates.** A small update of the CoA core made AFK Realm compile nearly the whole server again: putting the module patches back in gave the patched files a new date, and many files of the core include them. Unchanged patched files now keep their date, so an update compiles only what really changed. The first update after installing 1.0.3 still takes the long way once.

## New in 1.0.2

- **Easier to read in the dark look.** The section headings of the server settings were in Windows' own dark blue and hard to read on the dark background; they are light now. The selected row and the column headers of lists follow the dark look too, and purple text is lighter. Thanks to SimpleGeekAce for the report.

## New in 1.0.1

- **Server update with World Journey fixed.** The newest CoA core brings the Manastorm caches, and with World Journey installed the update stopped with "pool count mismatch". World Journey changes the levels of items while the server runs, and the new caches look for Mythic dungeon gear at level 60. Before database updates, AFK Realm now puts World Journey's original values back; World Journey takes its changes up again at the next start. Update AFK Realm and World Journey, then run the server update again.

## New in 1.0

- **No longer a preview.** Thanks to everyone who tested the previews.
- **Light or dark.** The new sun/moon button next to the language button switches AFK Realm between *Automatic (as Windows)*, *Light* and *Dark*. Every open window changes at once, no restart needed; on *Automatic* AFK Realm follows Windows when you change it there.
- **CoA Custom's files in your game with one click.** CoA Custom brings files that belong next to Ascension.exe – Ascension-b.exe, EsteriaAppearance.dll with its .bin files, the cursors. Choose the CoA Custom folder once under *Modules → Custom races → Files of CoA Custom next to the game*, and *Modules → Game client → Install* copies them all next to your game. Files of the same name you had before are kept as `.afk-backup`, and *Copy nothing* takes everything away again. They do not go into the ZIP: your players have the client of CoA Custom themselves. Start the game with Ascension-b.exe.
- **Racial abilities for new races.** Races from a server pack bring their own racial abilities: AFK Realm hands their spells to mod-custom-races, which teaches them at login and takes them back when the race is switched off. New races also speak with the voices of their base race.
- **A sturdier race importer.** *Create race packs from a client …* now finds every file a race needs wherever the other client keeps it, and tells you what is missing.
- **Choose how many backups to keep** in the backups window (3 by default).
- **More GitHub requests.** If the module list says GitHub's hourly limit is used up, enter a GitHub token of your own on the modules page: 5000 requests an hour instead of 60.
- **Game folder found more easily**: the `Data` folder itself or a repack folder works too.

## New in 0.9.6

- **New races: clear steps after saving.** Saving the races page alone does not put the races into the game. Now the *Game client* window opens right away after every such change and says what to do: click **Install** (it builds the client patch into your game), restart the server, and give every other player the new ZIP (*Export as ZIP …*) – without it they do not see the new races. The same goes for the classic classes and World Journey's map levels.

## New in 0.9.5

- **AFK Realm in your language.** English, Deutsch, Français, Español, Português (Brasil), Русский and 简体中文. AFK Realm starts in the language of Windows; the language button at the top right of every page switches it (AFK Realm restarts, your servers keep running). The setup pages of the modules by AFK Realm – World Journey, the auction house bots, custom races and classic classes – are translated too, with the zones and dungeons under the names your game client uses.
- **What stays English:** the descriptions of the server's own options (worldserver.conf, playerbots.conf and the modules of others), the log files, announcements in the game, and anything other modules bring. The READ ME in the ZIP for your players comes in your language and in English.
- **Your own language.** *Add a language …* in the same menu writes a file with every text of AFK Realm to translate. Put the finished file into `%APPDATA%\AFK-Realm\lang` (or a `lang` folder next to the exe) and it shows up in the menu at the next start. A file with the code of a built-in language can also correct single texts of it. Translations or corrections are very welcome as an issue or a pull request.
- **Updates that would break a module are named first.** Before an update, AFK Realm tries the patches of your installed modules on the new CoA core and mod-playerbots. A patch that would no longer fit is named in the update banner and in the question before the update, so you know beforehand which module change will be left out until its module is adapted.
- **Please tell me** when a translation reads oddly or a text does not fit its place – it was checked on screen, but not by native speakers of every language.

## New in 0.9.4

- **Switch between servers with one click.** The top of the side bar now shows the server you are looking at, by its server name. Click it for the list of every server you have opened in AFK Realm – your real server, a test copy, a friend's – and switch with one click. The same menu adds an existing server folder, starts a new install, or takes a server off the list (its folder stays). A server you switch away from keeps running; stop it before you start another one on the same ports.

## New in 0.9.3

- **New races for the bots too.** When you make new races playable on the races page, AFK Realm now asks whether to make the random bots anew. The bots keep the races they were made with, so only new bots can be of the new races. Not now? *Reset random bots …* under *Maintenance* does the same later.

## New in 0.9.2

- **Race packs from any client.** *Modules → Custom races → Create race packs from a client …*: choose another game client or repack (Ashen Order, From the Ashes, the client folder of CoA Custom …), tick the races you want, and AFK Realm puts them into your race folder – their rows, the files your own client lacks and their icon. Afterwards you can delete the big client. When a client updates, make its packs again.
- **More than 31 races.** The game client knows only 31 race numbers. With the 64-race patch of CoA Custom (dinput8.dll; making race packs from CoA Custom keeps it in your race folder) it knows 63. Switch it on under *More than 31 races*: AFK Realm places the DLL next to your game when you install the client patch and puts it into the ZIP for the other players. The server needs one rebuild for it. Races above 32 do what their base race may do.
- **No more frozen windows.** With large races, checking and installing the client patch takes a while; a small window now says what is going on and fills its bar race by race, and the main window checks the game client in the background. Opening the races page, the game client window a second time and the check after a change are much faster.
- **Fixes from the last test:** races from the race folder were missing in the game when the folder had never been saved – it is saved now, and a missing folder stops the build with a message. Building the client patch takes seconds instead of minutes. The race columns no longer overlap. Races from the race folder are sorted into the groups by looks like the others.

## New in 0.9.1

- **New races: Goblins, Worgen, Pandaren, Vulpera, Tuskarr and more.** A new module under *Modules by AFK Realm*, *mod-custom-races*, makes the races your game client carries but no player can choose playable – for you and for the bots. Which races there are is read from your own game client: in the CoA client 22 races, plus the High Elf, made from the Blood Elf. None is on until you choose it on the new setup page (*Modules → Custom races*). Each new race leans on a stock race of your choice: it starts where that race starts, gets its quests, stats, racial abilities and faction, and keeps its own looks. Switched off, a race's characters play their base race until it is on again; removed, the module takes everything back.
- **What to expect.** This is a test version. Goblin, Worgen, Tuskarr and Taunka were played from the character creation into the world. Races without a female model are male only. Races without helmet models of their own keep their helmets hidden – the preview of the character creation still shows a placeholder on the head. Some models of the client have flaws of their own that no patch can mend: on the Vulpera, for example, some armour pieces stick out. Tell me which races look wrong to you.
- After installing or changing the races: build the server, then *Modules → Game client → Install*, and give the other players a new ZIP.
- **Classic classes:** the limit of the character creation is 30 classes per race, not 300 rows in all; *mod-classic-classes* follows it.
- **A copied server folder builds again.** A test copy of your server stopped at the start of the build because CMake still knew the old place; the build now starts afresh there. And a build that fails while it is prepared no longer blames a module that was just added.
- **Modules that remove what they wrote.** A module whose rows the server writes while it runs now takes them back when you remove it.

## New in 0.9.0

- **A page for the bots.** The new *Bots* page in the side bar shows what your world is full of: how many random bots there are and how many are out in the world right now, the players beside them, their average level and how Alliance and Horde are balanced – and then the bots by class, race, level and zone, as bars. It refreshes by itself every 30 seconds while you look at it, and can count only the bots in the world or all of them.

## New in 0.8.4

- **Every race, every classic class – now without the crash.** The game keeps the list of which race may play which class in a space of about 300 entries, and the full list was longer. The newest *mod-classic-classes* keeps it at 300 by leaving out the Death Knight and the Hero (neither can be created next to the classic classes), and gives the Orc Druid the outfit the game had none for. Tested in the game: every race can choose every classic class. Update AFK Realm first, then the module, then install the client patch anew (*Modules → Game client*) and give the other players a new ZIP.
- **The right starting gear.** For the new pairs, CoA's data held only a showcase of high level armour – shown in the character creation, but no items, so such a character would have started without gear. They now wear and get the starting gear of their class, as a character of another race of the same faction does.

## New in 0.8.3

- **No more crash in the character creation.** With every race allowed every classic class, each race had all 32 classes – one more than the game takes, and *Create new character* crashed. The newest *mod-classic-classes* leaves the Death Knight out of the class list (he cannot be created next to the classic classes anyway); this version of AFK Realm is needed to build that patch. Install the client patch anew afterwards (*Modules → Game client*).

## New in 0.8.2

- **Every race, every classic class.** With the newest *mod-classic-classes*, a Gnome can be a Druid and a Tauren a Mage: the module gives the server a start for every pair, and AFK Realm adds the pairs to the character creation when it builds the client patch. Install the client patch anew afterwards (*Modules → Game client*) and give the other players a new ZIP.

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
