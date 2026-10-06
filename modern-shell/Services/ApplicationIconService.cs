using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AutoHotkeyUX.Modern.Services;

/// <summary>Owns window/tray HICON handles loaded directly from the embedded, approved application icon.</summary>
internal sealed class ApplicationIconService : IDisposable
{
    internal IntPtr WindowIcon { get; private set; }
    internal IntPtr TrayIcon { get; private set; }

    /// <summary>Loads DPI-sized native icons without extracting a sidecar file or sharing disposable handles.</summary>
    internal ApplicationIconService()
    {
        using var resource = typeof(ApplicationIconService).Assembly.GetManifestResourceStream("AutoHotkeyUX.Modern.AppIcon.ico")
            ?? throw new InvalidOperationException("The embedded application icon is missing.");
        using var buffer = new MemoryStream();
        resource.CopyTo(buffer);
        var bytes = buffer.ToArray();
        try
        {
            var dpi = GetDpiForSystem();
            var windowSize = GetSystemMetricsForDpi(11, dpi);
            var traySize = GetSystemMetricsForDpi(49, dpi);
            WindowIcon = CreateIcon(bytes, windowSize);
            TrayIcon = CreateIcon(bytes, traySize);
            ServiceDiagnostics.Write("Icon", $"Embedded green H icon loaded: window {windowSize} px, tray {traySize} px, DPI {dpi}.");
        }
        catch { Dispose(); throw; }
    }

    /// <summary>Selects the nearest sufficient ICO frame and lets Windows decode validated resource bits at the requested DPI size.</summary>
    private static IntPtr CreateIcon(byte[] bytes, int size)
    {
        if (bytes.Length < 6 || BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0, 2)) != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2, 2)) != 1)
            throw new InvalidDataException("Invalid embedded ICO header.");
        var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4, 2));
        var directoryLength = 6 + count * 16;
        if (count == 0 || directoryLength > bytes.Length) throw new InvalidDataException("Invalid embedded ICO directory.");
        var selected = -1;
        var bestDistance = int.MaxValue;
        for (var index = 0; index < count; index++)
        {
            var entry = bytes.AsSpan(6 + index * 16, 16);
            var width = entry[0] == 0 ? 256 : entry[0];
            var height = entry[1] == 0 ? 256 : entry[1];
            if (width != height) continue;
            var distance = width >= size ? width - size : size - width + 256;
            if (distance < bestDistance) { selected = index; bestDistance = distance; }
        }
        if (selected < 0) throw new InvalidDataException("The embedded ICO has no square frames.");
        var chosen = bytes.AsSpan(6 + selected * 16, 16);
        var length = BinaryPrimitives.ReadUInt32LittleEndian(chosen.Slice(8, 4));
        var offset = BinaryPrimitives.ReadUInt32LittleEndian(chosen.Slice(12, 4));
        if (length == 0 || offset < directoryLength || (ulong)offset + length > (ulong)bytes.Length)
            throw new InvalidDataException("Invalid embedded ICO frame bounds.");
        var frame = bytes.AsSpan((int)offset, (int)length).ToArray();
        var icon = CreateIconFromResourceEx(frame, length, true, 0x00030000, size, size, 0);
        if (icon == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not decode the application icon.");
        return icon;
    }

    /// <summary>Releases each owned icon after the notification entry and application window have closed.</summary>
    public void Dispose()
    {
        if (WindowIcon != IntPtr.Zero) { DestroyIcon(WindowIcon); WindowIcon = IntPtr.Zero; }
        if (TrayIcon != IntPtr.Zero) { DestroyIcon(TrayIcon); TrayIcon = IntPtr.Zero; }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateIconFromResourceEx([In] byte[] bits, uint size,
        [MarshalAs(UnmanagedType.Bool)] bool icon, uint version, int width, int height, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();
    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);
}
