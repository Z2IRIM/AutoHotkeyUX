# AutoHotkeyUX WinUI 3 UI Redesign — Coding Record

Date: 2026-10-06

## Goal

Replace the functional but plain WinUI shell presentation with the previously approved Windows 11 / WinUI 3 Gallery-inspired visual direction, while preserving the runtime and feature behavior already validated by CI.

## Scope

UI-only redesign of:

- application-wide visual resources
- left NavigationView shell
- Home dashboard
- New Script page
- Settings page
- template selection visual state

The embedded AutoHotkey runtime architecture, registry contracts, script creation semantics, compiler integration and tool-launch behavior were not redesigned in this task.

## Design principles

- Native WinUI 3 controls and theme resources only.
- Keep the standard system title bar for startup compatibility.
- Use Windows theme brushes instead of hard-coded light/dark colors.
- Use a consistent 12 px card radius and shared spacing language.
- Prefer clear hierarchy over dense dashboard decoration.
- Preserve native control states, focus behavior and accessibility.
- Keep all existing event handlers and page navigation contracts.

## Shared visual system

`App.xaml` now provides reusable styles for:

- page titles
- page subtitles
- eyebrow/category labels
- section titles
- surface cards
- interactive card buttons
- icon badges

This removes repeated card styling from individual pages and establishes a single visual language for later pages.

## Navigation shell

`MainWindow.xaml` now uses:

- a cleaner 236 px NavigationView pane
- refreshed Fluent icons
- stronger product identity block
- compact built-in-runtime footer
- native system title bar retained

No custom title bar or custom window chrome was reintroduced.

## Home

The Home page now has:

- clearer workspace header hierarchy
- compact runtime status pill
- prominent New Script hero action
- consistent Window Spy / Compile / Documentation tool cards
- dedicated built-in runtime status surface
- improved descriptions and spacing

Runtime detection and button behavior are unchanged.

## New Script

The New Script page now groups the workflow into:

1. Script details
2. Starter template
3. Preview
4. Final create action

Changes include:

- clearer labels and helper text
- better folder/name layout
- richer template cards
- selected template receives accent border and subtle accent surface
- larger read-only code preview
- explicit non-overwrite messaging
- consolidated create footer

Script creation behavior remains non-destructive and unchanged.

## Settings

Settings are now grouped into:

- Runtime
- Launcher
- Windows integration

The page uses native settings-list-style surfaces, separators, toggles, ComboBox and editor action controls.

Registry keys and saved values remain unchanged.

## Modified files

- `modern-shell/App.xaml`
- `modern-shell/MainWindow.xaml`
- `modern-shell/Pages/HomePage.xaml`
- `modern-shell/Pages/NewScriptPage.xaml`
- `modern-shell/Pages/NewScriptPage.xaml.cs`
- `modern-shell/Pages/SettingsPage.xaml`

## Functional boundaries

Preserved:

- Home → New Script navigation
- Home → Settings navigation
- Window Spy launch
- compiler launch
- documentation launch
- built-in runtime display and management
- script template generation
- folder picker
- collision-safe script creation
- launcher preference
- interpreter preference
- editor command
- update preference

No database/API migration.

## Risk notes

The UI intentionally avoids:

- custom title bar reconstruction
- custom window activation code
- custom control templates
- third-party UI frameworks

This keeps the visual redesign isolated from the recently stabilized WinUI startup and embedded-runtime path.

## Validation

GitHub Actions `Modern WinUI 3 Shell` is the required gate.

The final UI commit must pass:

- .NET build
- single-EXE publish
- executable verification
- startup smoke test
- embedded runtime materialization
- no separate AutoHotkey app registration
- artifact upload

Real-machine visual review is still required because CI can validate XAML/runtime startup but cannot judge spacing, typography or visual balance on the user's Windows 11 desktop.
