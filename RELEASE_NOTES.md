# AFK Realm 0.2.0 – preview

Build, run and tweak your own Conquest of Azeroth server (AzerothCore fork with Playerbots) on Windows 10/11.

## What's new since 0.1.0

- **Backups and rollback**: before every update AFK Realm saves the server – programs, settings, all databases and the versions they were built from. If a new version causes problems, *Backups → Restore* puts everything back. Backups can also be made by hand; the newest 3 are kept.
- **Player accounts**: a new window lists all real player accounts (bot accounts are filtered out) with their characters and last login. Accounts can be deleted, get a new password or a different access level.
- **Account transfer**: export an account with all its characters, items, mail and pets to an `.afkaccount` file and import it on another server. All ids are renumbered; a character name that is already taken is changed at the next login.
- **Update notice**: the server management shows when newer CoA or Playerbots code, or a newer AFK Realm release, is available.
- **Settings**: options that share one description in the config templates now show only their own default value; 0/1 options with two different meanings (for example how deleted characters are handled) are shown as a choice instead of on/off.
- **Wording**: no more fixed time estimates; long steps simply say they can take a while.

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

## Please note

- This is a preview. Please report problems with `logs\install.log` attached.
- The exe is not code-signed, so Windows SmartScreen may warn about an unknown publisher.
- You need your own CoA game client. The first installation may take a long time and needs about 40 GB of disk space.
