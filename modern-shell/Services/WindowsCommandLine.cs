using System.Runtime.InteropServices;

namespace AutoHotkeyUX.Modern.Services;

/// <summary>Uses the Windows parser for existing editor and activation commands with quoted paths.</summary>
internal static class WindowsCommandLine
{
    /// <summary>Splits a Windows command line without invoking a command shell.</summary>
    internal static string[] Split(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return [];
        var pointer = CommandLineToArgvW(command, out var count);
        if (pointer == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
        try
        {
            return Enumerable.Range(0, count).Select(index => Marshal.PtrToStringUni(
                Marshal.ReadIntPtr(pointer, index * IntPtr.Size)) ?? string.Empty).ToArray();
        }
        finally { LocalFree(pointer); }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(string command, out int count);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr pointer);
}
