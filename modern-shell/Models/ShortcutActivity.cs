namespace AutoHotkeyUX.Modern.Models;

/// <summary>Describes one completed shortcut action without retaining script contents.</summary>
internal sealed record ShortcutActivity(DateTimeOffset Time, string Action, bool Succeeded, string Source,
    long DurationMilliseconds, string Detail, string? Destination = null);
