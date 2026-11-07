#!/usr/bin/env python3
"""Build a one-race source client for the AFK race importer.

It reads the official Esteria race package, writes a small synthetic client
(Data/patch-ZZZZZZZ.MPQ) that the existing importer can read, and a manifest.
Only the selected race is present in the synthetic client's DBC rows.

Example:
  python build_source.py --source-race 44 --target-race 81 --name Darkfallen
"""
import argparse
import json
import os
import shutil
import struct
import subprocess
import sys
import tempfile

RACES = r"C:\Users\aspol\Downloads\RASSENFILES\Races"
SERVER_DBC = os.path.join(RACES, "Server", "DBC")
DATA_MPQ = r"C:\Users\aspol\Downloads\Data\patch-Z.MPQ"
OUT_ROOT = r"C:\Users\aspol\Downloads\RaceSourceBuilder"
BRIDGE = os.path.join(os.path.dirname(__file__), "mpq-bridge.ps1")


def run_bridge(*args):
    cmd = ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", BRIDGE] + list(args)
    p = subprocess.run(cmd, capture_output=True, text=True)
    if p.returncode != 0:
        raise RuntimeError("mpq bridge failed:\n" + p.stdout + "\n" + p.stderr)
    return p.stdout


def read_dbc(path):
    data = open(path, "rb").read()
    magic, records, fields, recsize, strsize = struct.unpack_from("<4sIIII", data, 0)
    if magic != b"WDBC":
        raise ValueError(path + " is not a DBC")
    start = 20 + records * recsize
    strings = data[start:start + strsize]
    rows = [bytearray(data[20 + i * recsize:20 + (i + 1) * recsize]) for i in range(records)]
    return rows, fields, recsize, strings


def get(rec, field):
    return struct.unpack_from("<I", rec, field * 4)[0]


def put(rec, field, value):
    struct.pack_into("<I", rec, field * 4, value)


def write_dbc(path, rows, fields, recsize, strings):
    with open(path, "wb") as f:
        f.write(b"WDBC")
        f.write(struct.pack("<IIII", len(rows), fields, recsize, len(strings)))
        for row in rows:
            f.write(row)
        f.write(strings)


