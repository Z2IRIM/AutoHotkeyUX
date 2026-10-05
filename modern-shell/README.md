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

The modern shell ships with a verified AutoHotkey v2 portable runtime embedded inside the single-file EXE. On first launch, the runtime and the required AutoHotkeyUX scripts are materialized into the app's private LocalAppData directory. A separate AutoHotkey installation is not required.

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

The runtime is private to the modern shell and does not register a separate AutoHotkey application in Windows Apps.

## Bundled runtime

The build pins AutoHotkey v2.0.29 portable ZIP and verifies its SHA-256 before embedding it.

At runtime the EXE validates and extracts the embedded payload to:

~~~text
%LOCALAPPDATA%\AutoHotkeyUX.Modern\runtime\2.0.29\
  v2\
  UX\
~~~

The bundled runtime is preferred over any separately installed AutoHotkey version. System-wide discovery remains as a fallback only.

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

The executable is an unpackaged, self-contained WinUI 3 single-file deployment. Windows App SDK, .NET, the AutoHotkey portable runtime, and the required AutoHotkeyUX scripts are bundled into the published EXE.

## Existing AutoHotkey compatibility

The shell continues to use the established settings and shell-association locations:

- HKCU\Software\AutoHotkey\...
- HKCU\Software\Classes\AutoHotkeyScript\...

The existing .ahk UX remains available and the C++ AutoHotkey runtime is not modified.
