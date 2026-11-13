# AFK Realm 1.3.55

- **CoA Custom 2.0 setup no longer fails on XvidDecoder.dll:** the AFK64 step replaced the package's small XvidDecoder.dll with the voices release's larger copy, and the setup's final size check then stopped with "The CoA package file is incomplete after the copy: ...\XvidDecoder.dll". An existing XvidDecoder.dll is now kept, and a game folder that already has AFK64.exe is no longer checked against the package's copy. The 2.0 package is fine — update to 1.3.55 and run **Install / update** again; no new download is needed.

# AFK Realm 1.3.54

- **Lumenveil's in-game keys stay on:** CoA Custom 2.0's client files bring a CoAVolFog.ini with Overlay=0, and the CoA setup copied it over your settings. That switches the settings window and all its keys off: Ctrl+F7 (settings), Ctrl+F12 (on/off), Ctrl+F3/F4/F5/F6 and the other Lumenveil shortcuts. The setup now keeps an existing CoAVolFog.ini; a fresh folder gets the package file with Overlay=1; an older install that still has the package's own file gets its overlay switched back on. The AFK Client repairs it after an update and whenever Lumenveil is chosen, and player packs carry the owner's CoAVolFog.ini.

# AFK Realm 1.3.53

- **Choose the graphics effect:** the HD pack page and the AFK Client launcher now have a choice "Off / Lumenveil / LuxVelum" instead of the LuxVelum switch. Before, switching LuxVelum on did nothing, because the game only loads what Visuals.txt says. "Off" is the plain game, for weak computers.
- **Gilgoblins get all their racials:** they only got Brineborn Cascade, and Current Rider, Brine Mending and Brineborn Cascade did nothing without CoA Custom's own server. Update the modules (mod-custom-races brings the three scripts), then rebuild.
- **Fading Island: dying there sends you to the island's spirit healer** instead of a capital (with the updated mod-custom-races).
- **CoA Custom's world content runs once:** a reinstall no longer imports the large mount and pet files again; a changed file in a new package runs again. The manastorm cache file is left out, because the CoA core already has it and the copy could undo newer values.

# AFK Realm 1.3.52

- **The AFK Client launcher starts AFK64.exe:** it looked only for CoA64.exe, so even a freshly installed AFK client started the old package exe. AFK64.exe now wins, then CoACustomRace.exe (CoA Custom 2.0), then CoA64.exe and the plain clients. "The game is already running" recognises AFK64 too.
- **Client build carries the AFK client, Lumenveil and Visuals.txt:** both the player ZIP and the AFK Client manifest now include AFK64.exe, Lumenveil64.dll and Visuals.txt. The client build fetches the newest voices release when the owner's game folder does not have them yet; an offline build keeps what is already there.
- **The choice survives launcher updates:** the AFK Client never replaces an existing Visuals.txt, exactly like the installer. A fresh install gets Lumenveil (1); 0 stays the plain game and 2 stays LuxVelum.
- **Every install path installs AFK64.exe:** CoA Custom setup, the HD pack page and Voices now put AFK64.exe next to an existing CoA64.exe instead of keeping only the old exe. Lumenveil64.dll and Visuals.txt travel with it.

# AFK Realm 1.3.51

- **Lumenveil is the standard client effect:** the voices release now carries AFK64.exe for CoA Custom 2.0, Lumenveil64.dll and a commented Visuals.txt (0 = default graphics, 1 = Lumenveil, 2 = LuxVelum). Every AFK install (client download, HD page and Voices) puts the AFK exe, the engine DLL and the choice in place; an existing player choice is kept.
- **Linux builds survive system header updates:** clang no longer stops at "stdint.h has been modified since the precompiled header was built". Linux configures without precompiled headers, and the stale-PCH retry also updates the existing cache.

# AFK Realm 1.3.50

- **Windows build with many modules fixed:** "modules.lib: fatal error LNK1248: image size exceeds maximum allowable size" no longer stops the build. AFK Realm splits the modules into several libraries. The first build after the update compiles the modules again.
- **CoA Custom needs no GitHub key any more:** the CoA Custom repository is public now, so the download works for everyone.

# AFK Realm 1.3.49

