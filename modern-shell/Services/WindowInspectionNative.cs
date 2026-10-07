using AutoHotkeyUX.Modern.Models;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace AutoHotkeyUX.Modern.Services;

/// <summary>Contains read-only window queries and bounded cross-process text messages.</summary>
internal static class WindowInspectionNative
{
    [StructLayout(LayoutKind.Sequential)] internal struct Point { internal int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { internal int Left, Top, Right, Bottom; }
    private delegate bool EnumWindow(nint window, nint parameter);

    /// <summary>Gets the pointer in physical screen coordinates.</summary>
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetCursorPos(out Point point);
    /// <summary>Finds the deepest Win32 window at the pointer.</summary>
    [DllImport("user32.dll")] internal static extern nint WindowFromPoint(Point point);
    /// <summary>Finds a top-level window for an inspected child.</summary>
    [DllImport("user32.dll")] internal static extern nint GetAncestor(nint window, uint flags);
    /// <summary>Returns the foreground window without activating it.</summary>
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    /// <summary>Finds an Explorer window for explicit inspection diagnostics without activating it.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint FindWindow(string className, string? title);
    /// <summary>Returns the real shell desktop when no Explorer folder window exists.</summary>
    [DllImport("user32.dll")] internal static extern nint GetShellWindow();
    /// <summary>Validates a window handle before using a retained target.</summary>
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindow(nint window);
    /// <summary>Checks visibility without accessing the inspected process.</summary>
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindowVisible(nint window);
    /// <summary>Reads ownership for excluding the manager and reporting process identity.</summary>
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    /// <summary>Checks modifier keys without installing a keyboard hook.</summary>
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    /// <summary>Reads a class name from window-manager metadata.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder text, int maximum);
    /// <summary>Reads a top-level caption from window-manager metadata.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint window, StringBuilder text, int maximum);
    /// <summary>Reads native screen bounds.</summary>
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint window, out Rect rectangle);
    /// <summary>Reads native client dimensions.</summary>
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetClientRect(nint window, out Rect rectangle);
    /// <summary>Converts client coordinates to physical screen coordinates.</summary>
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ClientToScreen(nint window, ref Point point);
    /// <summary>Converts the pointer to the target's client coordinate space.</summary>
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ScreenToClient(nint window, ref Point point);
    /// <summary>Enumerates children while the managed callback enforces a budget.</summary>
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumChildWindows(nint window, EnumWindow callback, nint parameter);
    /// <summary>Sends a standard marshaled text query with an explicit timeout.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SendMessageTimeoutW")]
    private static extern nint SendText(nint window, uint message, nuint capacity, StringBuilder text, uint flags, uint timeout, out nuint result);
    /// <summary>Acquires a screen DC only for a single pixel sample.</summary>
    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    /// <summary>Releases a screen DC after sampling.</summary>
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    /// <summary>Reads one RGB pixel from the acquired screen DC.</summary>
    [DllImport("gdi32.dll")] private static extern uint GetPixel(nint dc, int x, int y);
    internal static bool ModifiersHeld => GetAsyncKeyState(0x11) < 0 || GetAsyncKeyState(0x10) < 0;

    /// <summary>Reads class/caption metadata without synchronous remote WM_GETTEXT.</summary>
    internal static string Metadata(nint window, bool caption = false)
    {
        var text = new StringBuilder(caption ? 4096 : 256);
        if (caption) GetWindowText(window, text, text.Capacity); else GetClassName(window, text, text.Capacity);
        return text.ToString();
    }

    /// <summary>Returns a screen-relative window or client rectangle.</summary>
    internal static SpyBounds Bounds(nint window, bool client = false)
    {
        if (!client && GetWindowRect(window, out var rectangle))
            return new(rectangle.Left, rectangle.Top, rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top);
        if (client && GetClientRect(window, out rectangle))
        {
            var origin = new Point();
            ClientToScreen(window, ref origin);
            return new(origin.X, origin.Y, rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top);
        }
        return default;
    }

    /// <summary>Bounds child enumeration by count, cancellation and elapsed time.</summary>
    internal static List<nint> Children(nint target, Stopwatch budget, CancellationToken cancellationToken)
    {
        var result = new List<nint>();
        EnumChildWindows(target, (window, _) =>
        {
            if (cancellationToken.IsCancellationRequested || budget.ElapsedMilliseconds >= 100 || result.Count >= 512) return false;
            result.Add(window);
            return true;
        }, 0);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    /// <summary>Reads at most 4095 characters, yielding unavailable rather than hanging.</summary>
    internal static string Text(nint window, int milliseconds, out bool readable)
    {
        var text = new StringBuilder(4096);
        readable = milliseconds > 0 && SendText(window, 0x000D, (nuint)text.Capacity, text, 0x23, (uint)milliseconds, out _) != 0;
        return readable ? text.ToString() : "Unavailable";
    }

    /// <summary>Samples the pointer pixel and guarantees DC release.</summary>
    internal static string Pixel(Point point)
    {
        var dc = GetDC(0);
        if (dc == 0) return "Unavailable";
        try
        {
            var color = GetPixel(dc, point.X, point.Y);
            return color == uint.MaxValue ? "Unavailable" : $"#{color & 0xff:X2}{(color >> 8) & 0xff:X2}{(color >> 16) & 0xff:X2}";
        }
        finally { ReleaseDC(0, dc); }
    }
}
