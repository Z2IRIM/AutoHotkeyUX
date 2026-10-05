# AutoHotkey Modern UX

A small WinUI 3 shell for the daily-use AutoHotkeyUX workflows.

## Technology

- C#
- .NET 8
- WinUI 3
- Windows App SDK 2.5.1
- unpackaged
- self-contained
- x64 single-file publish

The modern shell does not replace the AutoHotkey interpreter. It discovers and reuses an installed AutoHotkey v2 runtime and the existing AutoHotkeyUX scripts.

## Scope

- Home dashboard
- New Script
- Blank / Hotkeys / Automation starter templates
- Window Spy
- Ahk2Exe
- v2 documentation
- launcher / interpreter preferences
- Edit Script editor command
- update-check preference

Installer and uninstaller modernization remain outside this first pass.

## Runtime detection

The shell checks, in order:

1. AutoHotkey InstallDir registry values
2. the effective .ahk Open shell association
3. Windows App Paths
4. standard Program Files locations
5. PATH

The Home and Settings pages show the exact executable path and discovery source.

## Build

~~~powershell
cd modern-shell
dotnet build -c Release -p:Platform=x64
~~~

## Run from source

~~~powershell
cd modern-shell
dotnet run -c Release -p:Platform=x64
~~~

## Publish a single EXE

Run:

~~~text
modern-shell\build-single-exe.cmd
~~~

Output:

~~~text
modern-shell\artifacts\win-x64\AutoHotkeyUX.Modern.exe
~~~

The executable is an unpackaged, self-contained WinUI 3 single-file deployment. Windows App SDK and .NET dependencies are bundled and extracted to a temporary directory when the EXE starts.

## Existing AutoHotkey compatibility

The shell continues to use the established settings and shell-association locations:

- HKCU\Software\AutoHotkey\...
- HKCU\Software\Classes\AutoHotkeyScript\...

The existing .ahk UX remains available and the C++ AutoHotkey runtime is not modified.
