# AutoHotkeyUX Gallery Shell Implementation — Coding Record

Date: 2026-10-06

## Goal

Implement the user-approved WinUI 3 Gallery-style preview as the real native WinUI shell, including the refined sidebar selection treatment.

## Visual target

The selected design direction is the approved Gallery preview with:

- integrated title bar
- Mica backdrop
- lightweight left navigation
- subtle selected-item fill
- short rounded accent indicator
- compact search surface
- Gallery-style Home layout
- right-side Getting Started / Examples rail
- compact Runtime row
- Recent Scripts surface

## Modified files

- `modern-shell/App.xaml`
- `modern-shell/MainWindow.xaml`
- `modern-shell/MainWindow.xaml.cs`
- `modern-shell/Pages/HomePage.xaml`
- `modern-shell/Pages/HomePage.xaml.cs`

## Navigation

The default `NavigationView` pane was replaced by a purpose-built sidebar because the stock selected state was visually too heavy for the approved target.

The new `SidebarNavButtonStyle` uses:

- transparent normal state
- very light hover surface
- subtle selected surface
- 3 px × 16 px rounded accent indicator
- accent icon only
- normal primary text color

The selection remains mutually exclusive in code-behind.

## Window chrome

The shell now:

- sets `ExtendsContentIntoTitleBar = true`
- uses the app-owned `AppTitleBar` as the drag region
- preserves native Windows caption buttons
- sets caption button backgrounds transparent
- enables `MicaBackdrop`
- falls back safely if the running Windows build does not support the customization

No fake minimize/maximize/close controls were introduced.

## Search

The title bar contains a native `AutoSuggestBox`.

Queries route to existing functionality:

- settings / preferences → Settings
- new / create → New Script
- window spy → Window Spy
- compile → compiler flow
- docs / help → documentation
- unrecognized query → Home

No new backend or persistence was added.

## Home

The Home page now follows the approved layout:

- Welcome / Do more with AutoHotkey header
- compact Create Script hero
- three quick-action cards
- compact runtime card with Ready badge
- actual Recent Scripts list
- responsive Getting Started / Examples rail

The right rail collapses on narrower windows.

## Recent Scripts

Recent Scripts is backed by real local data rather than fake preview rows.

Source:

`%USERPROFILE%\Documents\AutoHotkey\*.ahk`

Behavior:

- sort by modification time descending
- show the latest three files
- display compact relative time
- show an empty state when no files exist
- Open folder launches the normal Documents\AutoHotkey directory

## Functional boundaries

Preserved:

- embedded private AutoHotkey runtime
- Window Spy launch
- compiler launch
- documentation launch
- New Script page
- Settings page
- existing registry/settings contracts
- startup diagnostics
- single-EXE packaging

No database/API migration.

## Risk

Medium visual/runtime risk because custom title-bar integration and Mica are reintroduced after the earlier compatibility-first shell.

Mitigation:

- native WinUI APIs only
- caption buttons remain system-owned
- window chrome customization is guarded with fallback
- existing startup smoke test remains the blocking CI gate

## Validation

GitHub Actions `Modern WinUI 3 Shell` is the blocking compile / package / startup gate.

A local Windows 11 visual pass is still required after CI because automated CI cannot judge visual fidelity, Mica appearance, DPI spacing or caption-button alignment.


## Final CI result

GitHub Actions Run #52:

- Build: PASS
- Publish single EXE: PASS
- Verify executable: PASS
- Launch smoke test: PASS
- Embedded runtime materialization: PASS
- No separate AutoHotkey app registration: PASS
- Artifact upload: PASS

Validated UI/runtime head:

`94b7f84f69d3d9cc8929e8d45cb8d88c624cf6d9`

The custom Mica title bar, custom sidebar and rebuilt Home page all passed the startup diagnostics gate.
