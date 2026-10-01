<p align="center"><img src="assets/afk-realm.png" width="160" alt="AFK Realm logo"></p>

# AFK Realm

*Go AFK, come back to your own server.*

> **Preview:** AFK Realm is being tested right now. If you want to help, try it and report what worked and what did not (please attach `logs\install.log`).

Build, run and tweak your own **Conquest of Azeroth** server (an AzerothCore fork with Playerbots) on Windows 10/11, compiled straight from source, all through a point-and-click interface.

![Server management](docs/screenshots/8-server-management.png)

## Why

Pre-built repacks go stale quickly. Building from source keeps you on the latest fixes, but it needs Git, CMake, Visual Studio, OpenSSL, Boost, MySQL and a lot of patience. This tool does all of that for you and then helps you run the server day to day.

## Features

- **Guided installation**: pick a folder and a database password, then click *Install*
- **Automatic dependencies**: Git, CMake, Visual Studio 2022 C++ Build Tools, OpenSSL 3, Boost 1.87, portable MySQL 8.4, Python and mpqcli, with checksums verified
- **Always current**: every installation builds the newest CoA core and Playerbots
- **Clear progress**: every step, download percentages, compiled files and imported world tables, plus an optional live log
- **Databases ready before the first start**: imports the CoA world package, creates the auth and character databases and applies all SQL updates, and repairs databases whose setup was interrupted
- **Server management**: start and stop (clean shutdown that saves all characters), live status, and the reason shown right away if a server closes while starting
- **Server settings**: a list of popular options (XP, drop and reputation rates, flight paths, cross-faction play, Playerbots count and levels) plus every option of the worldserver and all module configs, searchable, with the description from each template and one-click reset to the default
- **Accounts**: create accounts with GM levels, list all player accounts (bot accounts are filtered out) with their characters, delete accounts, set new passwords and change access levels
- **Account transfer**: export an account with all its characters, items, mail and pets to an `.afkaccount` file and import it on another server – ids are renumbered, taken names are renamed at the next login
- **Bot reset**: one click deletes all random bots with their characters, guilds and arena teams (your own characters are kept); new bots are created at the next start
- **Map data**: extracts maps, vmaps, mmaps and the CoA client DBC tables from your game client with one click
- **Play with others**: set a VPN or LAN address (e.g. Radmin VPN); the tool configures the realm, the CoA remote-client setting and the Windows Firewall
- **Updates**: shows when newer server code or a newer AFK Realm release is available; checks the core, Playerbots and your own extra modules, and rebuilds only when something changed
- **Backups and rollback**: before every update the server is saved (programs, settings, all databases and their versions); if a new version causes problems, one click restores the previous state. Backups can also be made by hand; the newest 3 are kept
- **Portable**: everything lives in one folder; the database runs without a Windows service

## Screenshots

| Server settings | Player accounts |
|---|---|
| ![Server settings](docs/screenshots/9-server-settings.png) | ![Player accounts](docs/screenshots/10-player-accounts.png) |
| **Backups** | **Update with automatic backup** |
| ![Backups](docs/screenshots/11-backups.png) | ![Update](docs/screenshots/6-update-with-backup.png) |
| **Installation** | **Progress** |
| ![Welcome](docs/screenshots/1-welcome.png) | ![Progress](docs/screenshots/5-progress.png) |

## Getting started

1. Download `AFK-Realm.exe` from the [Releases](../../releases) page and run it.
   Windows SmartScreen may warn about an unknown publisher because the exe is not code-signed. The exe is built from this repository by GitHub Actions; you can also build it yourself (see below).
2. **Install a new server** → choose a folder such as `C:\CoA-Server` → set a database password → **Install**.
   The installation may take a long time and needs about 40 GB of disk space.
3. In **Server management**:
   1. *Create map data from the game client* (once; choose the game folder that contains the `Data` folder, and keep the game and its launcher closed)
   2. *Create account*
   3. *Start server*
4. Point your client's `realmlist.wtf` to `set realmlist 127.0.0.1` and log in.

You need the CoA game client yourself; it is not part of this project.

## How it works

The window is a small WinForms app (C# 5, .NET Framework 4.x, which is already part of Windows 10/11, no extra runtime needed). All the actual work is done by [`engine/engine.ps1`](engine/engine.ps1), a PowerShell script embedded in the exe. The script works on its own as well:

```powershell
# interactive
powershell -ExecutionPolicy Bypass -File engine\engine.ps1 -InstallRoot C:\CoA-Server
# unattended (modes: Install, Update, Rebuild, Setup, Backup, Restore)
$env:AC_DB_PASSWORD = '...'
powershell -ExecutionPolicy Bypass -File engine\engine.ps1 -InstallRoot C:\CoA-Server -NonInteractive -Mode Update
```

Sources used:

| Component | Repository | Branch |
|---|---|---|
| Core | [jealous-sound/azerothcore-wotlk-coa](https://github.com/jealous-sound/azerothcore-wotlk-coa) | `main` |
| Playerbots | [Zyth45/mod-playerbots](https://github.com/Zyth45/mod-playerbots) | `coa` |
| MPQ reader | [TheGrayDot/mpqcli](https://github.com/TheGrayDot/mpqcli) | release v0.9.9 |

Every installation and rebuild uses the newest commits of the CoA core and Playerbots; *Update* checks for newer ones later. The exact revisions built are recorded in `Dependencies\revisions.txt` and in the install log.

Folder layout after installation:

```
C:\CoA-Server\
  Server\         authserver, worldserver, configs, Data (map data)
  DB\             portable MySQL and its data
  Dependencies\   tools, source code, build files
  logs\           install.log, mysql-error.log
  Backups\        server backups made before updates (newest 3)
  Builder\        copy of the builder (the desktop shortcut points here)
```

## Troubleshooting

- **Something failed**: the error is shown in the window; the full log is `logs\install.log`. *Try again* continues where it stopped.
- **Visual Studio installation fails**: restart Windows (a pending restart is the most common cause), then try again.
- **Compiler runs out of memory** (errors C1076/C3859): the builder retries automatically with less parallelism; closing games and browsers helps.
- **Worldserver closes with "DataDir does not hold the CoA client DBC set"**: open *Create map data*, choose your CoA game folder and tick *Only refresh the CoA DBC tables*. The stock map extractor cannot read CoA's own patch archives, so the tool extracts these tables with the fork's `apps/coa-dbc/client_dbc.py`.
- **Black screen at the realm selection**: the core is too old for the client's realm cards; run *Check for updates and install*.
- **Friends cannot connect**: set the realm address under *Play with others*, restart the server, and make sure the VPN is connected on both PCs.

## Building the exe yourself

- Windows: run `build.bat`. It uses the C# compiler that ships with Windows.
- Linux: run `build.sh` (needs `mono-devel`).

## Disclaimer

This is an unofficial community tool. It is not affiliated with or endorsed by Blizzard Entertainment, Project Ascension, the AzerothCore project or the authors of the forks it builds. World of Warcraft is a trademark of Blizzard Entertainment. The tool downloads source code from the repositories listed above; their licenses apply to that code.

## License

MIT (see [LICENSE](LICENSE)) for the code in this repository.
