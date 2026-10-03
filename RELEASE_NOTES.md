# AFK Realm 0.6.1 – preview

Build, run and tweak your own Conquest of Azeroth server (AzerothCore fork with Playerbots) on Windows 10/11.

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

## Please note

- **Work in progress and highly experimental**: AFK Realm is unfinished and changes all the time; every release and every new function has to be seen as experimental. It is very likely that one thing or another does not work properly yet. Please report problems with `logs\install.log` attached.
- Many AzerothCore modules are written for the regular core and may not compile or work with CoA. Installing one is safe to try: a module that does not compile is taken out again.
- The exe is not code-signed, so Windows SmartScreen may warn about an unknown publisher.
- You need your own CoA game client. The first installation may take a long time and needs about 40 GB of disk space.
