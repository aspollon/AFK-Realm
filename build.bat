@echo off
rem Builds AFK-Realm.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
"%CSC%" /nologo /target:winexe /optimize+ /out:AFK-Realm.exe ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Numerics.dll /r:System.Core.dll ^
  /win32icon:assets\afk-realm.ico /resource:engine\engine.ps1,CoAInstaller.engine.ps1 /resource:assets\afk-realm-header.png,CoAInstaller.logo.png src\*.cs
if errorlevel 1 (
  echo BUILD FAILED
  if /i not "%~1"=="nopause" pause
  exit /b 1
)
echo Built AFK-Realm.exe
if /i not "%~1"=="nopause" pause
