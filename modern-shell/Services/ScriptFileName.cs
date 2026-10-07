using System.Text.RegularExpressions;

namespace AutoHotkeyUX.Modern.Services;

/// <summary>Shares script name normalization between legacy and visual creation.</summary>
internal static class ScriptFileName
{
    /// <summary>Normalizes one Windows file stem and rejects device names that cannot safely identify scripts.</summary>
    internal static string Normalize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var stem = new string((value ?? "").Where(character => !invalid.Contains(character)).ToArray()).Trim();
        if (stem.EndsWith(".ahk", StringComparison.OrdinalIgnoreCase)) stem = stem[..^4];
        stem = stem.TrimEnd(' ', '.');
        if (stem.Length == 0) stem = "Untitled";
        if (stem.Length > 128 || Regex.IsMatch(stem.Split('.')[0].TrimEnd(' '), "^(CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new ArgumentException("Choose a script name up to 128 characters without a reserved Windows device name.");
        return stem;
    }
}
