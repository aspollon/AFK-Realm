# RaceSourceBuilder

Local prototype for turning the Esteria race package into inputs the **existing AFK Realm race importer** can read.

It does not replace the importer and it does not change the client or the server.

## Stage 1: race matrix (read-only)

```powershell
python .\analyze.py
```

Reads:

- `RASSENFILES\Races\inventory.json`
- `RASSENFILES\Races\mpq-manifest.json`
- `RASSENFILES\Races\Server\DBC\ChrRaces.dbc`
- `...\CharSections.dbc`, `...\CreatureDisplayInfo.dbc`, `...\CreatureModelData.dbc`

Writes:

- `C:\Users\aspol\Downloads\RaceSourceBuilder\race-matrix.md`
- `C:\Users\aspol\Downloads\RaceSourceBuilder\race-matrix.json`

Current result: 40 races,

- **20 classic-ready**
- **9 need the expanded appearance runtime**
- **11 need a native/creature runtime**
- **0 with missing model files** (male-only display 0 is ignored)

Classic-ready includes Darkfallen, High Elf, Broken, Eredar, Nightborne, Pandaren, Void Elf,
Lightforged, Zandalari, Dark Iron, Dracthyr, Kul Tiran, Illidari, Mag'har and the two Sethrak rows.

Expanded: Vulpera, Highmountain, Mechagnome, Earthen (2), Haranir (2), Skyborne (2).
Native: Naga, Tuskarr, Vrykul (2), Forgotten (2), Ogre (2), Gnoll, Saberon, Jinyu.

## Stage 2: one-race source client

```powershell
python .\build_source.py --source-race 44 --target-race 81 --name Darkfallen
```

The builder reads the official classic Darkfallen rows and assets and writes a **small synthetic client**:

```text
C:\Users\aspol\Downloads\RaceSourceBuilder\Darkfallen\
  Data\patch-ZZZZZZZ.MPQ
  manifest.json
```

The synthetic client contains **only the selected race** in its DBC tables:

- `ChrRaces`: only target race 81
- `CharSections`: only the 1846 official rows, remapped to 81
- `CreatureDisplayInfo` / `CreatureModelData`: 980162 / 980163
- `CharHairGeosets`, `CharacterFacialHairStyles`, `BarberShopStyle`: only the race's rows
- the classic Darkfallen model, skins, textures and portraits

## Using it with the importer

1. AFK Realm â†’ **Custom races**
2. **Create race packs from a client**
3. Folder:
   ```text
   C:\Users\aspol\Downloads\RaceSourceBuilder\Darkfallen
   ```
4. The importer should list **only Darkfallen** because the source client contains only that race.
5. Create the race pack, save, reopen, enable the race and build the client patch.

The importer writes its normal `race.json` and `files` folder. The builder does not invent a second
pack format.

## Limits

- Stage 2 currently targets **classic Darkfallen**. The model/display IDs are still Darkfallen-specific.
- Expanded and native races are only inventoried; they need an appearance/runtime profile
  (`EsteriaAppearance.dll`, `.bin` catalogs, 6-byte/uint64 persistence).
- No client or server files are changed by this tool. It only writes below
  `C:\Users\aspol\Downloads\RaceSourceBuilder`.
- No Git release.

## Next steps

1. Generalize `build_source.py` from the hardcoded Darkfallen IDs to any classic race row.
2. Batch all 20 classic-ready races into one synthetic source client per race.
3. Add the capability manifest fields (`runtime`, `appearance`, `requires`) to the importer contract.
4. Decide whether expanded/native races are a separate client-fork profile or excluded.

## Local Glue test for the eye glow

The race DBCs do not contain the creator's per-file-string `CharModelGlowInfo`
table. The official Esteria packer patches `GlueParent.lua`; this local helper
does the same for the test client:

```powershell
python .\build_glue_test.py
```

It writes:

```text
C:\Users\aspol\Downloads\RaceSourceBuilder\DarkfallenGlue\root\patch-ZZZZZZ.MPQ
C:\Users\aspol\Downloads\RaceSourceBuilder\DarkfallenGlue\locale\patch-enUS-ZZZZZZ.MPQ
```

Copy the root file into `Descension\Data\` and the locale file into
`Descension\Data\enUS\`. They only add:

```lua
CharModelGlowInfo["DARKFALLEN"] = 0.15;
CharModelGlowInfo["DARKFALLENHORDE"] = 0.15;
```

and the matching fog entries. Remove both files to roll back.