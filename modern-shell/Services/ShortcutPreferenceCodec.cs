using AutoHotkeyUX.Modern.Models;
using System.Globalization;

namespace AutoHotkeyUX.Modern.Services;

/// <summary>Shares a strict bounded registry/message format with the versioned AHK preference module.</summary>
internal static class ShortcutPreferenceCodec
{
    internal const string SettingName = "ShortcutPreferences";
    internal const int MaximumCharacters = 8192;
    internal static readonly string[] Shortcuts = ["alt-left", "ctrl-alt-left", "alt-middle", "ctrl-alt-middle"];
    private static readonly string[] Fields = ["schema", "revision", "terminal-enabled", "terminal-key", "program", "position",
        "width", "height", "gap", "archive-enabled", "archive-key", "destination", "folder"];

    /// <summary>Serializes an entire snapshot to a single registry value without partial field updates.</summary>
    internal static string Encode(ShortcutPreferenceSnapshot snapshot)
    {
        if (!Guid.TryParseExact(snapshot.Revision, "N", out _)) throw new InvalidDataException("Invalid preference revision.");
        var p = snapshot.Preferences;
        var errors = Validate(p, false);
        if (errors.Count != 0) throw new InvalidDataException(string.Join(" ", errors.Values));
        return string.Join('\n', "schema=1", "revision=" + snapshot.Revision,
            "terminal-enabled=" + (p.TerminalEnabled ? "1" : "0"), "terminal-key=" + p.TerminalShortcut,
            "program=" + p.TerminalProgram, "position=" + p.TerminalPosition,
            "width=" + p.TerminalWidth.ToString(CultureInfo.InvariantCulture),
            "height=" + p.TerminalHeight.ToString(CultureInfo.InvariantCulture),
            "gap=" + p.PointerGap.ToString(CultureInfo.InvariantCulture),
            "archive-enabled=" + (p.ArchiveEnabled ? "1" : "0"), "archive-key=" + p.ArchiveShortcut,
            "destination=" + p.ArchiveDestination, "folder=" + p.ArchiveFolder);
    }

    /// <summary>Rejects unknown, duplicate, malformed and oversized snapshots rather than applying a partial configuration.</summary>
    internal static ShortcutPreferenceSnapshot Decode(string text)
    {
        if (text.Length == 0) return ShortcutPreferenceSnapshot.Default;
        if (text.Length > MaximumCharacters || text.Contains('\r') || text.Contains('\0'))
            throw new InvalidDataException("The shortcut preference snapshot is malformed or too large.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in text.Split('\n'))
        {
            var separator = line.IndexOf('=');
            if (separator < 1 || !Fields.Contains(line[..separator], StringComparer.Ordinal)
                || !values.TryAdd(line[..separator], line[(separator + 1)..]))
                throw new InvalidDataException("The shortcut preference snapshot contains an unknown or duplicate field.");
        }
        if (values.Count != Fields.Length || values["schema"] != "1" || !Guid.TryParseExact(values["revision"], "N", out _))
            throw new InvalidDataException("The shortcut preference schema or revision is unsupported.");
        var p = new ShortcutPreferences(Boolean(values["terminal-enabled"]), values["terminal-key"], values["program"], values["position"],
            Number(values["width"]), Number(values["height"]), Number(values["gap"]), Boolean(values["archive-enabled"]),
            values["archive-key"], values["destination"], values["folder"]);
        var errors = Validate(p, false);
        if (errors.Count != 0) throw new InvalidDataException(string.Join(" ", errors.Values));
        return new(values["revision"], p);
    }

    /// <summary>Returns field-specific errors shared by draft UI and save-time validation.</summary>
    internal static IReadOnlyDictionary<string, string> Validate(ShortcutPreferences p, bool checkFolder)
    {
        var errors = new Dictionary<string, string>();
        if (!Shortcuts.Contains(p.TerminalShortcut)) errors[nameof(p.TerminalShortcut)] = ShortcutError(p.TerminalShortcut);
        if (!Shortcuts.Contains(p.ArchiveShortcut)) errors[nameof(p.ArchiveShortcut)] = ShortcutError(p.ArchiveShortcut);
        if (p.TerminalProgram is not ("auto" or "wt" or "powershell")) errors[nameof(p.TerminalProgram)] = "Choose a supported terminal program.";
        if (p.TerminalPosition is not ("above" or "center")) errors[nameof(p.TerminalPosition)] = "Choose a supported window position.";
        if (p.TerminalWidth is < 480 or > 1600) errors[nameof(p.TerminalWidth)] = "Width must be between 480 and 1600.";
        if (p.TerminalHeight is < 240 or > 1000) errors[nameof(p.TerminalHeight)] = "Height must be between 240 and 1000.";
        if (p.PointerGap is < 0 or > 64) errors[nameof(p.PointerGap)] = "Pointer gap must be between 0 and 64.";
        if (p.ArchiveDestination is not ("beside" or "custom")) errors[nameof(p.ArchiveDestination)] = "Choose an extraction destination.";
        if (p.ArchiveFolder.Length > 2048 || p.ArchiveFolder.Any(char.IsControl))
            errors[nameof(p.ArchiveFolder)] = "The destination path is invalid or too long.";
        else if (p.ArchiveDestination == "custom")
        {
            if (!IsAbsoluteFolder(p.ArchiveFolder)) errors[nameof(p.ArchiveFolder)] = "Choose an absolute destination folder.";
            else if (checkFolder && !Directory.Exists(p.ArchiveFolder)) errors[nameof(p.ArchiveFolder)] = "The destination folder does not exist or is unavailable.";
        }
        return errors;
    }

    /// <summary>Rejects relative paths and device namespaces before using a user-selected output root.</summary>
    internal static bool IsAbsoluteFolder(string folder)
    {
        try
        {
            return Path.IsPathFullyQualified(folder) && folder.IndexOfAny(Path.GetInvalidPathChars()) < 0
                && !folder.StartsWith(@"\\?\", StringComparison.Ordinal) && !folder.StartsWith(@"\\.\", StringComparison.Ordinal)
                && folder == folder.Trim() && !folder.Any(char.IsControl);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { return false; }
    }

    /// <summary>Explains the one reserved Explorer selection combination distinctly from unsupported keys.</summary>
    private static string ShortcutError(string shortcut) => shortcut == "ctrl-left"
        ? "Ctrl + left click is used for Explorer selection. Add Alt or choose another shortcut."
        : "Choose a supported shortcut.";

    /// <summary>Accepts only exact protocol booleans.</summary>
    private static bool Boolean(string value) => value switch { "1" => true, "0" => false, _ => throw new InvalidDataException("Invalid shortcut boolean.") };

    /// <summary>Rejects signs, whitespace and non-integral numeric protocol values.</summary>
    private static int Number(string value) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
        ? number : throw new InvalidDataException("Invalid shortcut dimension.");
}
