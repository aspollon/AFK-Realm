# AFK Realm 0.3.0 – preview

Build, run and tweak your own Conquest of Azeroth server (AzerothCore fork with Playerbots) on Windows 10/11.

## What's new since 0.2.0

- **Module manager** (*Server management → Manage modules*): browse the AzerothCore module catalog, search it, tick modules to install them and untick them to remove them. Modules that are not in the catalog can be added by their Git address.
- **Checked before installing**: for each module AFK Realm looks at its files and README and shows what to expect – database changes, settings, whether it needs a core patch (not supported) or files for the game client, whether it needs Eluna, and when it was last changed. The README opens right in the window.
- **Safe to try**: the server is backed up first. If it cannot be built with a new module, the module is taken out again and the server stays exactly as it was.
- **Removing undoes the database changes**: AFK Realm applies a module's SQL files itself and records every change. Removing the module puts the rows and tables back – except rows that were changed again later, which are kept and listed. Changes that cannot be recorded are named, and a backup restores them.
- Restoring a backup also brings the module folders back to the state of that backup.

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

## Please note

- This is a preview. Please report problems with `logs\install.log` attached.
- Many AzerothCore modules are written for the regular core and may not compile or work with CoA. Installing one is safe to try: a module that does not compile is taken out again.
- The exe is not code-signed, so Windows SmartScreen may warn about an unknown publisher.
- You need your own CoA game client. The first installation may take a long time and needs about 40 GB of disk space.
