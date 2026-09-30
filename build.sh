#!/bin/sh
# Builds the same .NET Framework 4.x exe with Mono (mcs).
set -e
cd "$(dirname "$0")"
mcs -langversion:5 -target:winexe -optimize+ -sdk:4.5 -out:AFK-Realm.exe \
  -r:System.Windows.Forms.dll -r:System.Drawing.dll -r:System.Numerics.dll -r:System.Core.dll \
  -win32icon:assets/afk-realm.ico -resource:engine/engine.ps1,CoAInstaller.engine.ps1 -resource:assets/afk-realm-header.png,CoAInstaller.logo.png src/*.cs
echo "Built AFK-Realm.exe"
