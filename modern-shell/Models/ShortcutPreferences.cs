namespace AutoHotkeyUX.Modern.Models;

/// <summary>Holds one immutable set of context-specific shortcuts and action options.</summary>
internal sealed record ShortcutPreferences(
    bool TerminalEnabled = true, string TerminalShortcut = "alt-left", string TerminalProgram = "auto",
    string TerminalPosition = "above", int TerminalWidth = 800, int TerminalHeight = 440, int PointerGap = 12,
    bool ArchiveEnabled = true, string ArchiveShortcut = "alt-left", string ArchiveDestination = "beside",
    string ArchiveFolder = "")
{
    internal static ShortcutPreferences Default { get; } = new();
}

/// <summary>Identifies a complete saved revision for idempotent native acknowledgements.</summary>
internal sealed record ShortcutPreferenceSnapshot(string Revision, ShortcutPreferences Preferences)
{
    internal static ShortcutPreferenceSnapshot Default { get; } = new(Guid.Empty.ToString("N"), ShortcutPreferences.Default);
}
