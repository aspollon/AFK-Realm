# Changelog

## 1.2.12

- **Stable DXVK config for CoA64.** The CoA setup writes `dxvk.enableGraphicsPipelineLibrary = False` and `dxvk.trackPipelineLifetime = False`; an existing `dxvk.conf` is backed up as `.coa-patcher-backup`. This addresses the cold-start access violation where running Ascension first made CoA64 start.
- The complete client ZIP also contains the stable `dxvk.conf` and the x64 `d3d9.dll`/`dxgi.dll`.
- Includes the 1.2.11 fix that clears the stale client-patch offer.

## 1.2.11

- **The client-patch prompt no longer comes back after a successful setup.** `Manager.CheckClient(false)` now clears the old `OfferClient` flag and `client-offered.txt` when `ClientAddons.Pending` is empty. The Next check therefore reports the client as current instead of asking for the same patch again.
- This is a follow-up to 1.2.10 (x64 DXVK, patch-Z quarantine, full patch-T validation).

## 1.2.10

- **CoA64 HD setup.** The CoA installer now embeds and installs the x64 DXVK files `d3d9.dll` and `dxgi.dll` when they are missing from the game's `x64` folder. These are the two wrappers CoA64 uses. Existing files are not overwritten.
- **HD crash workaround.** `Data\patch-Z.MPQ` is moved to `patch-Z.MPQ.coa-patcher-backup` during CoA setup and restored by the uninstaller. CoA64 1.9.5 currently crashes with this Ascension HD world archive; character, creature, item and interface HD remain enabled.
- **Portrait fix.** The setup verifies the full package `patch-T.MPQ`; the stale 19 MB client copy (four DBCs) removed the custom-race models and race portrait icons.
- The release requires a new **CoA Custom > Install / update** run so the x64 files and the patch-Z quarantine reach the game folder.

## 1.2.9

- **The Base races page lists only CoA's own playable races.** It no longer shows NPC rows such as Troglodyte, the Test Race, or old non-playable Tuskarr, Broken, Vrykul and Forgotten copies. Rows that an old race entry copied to a new number (Troglodyte 73, Forgotten 64) are hidden too.
- **A CoA install now makes exactly the playable races of the CoA package active.** Stale keys from older client versions and client-only races are removed from `CustomRaces.Races`; missing race numbers of package races are repaired. There are no on/off switches: the package races are on, and the client's own NPC/test/legacy rows stay off. Base-race choices are kept.

## 1.2.8

- **The client patch is no longer reported as outdated right after a CoA install.** The race settings are refreshed before the patch is built instead of afterwards, so the generated patch record still matches its inputs.

## 1.2.7

- **Classic Classes use the CoA switch again.** `mod-classic-classes` no longer adds the extra classic row or hides `CharCreateSwapClassesButton`; the CoA button now switches from the CoA classes to the classic classes, keeping the module's Reborn class functions.

## 1.2.6

- **CoA client uninstall.** Every CoA setup run now records the client files it touches. **Remove CoA client files** restores files that existed before and deletes files the setup created. It leaves WTF, Cache, screenshots and personal add-ons alone.
- The record is stored in the game folder as `.afk-coa-install.tsv`; installations made before 1.2.6 have no record and cannot be removed safely.

## 1.2.5

- **AFK Realm can run from the game folder.** The "game or launcher is running" check now ignores AFK Realm's own process, so the manager does not block its own CoA setup when its exe is next to `Ascension.exe`.

## 1.2.4

- **Missing race config is recreated.** On a fresh or reset server, `mod_custom_races.conf` can be absent. The CoA setup now creates it from the module's `.conf.dist` before writing the race settings. Without this, `CustomRaces.UseCoAUI` could not be set to 1 and the client patch still contained the old `CustomRaces.lua` race selector.

## 1.2.3

- **CoA setup installs the complete client in one step.** It now also installs the add-ons of the installed modules, then builds the AFK client patch. Running Modules > Game client afterwards is no longer required.
- **One player ZIP for CoA servers.** The CoA player ZIP contains the CoA package, the module add-ons and the AFK client patch. The separate AFK-only export is hidden while a CoA package is configured.

## 1.2.2

- **CoA Custom setup no longer copies silently.** A locked file, a running game or launcher, or a full drive now stops the setup with an error instead of reporting success while CoA files are missing. After copying, every package file is checked for presence and size.
- **The CoA Custom paths are remembered.** Package, server and game-client folders are saved separately and are filled in again when the page is opened.
- **Setup refuses to run while the game or launcher is running.**

## 1.2.1

- **Base races page fixed.** Modules > CoA Custom > Base races now shows only the CoA custom races and only their base-race selection. The old race switches, stock-client races, importer and race-number tools are no longer reachable; visibility remains in the CoA UI.
- **Release check:** 1.2.0 still exposed the old Custom races page through its secondary entry. 1.2.1 replaces that entry with the base-only page.

## 1.2.0

- **CoA Custom setup in AFK Realm.** Modules > CoA Custom installs or updates a CoA Custom 1.9.3 package by copying its finished client files and settings. The setup is part of AFK Realm; there is no separate patcher EXE. The old race importer is no longer part of this path: the CoA UI owns race and class selection. The CoA server DBCs and SQL are copied too (the racial spells live in `Spell.dbc` and the other server DBCs); `worldserver.exe` is never replaced, so the AFK core and `mod-classic-classes` remain authoritative.
- **CoA is installed as-is.** The CoA client package is not run through AFK Realm's old race check/repair. The base-race page lists only CoA custom races, not the stock races of the base client; duplicates such as Pandaren or Forest Troll are marked with their faction, and their base races match that faction. The CoA UI remains authoritative for visibility and class choice.
- **Complete client ZIP.** Modules > CoA Custom can export the complete CoA client package (MPQs, client_root, add-ons and the AFK client patch) as one ZIP, ready to unpack into a game folder on another computer.
- **Private CoA Custom download.** CoA Custom is downloaded only from `ilusixn/CustomRacesCoa`; the old public/fallback package is no longer used. The first click asks for a separate CoA GitHub key (stored apart from the module-catalog token); without it there is no download. Fine-grained tokens need `Contents: Read-only` on that repository.
- **The web version has the same CoA setup.** The Windows and Linux web interface can check, install or update the package, download newer CoA releases, rebuild the client patch and export the complete client ZIP. New MPQs and files of a newer package are copied as well.
- **Linux:** the server page recommends `libjemalloc2` and Redis for noticeably faster databases and world server performance, with the Ubuntu/Debian and RHEL commands.
- **The CoA setup speaks every AFK Realm language.** Its labels, buttons, messages, log lines and the ZIP read-me are translated in German, Spanish, French, Portuguese, Russian and Simplified Chinese, with English fallback.
- **Classic classes and the CoA UI fit together.** The AFK client patch keeps `mod-classic-classes`, hides the CoA class-switch button after every class refresh, lights the race portrait circles for every race and class, and adds the classic classes to `CharBaseInfo` for races 1-127. The CoA and classic class icons use the same smaller scale and are centered in the class container, so the extra row fits the CoA creation screen.
- **Login and loading screens.** The AFK Realm badge stays on the login wallpaper (parented to `AccountLogin`, so it disappears with the login screen) and on the loading screens, not as a permanent glue overlay.
- **Version 1.2.0** in the window and the web project.

## 1.1.17

- Custom races: 1.1.16 gave a race whose model is a stock race's original model (CoA Custom's Darkfallen: the classic Blood Elf) the original hair textures only. The scalp layers it borrows (`Character\BloodElf\Female\ScalpUpperHair00_00.blp`, a copy of it under the unisex name, `Character\BloodElf\Male\Scalp…`) came from the HD patch as well and painted the HD hairline into the classic face. Now every texture such a race borrows from the stock race is the original one; a name only the HD patch has is replaced by the texture Blizzard's own CharSections gives the stock race in the same place (same sex, section, style and colour), and where Blizzard has none - the classic Blood Elf has no scalp layers - the layer stays empty and its HD file leaves the pack. Hair colours only the HD patch has (the man's colours 13-15) take the original texture of the nearest colour. Whether the model is the original one is now told by its body and its hair together (32x32 grid; Darkfallen: 100 % like the classic Blood Elf, 28-29 % like the HD one). The new file names are built with backslashes on every system (on Linux `Path.GetFileName` kept the whole path). Packer version 6: the packs are packed again by themselves at the next **Game client › Install**. Tested by packing CoA Custom's Darkfallen against the Descension client: every hair row points into the race's folder, no scalp layer is left, every texture of its sections is in the pack.

## 1.1.16

- Custom races: CoA Custom's Darkfallen model is the classic Blood Elf model (128x128 hair texture), but it borrows `Character\BloodElf\HairNN_NN.blp`, which the Descension client's HD patch (patch-Q) replaces with a 512x256 texture laid out for the HD Blood Elf. On the Darkfallen the hair, ears, brows, beards and earrings showed pieces of jewelry and black. The race packer now compares where the race model draws its hair on the texture with the stock race's original and replaced model; when the race model fits the original, the original textures from the game's own archives go into the race's folder (`Character\Darkfallen\Female\BloodElfHair00_00.blp` and so on) and its hair rows point there. The Blood Elf keeps the HD ones. Packer version 5: existing packs are packed again by themselves at the next **Game client › Install**.
- Windows: after an update (or a restart of its own) the old AFK Realm window stayed open next to the new one. Since AFK Realm has no console window (1.1.12) nothing closed the old browser window; the old process now closes it before it ends.

## 1.1.15

- Custom races: CoA Custom's Darkfallen has 40 hair styles in CharSections but only 10 (men 9) in CharHairGeosets, while her model has 20 hair shapes (men 16, the Blood Elf ones). A style beyond the table froze the barber and the creation when clicked through, and a character made with one was drawn after the next login with another shape under the Blood Elf hair texture - hair, ears and brows full of ornaments. The race packer now reads the hair shapes from the model (`00.skin`) and adds the CharHairGeosets rows the table never named (shape = style + the race's step): Darkfallen keeps styles 10-19 (men 9-15). Styles and beards that still have no shape are left out of the race's sections. Races of EsteriaAppearance.dll keep their rows (the DLL draws them). Packer version 4: existing packs are packed again by themselves at the next **Game client › Install**.
- Windows: when Edge ends at once with code 0 and no page comes (a browser process left over from an earlier AFK Realm window holds its profile, takes the window over and never shows it), AFK Realm tries once more with a new browser profile before it falls back to the default browser. `startup.log` notes when the page connected.

