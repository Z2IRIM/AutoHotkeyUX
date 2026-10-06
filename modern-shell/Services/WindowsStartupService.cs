using Microsoft.Win32;

namespace AutoHotkeyUX.Modern.Services;

/// <summary>Owns a single current-user login startup entry for this portable application.</summary>
internal sealed class WindowsStartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AutoHotkeyUX.Modern";
    internal bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return !string.IsNullOrWhiteSpace(key?.GetValue(ValueName) as string);
        }
    }

    /// <summary>Registers the current executable with --background, or removes only this app's entry.</summary>
    internal void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("The executable path is unavailable.");
            var command = $"\"{executable}\" --background";
            if (command.Length > 260) throw new InvalidOperationException("Move the portable EXE to a shorter path before enabling login startup.");
            key.SetValue(ValueName, command, RegistryValueKind.String);
        }
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
        ServiceDiagnostics.Write("WindowsStartup", enabled ? "Silent sign-in startup enabled." : "Sign-in startup disabled.");
    }

    /// <summary>Repairs this app's previously enabled command after the portable executable is moved.</summary>
    internal void RefreshExecutablePath()
    { if (IsEnabled) SetEnabled(true); }
}
