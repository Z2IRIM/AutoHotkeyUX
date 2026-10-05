# AutoHotkeyUX WinUI 3 Rewrite — Coding Record

Date: 2026-10-06

## Goal

Replace the first-pass WPF shell with a native WinUI 3 shell while preserving the existing AutoHotkeyUX business behavior and single-EXE distribution target.

## Key assumptions

- AutoHotkey v2 remains a separate installed runtime.
- The app is a modern presentation shell, not a replacement interpreter.
- Windows x64 is the initial distribution target.
- The existing AutoHotkey registry and shell-association schema remains canonical.
- Installer and uninstaller modernization are intentionally deferred.

## Problem analysis

The WPF prototype compiled and launched successfully but its visual result remained noticeably different from a modern Windows 11 application.

The first real-machine smoke test also exposed a runtime discovery weakness: an installed AutoHotkey runtime could exist while the WPF implementation reported "Runtime not detected". The original detector depended too heavily on AutoHotkey's InstallDir registry value.

## Modified / added / removed files

### Replaced

- modern-shell/AutoHotkeyUX.Modern.csproj
- modern-shell/App.xaml
- modern-shell/App.xaml.cs
- modern-shell/MainWindow.xaml
- modern-shell/MainWindow.xaml.cs
- modern-shell/Services/AutoHotkeyIntegration.cs
- modern-shell/build-single-exe.cmd
- modern-shell/README.md

### Added

- modern-shell/Pages/HomePage.xaml
- modern-shell/Pages/HomePage.xaml.cs
- modern-shell/Pages/NewScriptPage.xaml
- modern-shell/Pages/NewScriptPage.xaml.cs
- modern-shell/Pages/SettingsPage.xaml
- modern-shell/Pages/SettingsPage.xaml.cs
- modern-shell/Services/AutoHotkeyRuntimeLocator.cs
- modern-shell/Services/AutoHotkeySettings.cs
- .github/workflows/modern-winui-shell.yml

### Removed

- .github/workflows/modern-wpf-shell.yml

## Main changes

- Replaced WPF with WinUI 3 and Windows App SDK 2.5.1.
- Added native NavigationView navigation, Mica backdrop, WinUI cards, InfoBar feedback, ToggleSwitch and native ComboBox controls.
- Split the three primary views into Home, New Script and Settings pages.
- Preserved non-overwriting script creation and the three starter templates.
- Preserved Window Spy, Ahk2Exe and documentation actions.
- Preserved existing AutoHotkey launcher, editor and update registry semantics.
- Added multi-source runtime discovery:
  - AutoHotkey InstallDir
  - .ahk Open association
  - Windows App Paths
  - Program Files
  - PATH
- Runtime UI now shows the exact located executable and discovery source.
- Kept unpackaged, self-contained, single-file EXE publishing.

## Business decisions

### WinUI 3 replaces WPF instead of coexisting

Reason: keeping two presentation implementations would create duplicate maintenance and ambiguous product direction.

### Runtime discovery is independent from UI

Reason: runtime location is a system-integration responsibility and needs to be reusable by Home, Settings and tool-launch actions.

### Existing AutoHotkey registry contract remains canonical

Reason: modernizing the UI must not create a private configuration island incompatible with the legacy UX.

## Validation plan

Risk-driven validation:

1. GitHub Actions Windows build.
2. Self-contained single-file publish.
3. Real Windows launch smoke test.
4. Runtime discovery against the user's installed AutoHotkey v2.
5. New Script collision behavior.
6. Window Spy / Compile / Documentation launch.
7. Launcher setting only after the read-only smoke checks pass.

## Not yet validated

At commit creation time, WinUI 3 compilation and runtime behavior are not yet claimed as passed. GitHub Actions is the authoritative Windows compile gate.

## Risk

Medium.

The migration changes the UI framework and project dependencies, but the AutoHotkey runtime and legacy UX files remain untouched. The highest-risk areas are WinUI 3 deployment configuration, single-file extraction, and Windows shell integration.

## Database / API / migration impact

None.

## Compatibility impact

- C++ AutoHotkey runtime: unchanged.
- Existing .ahk AutoHotkeyUX: unchanged.
- Registry settings: reused without migration.
- Distribution: remains Windows x64 and single-file EXE.

## Rollback

Revert this WinUI 3 rewrite commit to return to the previously compiling WPF shell branch.

## Remaining work

- Close the WinUI 3 CI build gate.
- Run the generated EXE on the user's Windows machine.
- Confirm runtime detection now resolves the installed interpreter.
- Address visual or integration defects found by that smoke test.
