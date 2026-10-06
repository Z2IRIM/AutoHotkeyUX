# AutoHotkeyUX Simplified 125% Desktop UI — Coding Record

Date: 2026-10-06

## Goal

Implement the final approved desktop layout after real-machine review:

- remove the Home right rail;
- remove decorative H logos;
- enlarge the UI by roughly 25%;
- prevent narrow-window compression from destroying the desktop layout;
- preserve the existing WinUI/Mica/runtime behavior.

## Changes

### Shell

`modern-shell/MainWindow.xaml`

- Removed the square H logo from the title bar.
- Removed the square H logo from the sidebar identity block.
- Increased title bar height from 48 to 56.
- Increased sidebar width from 220 to 240.
- Increased search box from 340×32 to 430×40.
- Increased sidebar typography and navigation control sizes.
- Kept the refined RadioButton navigation selection treatment.
- Kept Mica and native Windows caption buttons.

### Desktop minimum size

`modern-shell/MainWindow.xaml.cs`

The app now starts at 1280×820 and prevents resizing below:

- width: 980 px
- height: 680 px

The guard uses `AppWindow.Changed` / `DidSizeChange`.

This intentionally treats AutoHotkeyUX as a desktop workspace instead of attempting to squeeze the UI into a phone-like layout.

### Global visual scale

`modern-shell/App.xaml`

Shared visual tokens were increased by approximately 25%:

- page title: 28 → 35
- page subtitle: 13 → 16
- section title: 14 → 18
- eyebrow: 11 → 13
- navigation height: 40 → 50
- navigation text: 13 → 16
- navigation icon: 16 → 19
- card padding / icon badges / radii increased proportionally

No ScaleTransform is used. Controls and typography are rendered at their real sizes.

### Home

`modern-shell/Pages/HomePage.xaml`

- Removed Getting Started.
- Removed Examples.
- Removed the second/right column completely.
- Home is now a single primary desktop content column.
- Increased header, hero, tool cards, runtime row and recent scripts sizing.
- Tool cards use adaptive layout:
  - wide desktop: 3 columns;
  - narrower desktop: 2 cards on the first row and Documentation spanning the second row.
- Runtime path continues to use ellipsis instead of forcing layout expansion.

### New Script

`modern-shell/Pages/NewScriptPage.xaml`

- Increased typography, field spacing, template card height, preview height and action sizes.
- No script-creation logic changed.

### Settings

`modern-shell/Pages/SettingsPage.xaml`

- Increased typography, row padding, runtime icon size and form/control widths.
- No registry or settings contracts changed.

## Functional boundaries

Unchanged:

- embedded AutoHotkey v2 runtime
- runtime discovery
- Window Spy
- compiler integration
- documentation
- New Script behavior
- Settings persistence
- Recent Scripts
- Mica
- PerMonitorV2 DPI awareness
- single-EXE packaging

## Validation

GitHub Actions Run #61 is the validation run for the final executable source.

At record creation time:

- Build: PASS
- Publish single EXE: PASS
- Verify executable: PASS
- Launch smoke test: PASS
- Artifact upload: running

The Windows smoke test therefore confirms the updated shell starts successfully and the embedded runtime path remains intact.

## Real-machine verification

After pulling the branch:

```powershell
cd "C:\Users\Z\Downloads\New folder\AutoHotkeyUX"
git pull --ff-only origin modern-wpf-shell

dotnet run --project .\modern-shell\AutoHotkeyUX.Modern.csproj -c Release -p:Platform=x64
```

Review:

1. Home has no right-side rail.
2. No H logo appears in the title bar or sidebar header.
3. UI is visibly larger than the previous build.
4. The window stops shrinking before the content becomes unusable.
5. Only the current sidebar page remains selected.
