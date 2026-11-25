# Vulkan shaders translated on the player's machine (1.3.76)

AFK64.exe draws on Vulkan only with a SPIR-V module for every DX9 shader the game loads; the device finds a module by
the md5 of the shader's bytes (`VulkanShaders\manifest.txt`). The voices release brings modules translated from a CoA
Custom client. A shader that the player's own archives add has none, and the device draws its geometry without it.

Found with fueryin's Ultra-HD pack: its `patch-ZZA` (installed as `patch-ZXA.MPQ`) replaces 1,459 shaders. 208 vertex
records (166 distinct: all 64 terrain permutations, the M2 `Diffuse_*` and WMO `MapObj*` vertex shaders) had no
module, and players saw the ground and buildings flicker red, green and blue or turn black. Deleting the shaders
"fixed" it by falling back to the stock ones.

## What AFK does

`ClientShaders.Ensure` runs after every AFK64.exe check (`ClientVoices.EnsureNewestExe`) and after the HD install:

1. A fingerprint of `Data\*.mpq` (names, sizes, times), the translator version and the release manifest is compared
   with `VulkanShaders\.afk-translated`. Equal: nothing to do.
2. Windows: Python 3.12 (embeddable) and glslang 16.6.0 are downloaded once into
   `%LocalAppData%\AFK Realm\shader-tools`. Linux: `python3` and `glslangValidator` from PATH.
3. CoA64's translator (`assets/vulkan-shaders/translator.zip`, from CoA64 `Tools/VulkanShaders`) runs with
   `--merge-into <game>\VulkanShaders`: only records whose md5 the manifest lacks are translated, their `.spv` copied
   in and their blocks appended (manifest last). The pack's 166 take about 15 seconds.

The release's manifest is kept as `release-manifest.txt`; `ClientVoices.SameShaders` compares with that, so the merged
manifest does not make AFK reinstall the release every time. A reinstall resets the stamp and translates again.

If the translation fails (no internet, no tools), the HD install falls back to leaving the pack's shaders out of
`patch-ZXA.MPQ` (`ClientHd.StripPackShaders`); its fonts stay. The next HD install tries again.

The players' launcher gets the merged set: `ClientVoices.PlayerFiles` already packs the owner's `VulkanShaders`.

A newer translator: rebuild `translator.zip` from CoA64 `Tools/VulkanShaders` (translate_shaders.py, README.md,
bls/*.py) and bump `ClientShaders.TranslatorVersion`.

## 2026-11-25: the launcher translates too

The player pack carries the owner's `VulkanShaders`, translated for the owner's archives. A player with other archives
(HD pack switched on in the launcher, another CoA Custom part) lacked modules and saw torn models on Azeroth Remastered,
while the same files copied by hand from Living Voices worked. AFK Client 1.3.80 links `src/ClientShaders.cs` and the
translator resource and runs `Ensure` after every update check (`Updater.TranslateShadersAsync`).

The merged `manifest.txt` differs from the server's by design, so the updater no longer hashes it against the server's:
it keeps the server's SHA-256 in `LocalState.ShaderManifest`, downloads only when the server ships a new list, and then
deletes the stamp (`ClientShaders.Reset`) so the next `Ensure` merges again. The owner's `.afk-translated` stays out of
the pack (`ClientVoices.PlayerFiles`).

The real cause of that report was on the owner's side: `ClientVoices.SameShaders` compared only the kept
`release-manifest.txt` with the release, so a live set replaced afterwards (the CoA Custom package's) was never noticed
and went into every pack. It now requires the live `manifest.txt` to begin with the release's text (merges append) and
every release module to be present with its size; the pack build calls `EnsureNewestExe` first, which reinstalls.

## Azeroth Remastered's set (2026-11-25)

CoA64 on Mado's engine reads `Vulkan\shaders\translated\` (Mado's shipped set). Its `manifest.txt` stays as shipped,
because Mado's FSR overlays (`manifest-m2/m3/m4.txt`) pin its SHA-256. The translator (translator `coa64-2026-11-25b`)
recognises that set and writes the records the player's client adds to `manifest-coa.txt` and `index-coa.txt` in Mado's
layout (`p[16]`, vertex classes 64/256/1000, pixel c13+ from `v[64 + (N - 13)]`). AFK Client and AFK Realm translate
into every set the game folder has.
