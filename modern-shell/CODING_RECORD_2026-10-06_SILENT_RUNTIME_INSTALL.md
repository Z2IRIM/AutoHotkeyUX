# AutoHotkeyUX Silent Runtime Install — Coding Record

Date: 2026-10-06

## Goal

Integrate AutoHotkey runtime installation into the modern WinUI shell without showing the legacy AutoHotkey setup/dashboard UI after installation.

## Source-derived behavior

The upstream AutoHotkey installer supports the command-line arguments:

- `/silent`
- `/user`
- `/to <install directory>`

The installer only opens `UX\ui-dash.ahk` after a fresh install when `Silent` is false.

## Modified file

- `modern-shell/Services/AutoHotkeyInstallerService.cs`

## Main change

The verified official setup package is now launched as:

`/silent /user /to "%LOCALAPPDATA%\Programs\AutoHotkey"`

This preserves the existing official installation logic while suppressing the legacy setup/dashboard UI.

## Security

No change to the existing download security model:

- installer is fetched from the official AutoHotkey download source;
- the matching official SHA-256 sidecar is downloaded;
- the installer is executed only after the checksum matches.

## Compatibility

- AutoHotkey runtime remains a normal official installation.
- Existing file associations and UX integration continue to be created by the official installer.
- The modern shell remains the user-facing install experience.
- The legacy dashboard is still installed as part of AutoHotkeyUX, but it is no longer launched automatically by this integrated install flow.

## Database / API / migration impact

None.

## Risk

Low.

The change only switches the official installer from interactive mode to its upstream-supported silent user-install mode.

## Rollback

Revert commit:

`5031f354aff9f7005cceb9d5710803dc4d3bb684`

## Remaining consideration

The official Start Menu shortcut for the legacy AutoHotkey Dash is still created by the upstream installer. Replacing that shortcut with the modern shell should be handled separately once the modern shell has a stable installed path.
