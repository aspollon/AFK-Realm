# AFK Realm loading screens

`CoALoading.blp` is CoA Custom's Ilusixn Custom Races loading screen, used with permission of the CoA Custom
project. The AFK Realm badge in the lower left was added for this project.

`LoadingScreens.dbc` maps every loading-screen row to that image. AFK Realm embeds both files in `AFK-Realm.exe`
and writes them into its base client patch (`Data\patch-ZZZZZ.MPQ`) for every game client, even when no module or
race is installed. The late archive name makes it load after CoA Custom's own `patch-ZZZD.MPQ`.

The two wide and standard BLP names point at the same image because CoA Custom uses one image for both.
