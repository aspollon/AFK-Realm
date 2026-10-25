#!/bin/sh
# Builds the same .NET Framework 4.x exe with Mono (mcs).
set -e
cd "$(dirname "$0")"
# The languages of the window (lang/*.json) go into the exe.
LANGRES=""
for f in lang/*.json; do [ -e "$f" ] && LANGRES="$LANGRES -resource:$f,CoAInstaller.lang.$(basename "$f")"; done
mcs -langversion:5 -target:winexe -optimize+ -sdk:4.5 -out:AFK-Realm.exe \
  -r:System.Windows.Forms.dll -r:System.Drawing.dll -r:System.Numerics.dll -r:System.Core.dll -r:System.Security.dll -r:System.IO.Compression.dll -r:System.IO.Compression.FileSystem.dll -r:System.Web.Extensions.dll \
  -win32icon:assets/afk-realm.ico -resource:engine/engine.ps1,CoAInstaller.engine.ps1 -resource:engine/account-transfer.sql,CoAInstaller.account-transfer.sql -resource:engine/module-journal.sql,CoAInstaller.module-journal.sql -resource:assets/afk-realm-header.png,CoAInstaller.logo.png -resource:assets/loading/LoadingScreens.dbc,CoAInstaller.loading.LoadingScreens.dbc -resource:assets/loading/CoALoading.blp,CoAInstaller.loading.CoALoading.blp $LANGRES src/*.cs
echo "Built AFK-Realm.exe"
