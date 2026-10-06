# AutoHotkeyUX DPI + Sidebar State Fix — Coding Record

Date: 2026-10-06

## Problem

Real-machine Windows 11 review exposed two issues:

1. sidebar navigation left stale accent indicators on previously visited tabs;
2. the whole WinUI surface looked bitmap-scaled / soft at the user's display scaling.

## Root causes

### Sidebar

The custom navigation used independent `ToggleButton` controls. The template only defined the visual changes for `Checked`; `Unchecked` and `Indeterminate` did not explicitly restore the selection indicator and icon state.

### DPI

The project's custom `app.manifest` replaced the normal WinUI template manifest but did not declare DPI awareness.

For desktop apps, an absent DPI-awareness declaration can leave the process DPI-unaware and allow Windows to bitmap-scale the window.

## Fix

### Per-monitor DPI

`modern-shell/app.manifest` now declares:

- legacy `dpiAware = true/pm`
- modern `dpiAwareness = PerMonitorV2`

This lets WinUI render at the monitor's native DPI instead of relying on whole-window bitmap scaling.

### Native mutually-exclusive navigation

The sidebar now uses `RadioButton` controls with:

`GroupName="PrimaryNavigation"`

instead of independent ToggleButtons.

The visual template also explicitly resets all unchecked states:

- selected background → transparent
- accent indicator → collapsed
- icon → secondary text color

Hover feedback is rendered on a separate overlay so hover and checked states no longer fight over the same background property.

### Pixel alignment

The root shell enables `UseLayoutRounding="True"` so one-pixel strokes and layout boundaries are aligned consistently.

## Modified files

- `modern-shell/app.manifest`
- `modern-shell/App.xaml`
- `modern-shell/MainWindow.xaml`
- `modern-shell/MainWindow.xaml.cs`

## Expected result

- only the current tab has the short accent indicator;
- switching tabs removes the old tab indicator immediately;
- no multi-highlight state;
- UI should render sharply at Windows scaling values such as 125%, 150%, and on mixed-DPI monitor setups;
- native Windows caption buttons and Mica remain unchanged.

## Functional impact

None to:

- embedded AutoHotkey runtime
- script creation
- Window Spy
- compiler
- documentation
- Settings persistence
- recent scripts
- single-EXE packaging

## Verification

GitHub Actions `Modern WinUI 3 Shell` is the compile/package/startup gate.

Real-machine visual verification is still required because CI runners cannot visually confirm text sharpness at the user's actual monitor DPI.
