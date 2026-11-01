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

4. Item inventory-type guard
   Item_C.h, Item_C.cpp, Tooltip.cpp, GameUI.cpp, GameUI_Scripts.cpp and
   TradeSkillFrame.cpp. A negative or out-of-range InventoryType (for example
   -1 from a bad item row) no longer indexes the 29-entry slot/token tables.

5. Two Forms after customize / race change / faction change
   RetailRace.h/.cpp and CGlueMgr.cpp. The client now prepares the two-form
   commit for customize, race change and faction change as well, and sends both
   looks as soon as the server confirms the change. Before this, only character
   creation committed the parked form; a character changed to Dracthyr or
   Worgen kept the server's default second form.

Build
-----
Use the repository's BuildCoA64.bat / CMake target Wow (x64, RelWithDebInfo).
The resulting bin/Win/CoA64.exe is packed as the single entry CoA64.exe in
CoA64.zip. No PDB or map is shipped.

Checksums of this build
-----------------------
CoA64.exe SHA-256:
FC989DDA3C0A219882A870F7130CE1BB2123B544AADC2E06BA53CA6229035BDC

CoA64.zip SHA-256:
34B8D8E5FC6E756F8A4793754677BAA44F236A6FBEC7C4C77C24ABFE5FF21EFB

Tests
-----
- native Direct3D9 HD, patch-ZZZZS/T/W enabled: three cold starts
- same with patch-4/5/Z enabled: two cold starts
- x64 DXVK 2.7.1 with "-dxvk": one start
- current 1.2.27 build: startup with the full HD set reaches the login connection
- two-form race/faction/customize needs an in-game test with a changed Dracthyr/Worgen character

