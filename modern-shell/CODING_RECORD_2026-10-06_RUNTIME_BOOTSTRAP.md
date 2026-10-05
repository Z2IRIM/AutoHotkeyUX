# AutoHotkeyUX Runtime Bootstrap — Coding Record

Date: 2026-10-06

## Goal

Handle the verified case where AutoHotkey v2 is not installed at all, instead of continuing to report an ambiguous runtime-detection error.

## Evidence

The user's Windows machine reported all of the following:

- no AutoHotkey executable on PATH;
- no HKCU/HKLM AutoHotkey installation registry key;
- no AutoHotkey uninstall registration;
- no `.ahk` file association;
- no AutoHotkey files in common machine-wide or per-user install locations;
- `winget list AutoHotkey` returned no installed package.

Conclusion: runtime detection was correctly returning no result because AutoHotkey was not installed.

## Modified / added files

- `modern-shell/Pages/HomePage.xaml.cs`
- `modern-shell/Services/AutoHotkeyInstallerService.cs`

## Main changes

- Missing runtime state now says `AutoHotkey not installed` instead of `Runtime not detected`.
- Runtime action becomes `Install AutoHotkey` when no runtime is present.
- Added automatic stable-version resolution from the official AutoHotkey 2.0 download source.
- Added official setup download to a temporary application directory.
- Added SHA-256 verification against the official `.sha256` sidecar before launch.
- The official setup executable is launched only after the checksum matches.
- The shell waits for setup to finish and refreshes runtime detection afterward.
- Existing installed-runtime behavior remains unchanged.

## Security decision

The application does not execute an unverified downloaded installer.

The downloaded AutoHotkey setup package is SHA-256 checked against the checksum published alongside the same release by the official AutoHotkey download server. A mismatch aborts installation.

## Validation

GitHub Windows CI build / startup validation is used as the compile gate.

Real-machine verification is still required for the download, installer UI, completed installation and post-install runtime refresh.

## Risk

Low-to-medium.

The existing runtime path is unchanged. The new path performs an HTTPS network download and starts the official AutoHotkey installer only when the runtime is absent.

## Database / API / migration impact

None.

## Compatibility impact

- Existing installed AutoHotkey users: unchanged.
- Machines without AutoHotkey: can bootstrap the latest stable v2 directly from the shell.
- C++ AutoHotkey runtime: unchanged.
- Existing AutoHotkeyUX scripts: unchanged.

## Rollback

Revert the runtime-bootstrap commits on `modern-wpf-shell`.

## Remaining work

- Confirm CI build passes with the installer service.
- Real-machine click test of `Install AutoHotkey`.
- Confirm AutoHotkey installation is detected after setup finishes.
- WinUI 3 Gallery-inspired visual redesign remains separate and requires preview approval before UI implementation.