def filter_table(src, dst, predicate, race_field=None, old_race=None, new_race=None,
                 id_remap=None, field_updates=None):
    rows, fields, recsize, strings = read_dbc(src)
    kept = []
    for row in rows:
        if not predicate(row):
            continue
        row = bytearray(row)
        if race_field is not None and old_race is not None and get(row, race_field) == old_race:
            put(row, race_field, new_race)
        if id_remap:
            old = get(row, 0)
            if old in id_remap:
                put(row, 0, id_remap[old])
        if field_updates:
            for f, v in field_updates.items():
                put(row, f, v)
        kept.append(row)
    write_dbc(dst, kept, fields, recsize, strings)
    return len(kept)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--source-race", type=int, default=44)
    ap.add_argument("--target-race", type=int, default=81)
    ap.add_argument("--name", default="Darkfallen")
    ap.add_argument("--out", default=os.path.join(OUT_ROOT, "Darkfallen"))
    args = ap.parse_args()

    if os.path.isdir(args.out):
        shutil.rmtree(args.out)
    stage = os.path.join(args.out, "stage")
    dbc_out = os.path.join(stage, "DBFilesClient")
    os.makedirs(dbc_out, exist_ok=True)

    # 1. Extract the remaining DBCs from the official Races.MPQ, not from the
    # local Data patch: Data is a different client state and has fewer hair /
    # beard rows than the official model needs.
    temp = tempfile.mkdtemp(prefix="race-source-dbc-")
    names = os.path.join(temp, "names.txt")
    with open(names, "w", encoding="utf-8") as f:
        for n in ("DBFilesClient\\CharHairGeosets.dbc", "DBFilesClient\\CharacterFacialHairStyles.dbc",
                  "DBFilesClient\\BarberShopStyle.dbc", "DBFilesClient\\CharHairTextures.dbc"):
            f.write(n + "\n")
    run_bridge("-Action", "extract-files", "-Mpq", os.path.join(RACES, "Races.MPQ"),
               "-Root", temp, "-NamesFile", names)

    # The official facial table contains several styles whose geosets are all
    # zero. They show up as empty earring/beard clicks in the creator. Keep one
    # real row per sex/style; the matching CharSections rows are filtered below.
    valid_faces = set()
    best_faces = {}
    face_src = os.path.join(temp, "DBFilesClient", "CharacterFacialHairStyles.dbc")
    # The official Darkfallen packer takes its facial/earring rows from the
    # Blood Elf (13), not from the sparse Darkfallen row 44. Race 13 has the
    # real geosets for styles the creator otherwise draws as empty clicks.
    face_source_race = 13 if args.source_race in (43, 44) else args.source_race
    if os.path.exists(face_src):
        face_rows, face_fields, face_size, face_strings = read_dbc(face_src)
        for r in face_rows:
            if get(r, 2) != face_source_race:
                continue
            sex, style = get(r, 1), get(r, 3)
            geosets = [get(r, k) for k in (4, 5, 6, 7)]
            geosets = [g if g != 0xFFFFFFFF else 0 for g in geosets]
            if not any(geosets):
                continue
            valid_faces.add((sex, style))
            best_faces.setdefault((sex, style), (sex, style, geosets))

    # 2. Build the one-race DBC tables.
    # ChrRaces: official row 44 -> target race, displays -> 980162/980163.
    src = os.path.join(SERVER_DBC, "ChrRaces.dbc")
    rows, fields, recsize, strings = read_dbc(src)
    source = next(r for r in rows if get(r, 0) == args.source_race)
    row = bytearray(source)
    put(row, 0, args.target_race)
    put(row, 4, 980162)
    put(row, 5, 980163)
    write_dbc(os.path.join(dbc_out, "ChrRaces.dbc"), [row], fields, recsize, strings)

    # CreatureDisplayInfo: 60028/60029 -> 980162/980163, model id follows.
    src = os.path.join(SERVER_DBC, "CreatureDisplayInfo.dbc")
    rows, fields, recsize, strings = read_dbc(src)
    kept = []
    for old, new in ((60028, 980162), (60029, 980163)):
        r = bytearray(next(r for r in rows if get(r, 0) == old))
        put(r, 0, new); put(r, 1, new)
        kept.append(r)
    write_dbc(os.path.join(dbc_out, "CreatureDisplayInfo.dbc"), kept, fields, recsize, strings)

    # CreatureModelData: 3658/3659 -> 980162/980163; paths stay classic Darkfallen.
    src = os.path.join(SERVER_DBC, "CreatureModelData.dbc")
    rows, fields, recsize, strings = read_dbc(src)
    kept = []
    for old, new in ((3658, 980162), (3659, 980163)):
        r = bytearray(next(r for r in rows if get(r, 0) == old))
        put(r, 0, new)
        kept.append(r)
    write_dbc(os.path.join(dbc_out, "CreatureModelData.dbc"), kept, fields, recsize, strings)

    # CharSections: official source race -> target, no duplicate keys.
    src = os.path.join(SERVER_DBC, "CharSections.dbc")
    rows, fields, recsize, strings = read_dbc(src)
    kept = []
    seen = set()
    for r in rows:
        if get(r, 1) != args.source_race:
            continue
        # kind 2 = earrings/beards. Skip styles the official facial table has
        # no non-zero geoset for; otherwise the creator shows empty clicks.
        # Keep one colour row per style: the textures are empty and the model
        # only changes the geoset per style, so 13 colour rows make 13 identical
        # clicks that appear not to work.
        if get(r, 3) == 2:
            if valid_faces and (get(r, 2), get(r, 8)) not in valid_faces:
                continue
            if get(r, 9) != 0:
                continue
        key = (get(r, 2), get(r, 3), get(r, 8), get(r, 9))
        if key in seen:
            continue
        seen.add(key)
        r = bytearray(r); put(r, 1, args.target_race)
        kept.append(r)
    # The official table has no female kind-2 rows, but the female model has
    # the matching earring geosets. Add one empty-texture row per real style so
    # the female creator gets earring choices instead of none.
    template = next((r for r in kept if get(r, 3) == 2), None)
    if template is not None:
        nid = max(get(r, 0) for r in kept) + 1
        for (sex, style) in sorted(best_faces):
            if sex != 1 or (sex, 2, style, 0) in seen:
                continue
            nr = bytearray(template)
            put(nr, 0, nid); nid += 1
            put(nr, 1, args.target_race); put(nr, 2, 1); put(nr, 8, style); put(nr, 9, 0)
            kept.append(nr)
            seen.add((sex, 2, style, 0))
    write_dbc(os.path.join(dbc_out, "CharSections.dbc"), kept, fields, recsize, strings)

    # Hair geosets and hair colours: same race field as the AFK client.
    for name, race_field in (("CharHairGeosets.dbc", 1), ("CharHairTextures.dbc", 1),
                             ("BarberShopStyle.dbc", 37)):
        src = os.path.join(temp, "DBFilesClient", name)
        if not os.path.exists(src):
            continue
        rows, fields, recsize, strings = read_dbc(src)
        kept = []
        for r in rows:
            if get(r, race_field) != args.source_race:
                continue
            r = bytearray(r); put(r, race_field, args.target_race)
            kept.append(r)
        write_dbc(os.path.join(dbc_out, name), kept, fields, recsize, strings)

    # CharacterFacialHairStyles is laid out differently in the Esteria data:
    #   source: id, sex, race, variation, geoset1..4
    #   AFK:    race, sex, variation, geoset1..4, unused
    # RacePacker reads race from field 0, so a straight copy yields no beards
    # or earrings. Rebuild the one-race table in the AFK layout.
    src = os.path.join(temp, "DBFilesClient", "CharacterFacialHairStyles.dbc")
    if os.path.exists(src):
        rows, fields, recsize, strings = read_dbc(src)
        kept = []
        for (sex, style), (_, _, geosets) in sorted(best_faces.items()):
            nr = bytearray(recsize)
            put(nr, 0, args.target_race)   # race
            put(nr, 1, sex)                # sex
            put(nr, 2, style)              # variation / style
            put(nr, 3, geosets[0])         # geoset 1
            put(nr, 4, geosets[1])         # geoset 2
            put(nr, 5, geosets[2])         # geoset 3
            put(nr, 6, geosets[3])         # geoset 4
            kept.append(nr)
        write_dbc(os.path.join(dbc_out, "CharacterFacialHairStyles.dbc"), kept, fields, recsize, strings)

    # 3. Extract the classic Darkfallen assets from the official MPQ.
    # The classic Darkfallen model shares hair, scalp and eye textures with
    # Blood Elf. Without these the importer falls back to the player's client
    # (wrong hair textures) or drops the textures entirely.
    assets = os.path.join(stage, "assets")
    run_bridge("-Action", "extract-match", "-Mpq", os.path.join(RACES, "Races.MPQ"),
               "-Root", assets, "-Match", "Darkfallen")
    for prefix in ("Character\\BloodElf\\", "TurtleHairImport\\"):
        run_bridge("-Action", "extract-prefix", "-Mpq", os.path.join(RACES, "Races.MPQ"),
                   "-Root", assets, "-Match", prefix)
    # move assets to stage root; DBCs already in stage/DBFilesClient
    for dirpath, dirs, files in os.walk(assets):
        for f in files:
            srcf = os.path.join(dirpath, f)
            rel = os.path.relpath(srcf, assets)
            dst = os.path.join(stage, rel)
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            shutil.copy2(srcf, dst)
    shutil.rmtree(assets)

    # The Esteria package has character-create portraits but no CoA-style
    # Interface\Icons\CoA_Race_*.blp. RacePacker uses that icon for the race
    # folder and RaceImport derives TemporaryPortrait-* from it, so without it
    # the race has no portrait at all. Reuse the official Horde portrait.
    portrait = os.path.join(stage, "Interface", "Glues", "CharacterCreate",
                            "UI-CharacterCreate-DarkfallenHordeMale.blp")
    if os.path.exists(portrait):
        icon_dir = os.path.join(stage, "Interface", "Icons")
        os.makedirs(icon_dir, exist_ok=True)
        for icon_name in ("CoA_Race_Darkfallen.blp", "CoA_Race_DarkfallenHorde.blp"):
            shutil.copy2(portrait, os.path.join(icon_dir, icon_name))

    # 4. Pack the synthetic source client.
    data = os.path.join(args.out, "Data")
    os.makedirs(data, exist_ok=True)
    mpq = os.path.join(data, "patch-ZZZZZZZ.MPQ")
    run_bridge("-Action", "pack", "-Root", stage, "-Out", mpq)

    manifest = {
        "schema": 1,
        "kind": "esteria-source-client",
        "race": {
            "target_id": args.target_race,
            "source_id": args.source_race,
            "name": args.name,
            "file": "DarkfallenHorde",
            "profile": "classic",
            "appearance": "five-field",
        },
        "source": {
            "package": RACES,
            "client_dbc": os.path.join(SERVER_DBC, "ChrRaces.dbc"),
            "assets": "Races.MPQ",
        },
        "client": mpq,
        "importer_hint": "Create race packs from this folder; only the target race is present.",
    }
    with open(os.path.join(args.out, "manifest.json"), "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2, ensure_ascii=False)
    print("source client:", mpq)
    print("manifest:", os.path.join(args.out, "manifest.json"))
    shutil.rmtree(temp, ignore_errors=True)


if __name__ == "__main__":
    main()
