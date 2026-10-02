# Changelog

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
