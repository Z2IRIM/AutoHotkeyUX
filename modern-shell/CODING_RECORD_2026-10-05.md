# AutoHotkeyUX Modern WPF Shell — Coding Record

Date: 2026-10-05

## Goal

Replace the frequently used AutoHotkeyUX entry points with a small modern Windows desktop application while preserving the existing AutoHotkey runtime and UX business behavior.

## Key assumptions

- AutoHotkey v2 remains installed separately.
- The modern shell is an additional presentation layer, not a new interpreter or installer.
- The first version targets Windows x64.
- Installer and uninstaller UI are outside this first pass.

## Problem analysis

The existing AutoHotkeyUX UI is implemented with AutoHotkey/Win32 GUI controls. Runtime actions and UI code are mixed, but most useful actions already exist as scripts, registry conventions, or executables.

A full rewrite of AutoHotkeyUX behavior would create unnecessary parallel logic. The implementation therefore keeps the existing registry contract and invokes existing tools where practical.

## Added files

- modern-shell/AutoHotkeyUX.Modern.csproj
- modern-shell/app.manifest
- modern-shell/App.xaml
- modern-shell/App.xaml.cs
- modern-shell/MainWindow.xaml
- modern-shell/MainWindow.xaml.cs
- modern-shell/Services/AutoHotkeyIntegration.cs
- modern-shell/build-single-exe.cmd
- modern-shell/.gitignore
- modern-shell/README.md
- .github/workflows/modern-wpf-shell.yml

## Main changes

- Added .NET 8 WPF desktop application.
- Added Home dashboard matching the approved UI preview.
- Added New Script workflow with Blank, Hotkeys and Automation templates.
- Script creation creates the target directory when required, writes UTF-8 without BOM, never overwrites an existing script, and automatically adds a numeric suffix on collision.
- Added Window Spy integration.
- Added Ahk2Exe integration.
- Added local CHM / online AutoHotkey v2 documentation fallback.
- Added runtime detection from existing AutoHotkey registry installation metadata.
- Added launcher mode and preferred v2 build settings using the existing per-user AutoHotkey shell association.
- Added editor command configuration using the existing Edit Script shell verb.
- Added update-check preference using the existing Dash\CheckForUpdates setting.
- Added one-command self-contained single-EXE publishing.
- Added Windows GitHub Actions build and publish verification.

## Business / compatibility decisions

### Keep existing AutoHotkey registry contract

Decision: the WPF app writes to existing AutoHotkey settings instead of introducing JSON or a second configuration store.

Reason: both old and new UI should observe the same behavior and settings.

### Do not modify AutoHotkey runtime

Decision: no changes were made to the C++ AutoHotkey repository.

Reason: this task is a UX modernization and does not require interpreter changes.

### Do not replace installer / uninstaller yet

Decision: the first pass focuses on daily-use UX.

Reason: installer paths, elevation, UI Access and repair behavior have higher failure cost and are not required for the requested modern dashboard app.

## Validation

Attempted local validation:

~~~text
git clone --branch modern-wpf-shell ...
dotnet build -c Release
~~~

The execution container could not resolve github.com, and the container does not have dotnet installed. Therefore no local build result is claimed.

A Windows GitHub Actions workflow was added to provide the authoritative .NET 8 build and single-file publish validation on the pull request.

## Not yet validated

- Real Windows launch of the generated EXE.
- Runtime interaction against an installed AutoHotkey v2 instance.
- Window Spy / compiler launch on the user's machine.
- Exact visual rendering at multiple Windows DPI scales.

## Risk

Low-to-medium.

The implementation is isolated under modern-shell/ and uses per-user registry writes. Existing .ahk UX files and the runtime are left intact.

The main remaining risk is platform-specific WPF/runtime integration and should be closed by CI plus one Windows smoke test.

## Database / API / migration impact

None.

## Compatibility impact

- Existing AutoHotkeyUX remains usable.
- Existing AutoHotkey registry settings are reused.
- No format migration is required.
- First published binary targets Windows x64.

## Rollback

Delete modern-shell/ and .github/workflows/modern-wpf-shell.yml, or simply stop using the modern executable. Existing AutoHotkeyUX behavior remains unchanged.

## Remaining work

- Confirm CI build/publish.
- Smoke-test the generated EXE on Windows with an installed AutoHotkey v2.
- If the first pass is stable, installer UI can be modernized separately later.
