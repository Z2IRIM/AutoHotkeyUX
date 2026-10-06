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

## Script manager and background shortcuts

- **Scripts** reads `.ahk` files from the Windows Documents known folder's `AutoHotkey` directory. Run/Stop/Restart operate only on interpreters owned by this manager. Edit uses the existing editor setting or Notepad without changing associations.
- In **Settings → Background and shortcuts**, enable **Start with Windows** for silent startup at user sign-in and **Explorer shortcuts** for the built-in editable script.
- **Alt + left click** in Explorer's file view opens a folder's terminal, extracts a supported archive into a new sibling folder, or opens the current directory's terminal when clicking empty space. Navigation panes and virtual directories are ignored. Windows Terminal is preferred; Windows PowerShell is the fallback.
- Quick extraction supports ZIP, 7z, RAR, TAR, TAR.GZ and TGZ. Source archives and existing folders are preserved. Password prompts and multipart workflows are not implemented. Extraction rejects unsafe paths/links and is bounded to 100,000 entries and 20 GiB output.
- Closing the window hides it to the system tray. Launching the EXE again restores the same manager. **Exit manager** preserves running scripts; sessions are checked by PID, exact process start time and runtime path on reopening.
- User scripts can opt into **Run at sign-in** on Scripts. This takes effect when the manager starts; Windows startup must also be enabled for sign-in launching. A missing script is diagnosed and does not block other startup scripts or the workspace.
- Diagnostics: `%LOCALAPPDATA%\AutoHotkeyUX.Modern\state\manager.log`, `manager-startup.json`, `managed-sessions.json`. No script contents are stored in session metadata.

Commands supported by the modern EXE:

~~~text
AutoHotkeyUX.Modern.exe --background
AutoHotkeyUX.Modern.exe --background --enable-core
AutoHotkeyUX.Modern.exe --exit-manager
AutoHotkeyUX.Modern.exe --extract "C:\Downloads\archive.zip"
AutoHotkeyUX.Modern.exe --verify-runtime "C:\Temp\runtime-report.json"
AutoHotkeyUX.Modern.exe --verify-ui "C:\Temp\ui-report.json"
~~~

`--enable-core` explicitly enables this user's login startup and Explorer shortcuts. The one-shot extraction helper does not open the manager UI. UI diagnostics must run when the manager is not already open; they load real pages, check search/navigation/minimum-width actions, write a report and exit.

Source-only backup: run `powershell -NoProfile -ExecutionPolicy Bypass -File .\package-source.ps1` from the repository root. Generated builds, runtime downloads, test files, dependencies and earlier backups are excluded.

Incremental source package: `powershell -NoProfile -ExecutionPolicy Bypass -File .\package-source.ps1 -Incremental -BaseRef <commit>`. This includes tracked changes against the specified baseline and a deletion manifest; its default output is `C:\DESKTOP\srcpack_Area\AutoHotkeyUX`.

## Application icon

The approved green H keycap is compiled into the EXE and embedded for native window/taskbar and tray use. `Assets/AutoHotkey.svg` is the editable master; `AutoHotkey.ico` contains 16, 20, 24, 32, 40, 48, 64, 128 and 256 px frames. The application needs no external icon file or Node.js runtime.

To regenerate the committed ICO/PNG after editing the SVG, run `node modern-shell/build-icon.mjs` from a development environment with `sharp` available. An optional second argument supplies a directory for module resolution: `node modern-shell/build-icon.mjs <node_modules-directory>`. Normal .NET builds use the committed ICO directly.

Native icon initialization is recorded in `state/manager.log`; `manager-startup.json` records window icon application and tray registration. Generated `artifacts/` and developer `backups/` are excluded from the self-extracting publish payload.

## Existing AutoHotkey compatibility

The shell continues to use the established settings and shell-association locations:

- HKCU\Software\AutoHotkey\...
- HKCU\Software\Classes\AutoHotkeyScript\...

The existing .ahk UX remains available and the C++ AutoHotkey runtime is not modified.
