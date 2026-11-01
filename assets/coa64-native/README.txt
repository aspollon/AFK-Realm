CoA64 1.9.6 native HD build
================================

Source
------
Repository: CustomRacesCoa
Tag:        coa-custom-1.9.6-x64
Commit:     b9be0f66b7e9bce57f2785d9330fb73e5e73765d

Changes from the tag
--------------------
1. CoA/Source/Runtime/Hd.cpp
   Hd::Available() returns true. HD therefore also works with the system
   Direct3D9 renderer, not only after DXVK loaded. "-hd 0" still disables HD.

2. CoA/Source/Runtime/Dxvk.cpp
   DXVK is opt-in. The client uses native Direct3D9 by default; start it with
   "-dxvk" to load the x64 DXVK folder instead.

3. WoW/Source/Client.cpp, OpenArchives()
   s_archiveHandles.SetCount(127) -> SetCount(512).

   This is the actual 1.9.6 crash fix. The official 127-handle array overflowed
   when HD was enabled together with patch-ZZZZS/T/W, causing
   STATUS_HEAP_CORRUPTION (0xC0000374) about 2.6 seconds after launch. Any one
   of patch-ZZZZS/T/W was enough to cross the limit. With 512 handles the full
   set starts, including patch-4/5/Z.

Build
-----
Use the repository's BuildCoA64.bat / CMake target Wow (x64, RelWithDebInfo).
The resulting bin/Win/CoA64.exe is packed as the single entry CoA64.exe in
CoA64.zip. No PDB or map is shipped.

Checksums of this build
-----------------------
CoA64.exe SHA-256:
254D4C59D27D245FB5D958A89FE870CA925D1C8CAF21BAF9DAB328815D25AF1B

CoA64.zip SHA-256:
976DB8726AC79FFF521D27DB436B0B450676E8E6C6ED4C2376F63253C6FC6205

Tests
-----
- native Direct3D9 HD, patch-ZZZZS/T/W enabled: three cold starts
- same with patch-4/5/Z enabled: two cold starts
- x64 DXVK 2.7.1 with "-dxvk": one start
All stayed running past 30 seconds and reached the login connection.
