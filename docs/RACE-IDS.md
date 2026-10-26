# CoA Custom race IDs

This table is a reference for **CoA Custom 1.9.2**. It is not a second setting: AFK Realm always reads the real
`ChrRaces.dbc` of the selected client. In the app, **Custom races → Show race IDs …** prints the authoritative table
for the current client and realm, and can copy it.

## Two different numbers

- **Source row** — the row in the client's `ChrRaces.dbc`. A race family, model, client patch and Esteria appearance
  are tied to this row.
- **Race number** — the number the server and the client patch use for the playable race
  (`CustomRaces.<race>.Id`).

AFK Realm preserves the source row for a custom/Esteria race. For the stock-copy rows in the second table it replaces
the placeholder copy; it does not move the custom race to a free number.

## Stock rows and stock copies

| Number | Client row | Note |
|---:|---|---|
| 1 | Human | stock |
| 2 | Orc | stock |
| 3 | Dwarf | stock |
| 4 | Night Elf | stock |
| 5 | Undead | stock |
| 6 | Tauren | stock |
| 7 | Gnome | stock |
| 8 | Troll | stock |
| 9 | Goblin | stock CoA row |
| 10 | Blood Elf | stock |
| 11 | Draenei | stock |
| 51 | Undead Gnome (`GnomeUndead`) | custom row; source copy |
| 52 | Orc (`Orc2`) | stock copy, not a custom race |
| 53 | Ogre | custom row; source Copy of Dwarf |
| 54 | Dracthyr (Alliance) | custom row; source copy of Night Elf |
| 55 | Eredar | custom row; source copy of Undead |
| 56 | Harpy | custom row; source copy of Tauren |
| 57 | Alpha Goblin | custom row; source copy of Gnome |
| 58 | Dracthyr (Horde) | custom row; source copy of Troll |
| 59 | Void Elf | custom row |
| 60 | Illidari Blood Elf | custom row; source copy of Blood Elf |
| 61 | Illidari Night Elf | custom row; source copy of Draenei |

The **source copy** is what the old importer reported as "57 is already Gnome". The custom race keeps 57; the Gnome
copy at 57 is replaced.

## Further CoA Custom rows

| Number | Race |
|---:|---|
| 12 | Zandalari Troll |
| 13 | Worgen |
| 14 | High Elf |
| 15 | Tuskarr |
| 16 | Kul Tiran |
| 17 | Taunka |
| 18 | Vrykul |
| 19 | Vulpera |
| 20 | Pandaren |
| 21 | Naga |
| 22 | Broken |
| 23 | Fel Orc |
| 24 | Forest Troll |
| 25 | Ice Troll |
| 26 | Skeleton |
| 27 | Earthen |
| 28 | Drakkari Troll |
| 29 | Pandaren (second model) |
| 30/31 | Murloc/Troglodyte rows |
| 32/33 | Forgotten |
| 34 | Fel Orc (Horde) |
| 35 | Naga (Alliance) |
| 36 | Broken (Horde) |
| 37 | Skeleton (Alliance) |
| 38 | Vrykul (Horde) |
| 39 | Tuskarr (Alliance) |
| 40 | Forest Troll (Horde) |
| 41 | Taunka (Horde) |
| 42 | Northrend Skeleton |
| 43 | Ice Troll (Horde) |
| 44 | Drakkari Troll (Horde) |
| 45 | Leper Troll |
| 46 | Troglodyte (Horde) |
| 47 | Alpha Worgen |
| 48 | Dark Iron Dwarf |
| 49 | Mag'har Orc |
| 50 | Furbolg |
| 62 | Lightforged Draenei |
| 63 | Nightborne |
| 65 | Test Race |
| 66 | Highmountain Tauren |
| 67 | Mechagnome |
| 68/69 | Earthen (Alliance/Horde) |
| 70/71 | Haranir (Horde/Alliance) |
| 72/77 | Skyborne (Horde/Alliance) |
| 74 | Vulpera (Esteria) |
| 75/102 | Centaur (Alliance/Horde) |
| 78 | Jinyu |
| 79 | Arakkoa |
| 80/81 | Darkfallen (Alliance/Horde) |
| 82 | Sethrak |
| 83 | Gnoll |
| 87 | Faceless One |
| 90 | Banshee |
| 97 | Saberon |
| 98/100 | Drakonid (Horde/Alliance) |
| 99 | Drakkin |
| 101/107 | Venthyr/Kyrian |
| 103 | Grummle |
| 104 | Slime Giant |
| 106 | Mantid |
| 109 | Ethereal |
| 110 | Sylvari |
| 112 | Hozen |

> The client may carry more rows than these; it may also carry fewer. Use **Show race IDs** in the app rather than
> copying a row from this table by hand. The table explains the common Discord numbers, not a replacement for the
> client.

## What users should do

1. Open **Custom races**.
2. Press **Download CoA Custom …** (downloads all ZIPs, extracts them together) or **Import races …** if they already
   have the folder.
3. Use **Show race IDs** if they need to compare a number with Discord.
4. Switch the races on, save, then install the client patch under **Modules → Game client**.

Do not change `CustomRaces.<race>.Id` by hand for an Esteria/custom race unless you know that its appearance data
follows it. Moving it can break Esteria appearance and existing characters.
