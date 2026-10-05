# AutoHotkey Modern UX

A small Windows desktop shell for the existing AutoHotkeyUX functionality.

## Scope

The first version intentionally keeps the project small:

- Home dashboard
- New Script
- Launcher / interpreter settings
- Editor command setting
- Update-check preference
- Window Spy shortcut
- Ahk2Exe shortcut
- AutoHotkey v2 documentation shortcut

The existing AutoHotkey runtime and AutoHotkeyUX scripts remain the source of truth for runtime behavior. This app is a modern Windows UI layer, not a replacement interpreter.

## Requirements

For development:

- Windows 10/11
- .NET 8 SDK

For the published executable:

- Windows x64
- AutoHotkey v2 should already be installed for runtime-dependent actions such as Window Spy and Ahk2Exe.

## Run from source

~~~powershell
cd modern-shell
dotnet run
~~~

## Build

~~~powershell
cd modern-shell
dotnet build -c Release
~~~

## Publish as one EXE

Run:

~~~text
modern-shell\build-single-exe.cmd
~~~

Output:

~~~text
modern-shell\artifacts\win-x64\AutoHotkeyUX.Modern.exe
~~~

The project is configured as a self-contained .NET 8 win-x64 single-file application, so the target machine does not need a separate .NET runtime installation.

## Integration

The shell reuses the existing AutoHotkey registry schema:

- HKCU\Software\AutoHotkey\...
- HKCU\Software\Classes\AutoHotkeyScript\...

It detects the normal AutoHotkey install directory and reuses the existing UX scripts where appropriate.

The modern shell does not modify the C++ AutoHotkey runtime.