- **New CoA Custom races get their racials:** the races added in CoA Custom 1.9.9 (Dreadlord, Titan, Sureki, Wretched, Fungarian, Sporeling, Stormforged, Mo'arg, Nerubian, Lich, Mogu, Lost One, Thin Human, Two-Headed Ogre) had none. Update the modules as well (mod-custom-races brings the spells), then rebuild.
- **Racials of the base race stay on the action bars:** with mod-custom-races updated, a custom race no longer loses and relearns its base race's racials (War Stomp on a Highmountain Tauren, for example) at every login.

# AFK Realm 1.3.48

- **"401 Unauthorized" on the HD pack or Voices page fixed:** an old or mistyped GitHub token on the modules page stopped AFK from fetching the newest CoA64.exe. AFK now simply fetches without it. (To clear the old token: Modules > GitHub token, empty it.)

# AFK Realm 1.3.47

- **Command line:** `AFK-Realm --no-window cli start` now starts the server. Before, a switch in front of `cli` opened only the web page in the background. (`AFK-Realm cli start` always worked.)
- **Start the server together with the web page:** `AFK-Realm --no-window --listen 0.0.0.0 --password <password> --start` runs the web interface for other computers and starts the game server right away, for example from Windows' task scheduler or a Linux service.

# AFK Realm 1.3.46

- **Linux: clang really used again.** On Ubuntu 26.04 clang could not link (it looked in a GCC folder without the C++ library), so every build quietly used the slower g++. AFK now shows clang the right folder. The next build starts once from scratch, after that it is fast.

# AFK Realm 1.3.45

- **Faster builds on Linux again:** since 1.3.35 the server was compiled with g++ instead of the much faster clang. clang is used again wherever it works. The next update builds once from scratch, after that it is quick again.

# AFK Realm 1.3.44

- **HD pack for your players, by choice:** after "HD pack …" in your own game, build the client launcher again. Your players then find "HD pack" and "LuxVelum" in the AFK Client's settings, both off. Whoever wants them switches them on and the launcher downloads them; nobody gets the 8.8 GB unasked. Players need the new AFK Client from your server page for the switches.

# AFK Realm 1.3.43

- **HD pack:** Game client > HD pack … puts fueryin's Ultra-HD graphics pack into your CoA Custom game without breaking the custom races, and lets you switch every part off again (HD world, LuxVelum fog/light/water, effects, sounds, add-ons), for example on a weak laptop. Download and unpack the pack first; the page finds it in Downloads.
- **Always the newest game exe:** Voices, HD pack and the CoA Custom setup all install the same, newest CoA64.exe. It runs fine whether you use voices, LuxVelum or the HD pack or not.
- **Dance music for every race:** the pack's dance music add-on now plays for CoA Custom races too (the song of the race they come from).

# AFK Realm 1.3.42

- **Voices: the right CoA64.exe.** The CoA64.exe that came with the voices until now lacked twelve races of CoA Custom 1.9.9 (Titan, Lich, Mogu, Dreadlord ...: no textures). "Install" on the Voices page now puts the corrected one in place, also over the old one.

# AFK Realm 1.3.41

- **Ollama chat page:** new switches for the newest options of mod-ollama-chat-coa: "Only bots in a group with a real player", the processor cores the model may use, and the roleplay sheets (on, read in chat, write ahead, language) plus the Companion Voice labels. No more editing the config by hand.

# AFK Realm 1.3.40

- **Voices for everyone:** the Voices page now brings the CoA64.exe that loads plugins itself (your own exe is kept as a backup). Anyone with AFK Realm and CoA Custom gets quest voice-over and chat read aloud with one click: Game client > Voices … > Install.

# AFK Realm 1.3.39

- **Fix: the AFK Client launcher now brings CoA Custom.** "Build client launcher and manifest" (and the client pack on the registration page) left out the client files of the CoA Custom package: players got the module add-ons and the AFK patch, but not the CoA Custom MPQs, so the game showed no custom races. On a CoA server the pack now carries the package's client MPQs, root files, DXVK and add-ons, the same as the player ZIP.

# AFK Realm 1.3.38

- **Voices for CoA64** (Game client > Voices …): quest givers speak their quest texts (Living Voices), and party chat, whispers, say and yell of players, bots and NPCs are read aloud in a voice that fits each race (Companion Voice), both by Crono141. Needs a CoA64.exe that loads plugins (your own build). Volume, speed and channels are set on the page.
- **Players get it too:** the player ZIP and the AFK Client launcher carry your CoA64.exe, the voice plugin and the voices from your game folder.
- A CoA Custom install keeps your own CoA64.exe instead of putting the official one back.

# AFK Realm 1.3.37

- **Linux:** when you close the window, AFK Realm can keep running in the background (registration page, remote access). A desktop notification now says so. To end it: right-click AFK Realm in the application menu and choose "Quit AFK Realm", or use the new power button at the bottom left of the window.

# AFK Realm 1.3.36

- **Linux fix:** replacing the copy of AFK Realm in the server folder (which the menu entry starts) no longer breaks one that is still running.

# AFK Realm 1.3.35

- **Linux fix:** the build uses g++ first, so a fresh Ubuntu no longer stops at "-lstdc++ not found".
- **Fix:** the menu entry (desktop shortcut on Windows) kept starting the old AFK Realm after you downloaded a new one. A newer version now replaces it by itself.

# AFK Realm 1.3.34

- **Linux fix:** on a fresh Ubuntu the build could stop with "-lstdc++ not found". AFK Realm now checks which compiler really works and uses it, or tells you the one command that fixes it.
- **Fix:** a fresh server build could stop at the racial patch with the current CoA core.
- **CoA Custom 1.9.9:** the Titan's Aegis of the Keepers and the Qiraji's Swarm Flight work.

# AFK Realm 1.3.33

- **Server settings:** the window is bigger, the divider between list and description can be dragged, and long values (like the Ollama prompts) are shown and edited in full, wrapped, instead of cut off.
- **World Journey** (module update): the portals to the Blasted Lands in the capitals open at the level Outland starts on the journey instead of 58.

# AFK Realm 1.3.32

- **New files you added to the server's source code are kept** when the server is updated or built again: they go into your hand-changes patch with your other changes instead of being deleted.
- **Fix:** an update could stop with "mod-dungeon-clear is at ... instead of ." on servers set up before Dungeon Clear was downloaded on its own.

# AFK Realm 1.3.31

- **Ollama chat:** settings for the new memory of the bots - they recall what matters, reflect on what they went through, have goals of their own and tell each other about you. Optional: an embedding model (nomic-embed-text) so they remember by meaning.

# AFK Realm 1.3.30

- **Ollama chat:** a new setting for how long a bot stays with you while you talk to it, instead of walking off mid-sentence.

# AFK Realm 1.3.29

- **Module updates bring their new recommended settings along.** Until now a module's recommended setting was set once at install; a better value in a later version of the module only reached new servers. Now it is taken over too, unless you changed the option yourself.
- **Ollama chat page:** bots that speak to you on their own, chatter in the General and Trade channels on or off, how many exchanges a bot remembers, the bots' knowledge of the world, bonds and romance, Auctions and Immersion speaking through the model, and the prompt length.

# AFK Realm 1.3.28

- **Ollama chat has its own setup page** under *Modules & races*: the connection to Ollama with a connection check that lists your models, how often bots talk on their own and to each other, and roleplay, memory and relationships.

# AFK Realm 1.3.27

- **Immersion works without the extra click.** Its database tables used to be installed only by the button on its setup page; without them the module spawned no city, fishing, legendary or PvP bots. AFK Realm now puts them in by itself when the server starts.

# AFK Realm 1.3.26

- **New, experimental: mod-ollama-chat-coa** under *Modules by AFK Realm* - bots whose chat comes from a language model (Ollama on your PC): they answer you, chat with each other, remember and form opinions. It needs Ollama installed and running; see the module's README.

# AFK Realm 1.3.25

- **More robust fix for "detected dubious ownership"** (servers on exFAT/FAT32, USB or network drives): AFK Realm's own portable Git now also trusts the server folders in its own settings.

# AFK Realm 1.3.24

- The update texts under *Maintenance* name your server's own core: CoA core and Playerbots, AzerothCore and Playerbots, or EsteriaWoW and its game client.

# AFK Realm 1.3.23

- **Fix: servers on some drives could not be updated or built.** On drives that do not record who owns a file (exFAT and FAT32, many USB and network drives) – or in a server folder made under another Windows account – Git stopped at "Downloading the server source code" with "detected dubious ownership" and exit code 128. AFK Realm now tells Git that its own folders are safe; nothing in your Git settings is changed.

# AFK Realm 1.3.22

- **EsteriaWoW uses its AFKRealm branch**, the state its author keeps for AFK Realm. Run an update once to move your server there.
- **The EsteriaWoW game client is built too.** With every update that brings new client code, AFK Realm builds `WowClient64.exe` from wowdecomp (with its fixes for crashes and freezes of the custom races), puts it into your game folder and into the client pack for your players. The first build of the client takes a while. If it fails, the server update still finishes and your game keeps its program.

# AFK Realm 1.3.21

- **The module list uses the whole window again.** *Modules by AFK Realm*, *Recommended by AFK Realm* and the *AzerothCore module catalog* are now tabs, each with the full height; the search looks through all of them.
- **Recommended modules** have a tab of their own, starting with Dev Menu by ilusixn (an in-game world editor for game masters).

# AFK Realm 1.3.20

- **Build again without an update.** Under *Maintenance* there is now *Build again*: it builds the server from the versions it has, without updating anything.
- **Your own changes to the source code survive.** Put `.patch` files into the new `Patches` folder of the server (*Open the Patches folder*), and they go into every build. Changes you make by hand in `Dependencies\Source` are saved there as a patch automatically before the next build, instead of being overwritten. If an update no longer fits one of your patches, the build stops, tells you which one, and your server stays as it was.
- **Dev Menu by ilusixn** is in the module list: an in-game world editor for game masters (`/dev`). Its add-on is handed out with the other add-ons.

# AFK Realm 1.3.19

- **EsteriaWoW:** the tables of EsteriaWoW's housing are set up. The module keeps its SQL where the server's updater does not look, so the worldserver stopped on startup when it looked for them.

# AFK Realm 1.3.18

- **Each server remembers its own game client.** AFK Realm remembered one game folder for all servers, so the EsteriaWoW server was given the tables of the CoA client and its worldserver stopped. Now Conquest of Azeroth, EsteriaWoW and AzerothCore each keep their own client folder, and "Create map data" refuses a client that does not belong to the server.

# AFK Realm 1.3.17

- **EsteriaWoW:** the worldserver no longer stops while loading its data. AFK Realm had taken all game tables from the EsteriaWoW client, including some in a format the server cannot read; now it takes only the seven tables with EsteriaWoW's races, looks and spells, as EsteriaWoW's own setup does. Run "Create map data" once more with "Only refresh the DBC tables" ticked - that takes about a minute.

# AFK Realm 1.3.16

- **EsteriaWoW:** the server can be started - AFK Realm took EsteriaWoW's map data for incomplete because it counted the DBC tables like for CoA, and asked for "CoA DBC tables". The map data window speaks of the right client now.

# AFK Realm 1.3.15

- **Important fix: server updates of Conquest of Azeroth servers work again.** Since 1.3.1, every update, rebuild and new installation of a CoA (and AzerothCore) server stopped at "Downloading the server source code" with "The property 'Count' cannot be found on this object". Your server was not changed by it: the backup was made and nothing was built. Update AFK Realm and run the server update again.

# AFK Realm 1.3.14

- **EsteriaWoW:** two character tables the EsteriaWoW server needs (for the retail looks and body shapes of characters) are created by AFK Realm - the EsteriaWoW repository has no SQL for them, so a new server could not start its character database.

# AFK Realm 1.3.13

- **EsteriaWoW:** the database updates of EsteriaWoW are applied in the order they were written - two naming styles mixed in its update files made a newer update come before the older one it builds on.

# AFK Realm 1.3.12

- **"Repair" uses AFK Realm's fixes too.** Setting up the database and configuration again without compiling now also brings in AFK Realm's corrections to the server's files, so the EsteriaWoW database fix of 1.3.11 works from "Repair" as well.

# AFK Realm 1.3.11

- **EsteriaWoW:** the database of a new server is set up completely - one module's tables were created only after its updates, which stopped the setup.

# AFK Realm 1.3.10

- **EsteriaWoW:** the worldserver links - a small file EsteriaWoW keeps out of its repository (the empty loader for custom scripts) is added by AFK Realm.

# AFK Realm 1.3.9

- **EsteriaWoW:** the modules are now built in four parts, each well below the 4 GB Visual Studio allows for one file. (The way 1.3.7 tried did not get around the limit with Visual Studio.)

# AFK Realm 1.3.8

- **EsteriaWoW:** the build preparation (CMake) accepts the new way the modules are built.

# AFK Realm 1.3.7

- **EsteriaWoW builds further:** with all its modules, the module part of the server became larger than Windows' build tools allow for one library file (4 GB). AFK Realm now builds it in a way that has no such limit.

# AFK Realm 1.3.6

- **EsteriaWoW builds further:** an old copy of a module that EsteriaWoW replaced long ago (mod-attunement-plus; the current one is part of mod-custom-server) is left out of the build, as it no longer fits the core.

# AFK Realm 1.3.5

- **AzerothCore comes with Playerbots and Dungeon Clear** again, kept up to date with every update like on Conquest of Azeroth; the DungeonClear add-on is installed into the game and handed out to players by itself. Players who do not want bots switch them off in the bot settings.

# AFK Realm 1.3.4

- **EsteriaWoW compiles on Windows.** Two places of the EsteriaWoW code used names Windows reserves for itself, so Visual Studio stopped with hundreds of errors. AFK Realm now fixes them in the downloaded code before it builds, and leaves the fix out by itself once EsteriaWoW changes those places.

# AFK Realm 1.3.3

- **Add-ons of modules on every server.** A module with an add-on in its folder now gets it into your game and to your players on Conquest of Azeroth servers too, not only on AzerothCore and EsteriaWoW - also when the module was not made for AFK Realm.

# AFK Realm 1.3.2

## Add-ons for your players

- **Hand out your own add-ons.** Game client > "Add-ons for your players …" lists the add-ons in your game; the ones you tick go to your players with the client pack and the AFK Client, which keeps them up to date - on every kind of server.
- **Add-ons of modules are found by themselves.** On AzerothCore and EsteriaWoW, a module that carries an add-on in its folder gets it into your game and to your players, even when the module was not made for AFK Realm.

# AFK Realm 1.3.1

## EsteriaWoW

- **A third server: EsteriaWoW.** Choose it when you set up a new server (under "Custom"). AFK Realm downloads only what the server is built from (about 2.5 GB of the 7 GB project), builds it natively - no Docker - and sets it up with the settings the EsteriaWoW realm runs with: its races, the third faction, Eluna, Playerbots, Dungeon Clear and its other modules.
- **Map data from the EsteriaWoW client:** "Create map data" takes the race, look and spell tables straight from the client's patch archives, so the server knows the same races as the game.
- The EsteriaWoW game client is not downloaded by AFK Realm yet; point AFK Realm at the client you have.

## AzerothCore

- **AzerothCore is now the plain core,** exactly as published: no Playerbots, no Dungeon Clear, no other module. Modules from the catalog can be added later.
- **Getting a client:** the Game client window, the registration page and the AFK Client send players to ChromieCraft's clean 3.3.5a client.

**Experimental:** EsteriaWoW and AzerothCore are new in AFK Realm and have had little testing. Please report what does not work.

# AFK Realm 1.3.0

## Conquest of Azeroth or AzerothCore

- **Choose your server.** When you set up a new server, AFK Realm now asks which one: **Conquest of Azeroth**, as before, or the stock **AzerothCore** (WotLK 3.3.5a) with Playerbots. Both are built from source and managed the same way: start and stop, updates, update schedule, backups, accounts, bots, modules, game master tools, map data.
- **AzerothCore** uses the core and Playerbots of the mod-playerbots project and Dungeon Clear, and needs a normal 3.3.5a game client (build 12340). The modules by AFK Realm (World Journey, the auction house bots, classic classes, custom races …) are made for CoA, so they are not offered there; everything from the AzerothCore module catalog is.
- Your existing server stays a Conquest of Azeroth server; nothing changes for it.
- **Experimental:** the AzerothCore build is new and has had little testing. Please report what does not work.
