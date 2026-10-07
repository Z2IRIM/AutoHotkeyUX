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
        if (!enabled || trigger.Kind != FlowTriggerKind.Hotkey || !ScopesOverlap(trigger, new() { Scope = FlowScopeKind.ExplorerDesktop })) return null;
        foreach (var key in new[] { preferences.TerminalEnabled ? preferences.TerminalShortcut : "", preferences.ArchiveEnabled ? preferences.ArchiveShortcut : "" })
        {
            if (key.Length == 0) continue;
            var modifiers = FlowModifiers.Alt | (key.StartsWith("ctrl-", StringComparison.Ordinal) ? FlowModifiers.Ctrl : FlowModifiers.None);
            var button = key.EndsWith("middle", StringComparison.Ordinal) ? "MButton" : "LButton";
            if (trigger.Modifiers == modifiers && trigger.Key == button)
                return "This mouse shortcut overlaps the built-in Explorer shortcuts. Change one shortcut in Settings or this workflow before running.";
        }
        return null;
    }
    /// <summary>Checks intact managed workflows while allowing the same path to restart under its existing save claim.</summary>
    internal static string? Check(string path, IReadOnlyList<RunningScriptSession> sessions, AutoHotkeySettings settings, string? builtinPath = null)
    {
        if (builtinPath?.Equals(path, StringComparison.OrdinalIgnoreCase) == true)
            return CheckBuiltInAgainstRunning(ReadPreferences(settings), sessions, path);
        var candidate = Read(path, claimHeld: true);
        if (candidate?.Trigger.Kind != FlowTriggerKind.Hotkey) return null;
        var preferences = ReadPreferences(settings);
        var builtin = BuiltIn(candidate.Trigger, preferences, settings.ReadBoolean("Modern", "ExplorerShortcuts", false));
        if (builtin is not null) return builtin;
        foreach (var session in sessions.Where(session => session.State == ScriptState.Running && !session.ScriptPath.Equals(path, StringComparison.OrdinalIgnoreCase)))
        {
            var other = Read(session.ScriptPath);
            if (other is not null && Collides(candidate.Trigger, other.Trigger)) return "This shortcut overlaps the running workflow " + Path.GetFileName(session.ScriptPath) + ". Stop it or change one shortcut before running.";
        }
        return null;
    }
    /// <summary>Rejects built-in activation or preference changes that would collide with an already running workflow.</summary>
    internal static string? CheckBuiltInAgainstRunning(ShortcutPreferences preferences, IReadOnlyList<RunningScriptSession> sessions, string? builtinPath = null)
    {
        foreach (var session in sessions.Where(session => session.State == ScriptState.Running && !session.ScriptPath.Equals(builtinPath, StringComparison.OrdinalIgnoreCase)))
        {
            var flow = Read(session.ScriptPath);
            if (flow is not null && BuiltIn(flow.Trigger, preferences) is not null)
                return "The built-in shortcut would overlap " + Path.GetFileName(session.ScriptPath) + ". Stop that workflow or choose another shortcut before applying.";
        }
        return null;
    }
    /// <summary>Compares modifier/key pairs and only the application scopes that can both be true.</summary>
    internal static bool Collides(FlowTrigger left, FlowTrigger right) => left.Kind == FlowTriggerKind.Hotkey && right.Kind == FlowTriggerKind.Hotkey
        && left.Modifiers == right.Modifiers && left.Key.Equals(right.Key, StringComparison.OrdinalIgnoreCase) && ScopesOverlap(left, right);
    /// <summary>Treats Explorer/desktop and explorer.exe as overlapping, without blocking unrelated applications.</summary>
    private static bool ScopesOverlap(FlowTrigger left, FlowTrigger right) => left.Scope == FlowScopeKind.AnyApplication || right.Scope == FlowScopeKind.AnyApplication
        || left.Scope == FlowScopeKind.ExplorerDesktop && (right.Scope == FlowScopeKind.ExplorerDesktop || right.Application.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase))
        || right.Scope == FlowScopeKind.ExplorerDesktop && left.Application.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase)
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
