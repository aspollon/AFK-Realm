# Changelog

## 0.6.5-preview

- New: database editor (Server management → Database editor): browse the server's databases and tables, filter rows with a WHERE condition, edit cells, add and delete rows (written only on "Save changes", in one transaction), and a SQL tab that runs any statements and shows the results
- Database editor: "Import SQL file …" runs a file after saying what it does (tables replaced, created, rows deleted or written); the database is only asked for when the file does not name it. "Export …" writes the ticked tables or whole databases (structure and rows) to one SQL file
- Database editor: before an import the tables the file touches are copied to Backups\\import-undo (newest five kept); "Undo last import …" puts them back and removes tables the import created
- Server management: new button "Restart server" (clean stop of world- and authserver, then start; the database keeps running)
- The editor is read-only until "Allow changes" is ticked; that shows a warning and offers to back up the whole server first ("Back up the server first …" does it at any time)
- Manual: new chapter "Database editor", including when a change shows in the game (restart or reload, client cache, characters that are online)

## 0.6.4-preview

- Game master tools: choosing a character shows its quest log, with how far each objective is ("Large Candle 3/8"); "Quest log" and "Refresh" read it fresh from the running server
- "Quests near the character" is now "New quests nearby" and lists only quests the character does not have in its log yet

## 0.6.3-preview

- Fix: "Reset random bots" reported "The worldserver started normally instead of deleting the bots. Nothing was deleted." on fast PCs although the bots had been deleted: the worldserver finished loading before it shut itself down

## 0.6.2-preview

- Module manager: new section "Modules by AFK Realm" at the top of the list, with modules made for this server
- First module of that kind: mod-playerbots-auctions – the bots use the auction house like players (sell, buy, bid, craft, gather)
- Modules can bring changes for the CoA core or Playerbots and settings for other config files (file afk-realm.json in the module). AFK Realm applies the patches to the fresh source with every build, leaves one out when it no longer fits or is no longer needed, and takes patches and settings back when the module is removed
- Fix: every update and every module change compiled the whole server again (20 to 30 minutes), because the build folder was thrown away each time; now only what changed is compiled
- Fix: in the server settings, "Save changes" stayed grey after typing a new value until the field was left; a change now counts while typing
- The update notice at the top of the server management now also tells when an installed module has new changes (before, only "Check for updates and install" looked at modules)

## 0.6.1-preview

- Game master tools: "Change name" lets the player choose a new character name at the next login

## 0.6.0-preview

- AFK Realm updates itself: a click on the "new version" banner downloads the release from GitHub, checks it, replaces the program and restarts it; a running server keeps running

## 0.5.0-preview

- Server name: a new installation asks for the name players see in the realm list; "Server name" in the server management renames the server later
- A database error without a message from MySQL no longer shows an empty error window

## 0.4.1-preview

- Fix: game master actions failed with "character does not exist" for characters with a first and last name (CoA allows a space in names)
- The character list in the game master window is wider, so long names are shown in full

## 0.4.0-preview

- Game master tools: connection to the running server (SOAP, this PC only, own administrator account)
- Quest helper: find quests by NPC name, quest title or id, or list the open quests around a character; give, complete, reward, remove and check them with one click, for offline characters too
- Player actions: unstuck, revive, kick, set level, send gold and mail, customize appearance, change race or change faction at the next login, server-wide announcement
- Server console in the window for every other GM command, with the server's answer
- Authserver and worldserver run without console windows; "Open server consoles" shows both in one window, with a command line for the worldserver (the classic windows can be switched back on)
- User guide (docs/MANUAL.md) that explains every window
- Fix: the build stopped at "Prepare the build (CMake)" with "the version field is not 4 integer components" on PCs with more than one Visual Studio 2022 installation

## 0.3.0-preview

- Module manager: the AzerothCore module catalog with search and filters; tick to install, untick to remove; a check of each module before installing (database changes, settings, core patches, client files, Eluna, age) and its README in the window; modules by Git address
- Module installs and removals back up the server and rebuild it; a module that does not compile is taken out again and the server stays unchanged
- Database changes of modules are recorded (schema `afk_modules`, part of every backup) and undone when the module is removed; rows changed later are kept and reported
- Restoring a backup also brings the module folders back to the state of the backup
- Playerbots versions listed in the catalog are shown as included, since AFK Realm always builds the CoA version

## 0.2.0-preview

- Automatic server backup before every update, manual backups, one-click rollback (newest 3 kept)
- Player account management: list real player accounts with their characters, delete accounts, set new passwords, change access levels
- Account transfer between servers via `.afkaccount` files (export and import with renumbered ids)
- Update notice for newer server code and newer AFK Realm releases
- Settings: shared descriptions show only the option's own default; 0/1 variants are shown as a choice
- Neutral wording instead of fixed time estimates
- Fix: the build stopped with "Git was NOT FOUND" when Git was installed but not on PATH

## 0.1.0-preview

- First public preview: guided installation, automatic build tools, newest CoA core and Playerbots, portable MySQL, CoA world data, databases prepared before the first start
- Map data and CoA client DBC extraction from the game client
- Server management: start/stop, accounts, play over VPN or LAN
- Settings editor for the worldserver, Playerbots and all module configs
- One-click random bot reset
- Update and repair functions
