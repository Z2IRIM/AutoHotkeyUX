using Microsoft.Win32;

namespace AutoHotkeyUX.Modern.Services;

/// <summary>
/// Reads and writes the existing AutoHotkey registry-backed UX settings.
/// </summary>
internal sealed class AutoHotkeySettings
{
    private readonly string _baseKey;

    /// <summary>Uses the existing registry contract, with an optional isolated key for service verification.</summary>
    internal AutoHotkeySettings(string baseKey = @"Software\AutoHotkey") => _baseKey = baseKey;

    /// <summary>
    /// Reads a string value from the existing HKCU AutoHotkey settings hierarchy.
    /// </summary>
    internal string Read(
        string section,
        string name,
        string defaultValue = "")
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            $@"{_baseKey}\{section}");
        return key?.GetValue(name)?.ToString() ?? defaultValue;
    }

    /// <summary>
    /// Writes a string value using the same schema as inc/config.ahk.
    /// </summary>
    internal void Write(
        string section,
        string name,
        string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(
            $@"{_baseKey}\{section}");
        key.SetValue(name, value, RegistryValueKind.String);
    }

    /// <summary>
    /// Reads a boolean while accepting both legacy string and DWORD forms.
    /// </summary>
    internal bool ReadBoolean(
        string section,
        string name,
        bool defaultValue)
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            $@"{_baseKey}\{section}");
        var value = key?.GetValue(name);

        return value switch
        {
            int number => number != 0,
            string text when bool.TryParse(text, out var parsed) => parsed,
            string text when int.TryParse(text, out var number) => number != 0,
            _ => defaultValue
        };
    }

    /// <summary>
    /// Writes a registry DWORD boolean compatible with existing AutoHotkeyUX reads.
    /// </summary>
    internal void WriteBoolean(
        string section,
        string name,
        bool value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(
            $@"{_baseKey}\{section}");
        key.SetValue(
            name,
            value ? 1 : 0,
            RegistryValueKind.DWord);
    }

    /// <summary>
    /// Reads the effective Edit Script shell command.
    /// </summary>
    internal string ReadEditorCommand()
    {
        using var key = Registry.ClassesRoot.OpenSubKey(
            @"AutoHotkeyScript\shell\edit\command");
        return key?.GetValue(null)?.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Writes the per-user Edit Script shell command without elevation.
    /// </summary>
    internal void WriteEditorCommand(string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(
            @"Software\Classes\AutoHotkeyScript\shell\edit\command");
        key.SetValue(
            null,
            command,
            RegistryValueKind.String);
    }
}
