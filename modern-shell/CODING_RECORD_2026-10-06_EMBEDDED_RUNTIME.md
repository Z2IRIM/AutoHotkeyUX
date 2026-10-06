# AutoHotkeyUX Embedded Runtime Architecture — Coding Record

Date: 2026-10-06

## Goal

Eliminate the separate AutoHotkey installation from the Windows Apps list by bundling the AutoHotkey v2 portable runtime inside the modern WinUI single-file EXE.

## Architecture

Build-time:

1. Download pinned AutoHotkey v2.0.29 portable ZIP from the official download source.
2. Verify SHA-256:
   `B2D0200724A6B6AD22C965C939C5E5A2C64A35D1CCB455A3CA3F8CE415C5A296`.
3. Package the AutoHotkeyUX scripts required by the modern shell into a second ZIP.
4. Embed both ZIPs and a generated manifest into the C# assembly.
5. Publish the WinUI app as the existing single EXE.

Runtime:

1. Read the embedded manifest and payloads.
2. Verify runtime and UX payload hashes.
3. Extract into:
   `%LOCALAPPDATA%\AutoHotkeyUX.Modern\runtime\<version>\`
4. Place the portable core under `v2\` and UX scripts under `UX\`.
5. Prefer this built-in runtime over registry, PATH, App Paths and external AutoHotkey installations.
6. Clean up old private runtime versions after the new version is ready.

## Modified / added files

- `modern-shell/prepare-embedded-runtime.ps1`
- `modern-shell/Services/EmbeddedAutoHotkeyRuntime.cs`
- `modern-shell/Services/AutoHotkeyRuntimeLocator.cs`
- `modern-shell/Services/AutoHotkeyIntegration.cs`
- `modern-shell/MainWindow.xaml.cs`
- `modern-shell/Pages/HomePage.xaml.cs`
- `modern-shell/AutoHotkeyUX.Modern.csproj`
- `modern-shell/RuntimePayload/.gitignore`
- `.github/workflows/modern-winui-shell.yml`
- `modern-shell/README.md`
- `modern-shell/THIRD_PARTY_NOTICES.md`

Removed:

- `modern-shell/Services/AutoHotkeyInstallerService.cs`

## Behavior changes

- A separate AutoHotkey install is no longer required.
- The app no longer downloads or runs AutoHotkey setup.exe at runtime.
- No AutoHotkey uninstall entry is created by the modern shell.
- No old AutoHotkey Dash UI is launched by the modern shell.
- The bundled runtime is preferred even if AutoHotkey is separately installed.
- Window Spy is launched explicitly with the bundled interpreter instead of depending on the Windows .ahk association.

## Safety and recovery

- Build fails if the downloaded portable ZIP does not match the pinned SHA-256.
- Runtime validates the embedded payload before extraction.
- ZIP extraction rejects path traversal.
- Extraction is staged before replacing the active private runtime directory.
- Old private runtime cleanup is best-effort and never blocks startup.
- Existing system AutoHotkey installations are not automatically removed.

## Migration note for the current test PC

The existing AutoHotkey instance previously installed during testing remains in Windows Apps. Once the bundled-runtime build is validated on the real PC, that separate AutoHotkey installation can be uninstalled manually; the modern app should continue using its private runtime.

## Compatibility

- AutoHotkey v2 runtime remains unmodified.
- Existing external AutoHotkey installations remain available as fallback.
- Existing project registry/settings behavior is preserved.
- No database or API migration.

## Remaining work

- Real-machine verification after pulling the latest branch.
- Confirm the single EXE extracts and uses the built-in runtime after the system AutoHotkey installation is removed.
- The optional Windows `.ahk` association flow should later be updated to explicitly target the private runtime.
- WinUI 3 Gallery-inspired visual redesign remains separate and still requires preview approval before UI implementation.


## Windows Apps regression guard

The CI startup smoke test snapshots the AutoHotkey uninstall/app registration keys before launching the modern shell and fails if the shell creates either key:

- `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\AutoHotkey`
- `HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\AutoHotkey`

The same smoke test also requires the private `AutoHotkey64.exe` and `UX\WindowSpy.ahk` files to be materialized. This directly guards the product requirement that the bundled runtime must not create a second Windows Apps entry.
