#!/usr/bin/env python3
"""Read-only inventory for the Esteria race package.

Reads Races/inventory.json, Races/Server/DBC/ChrRaces.dbc and CharSections.dbc,
plus the MPQ manifest. Writes a Markdown and JSON matrix. It does not touch
the client, the server or the race package.
"""
import json
import os
import struct
import sys
from collections import Counter

RACES = r"C:\Users\aspol\Downloads\RASSENFILES\Races"
SERVER_DBC = os.path.join(RACES, "Server", "DBC")
OUT = r"C:\Users\aspol\Downloads\RaceSourceBuilder"

EXPANDED = {
    "HighmountainTauren", "Earthen", "EarthenHorde", "Haranir", "HaranirHorde",
    "Skyborne", "SkyborneHorde", "Vulpera", "Mechagnome",
}
NATIVE = {
    "Naga", "NagaHorde", "Tuskarr", "Vrykul", "VrykulHorde", "ThinHuman",
    "ThinHumanHorde", "Gnoll", "Saberon", "Jinyu", "Ogre", "OgreHorde",

}

def read_dbc(path):
    d = open(path, "rb").read()
    magic, records, fields, recsize, strsize = struct.unpack_from("<4sIIII", d, 0)
    if magic != b"WDBC":
        raise ValueError(path + " is not a DBC")
    start = 20 + records * recsize
    strings = d[start:start + strsize]
    rows = [struct.unpack_from("<%dI" % fields, d, 20 + i * recsize) for i in range(records)]
    return rows, fields, strings

def text(strings, off):
    if not off or off >= len(strings):
        return ""
    end = strings.find(b"\0", off)
    return strings[off:end].decode("utf-8", "replace") if end >= 0 else ""

def main():
    os.makedirs(OUT, exist_ok=True)
    inventory = json.load(open(os.path.join(RACES, "inventory.json"), encoding="utf-8"))
    manifest = json.load(open(os.path.join(RACES, "mpq-manifest.json"), encoding="utf-8"))
    entries = {e.get("entry", "").lower(): e for e in manifest}

    races, _, _ = read_dbc(os.path.join(SERVER_DBC, "ChrRaces.dbc"))
    sections, sfields, sstrings = read_dbc(os.path.join(SERVER_DBC, "CharSections.dbc"))
    displays, _, _ = read_dbc(os.path.join(SERVER_DBC, "CreatureDisplayInfo.dbc"))
    models, mfields, mstrings = read_dbc(os.path.join(SERVER_DBC, "CreatureModelData.dbc"))

    by_id = {r[0]: r for r in races}
    display_by_id = {r[0]: r for r in displays}
    model_by_id = {r[0]: r for r in models}
    sec_count = Counter()
    sec_keys = {}
    for s in sections:
        race = s[1]
        sec_count[race] += 1
        sec_keys.setdefault(race, set()).add((s[2], s[3], s[8], s[9]))

    report = []
    for item in inventory.get("races", []):
        rid = item.get("id")
        name = item.get("name", "")
        file_string = item.get("file_string", "")
        row = by_id.get(rid)
        sources = []
        profile = "classic"
        keyname = file_string or name
        if keyname in EXPANDED or name in EXPANDED:
            profile = "expanded"
        if keyname in NATIVE or name in NATIVE:
            profile = "native"
        model_paths = []
        for sex, display_id in enumerate(item.get("display_ids", [])):
            if not display_id:
                continue
            d = display_by_id.get(display_id)
            if not d:
                sources.append("display %s missing" % display_id)
                continue
            m = model_by_id.get(d[1])
            if not m:
                sources.append("model %s missing" % d[1])
                continue
            path = text(mstrings, m[2])
            model_paths.append(path)
            candidates = [path]
            if path.lower().endswith(".mdx"):
                candidates.append(path[:-4] + ".m2")
            if not any(c.lower() in entries for c in candidates):
                sources.append("model file missing: " + path)
        for p in item.get("models", []):
            candidates = [p]
            if p.lower().endswith(".mdx"):
                candidates.append(p[:-4] + ".m2")
            if not any(c.lower() in entries for c in candidates):
                sources.append("inventory model missing: " + p)
        status = "ok"
        if sources:
            status = "missing"
        elif profile == "expanded":
            status = "needs-expanded-runtime"
        elif profile == "native":
            status = "needs-native-runtime"
        report.append({
            "id": rid,
            "name": name,
            "file": file_string,
            "profile": profile,
            "status": status,
            "displays": item.get("display_ids", []),
            "models": model_paths,
            "sections": sec_count.get(rid, 0),
            "section_keys": len(sec_keys.get(rid, ())),
            "issues": sources,
        })

    report.sort(key=lambda r: (r["status"], r["id"] or 0))
    with open(os.path.join(OUT, "race-matrix.json"), "w", encoding="utf-8") as f:
        json.dump(report, f, indent=2, ensure_ascii=False)
    lines = ["# Esteria race source matrix", "", "Read-only inventory. No client/server changes.", "",
             "| ID | Race | File | Profile | Status | Sections | Models | Issues |",
             "|---:|---|---|---|---|---:|---|---|"]
    for r in report:
        lines.append("| %s | %s | %s | %s | %s | %s | %s | %s |" % (
            r["id"], r["name"], r["file"], r["profile"], r["status"], r["sections"],
            "<br>".join(r["models"]), "; ".join(r["issues"]) or ""))
    with open(os.path.join(OUT, "race-matrix.md"), "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    print("Wrote", os.path.join(OUT, "race-matrix.md"))
    print("Wrote", os.path.join(OUT, "race-matrix.json"))
    print("Races:", len(report), "classic-ok:", sum(1 for r in report if r["status"] == "ok"),
          "expanded:", sum(1 for r in report if r["status"] == "needs-expanded-runtime"),
          "native:", sum(1 for r in report if r["status"] == "needs-native-runtime"),
          "missing:", sum(1 for r in report if r["status"] == "missing"))

if __name__ == "__main__":
    main()
