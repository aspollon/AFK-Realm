# Azeroth Remastered on Mado's engine (2026-11-25)

From the CoA64 build on Mado's engine (CoA64 branch `mado-engine`), Azeroth Remastered (Visuals 3) needs Mado's data
beside the exe. `src/ClientAzerothRemastered.cs` fetches his public player package once per version when Visuals.txt
says 3:

- Source: Shotboom12/Mados-Azeroth-Remastered, release v1.0.4,
  `Azeroth.Remastered-2026-11-24-BRUME1-English-v1.0.4.zip` (about 450 MB). Mado allowed its use in AFK.
- The package's `client\` goes into the game folder (shaders, Cata water, Lumenveil and wind data, For Ever and AR
  light tables, FSR and DLSS runtimes, the WaystoneHD add-on, `Data\patch-w.mpq`), its `LICENSES\` into
  `Azeroth Remastered LICENSES\`. Not taken: `WowClient64.exe` (AFK64.exe has the engine), the launcher script (the
  exe sets the FSR/DLSS mip bias itself) and `XvidDecoder.dll` (the voices release has it).
- The first install writes Mado's recommended settings (`HD-Settings.txt`) into `WTF\Config.wtf`, as his installer
  does, except `gxApi` (Visuals.txt decides).
- `Vulkan\.afk-azeroth-remastered` holds the version; a new package needs a new `Version` and `Url`.

Callers: AFK Client before the shader translation (`client/Updater.cs`), AFK Realm when it installs the exe
(`ClientVoices.EnsureNewestExe`) and after the HD page writes the choice (`ClientHd`). The shader translation then adds
this game folder's own shaders to Mado's set (`manifest-coa.txt`, docs/2026-11-21-vulkan-shaders.md).

Release together with the voices release of the new exe: its `GraphicsData.zip` must carry Mado's `forever_data`,
or AFK puts the older tables back over his.