## 1.1.14

- Custom races: a fresh Descension client has `Interface\CharacterFrame\TemporaryPortrait-<Sex>-<race>.blp` for the stock races only, so a custom race's portrait in the character list and the paper doll stayed blank. The race's icon is put under that name into the race archive when the client has no picture (stamp `portraits 1`, so the next **Game client › Install** rebuilds the archives).
- Custom races: hair rows that name `Character\BloodElf\Female\Scalp…` / `\Male\Scalp…` point at the unisex `Character\BloodElf\Scalp…` when the pack has it; the other file painted a wrong hairline and forehead on Darkfallen.

## 1.1.13

- Windows: when the app window closed before its page ever reached AFK Realm (the browser did not start, an antivirus stopped it, a firewall blocks 127.0.0.1), AFK Realm ended after a few seconds without a word and without an error. It now opens the page in the default browser instead. Every start writes what it did to `startup.log` in the settings folder (`%APPDATA%\AFK-Realm`): administrator restart, another AFK Realm already running, which browser the window used, how it ended. The Get help ZIP includes it.
- Linux, existing server folders: the engine stops with a plain explanation when the database's start script cannot be made executable (a drive without Linux permissions), instead of PowerShell's "Cannot run a document in the middle of a pipeline".
- Linux: a new server folder on a drive mounted with `noexec` is refused with the command that fixes it (`sudo mount -o remount,exec <mount point>` and the same in `/etc/fstab`). The permissions there look right, but no program runs - the database's start script failed with "Cannot run a document in the middle of a pipeline".

## 1.1.12

