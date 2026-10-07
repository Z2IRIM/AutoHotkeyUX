# Shortcut Preferences Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan in the current session. Steps use checkbox syntax for tracking. User has approved the preview and requested implementation; continue within that scope without another approval handoff.

**Goal:** Deliver the approved Explorer shortcut preferences page with real runtime configuration and bounded recent activity.

**Architecture:** One complete Gate. Typed immutable preferences and a bounded codec use the existing registry owner; v3 AHK registers and persists live changes atomically. Existing extraction queue captures destination preferences and publishes results to a session history owner; the WinUI page edits a draft and subscribes only while visible.

**Tech Stack:** Existing .NET 8 / WinUI 3 / AutoHotkey v2 / SharpCompress 0.50.3.

**Spec:** `modern-shell/SHORTCUT_PREFERENCES_DESIGN_2026-10-07.md`

## Global Constraints

- Preserve approved green H icon, main window proportion, existing script sessions and unrelated RelayPrompt.md.
- Defaults: Alt + left click for both context-specific actions; terminal 800 × 440 DIP, gap 12 DIP; archive output beside source.
- Accepted ranges: width 480–1600, height 240–1000, gap 0–64. Reject Ctrl + left click; validate absolute existing custom destination at save.
- Atomic registry snapshot with schema 1 and unique revision; AHK v3 uses cached settings, notification on change and idempotent acknowledgements.
- Preserve edited built-in root scripts/modules; upgrade only hash-confirmed original predecessors, with backup.
- Recent activity: this session, at most 50 entries; accepted archive jobs keep their original destination snapshot.
- Retain extraction protections and keep both original archives and existing output.
- Add brief duty comments to new/modified functions; avoid unrelated refactors and unnecessary test suites.
- Coding record and incremental source ZIP under C:\DESKTOP\srcpack_Area\AutoHotkeyUX; no tests, runtime or dependency binaries in the source ZIP.

## Review Focus

- Delayed/lost IPC reply: distinguish confirmed saved state from uncertain acknowledgement; never blindly replay extraction.
- Custom root or helper edits: configuration must not replace user source or restart an incompatible custom script.
- Master enable/disable racing a save: one serialization owner and correct final state.
- Destination changed while archive is queued: frozen destination and failure cleanup stay within the generated staging folder.
- Disabled actions, same keys, unavailable Windows Terminal and top/negative-coordinate monitors: correct context and visible diagnostics.

## Task 1: Complete shortcut preferences Gate

**Files / ownership:**
- New `Models/ShortcutPreferences.cs`, `Models/ShortcutActivity.cs`: immutable typed values.
- New `Services/ShortcutPreferenceCodec.cs`, `ShortcutPreferencesService.cs`, `ShortcutPreferencesRuntime.cs`, `ShortcutActivityService.cs`: validation/protocol, one save owner, native AHK channel, bounded session history.
- Modify `Services/ApplicationServices.cs`, `ScriptStartupService.cs`, `ExplorerShortcutInstaller.cs`, `ShortcutCommandService.cs`, `ArchiveExtractionService.cs`, `App.xaml.cs`: reuse service composition, startup, migration and queue.
- New versioned `Resources/ExplorerShortcuts/v3/{Shell,Actions,Preferences}.ahk`; modify root resource and csproj resource map, leaving v2 source intact.
- New `Pages/ShortcutSettingsPage.xaml{,.cs}`, page placement/activity/diagnostics partials as needed; modify Settings and MainWindow route/diagnostics.
- Modify `WindowSpyService.cs`/native metadata boundary only for the known ClassNN limit issue.
- Targeted checks in existing `tools/ModernShell.SmokeTests`; add native verification through the existing explicit diagnostic commands.
- New `SCRIPT_CREATION_GUIDE.md`, `examples/OpenDocuments.ahk`, coding record and update README.

**Interfaces:**
- `ShortcutPreferencesService.Saved` returns immutable saved values; `SaveAsync(ShortcutPreferences)` validates and applies one snapshot. Changed also refreshes persisted values after an explicitly unconfirmed acknowledgement.
- `ShortcutPreferenceCodec.Encode/Decode` implements exact schema/revision and strictly bounded named fields; `Validate(preferences, checkFolder)` checks shared limits.
- `ShortcutPreferencesRuntime.Apply(snapshot)` checks owned script identity and an ephemeral token, sends a bounded configure message, and resolves read-back acknowledgement.
- `ArchiveExtractionService.Extract(string, string? destinationRoot)` retains current sibling behavior with null and adds unique per-archive output under a chosen root.
- `ShortcutActivityService.Record(ShortcutActivity)` / `Snapshot()` / Changed are thread-safe and limited to 50.

- [ ] Back up baseline source, approved preview and deployed EXE; record SHA and live session identities before code edits.
- [ ] Add minimal failing service checks for invalid settings, snapshot round-trip/unknown data, fixed-destination extraction, bounded history, custom v2 preservation and transaction failure. Run and record RED.
- [ ] Implement typed preferences, codec, native runtime channel and v3 migration. Verify with isolated registry/process fixtures, including rejected configurations preserving old settings.
- [ ] Connect frozen extraction options and real terminal/extraction history, preserving bounded queue and shutdown drain. Cover a queued destination change and failure isolation.
- [ ] Implement approved WinUI page and Settings secondary route; fields edit a draft, save/discard preserve it correctly, recent details expand inline, placement preview reacts to dimensions and click point, narrow layout reflows.
- [ ] Correct ClassNN unverified ordinal and verify beyond-enumeration behavior with a focused native fixture.
- [ ] Run covering service checks, AHK syntax/runtime configuration and terminal placement, native WinUI page verification; build/publish once code is final. Read actual outputs and stop testing once risk-matched evidence is sufficient.
- [ ] Commit feature, generate full change review package, dispatch one fresh reviewer under requesting-code-review; address material findings with targeted regression checks.
- [ ] Deliver source guide/example, Markdown coding record, incremental ZIP, merge into original branch and deploy approved app. Verify new manager and owned built-in runtime, preserving other scripts.
