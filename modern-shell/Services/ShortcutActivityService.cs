using AutoHotkeyUX.Modern.Models;

namespace AutoHotkeyUX.Modern.Services;

/// <summary>Owns bounded event-driven activity for the current manager session without disk polling.</summary>
internal sealed class ShortcutActivityService
{
    internal const int Capacity = 50;
    private readonly object _gate = new();
    private readonly Queue<ShortcutActivity> _entries = new();
    internal event EventHandler? Changed;

    /// <summary>Appends a completed action and discards the oldest entry when the session limit is reached.</summary>
    internal void Record(ShortcutActivity activity)
    {
        lock (_gate)
        {
            _entries.Enqueue(activity);
            while (_entries.Count > Capacity) _entries.Dequeue();
        }
        try { Changed?.Invoke(this, EventArgs.Empty); }
        catch (Exception ex) { ServiceDiagnostics.Write("Shortcut", "An activity subscriber failed.", ex); }
    }

    /// <summary>Returns newest-first immutable entries while actions can complete on a worker.</summary>
    internal IReadOnlyList<ShortcutActivity> Snapshot() { lock (_gate) return _entries.Reverse().ToArray(); }
}
