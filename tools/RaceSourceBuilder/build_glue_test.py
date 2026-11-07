#!/usr/bin/env python3
"""Add the Darkfallen Glue glow mappings to a local test client.

The race DBCs alone cannot turn on the creator's model glow: the Glue parent
has a per-file-string CharModelGlowInfo table. The official Esteria packer
patches that table. This script builds a small root/locale test patch pair.
"""
import os
import shutil
import subprocess
import tempfile

DESC = r"C:\Users\aspol\Downloads\Descension\Data"
ROOT_MPQ = os.path.join(DESC, "patch-B.MPQ")
LOCALE_MPQ = os.path.join(DESC, "enUS", "patch-enUS-3.MPQ")
OUT = r"C:\Users\aspol\Downloads\RaceSourceBuilder\DarkfallenGlue"
BRIDGE = os.path.join(os.path.dirname(__file__), "mpq-bridge.ps1")
NAME = "Interface\\GlueXML\\GlueParent.lua"

ADD_GLOW = [
    'CharModelGlowInfo["DARKFALLEN"] = 0.15;',
    'CharModelGlowInfo["DARKFALLENHORDE"] = 0.15;',
]
ADD_FOG = [
    'CharModelFogInfo["DARKFALLEN"] = CharModelFogInfo["HUMAN"];',
    'CharModelFogInfo["DARKFALLENHORDE"] = CharModelFogInfo["ORC"];',
]


def run(*args):
    p = subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", BRIDGE] + list(args),
                       capture_output=True, text=True)
    if p.returncode != 0:
        raise RuntimeError(p.stdout + p.stderr)


def patch_text(text):
    # Glow is what the creator reads for the emissive eye/body layers.
    anchor = 'CharModelGlowInfo["HUMAN"] = 0.15;'
    if anchor in text:
        text = text.replace(anchor, anchor + "\n" + "\n".join(ADD_GLOW), 1)
    else:
        text = text.replace("CharModelGlowInfo = { };", "CharModelGlowInfo = { };\n" + "\n".join(ADD_GLOW), 1)
    # Fog and ambience are cosmetic, but keep the race from falling back to the
    # model's default grey background.
    anchor = 'CharModelFogInfo["HUMAN"] ='
    if anchor in text:
        line_end = text.find(";", text.find(anchor)) + 1
        text = text[:line_end] + "\n" + "\n".join(ADD_FOG) + text[line_end:]
    else:
        text = text.replace("CharModelFogInfo = { };", "CharModelFogInfo = { };\n" + "\n".join(ADD_FOG), 1)
    return text


def main():
    if os.path.isdir(OUT):
        shutil.rmtree(OUT)
    stages = {}
    for label, mpq, sub in (("root", ROOT_MPQ, "root"), ("locale", LOCALE_MPQ, "locale")):
        tmp = tempfile.mkdtemp(prefix="glue-" + label + "-")
        names = os.path.join(tmp, "names.txt")
        with open(names, "w", encoding="utf-8") as f:
            f.write(NAME + "\n")
        run("-Action", "extract-files", "-Mpq", mpq, "-Root", tmp, "-NamesFile", names)
        src = os.path.join(tmp, NAME.replace("\\", os.sep))
        text = open(src, "r", encoding="utf-8-sig").read()
        patched = patch_text(text)
        stage = os.path.join(OUT, "stage-" + sub)
        dst = os.path.join(stage, NAME.replace("\\", os.sep))
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        with open(dst, "w", encoding="utf-8", newline="\n") as f:
            f.write(patched)
        stages[sub] = stage
        shutil.rmtree(tmp, ignore_errors=True)

    os.makedirs(os.path.join(OUT, "root"), exist_ok=True)
    os.makedirs(os.path.join(OUT, "locale"), exist_ok=True)
    run("-Action", "pack", "-Root", stages["root"], "-Out", os.path.join(OUT, "root", "patch-ZZZZZZ.MPQ"))
    run("-Action", "pack", "-Root", stages["locale"], "-Out", os.path.join(OUT, "locale", "patch-enUS-ZZZZZZ.MPQ"))
    print("root:  ", os.path.join(OUT, "root", "patch-ZZZZZZ.MPQ"))
    print("locale:", os.path.join(OUT, "locale", "patch-enUS-ZZZZZZ.MPQ"))


if __name__ == "__main__":
    main()
