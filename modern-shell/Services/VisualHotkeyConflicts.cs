using AutoHotkeyUX.Modern.Models;

namespace AutoHotkeyUX.Modern.Services;

/// <summary>Detects known collisions without disabling built-in shortcuts or claiming external scripts.</summary>
internal static class VisualHotkeyConflicts
{
    /// <summary>Serializes shortcut configuration commits with collision checking and interpreter starts.</summary>
    internal static object MutationGate { get; } = new();
    /// <summary>Reports overlapping built-in Shell mouse shortcuts for a candidate workflow.</summary>
    internal static string? BuiltIn(FlowTrigger trigger, ShortcutPreferences preferences, bool enabled = true)
    {
        if (!enabled || trigger.Kind != FlowTriggerKind.Hotkey) return null;
        foreach (var key in new[] { preferences.TerminalEnabled ? preferences.TerminalShortcut : "", preferences.ArchiveEnabled ? preferences.ArchiveShortcut : "" })
        {
            if (key.Length == 0) continue;
            var modifiers = FlowModifiers.Alt | (key.StartsWith("ctrl-", StringComparison.Ordinal) ? FlowModifiers.Ctrl : FlowModifiers.None);
            var button = key.EndsWith("middle", StringComparison.Ordinal) ? "MButton" : "LButton";
            if (trigger.Modifiers == modifiers && trigger.Key == button && ScopesOverlap(trigger, new() { Scope = FlowScopeKind.ExplorerDesktop, Key = button, Modifiers = modifiers }))
                return "This mouse shortcut overlaps the built-in Explorer shortcuts. Run an intact v3 workflow from Scripts and choose Replace built-in, or change one shortcut before running.";
        }
        return null;
    }
    /// <summary>Checks intact managed workflows while allowing the same path to restart under its existing save claim.</summary>
    internal static string? Check(string path, IReadOnlyList<RunningScriptSession> sessions, AutoHotkeySettings settings, string? builtinPath = null, FlowTrigger? launchTrigger = null)
    {
        if (builtinPath?.Equals(path, StringComparison.OrdinalIgnoreCase) == true)
            return CheckBuiltInAgainstRunning(ReadPreferences(settings), sessions, path);
        var candidate = launchTrigger ?? Read(path, claimHeld: true)?.Trigger;
        if (candidate?.Kind != FlowTriggerKind.Hotkey) return null;
        var preferences = ReadPreferences(settings);
        var builtin = BuiltIn(candidate, preferences, settings.ReadBoolean("Modern", "ExplorerShortcuts", false)
            || sessions.Any(session => session.State == ScriptState.Running && session.ScriptPath.Equals(builtinPath, StringComparison.OrdinalIgnoreCase)));
        if (builtin is not null) return builtin;
        foreach (var session in sessions.Where(session => session.State == ScriptState.Running && !session.ScriptPath.Equals(path, StringComparison.OrdinalIgnoreCase)))
        {
            if (Unknown(session)) return UnknownMessage(session);
            if (session.RegisteredTrigger is { } other && Collides(candidate, other)) return "This shortcut overlaps the running workflow " + Path.GetFileName(session.ScriptPath) + ". Stop it or change one shortcut before running.";
        }
        return null;
    }
    /// <summary>Rejects built-in activation or preference changes that would collide with an already running workflow.</summary>
    internal static string? CheckBuiltInAgainstRunning(ShortcutPreferences preferences, IReadOnlyList<RunningScriptSession> sessions, string? builtinPath = null)
    {
        if (!preferences.TerminalEnabled && !preferences.ArchiveEnabled) return null;
        foreach (var session in sessions.Where(session => session.State == ScriptState.Running && !session.ScriptPath.Equals(builtinPath, StringComparison.OrdinalIgnoreCase)))
        {
            if (Unknown(session)) return UnknownMessage(session);
            if (session.RegisteredTrigger is { } trigger && BuiltIn(trigger, preferences) is not null)
                return "The built-in shortcut would overlap " + Path.GetFileName(session.ScriptPath) + ". Stop that workflow or choose another shortcut before applying.";
        }
        return null;
    }
    /// <summary>Refuses to infer an old process's registered keys from a file that may have changed after launch.</summary>
    private static bool Unknown(RunningScriptSession session) => !session.ShortcutSnapshotKnown &&
        (session.RegisteredTrigger is not null || File.Exists(session.ScriptPath + ".flow.json") || Directory.Exists(session.ScriptPath + ".flow.pending"));
    /// <summary>Explains the required explicit restart for older or damaged managed visual snapshots.</summary>
    private static string UnknownMessage(RunningScriptSession session) => "The registered shortcut of " + Path.GetFileName(session.ScriptPath) + " was not recorded reliably. Restart or stop that workflow before changing/running shortcuts.";
    /// <summary>Compares modifier/key pairs and only the application scopes that can both be true.</summary>
    internal static bool Collides(FlowTrigger left, FlowTrigger right) => left.Kind == FlowTriggerKind.Hotkey && right.Kind == FlowTriggerKind.Hotkey
        && left.Modifiers == right.Modifiers && left.Key.Equals(right.Key, StringComparison.OrdinalIgnoreCase) && ScopesOverlap(left, right);
    /// <summary>Accounts for mouse-under-window Shell predicates as well as foreground-only keyboard scopes.</summary>
    private static bool ScopesOverlap(FlowTrigger left, FlowTrigger right) => left.Scope == FlowScopeKind.AnyApplication || right.Scope == FlowScopeKind.AnyApplication
        || left.Scope == FlowScopeKind.ExplorerDesktop && (left.Key.EndsWith("Button", StringComparison.Ordinal) || right.Scope == FlowScopeKind.ExplorerDesktop || right.Application.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase))
        || right.Scope == FlowScopeKind.ExplorerDesktop && (right.Key.EndsWith("Button", StringComparison.Ordinal) || left.Application.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase))
        || left.Scope == FlowScopeKind.ActiveApplication && right.Scope == FlowScopeKind.ActiveApplication && left.Application.Equals(right.Application, StringComparison.OrdinalIgnoreCase);
    /// <summary>Limits conflict detection to intact known visual files, preserving ordinary/manual-code run behavior.</summary>
    private static VisualFlowDocument? Read(string path, bool claimHeld = false)
    {
        if (!File.Exists(path + ".flow.json")) return null;
        try { return new VisualFlowStore().Open(path, claimHeld).Document; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
        { ServiceDiagnostics.Write("VisualFlow", "Shortcut collision information unavailable for " + path, ex); return null; }
    }
    /// <summary>Matches the core's visible default fallback for corrupt preferences without rewriting registry data.</summary>
    private static ShortcutPreferences ReadPreferences(AutoHotkeySettings settings)
    {
        try { return ShortcutPreferenceCodec.Decode(settings.Read("Modern", ShortcutPreferenceCodec.SettingName)).Preferences; }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
        { ServiceDiagnostics.Write("VisualFlow", "Using the core's default shortcut preferences for collision detection.", ex); return ShortcutPreferences.Default; }
    }
}