- **A failed start says why.** Since 1.1.6 the Windows version has no console window, so a start that failed just vanished after a few seconds. Now the error goes into a message box and `startup-error.txt` in the settings folder (`%APPDATA%\AFK-Realm`, also in the Get help ZIP). Answering No to the question for administrator rights says that AFK Realm needs them.
- Custom races: a missing texture of a race (its own client lacks it too - CoA Custom's High Elf has 138 such paths, a skin colour 62 among them) takes the same layer of the nearest colour of the same look. Before, an upper face or the torso was left empty, and the High Elf showed half faces with CoA Custom's looks.
- New server folder: an apostrophe or other signs in the path are refused (the Visual Studio installer rejects `C:\Vol'jin` with error 8004). On Linux a folder on a drive without Linux file permissions (an NTFS/exFAT drive of Windows, a network share) is refused: the database ignored its world-writable settings file there and its start script could not run ("Cannot run a document in the middle of a pipeline").
- mod-custom-races (update the module): the character creation crashed now and then when a race or sex was chosen with many races on - the game's race list ran past the room of the script engine; the race portrait of the navigation is made anew after the game paints it.

## 1.1.11

- **Custom races: reputations and friendly NPCs.** A character of an added race saw every NPC as neutral and an empty reputation page, and a race change to it seemed to wipe the reputations. The server gave the race its base race's reputations (mod-custom-races lends the race masks of the faction table at its start), but the game client works out starting standings and which factions it shows from its own `Faction.dbc`, which knew nothing of the race. The client patch now lends the same masks in the client's `Faction.dbc`: every race mask that admits the base race admits the race too. For a race above 32 the bit the client reads (its number's lowest five bits) is lent, unless a race of the realm with another base race has that number.

## 1.1.10

- Custom races page: races could no longer be imported once a race folder had races - **Add races …** skipped the first step. It now always asks where the races come from (go on with the races you have, take races from CoA Custom's folder, choose another client, or download CoA Custom), and an **Import races …** button sits next to it.

## 1.1.9

- **Moves of client races tidied up by themselves.** Moving the same race again and again wrote chains (`16:76 76:77 77:83 83:84`). The Custom races page now writes one move per race, from its number in the client to where it ends (`16:84`), leaves out moves that do nothing (a race the client does not have, a number that is taken, a move back), and puts them in an order in which every new number is free. Existing settings are tidied at the next opening of the page or client install; what the moves do stays the same.
- **Fixed: moved client races were switched off** ("no row for it (its row is under number N)"). Client races moved to other numbers on the Custom races page (`CustomRaces.MoveClientRaces`) were moved *after* the edits that make a race playable or copy it to its number, while the page writes the number after the move. A copied race found nothing to copy, and a race edited in place had another race's row edited. The moves now come first. A move onto a number another race has, or of a race the client does not have, is left out with a note, as the page reads it (chained moves like `42:65 65:72` keep working).
- **Races with textures their own client lacks stay on.** When packing a race again did not bring the textures the race check missed (CoA Custom's Zandalari Troll has rows for torso skins it does not ship), its rows stop pointing at them: an extra layer (torso, legs, upper face) is left empty as on stock races, a missing main texture takes the one of the nearest colour. Only if textures are still missing after that is the race switched off.
- Race check: a race found under another number by its file name prefers the row with its own name (a Troglodyte and a Murloc of the client share the file name).

## 1.1.8

- Windows: the build stopped at CMake with "Could NOT find Boost (missing: filesystem program_options iostreams regex thread) (found suitable version ...)" when another Boost was set in Windows (environment variable `BOOST_ROOT`, e.g. a newer Boost installed for something else, without the compiled libraries). The core's CMake takes that variable before anything on the command line. The engine now points `Boost_ROOT`/`BOOST_ROOT` at the Boost it uses for its own run (Windows' settings are not changed), says so in the log, and makes CMake look for Boost again (`-U Boost_*`), so a failed run's cache does not keep the other one.

## 1.1.7

- **Fixed: every imported race was switched off at Game client › Install** ("the race table of the game has no row for it"). The race check read the new client patch as if the game loaded it before `patch.MPQ` (by the characters of the names, `patch.MPQ` sorts after `patch-ZZZZZ.MPQ`), so the rows of the client's own `patch-M.MPQ` hid the rows of the imported races. It also left out the race archives (`patch-ZYG..`) with their models. The check now reads every archive the game reads, in the game's order. The final check and **Check the races …** read the installed patch and race archives too.
- Race check: when a race of the game client has no row under its number, it looks for the row by the race's file name and takes it from there (a race moved to another number) instead of switching the race off; the message says where the row is.
- `logs\race-check.txt` lists the race settings (number, source, copy, import, file, base of every race, the moves of client races), so one file shows what the check saw.

## 1.1.6

- Custom races: a race of the game client whose number differs from the number of its row there (an Esteria race given CoA Custom's number by the race check, or a number changed by hand) was switched off at the next client install ("the race table of the game has no row for it"): its rows were edited in place instead of copied to its number. The race check now copies them (as for the High Elf), the page writes `Copy = 1` for such a race, and the numbers check of the page leaves a copied race at its number.
- Windows: the web version no longer opens an empty console window next to its own window (it is a window program now). Started from a command line with `cli`, `--help`, `--no-window` or `--listen`, it writes into that console as before.
- **The web version is AFK Realm now, on Windows too.** The release's `AFK-Realm.exe` is the web version (the classic WinForms exe is no longer built or released). The classic exe of 1.1.5 and older updates itself from that file name, so its usual update notice moves every classic user to the web version in place: same file, same desktop shortcut, same settings and servers, servers keep running. `AFK-Realm-web.exe` stays in the release (the same file) for the web builds 1.1.0-1.1.5; the web version updates from `AFK-Realm.exe` from now on. The build stops if the Windows file reaches the 50 MiB the classic updater accepts.
- Race packs stay up to date by themselves: every pack records the packer version (`packer`) and what its client was (`sourceStamp`: the CoA Custom version AFK Realm downloaded, plus names and sizes of its archives). At **Game client › Install**, a pack made by an older packer, or from an older CoA Custom than the one in the CoA Custom folder now, is packed again from that folder before the patch is built - no re-import by hand. A pack whose client is gone keeps working as it is; `afk-repack-tried.txt` in its folder stops a retry until something changes. Packs of another client (Ashen Order …) are never packed from CoA Custom.
- Linux: names are case-sensitive there, Windows paths use backslashes. Every path into the game folder, the CoA Custom package and the race folder now goes through one function that uses the right separators and the spelling that exists on disk (`Data`/`data`, `Interface/AddOns`/`addons`, `.MPQ`/`.mpq`, `Esteria*.bin`). Fixed on the way: the CoA Custom download and the game client download wrote files named `Data\Retail\...` instead of folders, the Esteria fitting files next to the game landed in the same wrong place, and model heights of imported races were not found.

## 1.1.5

- **Problem? Get help**: a button that is always in view (side bar of the web version, top bar of the Windows version) gathers the logs of the build and the server, crash reports, the settings without passwords, the database error log and the logs, crash reports and file list of the game client into one ZIP in the Downloads folder, and says how to send it on Discord. Crash dumps go into a second ZIP. Every error message of a task offers the same button.
- Custom races page: one list with every race once (grouped by side, "starts like", men/women only, its state), plain-language decisions with the recommended answer first (CoA Custom missing, no room, a place taken), a guided first setup (get races, choose, check, into the game); race numbers, the 64-race patch and file tools moved under "Advanced". A race the game has and CoA Custom too takes CoA Custom's looks by default, with a link to switch back.
- The High Elf (made from the Blood Elf row) is Alliance again and starts like a Human; it was shown as Horde, starting like an Orc.
- Linux: game folders whose archives are named `.MPQ` (upper case, as the game ships them) are found again.
- Race check and repair: before the client patch goes into the game, every custom race that is switched on is checked for both sexes against what the game client will read (race row, model and its .skin, skins/faces/hair/underwear rows, a face and an underwear for every skin colour, double rows, HD rows paired with the wrong look, missing textures, helmet coverage of its prefix, Esteria races without CoA Custom's exe or number) - and repaired: helmets of the base race, strict section rules, CoA Custom's exe and 64-race patch switched on, the Esteria race given CoA Custom's number (mod-custom-races moves its characters along), an imported race packed again from CoA Custom's folder. The patch is built again until the check is clean; a race that cannot be repaired is switched off with the reason. Before that, the Custom races settings are written again from the client and race folder, so a page never saved after an update cannot give stale values. The result goes to `logs\race-check.txt` and the install message; the Custom races page has a **Check the races …** button.
- Copied races (the High Elf from the Blood Elf): Death Knight rows that have an ordinary twin are dropped instead of being made playable, and HD rows keep the pairing the copy gave them. The High Elf showed Death Knight faces and its eyes beside its eyes.
- Custom races: a race counts as having its own helmets only when its prefix has (nearly) every helmet the stock races have, in the game client and its race pack. A race with helmets for some items only (Esteria's Vulpera, Eunoia's races) showed the blue and white placeholder cube for every other helmet, the CoA items above all; it now wears the helmets of its base race, as CoA Custom does it. Save the Custom races page and install the client patch again.
- Race packs: in a model folder the player's client already has in part (`Creature\Banshee`, `Character\Worgen`), files the other client changed under the same name are taken as well, not only those that are missing. CoA Custom's fixed models and textures stayed the old ones. Re-pack the affected races.
- Race packs: races with women only (CoA Custom's Banshee, Harpy and Alpha Goblin) are marked `FemaleOnly`; the character creation offers only the woman and the server makes men of them (bots) women.
- Racials: the rows of an older CoA Custom server pack (1.8) are added to those of mod-custom-races instead of replacing them, so the races of CoA Custom 1.9 (Hozen, Sylvari, Mantid, Ethereal, Kyrian, Venthyr, Slime Giant, Centaur, Banshee, Faceless One) get their racials.

## 1.1.4

- Male-only race packs save `MaleOnly = 1` even when the race folder came from an older client or pack. The web and Windows pages use the same known list as the client script.

## 1.1.3

- Custom races with two faction rows use a base of their own side. A saved Alliance base is not reused for the Horde row: Pandaren Horde falls back to Orc, Dracthyr Horde to Orc, Darkfallen Horde to Blood Elf, and an old wrong base is corrected when the page is saved. The web and Windows pages agree.
- Every known male-only race is now marked as such even when the client tables still carry a female display or a reused female row: Fel Orc, Broken, Vrykul, Tuskarr, Taunka, Forest Troll, Ice Troll, Skeleton, Northrend Skeleton, Troglodyte, Thin Human, Gnoll and Saberon. The client script also carries this list, so older race folders cannot offer the female button.
- Race pack files: decoration texture repair checks `.mdx` models too (Dracthyr female), not only `.m2` (Highmountain Tauren and the other female models). Re-pack the affected races.
- The 1.1.0-1.1.2 web/Linux interface is the current base.

## 1.0.67

- Client: the Esteria texture-worker delay crashed the client before the login screen. The ten-second thunk of 1.0.64-1.0.66 had lost its `pushad`; its `popad` took 32 bytes off the stack that were never put there. The thunk is whole again, and a DLL that already carries the broken thunk is repaired.
- Client: the base relocation of the replaced `push` at the worker site is removed. `EsteriaAppearance.dll` is built for ASLR; loaded at another address, Windows added the difference inside the new jump. This affected every delay version since 1.0.50.

## 1.0.66

- The two faction rows of a race are separate entries on the Custom races page (Pandaren Horde/Pandaren Alliance, Dracthyr Horde/Dracthyr Alliance, Darkfallen and so on). Changing the Alliance entry does not change the Horde one; the page now gives a new Horde entry a Horde default instead of Human. Existing saved bases are kept, so set the Horde row as well.
- The default Horde Vulpera no longer takes VulperaEsteria's racials. `CoaRaceOf` mapped every pack with “vulpera” in its name to CoA Custom row 74; it now uses row 19 for the default Vulpera and row 74 only for VulperaEsteria/Esteria. Stale `Racials` entries are removed when the page is saved.

## 1.0.65

- Custom race packs now include files in folders the player's client already partly has. Before, a race whose male files were already in the client (Dracthyr, Haranir and others) only got the model files for the female side; her textures and meshes outside the model header were skipped. The packer now adds every source file the client lacks without replacing existing files. Re-import/re-pack the races after updating.
- Race archives group up to 16 races or about 1.5 GB per `patch-ZYG##.MPQ` (was four). Small races share archives, so many active races no longer mean dozens of MPQs.

## 1.0.64

- Client download: empty files in the public manifest (`Data\Content\Transmogrification*.json`, `HD_Patches_Description.txt`) are written as empty files instead of looking for a `.part` object that cannot exist. This fixes “Could not put …TransmogrificationItemData.json into the game folder: …\.part not found”.
- Server updates: a PowerShell variable-name collision (`$paths` in the three-way patch helper vs. the global `$Paths`) no longer aborts patch application with “The property 'Log' cannot be found”. Module patches that need a three-way merge can now be applied; failed reverse checks are quiet.
- Client startup: the Esteria appearance texture worker waits ten seconds instead of two before using the D3D9 device, and an already patched two-second DLL is re-patched. This targets the ACCESS_VIOLATION in `EsteriaAppearance.dll` that some female Haranir/Dracthyr characters hit on a cold client start.
- Update notices: the game-client and CoA Custom notices only appear when the package is already installed and newer. A missing client or CoA Custom package is no longer advertised as a download at the top of the main page.
- Race archives: the races now share grouped `patch-ZYG##.MPQ` archives (at most four races or about 1.5 GB each), and the old one-per-race archives are removed. With many active races the client no longer has to load dozens of extra MPQs, which is the suspected cause of the missing-WotLK-data report.

## 1.0.63

- The game-client and CoA Custom update notices now sit in the same top strip as the core and module update notices, instead of inside their cards. Their card buttons still change to “Update game client …”, “Update custom races …” or “Download …”.
- The server line shows uptime as hours:minutes:seconds and adds the worldserver's last update time from `server info` (“5 ms tick”, with the mean) next to the memory usage. The value is fetched over the local SOAP line at most every ten seconds.
- Custom race portraits use the stock round portrait mask again. The round texture is remembered per button so repeated layouts and scrolling do not create a new texture every time; custom race icons stay round without exhausting the client's textures. (Needs the updated mod-custom-races client file.)

## 1.0.62

- Client and CoA Custom update notices: the main page now shows when a newer game-client manifest or a newer `coa-custom` release exists, in the Game client and Custom races cards. The buttons say “Update game client …”, “Update custom races …” or “Download …” and open the relevant window. The Game client dialog also checks the manifest and labels its download button with the available version.

## 1.0.61

- Game client: AFK Realm can download and update the complete client from the public CoA development launcher. The launcher's manifest and content-addressed object store are used directly: every file is checked against its size and SHA-256, interrupted downloads resume from `.part` files, and a state file makes later updates skip unchanged files. WTF, Cache, add-ons, logs and realmlist are never written; DXVK/graphics wrappers and changed game files are kept or backed up. A real Cancel button is available for the 43 GB first download.
- The Game client dialog now has a “Download client …” button; after the download AFK Realm can install the module add-ons and client patch as before.
- Races: Custom races has a “Download CoA Custom …” button. It downloads all ZIP files of the newest `coa-custom-*` release (client and server), verifies every GitHub asset, extracts all of them into the same race folder and opens the race selection. A background check shows when a newer CoA Custom version is available.

## 1.0.60

- Races: the client's stock-copy rows are recognized as placeholders, not as separate races. This is the 51–61 block: Human2 at 51, NightElf2 at 54, Undead2 at 55, Tauren2 at 56, Gnome2 at 57, Troll2 at 58, BloodElf2 at 60 and Draenei2 at 61, plus the Orc2/Dwarf2 rows. A custom race whose intended number is one of these now keeps that number and replaces the copy, instead of being pushed to a free number. This fixes Alpha Goblin 57, Dracthyr 54/58, Eradar 55, Harpy 56, Illidari 60/61, Ogre 53 and Undead Gnome 51, and preserves Esteria appearance/race IDs.
- Client: the old AFK `patch-Y.MPQ` is now removed on patch build/export as well as install, including in locale folders, but only when it carries AFK Realm's own stamp. Previously a ZIP export could leave it behind.
- Portraits: update mod-custom-races; its stock-copy handling and direct icon portraits are needed together with this build.

## 1.0.59

- Core: the CoA core moved to `Conquest-of-AzerothCore/azerothcore-wotlk-coa`. AFK Realm builds from the new address; an existing source folder is switched over on the next build, and the revision recorded under the old address still counts.
- Modules: a module patch whose three-way merge leaves conflict markers is undone completely (files and index) and reported as not applied, instead of leaving a half-patched core that fails to compile (`<<<<<<<` in Player.cpp).
- Races: the updated race module draws icon portraits without `SetPortraitToTexture`; with many custom races the client ran out of portrait textures and every button showed the whole portrait sheet.
- Classic classes: the updated module's core patch no longer depends on the include list of Player.cpp.

## 1.0.58

- Races: Dracthyr Alliance and Horde now use their own CoA source rows (54 and 58) and are no longer treated as the same race when numbers are assigned. This fixes the fresh 1.9.1 import where both asked for 54 and the client reported that 54 was already Night Elf.
- Races: race-folder packs are matched to the client row they came from before falling back to name/file matching, so two variants that share a model file do not both attach to the first row.
- Races: the CoA racial mapping is corrected: Pandaren uses CoA race 20 (not 18/Vrykul), both Vulpera variants use 74 (not Pandaren 20), and Darkfallen uses its source row 80/81. This fixes the wrong Pandaren, Vulpera and Darkfallen racial sets.

## 1.0.57

- Client: CoA Custom's loading screens now come only with a CoA Custom client, not on every server. A stock client keeps the original WoW loading screens.
- Client: the late `patch-ZZZZZ.MPQ` and the AFK badge remain on the CoA loading screens. Stock loading screens are not replaced by AFK Realm at the moment; putting the badge on every stock screen needs a separate pack of modified stock BLPs, because the loading screen is drawn by the engine and is not an add-on frame.
- Races: the character-creation fixes from 1.0.56 remain (native CoA options/icons, no doubled Esteria rows, base-race portrait fallback). The affected user needs the updated module as well as AFK Realm.

## 1.0.56

- Client: AFK Realm's loading-screen branding is now a base client patch, not a module feature. It uses CoA Custom's Ilusixn loading screen for every map, adds a `powered by AFK Realm` badge in the lower left, and is present even when no module or race is installed.
- Client: the AFK client patch is now `Data\patch-ZZZZZ.MPQ`, after CoA Custom's `patch-ZZZD.MPQ`; a `patch-Y` archive would be overridden by CoA's loading-screen archive. The old `patch-Y.MPQ` is removed when it carries AFK Realm's own stamp.
- Races: character creation no longer doubles the Esteria appearance rows on CoA 1.9 clients. Those clients draw the rows themselves (`COA_EsteriaRefresh`); the module skips its old second set.
- Races: a race with a native CoA icon now keeps it instead of being replaced by the generic round atlas, so many-race buttons no longer all show the same picture.

## 1.0.55

- Races: an imported CoA race can keep its source number (Gnoll 83) when the client still has a hidden or non-playable `ChrRaces.dbc` row at that number. It is no longer pushed to a low free number such as 14 just because no visible race uses 83.
- Races: orphan `CustomRaces.<race>.Id` lines from a race deleted out of the race folder are cleared before numbers are assigned, so they cannot block 83 invisibly.
- Races: a client race with its own CoA number is restored from an old copied number when no character uses the copy, and an imported race is matched to the same client race even if a client update renamed its model file.

## 1.0.54

- Races: the automatic number assignment now includes CoA Custom's free numbers 64 to 127 when the high race numbers are on. A client with many free high numbers no longer reports "All 23 free race numbers are taken" while the page shows 23 of 85 free numbers in use; an imported race such as the Hozen can take a free high number.

## 1.0.53

- Races: an imported CoA race keeps its CoA number even when the CoA pack folder is gone. The fallback uses the race pack's source number and the known CoA mapping (Gnoll 83, Sethrak 82, Saberon 97, and so on) instead of assigning a free number such as 14.
- Races: a race that belongs to the client under its own CoA number (for example Gnoll 83) is put back from an old copied number when no character uses the copied number. Characters are never moved silently.
- Races: a race pack whose model file was renamed by a CoA client update is matched to the client's race by its source number and name, so it does not become a second race competing for the same number.
- Races: the high race numbers switch on the page must be on for 83; with it off, only numbers below 32 are available. The page now shows this directly.

## 1.0.52

- Client start/race patch: if the CoA Custom pack folder was deleted after its files were installed, the client patch no longer insists on that folder. It uses the installed `dinput8.dll` next to `Ascension.exe` for state, repair and the ZIP, and keeps the other installed client-root files instead of deleting them.
- Race setup: the 64-race patch and CoA Custom's high race numbers now have visible switches, so they can be turned off when a client has no 64-race DLL. Import still turns both on automatically.
- Race setup: a race saved by an older client or an older race folder is removed from `CustomRaces.Races` when the page is opened or saved. If the client patch is built before that, a stale `ChrRaces.dbc` row is skipped with a note instead of stopping the whole build.
- Config: a hand-edited `.conf` with two lines for the same option now follows the server and uses the last line. Saving replaces all copies, so appending `CustomRaces.MoreRaces = 1` can no longer be overridden by an older `= 0` above it.
- Client patch: the missing-64-race-DLL error now says to import the CoA client again or put the DLL next to `Ascension.exe`; it no longer tells the user to switch off an option that had no switch.

## 1.0.51

- Race import: choosing a CoA Custom pack now finds its `client_root` and 64-race `dinput8.dll` even when the user picks the pack root, `files`, `files\client` or `files\client_root`. The import switches `CustomRaces.MoreRaces` and `CustomRaces.HighRaces` on and fills `CustomRaces.MoreRacesFile` from the DLL in `files\client`, `client_root`, the pack root or the race folder's `_Client` copy.
- Race setup: an already chosen CoA Custom client now fills a missing or stale `MoreRacesFile` and turns `MoreRaces`/`HighRaces` on when the page opens, so an earlier import that left the DLL disabled is corrected.
- Race import: if the pack's DLL is only kept by the race packer in `_Client\dinput8.dll`, that copy is used. A plain repack with a 64-race DLL is configured as well; it still does not invent a CoA `client_root`.

## 1.0.50

- Client patch: AFK Realm now applies both client binary fixes itself when it builds, installs or exports the client patch from a recognised CoA Custom 1.9 `client_root`. The `Ascension.exe` customize/character-select thunks and the `EsteriaAppearance.dll` two-second worker delay are written to temporary copies; the source pack is not modified. Both the installed files and the ZIP use the patched copies, so players no longer run the Python tools by hand.
- Client patch: the patchers are fingerprint-checked. An already patched or unknown client is left unchanged; `mod-custom-races/tools/customize_extra_patch.py` and `esteria_start_delay.py` remain for verification, manual preparation or a different CoA build. The client root still supplies `dxvk.conf`, DXVK 2.7.1 `d3d9.dll` and `Data\Retail\Assets\EquipmentFitting\*.bin`.

## 1.0.49

- Client start: CoA Custom 1.9's `EsteriaAppearance.dll` starts its texture worker before the D3D9 device and its first texture objects are ready. That timing race made cold starts crash before the login screen; another GPU-heavy program only hid it. The client pack now carries a version-checked DLL patch that waits two seconds at the worker start. `mod-custom-races/tools/esteria_start_delay.py` reproduces it; the proper upstream fix is a ready signal from the renderer.
- Client start: the preceding `dxvk.conf`/DXVK 2.7.1 changes remain, but they were not sufficient on their own. The worker delay is the part that removes the crash.

## 1.0.48

- Client patch: CoA Custom keeps the armor-fitting tables for the imported races in `client_root\Data\Retail\Assets\EquipmentFitting\*.bin`. The client-root allowlist treated every `Data\...` path as the game's own folder and silently dropped this subtree. It is now copied to the game's `Data` folder and included in the ZIP for other players, so the new races keep their fitted armor models.
- Client patch: the rest of `files\client` remains a source for the race packer, not a wholesale client overwrite; `files\server\dbc` remains the server source. Importing the CoA package therefore still builds race-specific `patch-ZY<race>.MPQ` files instead of replacing the player's client archives.

## 1.0.47

- Races: customizing an existing Esteria race now keeps the sixth appearance value. The client patch adds the value from the same `HighmountainFields` the preview uses (one byte for Highmountain/Earthen, eight bytes plus `HRC1` for Haranir), and `patches/core-customize-extra.patch` stores it in `characters.extraAppearance`.
- Races: a cold start now sets `EsteriaAppearanceContext` before the character-select hook, so face, hair, beard and tail use the same Esteria sections as the preview. The unit hooks stay untouched; they also run for NPCs and bots, and a non-character pointer there crashed the world entry.
- Races: opening a paid customization no longer randomizes an existing Highmountain look; only a new character starts from the creator's random/first-choice look. A single option can therefore be changed and saved.
- Client start: the client root must use a `dxvk.conf` without `dxvk.enableGraphicsPipelineLibrary` and `dxvk.trackPipelineLifetime`, and the supplied DXVK 2.7.1 `d3d9.dll`. The old settings crashed the Esteria texture loader on a cold start; running another GPU program only hid the timing race.
- Client patch: the patched CoA `Ascension.exe` is built by `mod-custom-races/tools/customize_extra_patch.py`; the server core change is `patches/core-customize-extra.patch`. Characters keep their appearance across logout and a full client restart.
- Module patches: if a patch's code still matches but its context moved (for example after a module update), the engine retries it with a three-way merge instead of silently building without the change. Patches that really conflict are still reported and left out.

## 1.0.46

- Races: the login appearance repair added in 1.0.45 was removed. Esteria races such as Highmountain use appearance values that do not match the stock `CharSections` ranges; the repair treated those values as invalid and reset the look at every login. This release keeps male-only conversion at login, but no longer rewrites a valid look.
- Races: a race-changed character needs one visit to the character customization. Its old race's appearance cannot be mapped to the new race automatically.

## 1.0.45 (withdrawn)

- This release's automatic appearance repair reset valid Highmountain/Esteria looks on login. Use 1.0.46.

## 1.0.44

- Races: the server now receives the appearance tables of the imported races as well (`CharSections`, hair geosets, facial hair and hair textures). Bot creation can randomize skin, face and hair for every imported race, not only for races that happened to be in the server's old DBC files.
- Races: the server's original appearance tables are kept as `.afk-base`; when the last imported race is removed, they are restored. This covers the server side as well as the client patch.
- Races: old male-only bots are repaired at login. Characters with all appearance bytes zero get valid values from their race's server sections.
- Character window: the Hide Gear addon adds a movable button at the character frame and a `/hidegear` command. It hides all armor for everyone; weapons stay visible, and the choice is saved per character.
- Races: Gnoll and Saberon remain male only; old female Gnoll bots are turned into men at login.

## 1.0.43

- Races: an imported race whose model lies under `custom\...` keeps the file name of its source row in `ChrRaces.ClientFilestring` (Gnoll becomes `GnollHorde`, not the pack key `Gnoll`). This fixes the missing body textures and the white customization background.
- Races: the character-customization camera rows of the source race are copied into the client patch, so the Customize step uses the race's own camera instead of the one left from the previous race.
- Races: Gnoll and Saberon are male only, as in CoA Custom. The female choice is no longer offered for them; old female Gnoll characters are turned into men at login.
- Races: the Esteria customization buttons use the labels the appearance DLL returns; Gnoll has its own fallback names (Mane, Eyes, Outfit Color and the armor/outfit choices) instead of the generic skin/hair labels.
- Races: `race.info.json` records the source file and a male-only flag (version 5), so older race packs get the corrected values the next time the page is opened.
- Playerbots: the two walking switches are gone from the server settings. The Immersion module no longer changes them and its walking patch is removed; walking stays off.
- Immersion Life: Feles Noctis and Simple GeekAce are always created with the custom city activities; the obsolete "always create Feles Noctis" switch is gone.

## 1.0.42

- Races: the setup page is now split into imported races at the top and the races already in the game client below. The old armour/male/untested groups are gone.
- Races: one Import races button asks once for a client or repack, detects its archives, `Ascension.exe`, `dinput8.dll` and CoA Custom client files, and keeps the needed files next to the game up to date when the client patch is installed.
- Races: a race already in the game client can be moved to a free number, so an imported race can keep its original number. Stock races and races with characters cannot be moved.
- Races: an imported race whose number is taken now asks what to do - use a free number, move the existing client race, or cancel. Esteria races warn that moving their number can break their appearance.
- Races: `race.info.json` now records the source file beside the model folder, so rows that only share a number are no longer mistaken for each other.
- Races: the client patch now applies the number moves before the imported races are written.
- Immersion Life: beggars have their walking switch on the custom city characters tab.
- Playerbots: the walking switches are in the server settings. The Immersion module turns walking anywhere and indoor walking on when it is installed.

## 1.0.41

- Immersion Life: city residents now have a stable walk chance. 85 % walk, 15 % hurry by default; the value is on the City population tab.
- Immersion Life: beggars walk by default instead of running.
- Playerbots: `AiPlayerbot.RandombotsWalkingRPG` now means walking anywhere, as documented. `AiPlayerbot.RandombotsWalkingRPG.InDoors` now walks only under a roof; before, it was loaded but unused, while the first switch only worked indoors.

## 1.0.40

- Races: the free-number count and the assignment now use the same slots. A race no longer reports "all 21 free race numbers are taken" when most of them are free.
- Races: a CoA Custom race whose own number is taken can still take a normal free number. The page prefers numbers outside CoA Custom's race table, but no longer refuses the race when every free slot happens to appear there.
- Races: IDs above the free slots no longer count against the free-number limit, so the status line and the free buttons agree.

## 1.0.39

- Auction house bots: prices now form a band from what items really sold for: a low, a usual and a high price. Bots ask within it, undercut only down to its low and cannot ask more than its cap.
- Until an item has enough sales, the band is a fallback around the usual vendor-based price. No external scan or price data is needed, so realms with very different prices cannot poison it.
- The band is stored with the existing price memory; older tables get the two new columns automatically.
- The auction house page has the price-band settings on its Prices tab.

## 1.0.38

- Immersion Life: the module's quest system is gone. Quest code, quest settings and the quest hubs are removed; the normal Playerbots do the questing. Old quest hub rows are ignored and cleaned from existing databases.
- Immersion Life: city hub levels now follow World Journey's `journey_zone.bot_min/bot_max` for the zone each hub stands in, so capitals, towns, Outland and Northrend keep the same level brackets as the journey.
- Without World Journey, or for a hub whose zone has no journey bracket, Immersion keeps the hub's original level range as the fallback.
- All hub, custom, fishing and old quest level ranges are clamped to the server's maximum level, so nothing asks for level 80 on a level 60 realm.
- The setup page applies the quest-hub cleanup together with the module's other database files.

## 1.0.37

- The module selection screen can sort the AzerothCore catalog: Recommended, Name A-Z, Name Z-A, newest first, oldest first and most stars. The choice stays while searching and reloading.
- The texts added in 1.0.35 and 1.0.36 are correct in all six languages again; some had lost their accents and non-Latin characters.

## 1.0.36

- Immersion Life: the chatter settings are now on the City population tab. Chat range, conversations, player reactions and the chat timings of legendary residents, beggars and buyers are together there.
- Fishing and PvP have their own chatter cards on their tabs.
- Cities and towns: the quest hubs and their controls are gone from the page. The normal playerbots do the questing; the module's own quest system stays off.
- Custom city characters: every custom location the module ships with remains on the page, per role.
- The legendary residents' config line for the yell chance is a proper option again instead of a broken line.
- No module defaults are changed by the page.

## 1.0.35

- Immersion Life: the setup page now shows only the settings worth changing, in six tabs: city population and time of day, movement and life, every city and town, custom city characters, fishing and PvP.
- Cities and towns: every hub has its own switch and population; quest hubs also have questing and their level range.
- Custom characters: legendary residents, beggars, buyers and training scenes can be switched on, and their population is set per city.
- Fishing: every fishing city has its own switch and population. PvP: every zone has its own switch and population range.
- The raw *All settings* tab is gone. Uncommon internals stay in the config file. No module defaults are changed by the page.
- All new labels are available in the six languages.

## 1.0.34

- Immersion Life: the setup page now has an All settings tab. Every option of the module is generated from its config template, grouped by area, searchable, with its description and the right control for switches, choices and values.
- No module defaults are changed by the page; every default stays as the module ships.

## 1.0.33

- Chatter quality: the automatic variants are gone. The auction bots again use only the 17,721 hand-written lines, and the generated 6,000 Immersion lines and 800 conversations were removed.
- The auction/Immersion databases are cleaned of the generated rows. The hand-written conversations and the level 60 buyer lines stay.
- The installer no longer applies the generated chat catalogs.

## 1.0.32

- Auction house bots: the spoken catalog grows to 40,000 lines and gains 1,000 short conversations between two or three nearby bots (auction life, prices, loot, deaths, guilds and city gossip).
- Immersion Life: 6,000 new ambient lines for legendary residents, beggars, buyers and fishers, plus 800 three-step conversations between nearby residents.
- Immersion Life: buyer advertisements now fit the level 60 realm (55-60 greens, endgame goods) instead of level 70-80.
- Immersion Life: the database installer applies the two new chat catalogs automatically.

## 1.0.31

- Auction house bots: the setup page has a Loot filter tab with the module's built-in filter: on/off, lowest quality, food and drink, other consumables, excluded item ids and excluded name parts.
- Immersion Life: the shared guild of the legendary residents is now "AFK Realm". Existing configurations that still say "The GOATS" are moved to the new name, and an owned "The GOATS" guild is renamed instead of leaving it behind.

## 1.0.30

- Server build: module patches are applied as normalized LF text, so a Git checkout that turns a `.patch` file into CRLF no longer stops the build with "corrupt patch". The update check tests the same normalized text.
- Azeroth flying: the module's patch file is kept at LF in Git (`.gitattributes`), so a fresh checkout no longer breaks the build before the patch is applied.

## 1.0.29

- Immersion Life: the database button now starts the portable database when it is stopped, applies the module's WORLD and CHARACTERS files and leaves the database running. No manual start is needed before the first installation.

## 1.0.28

- Azeroth flying: the module now builds on the current CoA core again. `AscensionMountSpeed.h` declares `IsNoFlyArea`, so `AzerothFlying.cpp` no longer stops with C2039/C3861 during a server build.
- Azeroth flying: mounts whose spell is marked "flying areas only" (most store, achievement and holiday mounts) can now be summoned in Eastern Kingdoms and Kalimdor as well; the core's location check treats the old world as flyable while `AzerothFlying.Enable` is on.
- Immersion Life: the database installer also applies `world_city_life_quest_zones.sql`, so the quest hubs are complete on existing installations as well.
- Immersion Life: the module card no longer calls the module a local test. It names the defaults correctly: PvP Life and Fishing Life off, legendary residents on.

## 1.0.27

- Database editor: dark mode now themes the tab strip, tree, input fields, table headers, row headers and scrollbars too; no white controls with unreadable text are left.
- Immersion Life: the module is now part of AFK Realm's own modules. It brings cities and towns, questing, PvP Life, Fishing Life and legendary residents for playerbots. PvP Life and Fishing Life are off by default; the legendary residents stay on.
- Immersion Life setup page: the shared city budget, the main cities, questing, legendary residents, PvP Life and Fishing Life, plus a one-time installer for the module's database tables.
- Azeroth flying: the module now fits the CoA core with `CoA.LegacyMountFlight`. Leave that option at `0`; the module keeps the core's normal mount rules and adds Eastern Kingdoms and Kalimdor only.
- Auction house bots: a compatibility patch for the Zyth45 Playerbots 2.7 branch and the current CoA core, so Warcraft Reborn bots keep their Mystic Enchants and the server compiles.

## 1.0.25

- Game client: the long-path warning now names the real cause. `Ascension.exe` is not long-path aware, so custom textures can be missing anywhere, not only for Haranir and Esteria Vulpera. A short client path such as `C:\CoA` is the reliable fix.

## 1.0.24

- Custom races: an imported race can no longer take a race number that belongs to another race of the client. A Gnoll saved under 63 is moved to a free number, and the client patch refuses to overwrite Nightborne with it; CoA Custom's Gnoll stays 83 (its Esteria look-up number remains 63 internally).
- Game client: warns when the game folder path is long. Haranir and Esteria Vulpera load many files below the client and need a short path such as `C:\CoA`.

## 1.0.23

- Custom races: the separate “Server data” card is gone. CoA Custom and imported race packs bring their racial rows without choosing another server folder; the setup page now only has the pack folder and the module switch.

## 1.0.22

- Custom races: the separate exe and `dinput8.dll` pickers are gone. Choose the CoA Custom folder once (or let AFK Realm find the usual `Downloads\CoA-Custom\CoA-Custom`), and it takes the exe and the 64-race DLL of that pack automatically. Only the module switch remains.

## 1.0.21

- Custom races: choosing the client_root now finds `Ascension.exe` or `Ascension-b.exe` and the 64-race `dinput8.dll` of the same CoA Custom pack by itself. The separate pickers stay as a fallback for unusual folder layouts; normal users no longer have to choose three files by hand.

## 1.0.20

- Custom races: CoA Custom 1.8.3 renamed its patched game exe back to `Ascension.exe`. The separate exe picker now accepts both `Ascension.exe` and `Ascension-b.exe`, and the client patch copies the chosen file under its own name. The races setup page warns when the client-root folder has neither, and the 64-race `dinput8.dll` is still taken from `files\client` of the same pack.

## 1.0.19

- Custom races: the client-root folder can lack `Ascension-b.exe` (for example when it holds only the Esteria files). The setup page now warns about it and offers a separate picker for `Ascension-b.exe` (`CustomRaces.ClientExe`). The client patch copies that exe next to the game even when the root folder does not contain it.

## 1.0.18

- Module manager: the "Modules by AFK Realm" section no longer squeezes the AzerothCore module catalog out of the window. It now has a resizable split area with its own scrollbar; the catalog always keeps room.
- Azeroth flying: a setup page for mod-azeroth-flying with the master switch, the no-fly area list and the warning that old Azeroth was not built for flying.

## 1.0.17

- Modules by AFK Realm: **mod-azeroth-flying** is listed. It lets mounts with a flying effect fly in Eastern Kingdoms and Kalimdor when `AzerothFlying.Enable` is on (off by default). The old-world capitals and Teldrassil are a configurable no-fly list; flying into one dismounts the player. The module warns that old Azeroth was not built for flying.

## 1.0.16

- Client patch: the row limit of a module now counts only the rows the module really adds. The CoA client carries 31 class rows for Troll 8; *Modules → Game client → Install* no longer stops with "DBFilesClient\CharBaseInfo.dbc would have 31 rows for 8, more than the 30 the game takes". The stock rows stay as they are.
- Installation and server consoles: long lines wrap instead of running off the right edge. The detail log of an install/update and the two server consoles now wrap their text, so the horizontal scrollbar is gone.

## 1.0.15

- Custom races: the archive of an imported race is now built from the files that are really in its race folder, not from the cached list in race.info.json. A pack that was still being copied when the setup page first read it no longer leaves the archive with only its stamp (Grummle showed a blue cube). The stamp covers the real files, so a changed race folder rebuilds the archive.
- Custom races: the race folder importer now offers only player races, keeps races with the same name apart by their file name or side (Vulpera Esteria, Earthen Alliance/Horde), matches existing packs by source number, file and name, and finds their icons under more names. The compositor fallback for Esteria races now runs when the pack is made and chooses a donor on the same model by prefix, name and side; old compositor-only packs need *Create race packs* once more.
- Modules: the update check no longer announces an update that the engine would always skip because the module has local changes. When the local changes are exactly the upstream commit's contents, only the recorded HEAD is moved forward.

## 1.0.14

- Custom races: the importer now borrows skin, face and HD layers from a donor race in the target client when a pack has compositor-only sections (old packs or races without a donor in the source client). This makes the import robust even when packs were created before the compositor fallback was added.

## 1.0.13

- Custom races: races of the CoA client that share a name (Vulpera 19 and VulperaEsteria 74) are now listed separately in the setup page, with keys taken from their file names, so both can be chosen and configured independently.

## 1.0.12

- Custom races: generic fixes for CoA Custom's Esteria races. Race packs whose sections are compositor-only (Esteria races that paint through EsteriaAppearance.dll) now take the skin, face and HD layers of another race on the same model, so they show their own skin instead of neutral placeholders. Icons are found under more names, helmet prefixes are read from the pack's files, and races sharing a name (Vulpera 19 and VulperaEsteria 74) no longer collapse into one pack. MaleOnly detection now accepts a race that brings only one section row per style.
- Labels of the character-creation style buttons are taken from the source race's row.

## 1.0.11

- Custom races: a race of the race folder made from CoA Custom keeps CoA Custom's number when the file name of its row there matches (DrakonidHorde, DrakkinAlliance), not only when its own file name does (HighmountainTauren). The Drakonid and the Drakkin took free numbers instead of 98 and 99, so EsteriaAppearance.dll, which paints their skin, knew nothing of them and they stayed white. Race packs keep that file name (race.info.json is made anew); a race already on under another number keeps it until its number is freed

## 1.0.10

- Race packs take more of what a race needs from the other client: its helmet models and every other item part made for its prefix (Item\...\*_<prefix><M|F>.m2 / .skin / .blp, with the skins and textures the models name - without them every helmet of the race was a checkered box, at the character creation too), its display textures (TextureVariation 1-3, which texture slots of the model take: horns, eyes, a blindfold - they are written into the race's display instead of the base race's), every folder under custom\ named after the race (custom\drakonid\ for the Drakonid, wherever its model lies), the whole folder of its model where the player's client has nothing in that folder (Character\IllidariNightElf\ for example; never a folder of a stock race), and the sounds of its own voice the player's client lacks (their rows of SoundEntries.dbc and their files, so its emotes keep its voice). A race whose pack brings helmets keeps them in the game (CustomRaces.<race>.OwnHelmets). Race packs made before need *Create race packs* once more (reported on Discord)
- Custom races: a race without helmet models of its own wears those of its base race, as CoA Custom does it (its races take the helmet prefix of the closest stock race; only the Goblin has helmets of its own). AFK Realm writes CustomRaces.<race>.HelmetPrefix with the prefix of the race it leans on, and mod-custom-races gives the race that prefix in the client patch (imported races as well); the race keeps its helmets visible in the game. Before, every helmet of such a race was a checkered box, at the character creation too, and the module hid them in the game
- Custom races: the texts name CoA Custom's patched exe as it comes - Ascension.exe in CoA Custom's own packs (AFK Realm keeps the old one as Ascension.exe.afk-backup), Ascension-b.exe in some others - instead of always Ascension-b.exe

## 1.0.9

- Archives: the files of an archive AFK Realm writes (the client patch, the archives of imported races) are compressed with zlib in sectors of 4096 bytes, as the game's own archives are; a file that would not get smaller is stored whole. They were all stored whole, so the archive of a race with many textures grew past the 2 GB the game can read and the install stopped ("The archive patch-ZY24.MPQ.new would be larger than 2 GB", reported on Discord). Archives already installed stay as they are until they are built again

## 1.0.8

- Custom races: the racial rows AFK Realm writes for each race (Server\configs\modules\custom-races-racials\<race>.sql) write the line breaks of a spell text as \n. They were written as they are, so a racial whose description had a line break split its statement over two lines; mod-custom-races runs one statement per line, the database could not read the halves, and the core stops the whole worldserver on such an error - right after "mod-custom-races: ... voice(s) of their base races lent to ... race(s)" (reported on Discord). mod-custom-races now joins such lines itself and leaves out a statement it cannot complete, so files written before start as well
- Server start: when the worldserver stops while starting, the message shows the last lines of Errors.log as well. The core writes the reason of a stop of its own (a database error, an assertion) there, not into Server.log, so the message only showed the last normal line before

## 1.0.7

- Custom races: *Races of CoA Custom's game keep their own numbers (up to 127)* (CustomRaces.HighRaces). CoA Custom's EsteriaAppearance.dll knows its races only by the numbers CoA Custom gave them (Highmountain Tauren 66, Mechagnome 67, Earthen 68/69, Haranir 70/71 …, up to 103), so with AFK Realm's free numbers they had only the stock choices of looks. Switched on, a race of the client keeps its own number above 31 (ClientRace.KeepsNumber), a race of the race folder made from CoA Custom takes CoA Custom's number while it is free (checked against files\dbc\ChrRaces.dbc of the pack), and CustomRaces.<race>.Appearance tells the server which races keep a sixth byte (66, 68, 69) or a 64-bit number (70, 71) of looks. A race played under another number keeps it. Needs the CoA Custom folder (its Ascension-b.exe); the ZIP's READ ME tells the players to start Ascension-b.exe. Race archives may have three digits (patch-ZY103.MPQ)

## 1.0.6

- Client patch: CharSections.dbc and HDCharSections.dbc are written sorted by race, sex, section, variation and colour (rows and ids unchanged). The client counts the rows of a race, sex and section where they lie together; rows of one group spread over the table - imported races' HD rows taken from the nearest style, copies of a client race - made it write past what it counted, which crashes the game at its exit (found and fixed the same way in EsteriaWoW's race guide, where CoA Custom's races come from)
- Archives: an archive AFK Realm writes (the client patch, a race archive) may be 2 GB at most, not 4 GB: the game reads archive offsets as signed numbers and fails past 2 GB
- Game client ZIP: READ ME FIRST.txt says that the game folder must be writable when EsteriaAppearance.dll comes along (it keeps the looks of the races in Interface\AddOns\EsteriaAppearanceCache)

## 1.0.5

- Custom races: the racial abilities of CoA Custom's races. CoA Custom has them compiled into its server (CoaCustomRacials.h), not in a file of its pack, so they were never found. mod-custom-races now carries that table (races.json "coaCustom"); a race gets the racials CoA Custom gives its race number (a race of the client: its row; a race of the race folder: the number it had in its client). The rows of the spells come from the server data folder, else from the race pack (made anew from CoA Custom's client, it carries them) or the game client, as plain spell_dbc rows; they go into Server\configs\modules\custom-races-racials (CustomRaces.RacialsFolder). The code of the racials Spell.dbc alone cannot express is CoA Custom's own, taken over by mod-custom-races; AFK Realm writes CoA Custom's spell rows, script bindings and procs of them (mod-custom-races data\coa-custom\racials.sql) into the file of each race, the spells their code casts besides included. Where a CoA Custom folder is chosen (Files of CoA Custom next to the game), its own rows (files\sql\10_esteria_racials_*.sql) win over that copy, so a newer CoA Custom brings changed rows along by itself. A racial that still needs code is named on the race's card
- Custom races: "Files of CoA Custom next to the game" copies the game's exe as well. CoA Custom 1.8 ships its own Ascension.exe in client_root, and AFK Realm left it out because it never replaces the game itself for a module; for the folder the user chose it does now, keeping the old exe as Ascension.exe.afk-backup (it comes back with *Copy nothing*). The realm list is still never replaced (reported by Feles Noctis)
- Game client ZIP: the files of CoA Custom next to the exe (its client_root: Ascension.exe, EsteriaAppearance.dll with its .bin files, cursors) go into *Export as ZIP …* as well, next to Data\ and Interface\AddOns, so the other players unpack one ZIP into their game folder and have everything (mod-custom-races' "zip": true; a module can still keep files out with "zip": false). READ ME FIRST.txt names them, says to keep a copy of one's own Ascension.exe, and to allow the .dll files in an antivirus program
- Backups: *Restore only parts …* takes just the accounts, the characters (with the Playerbots database) or the settings from a backup and leaves the rest of the server as it is; *From another server …* does the same with the backup folder of another AFK Realm server. The realm list stays this server's; restored settings get this server's database connection and folders again; the restored databases are brought up to this server's version (dbimport and the module SQL files) afterwards, so a backup of an older server works too. The engine mode is RestoreParts (-Snapshot <folder name or full path> -Parts accounts,characters,settings) (suggested on Discord)
- World Journey page: the card *Dungeons alone* (Journey.SoloDungeon.*: on/off, health, melee and spell damage of creatures and bosses in dungeons entered alone)
- Long server folders: a new server in a folder longer than 24 characters builds through a short junction (<drive>\afk\<8 characters>) that leads to its Dependencies folder, because Boost's installer, the compiler and MSBuild fail on paths over 260 characters even with Windows' long paths switched on. A server already built at its long path keeps it. The folder may now be up to 120 characters long (was 60)
- Manastorm return pads: the CoA core spawns Manastorm's Orgrimmar Teleport Pad as gameobject 6901536, and its Basalthane raid updates delete 6901536-6901538 again, so on a fresh server manastorm_hub_return points at nothing and Manastorm does not load ("hub returns require exactly two valid rows … valid resolved rows=1", "Manastorm datastore failed to load"). After the database updates, a return pad that is missing is spawned again under a free guid with the values of Manastorm's own SQL and Manastorm is pointed at it (Comment 'AFK Realm: Manastorm return pad'). Nothing happens while the core's pads are fine; once the core brings its own again, the pads AFK Realm added are taken out (reported on Discord)
- Removing a module: what a module with "ledgers" changed in the world database while the server ran goes back before its folder is gone for good. The ledgers are remembered in afk_modules.ledgers, so a module whose folder disappeared another way has its originals put back at the next server update, too. Before, removing World Journey left the levels of items, creatures and quests as the journey had set them

## 1.0.4

- Client patches: "{height:RACE:SEX}" in a script of the patch becomes how tall the model of that race stands, in yards - its vertices in the standing pose (the box a model file carries can hold all its animations) times the scales of its display and model; imported races are measured from the race folder. mod-custom-races sizes the camera of the character creation by it, so a race much taller than its base race (the Highmountain Tauren) is no longer half out of the picture

## 1.0.3

- Server update: the module patches are taken out and put in again before every build, which gave every patched file a new time - with patched headers of the core the compiler then built nearly the whole server again, even for a change of one file. A patched file whose content is the same as at the last build now gets its old time back (Dependencies\patched-files.txt), so only what really changed is compiled

## 1.0.2

- Dark look: the group headings of lists (the sections of the server settings) are written light instead of in Windows' dark blue, lists and their column headers take the dark style of Windows, a selected row is drawn in the colours of the look, and writing in the accent colour is lighter (reported by SimpleGeekAce)

## 1.0.1

- Server update: a module that changes rows of the world database while the server runs and keeps their originals in tables of its own names them in its manifest ("ledgers"); before the database updates AFK Realm puts the originals back where the module's value is still in place, so the updates of the core see the stock rows they were written for, and the module takes its changes up again at the next start. With World Journey, CoA's new Manastorm caches (mod-manastorm-endless) stopped the update with "pool count mismatch": the journey had moved the required level of Mythic dungeon gear from 60 to its own range, and the update looks for that gear at level 60

## 1.0.0

- Light and dark look: the sun/moon button next to the language button switches between *Automatic (as Windows)*, *Light* and *Dark*. Every open window changes at once, without a restart; on *Automatic* AFK Realm follows Windows when it changes there. The choice is kept in `%APPDATA%\AFK-Realm\theme.txt`. Dialogs, lists, tables, drop-down lists and the database editor follow the look; the window title no longer says "(preview)"
- Custom races: "Files of CoA Custom next to the game" on the races page. Choose the CoA Custom folder once (the pack, its `files` folder or `files\client_root`); installing the client patch copies every file of `client_root` (Ascension-b.exe, EsteriaAppearance.dll with its .bin files, cursors) next to the game. A file of the same name that was there before is kept as .afk-backup; a file already there, the same, is left in place when it is no longer wanted; *Copy nothing* takes the copied files away and brings the backups back. They stay out of the ZIP (the players have CoA Custom's client themselves). The 64-race patch of the same pack is taken when none is chosen yet
- Client patches: "rootFolders" puts every file of a folder of the user's own next to the game's exe (with "zip": false they stay out of the ZIP for the players); "rootFiles" and "rootFolders" remember files that were there already and leave them in place
- Custom races: racial abilities of their own for races of a server pack. AFK Realm writes the rows of their spells from the pack's server data folder for mod-custom-races, which teaches them at login and takes them back when the race goes off. New races speak with the voices of their base race (mod-custom-races)
- Race packs: the importer reads the names of a client's archives from their (listfile) and its folders, finds every file a race needs wherever the client keeps it, and lists what is missing
- Game folder: the game folder is found behind the "Data" folder, a folder inside it, or a repack unpacked into a folder of its own
- Backups: the number of server backups to keep can be chosen in the backups window (1–99, 3 by default)
- GitHub token (optional) on the modules page: 5000 requests an hour instead of 60 for the module list and the update check, kept encrypted for the Windows user; an outdated token is dropped for the session instead of costing the anonymous requests
- `tools/lang.py --module` builds again (System.Security was missing for the module texts)

## 0.9.6-preview

- Setup pages: when a saved change needs the game client (new races, other classic classes, World Journey's map levels), the game client window opens right away – every time, not only once – and says the three steps: Install, restart the server, give the other players the new ZIP

## 0.9.5-preview (in progress)

- Languages: AFK Realm speaks English, German, French, Spanish, Portuguese (Brazil), Russian and Simplified Chinese. It starts in the language of Windows (English when there is none for it) and the language button at the top right of every page switches it; AFK Realm then restarts itself, running servers keep running. "Add a language …" in the same menu writes a file with every text of AFK Realm into `%APPDATA%\AFK-Realm\lang` to translate; files there (or in a `lang` folder next to the exe) are picked up at the next start and can also replace single texts of a built-in language. Chinese uses Microsoft YaHei UI
- Languages of the modules: a module can bring `lang/<code>.json` with the texts AFK Realm shows of it (names and reasons in afk-realm.json, its option descriptions, races and zone names); the four modules by AFK Realm carry all six languages, zones and dungeons with their names in the game's own language
- Messages of the engine (steps of an installation, errors, module notes) are shown in the language of the window; the log stays English, and so do announcements in the game, file names and the batch file of the map data. The READ ME in the ZIP for other players is written in the owner's language and in English
- Settings: an option described in a block of its own after another option's description (`CustomRaces.MoreRacesFile`) no longer shows both descriptions
- Setup pages: a choice whose buttons do not fit beside its label goes below it; bots page and game master tools make room for longer texts
- `tools/lang.py` lists, checks and merges the texts of AFK Realm and of a module (`--module <folder>`)
- Update check: before an update, the patches of the installed modules (in their newest version) are tried on the new CoA core and mod-playerbots; one that would not fit is named in the banner and in the question before the update

## 0.9.4-preview

- Several servers: the top of the side bar shows the server you look at (its realm's name) and opens the list of every server opened in AFK Realm, to switch between them with one click; "Add an existing server …" and "Install a new server …" are there too, and a server can be removed from the list (its folder stays). Switching away from a running server leaves it running

## 0.9.3-preview

- Custom races: after new races were made playable on the races page, AFK Realm offers to make the random bots anew, since the bots keep the races they were made with and only new bots can be of the new races

## 0.9.2-preview

- Custom races: "Create race packs from a client …" on the setup page reads the races of another client or repack (a game folder, or a folder with its patch archives such as the client folder of CoA Custom) and puts the ones you tick into the race folder: their rows, the files your own client lacks and their icon. The other client is no longer needed afterwards. Texture names of models are read in full (some were cut or padded before)
- Custom races: more than 31 races with a 64-race patch of the game client (the dinput8.dll of CoA Custom, kept in the race folder when you make race packs from it). On the setup page under "More than 31 races"; AFK Realm puts the DLL next to Ascension.exe when you install the client patch, keeps a DLL of the same name that was there as .afk-backup, takes it away again when the option is off, and adds it to the ZIP. Races above 32 do what their base race may do (core patch of mod-custom-races: races without a bit of their own use their base race's bit in race masks)
- Client patches: "rootFiles" puts a file of the user's own next to the game's exe while a setting asks for it
- Custom races: races of the race folder are sorted into the groups by looks like the others, not into a group of their own; a race of the folder leans by default on a race of the side it had in its client
- Custom races: the race folder found by itself (Downloads\AFK-Rassen) is saved, and a race whose folder is missing stops the build with a message instead of being left out unseen. Building the client patch with imported races takes seconds instead of minutes. The race columns of the character creation are further apart so their names no longer overlap
- The game archives AFK Realm reads may hold encrypted files
- No more frozen windows with large races: checking the game client, installing and the ZIP run in the background with a window that says what is going on and how far it is (each race archive file by file); the main window checks the game client in the background after a setup page or the game client window closes; the races page reads the game client and the race folder before it opens, with a progress window
- Custom races: a skin or face of a race from the race folder that has no HD row of its own (some skin colours of CoA Custom's Undead Gnome) takes the HD row of the same style in the nearest colour; before, its old texture landed askew on the HD model (eyes in the wrong place)
- Bots page: races added by a module show their names (not "Race 23") and count for their faction in Alliance : Horde
- Faster: a race of the race folder keeps a small summary beside its race.json (race.info.json), so its large race.json is read only for a build; the races of the game client and the client patch to compare with are worked out once and kept until the client, a setting or a module changes

## 0.9.1-preview

- Client patches: "setRows" changes fields of the rows of a DBC table that match (ChrRaces.dbc: a race of the client made playable for a faction); "cloneRows" can "replace" the row a copy would duplicate; "addRows" takes a "maxPerRace" limit (the character creation of the CoA client takes 30 classes per race in CharBaseInfo.dbc)
- Modules: a module that writes rows while the server runs brings SQL in data/sql/remove that takes them back; AFK Realm runs it when the module is removed, before the recorded changes are undone
- New module under "Modules by AFK Realm": mod-custom-races - the races the game client carries but no player can choose (in the CoA client: Goblin, Worgen, Pandaren, Vrykul, Naga and more, 23 in all) playable for players and bots, each leaning on a stock race of your choice for its start, quests, racial abilities and faction; with a setup page (Modules → Custom races) that reads the races from your own game client - after an update of the game too - and hands out its free race numbers; a race without helmet models of its own keeps helmets hidden, for players and bots
- Custom races from other game clients: a race folder (one folder per race with its rows in race.json and its files) is offered on the setup page next to the races of your client; a race of your client can take the looks of the race folder instead. The rows go into the client patch, the files into an archive of their own for each race (Data\patch-ZY<number>.MPQ), built once and kept while the race is on, and into the ZIP for the other players. Every race gets its icon on the race choice when the race folder has one
- Custom races: the character creation of the Ascension client no longer crashes or paints wrong textures on added races: every skin, face, hair and underwear row is usable, a face and an underwear for every skin colour, women's rows for races that only have men, the rows of a race in one block, HD rows on the ids of their plain rows, empty textures no longer drawn as the Human skin (found by ilusixn for CoA Custom)
- Client patches: "importRace", "sectionRules" and "addFile" for modules that bring races
- Client patches: "copyRows" with "idGroup" and "idKey" gives copied rows the same new ids in tables that pair by id (the HD tables of the Ascension client: HDCharSections row n is the HD version of CharSections row n)
- Client patches: an edit or a "fill" with "forEach" stands for one per word of a setting, with "{each}" replaced by the word ("{conf:KEY}" in a fill line: the value of a setting); a setting can give a text ("text": true)
- Client patches: a file or edit can depend on server settings ("when"), a number of an edit can come from a setting, "fill" takes several markers and {words} and {text}, "copyRows" copies rows by field (with new ids), and "setRows"/"copyRows" can write texts
- A copied or moved server folder (a test copy of the server, say) builds again: the build folder still named the old place and CMake refused it; it now starts afresh there. A build that fails while it is prepared no longer blames a module that was just added

## 0.9.0-preview

- New: the Bots page (side bar, under Server). How many random bots there are and how many are in the world, the players in the world, the average level and Alliance : Horde; then the bots by faction, class (with the names the game shows for CoA's classes), race, level band and zone (the fifteen zones with the most bots, the rest together). It counts the bots in the world or all bots, reads the database in the background and refreshes every 30 seconds while the page is open

## 0.8.4-preview

- Client patches: "fillRows" fills the empty rows of a DBC table from a sibling row (CharStartOutfit.dbc: an outfit that only shows armour takes the items of the same class and sex of another race of the same faction)
- Client patches: "cloneRows" copies rows within a DBC table with some bytes changed and a new id (for tables with byte fields such as CharStartOutfit.dbc); "addRows" takes a "maxRows" limit and stops with an error instead of building a table the game cannot hold, and keeps only the first of rows a table holds twice
- mod-classic-classes: the character creation no longer crashes. The CoA client holds CharBaseInfo.dbc in a list of about 300 rows; the module now keeps it at 300 (the Death Knight and the Hero, who cannot be created next to the classic classes, make room) and gives the Orc Druid the start outfit the client lacks. The new pairs wore a showcase of high level armour in the character creation and started without gear; they now wear and get the start outfit of their class

## 0.8.3-preview

- Client patches: "dropRows" reads the fields of a DBC table in the width the table stores them in (CharBaseInfo.dbc of the CoA client has a byte per field); before, it read four bytes and failed on such a table. mod-classic-classes needs it: the character creation of the client crashed with all 32 classes for a race, so the module takes out the Death Knight's rows

## 0.8.2-preview

- Client patches: a module can add rows to a DBC table of the client ("addRows": every combination of the value lists in "cross", one list per field; rows the table has are skipped, the field width of the table is kept). mod-classic-classes uses it to let every race choose every classic class in the character creation (CharBaseInfo.dbc)

## 0.8.1-preview

- Update check: whether the CoA core, Playerbots, a module or AFK Realm has something new is asked of Git (git ls-remote), which GitHub does not limit. Before, the check used only the GitHub API, which answers 60 questions an hour without signing in; after many starts it stayed silent and no update was shown. The API now only adds how many changes there are; when it refuses, the banner says "new version" and from when GitHub counts again. All modules are checked, not only the first ten
- A newer AFK Realm found while GitHub refuses opens its release page instead of updating in place
- The update notes sit under the server buttons now, as wide as the window, and the bar grows with them; before, they stood right of the status and ran out of the window. The server note names the parts with news ("CoA core, Playerbots and 3 more"); the whole list with the number of changes is in its tooltip and in the question before the update
- An update check that answered before the window was ready was lost; it now waits for the window

## 0.8.0-preview

- New module under "Modules by AFK Realm": mod-classic-classes - the classic classes (Warrior to Druid, without the Death Knight) next to the classes of CoA, for players and the random bots, played the way CoA plays Ascension's Warcraft Reborn classes; with a setup page (Modules → Classic classes) for which classes can be created
- New: client patches. A module can change files of the game client in its afk-realm.json ("client": {"patch": ...}): files of its own, lines added to text files of the client, rows taken out of DBC tables. AFK Realm builds one archive, Data\patch-Y.MPQ, from the player's own game client (so it never hands out files of the game), builds it anew when the client, a module or a setting it reads has changed, takes it out with its module and puts it into the ZIP for other players with a note on the client version it fits
- New: a module can ask for new random bots ("bots": {"reset": true}); AFK Realm then offers once, after the module came, to reset the random bots
- "Game client add-ons" is now "Game client" and also lists the client patch; after a setting on a setup page changed, the window opens by itself when the client patch has to be built again
- The module check no longer takes Lua files under a module's client/ folder for Eluna scripts, and no longer warns about client files a module made for AFK Realm brings itself
- World Journey setup: the Extra hard text names only the values coa.conf holds; the manual describes CoA's creature multipliers without fixing their values

## 0.7.3-preview

- World Journey setup, Difficulty: an "Extra hard" switch next to the presets. It turns CoA's own creature multipliers on (CoA.CreatureScaling.Enable in coa.conf, since October 2026) on top of the chosen level - open world creatures 2.5x health and 2x damage against players and pets, dungeons 2.5x health and 1.5x damage, with the values read from coa.conf. World Journey switches them off by default; "Reset to defaults" does too
- Setup pages can write options of a second config file and save them together with the module's

## 0.7.2-preview

- World Journey setup: the switch "Every character plays the scaled world" now says what it does with the question at character creation (it still appears, but "off" no longer counts) instead of pointing to the Destiny Weaver

## 0.7.1-preview

- Fix: the module check said a module needs Eluna when it only brings an add-on for the game client (World Journey); Lua files next to an add-on's .toc no longer count as Eluna scripts

## 0.7.0-preview

- New look: the server management is now a window with a side bar (Server, Settings, Modules, Game master, Accounts, Database, Maintenance) instead of one long page; the state of the database, login and game world and the Start/Stop/Restart buttons stay at the top on every page
- New look everywhere: rounded buttons, switches, sliders and cards in AFK Realm's colours, every window with the same header, lists and tables with flat headers, the installation steps on a white sheet, flat progress bars
- New: setup pages for the modules by AFK Realm (Modules → World Journey / Auction house bots), shown only when the module is installed. Sliders, switches and choices instead of numbers in a list; they write the module's own .conf file, so editing the file by hand keeps working
- World Journey setup: where each part of the world begins (with a picture of the journey from 1 to 60), the endgame, every zone and dungeon with its level on the journey and the option to move one by hand, the level window with a preview, difficulty presets and multipliers by rank, also for one part of the world, and what follows the journey
- Auction house bots setup: who trades, trips and pace, what is sold and for how much, buying, crafting, deals by chat and chatter, and the Trading Post
- Fix: AFK Realm closed right after starting when a module had a new add-on for the game client waiting
- New module under "Modules by AFK Realm": mod-world-journey - the old world, Outland and Northrend as one journey from 1 to 60, for the classes of CoA
- New: game client add-ons (Server management → Game client add-ons). A module can bring an add-on for the game client in its afk-realm.json ("client"); AFK Realm asks once for the game folder, copies the add-on into Interface\AddOns, clears the client's cache when the module asks for it, updates the add-on when the module changes and takes it out when the module is removed
- After a module brought, changed or lost an add-on, the game client window opens by itself once; the management page says when the game is not up to date
- "Export as ZIP …" packs the add-ons for other players, with a note on where to unpack them and which folder to delete
- Nothing is copied while the game or its launcher runs from the chosen folder
- The module check shows when a module brings a client add-on
- Server settings: section titles keep "the" in lower case ("The Shape of the Journey")

## 0.6.6-preview

- New: scheduled restart (Server management → Scheduled restart). The server can be restarted on its own after it has run for a number of hours, every day at a set time, or when the worldserver uses more than a set amount of memory
- Players are told in the game beforehand (at the chosen number of minutes, then at five and at one minute); then the server is stopped cleanly and started again, as with "Restart server"
- The management page shows how long the worldserver has been running, how much memory it uses and when the next restart is due; an announced restart can be postponed by an hour
- Every scheduled restart is written to logs\scheduled-restart.log
- The restart is done by AFK Realm itself, so it only happens while AFK Realm is open; nothing is handed to the server that would shut it down with nobody there to start it again

## 0.6.5-preview

- New: database editor (Server management → Database editor): browse the server's databases and tables, filter rows with a WHERE condition, edit cells, add and delete rows (written only on "Save changes", in one transaction), and a SQL tab that runs any statements and shows the results
- Database editor: "Import SQL file …" runs a file after saying what it does (tables replaced, created, rows deleted or written); the database is only asked for when the file does not name it. "Export …" writes the ticked tables or whole databases (structure and rows) to one SQL file
- Database editor: "Undo last change …" takes back the last save in the Table tab (the old rows are restored), the last statements of the SQL tab or the last import (the tables they name are copied beforehand to Backups\\undo; newly created tables are removed); the last ten changes are kept
- Database editor: every change while the worldserver is running (save, SQL, import, undo) first warns and offers to stop the server; "Yes" stops it cleanly and then carries out the change
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
