namespace AutoHotkeyUX.Modern.Models;

internal enum ScriptState { Starting, Running, Stopping, Stopped, Failed }

/// <summary>Reports only the lifetime of an interpreter owned by this manager.</summary>
internal sealed record RunningScriptSession(
    string ScriptPath,
    int? ProcessId,
    DateTime? ProcessStartUtc,
    DateTime StartedUtc,
    ScriptState State,
    string? Error = null);

/// <summary>Persists the minimum identity needed to reject recycled Windows PIDs.</summary>
internal sealed record PersistedScriptSession(string ScriptPath, int Pid, DateTime ProcessStartUtc);
