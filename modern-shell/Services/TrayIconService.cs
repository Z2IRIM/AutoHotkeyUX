using System.Runtime.InteropServices;

namespace AutoHotkeyUX.Modern.Services;

/// <summary>Exposes a native tray entry so the silent workspace can be restored without changing its UI.</summary>
internal sealed class TrayIconService : IDisposable
{
    private const uint CallbackMessage = 0x8001;
    private readonly IntPtr _window;
    private readonly Action _open;
    private readonly Action _exit;
    private readonly SubclassProcedure _procedure;
    private readonly IntPtr _icon;
    private readonly uint _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private bool _disposed;
    internal bool UsesCustomIcon => _icon != IntPtr.Zero;

    /// <summary>Registers an accessible notification entry using an application-owned icon when available.</summary>
    internal TrayIconService(IntPtr window, Action open, Action exit, IntPtr icon)
    {
        _window = window; _open = open; _exit = exit;
        _procedure = WindowProcedure;
        _icon = icon;
        if (!SetWindowSubclass(window, _procedure, 1, IntPtr.Zero))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try { AddIcon(); }
        catch { RemoveWindowSubclass(window, _procedure, 1); throw; }
    }

    /// <summary>Adds or restores the tray icon after Explorer's taskbar is recreated.</summary>
    private void AddIcon()
    {
        var data = CreateData();
        if (!ShellNotifyIcon(0, ref data)) throw new InvalidOperationException("The Windows tray icon could not be created.");
        data.Version = 4;
        ShellNotifyIcon(4, ref data);
    }

    /// <summary>Handles native activation and the tray's context menu on the owning window thread.</summary>
    private IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, nuint id, IntPtr data)
    {
        try
        {
            if (message == _taskbarCreated && !_disposed) AddIcon();
            if (message == CallbackMessage && !_disposed)
            {
                var notification = (uint)lParam.ToInt64() & 0xFFFF;
                if (notification is 0x400 or 0x401 or 0x203) _open();
                else if (notification is 0x7B or 0x205) ShowMenu();
                return IntPtr.Zero;
            }
        }
        catch (Exception ex) { ServiceDiagnostics.Write("Tray", "Notification action failed.", ex); }
        return DefSubclassProc(window, message, wParam, lParam);
    }

    /// <summary>Provides Open and Exit manager actions without adding a parallel UI framework.</summary>
    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
        try
        {
            AppendMenu(menu, 0, 1, "Open AutoHotkey");
            AppendMenu(menu, 0, 2, "Exit manager (keep scripts running)");
            GetCursorPos(out var point);
            SetForegroundWindow(_window);
            var command = TrackPopupMenu(menu, 0x0102, point.X, point.Y, 0, _window, IntPtr.Zero);
            if (command == 1) _open(); else if (command == 2) _exit();
        }
        finally { DestroyMenu(menu); }
    }

    /// <summary>Builds the native structure using the approved icon with a standard fallback and concise tooltip.</summary>
    private NotifyIconData CreateData() => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = _window, Id = 1,
        Flags = 1 | 2 | 4, Callback = CallbackMessage,
        Icon = _icon != IntPtr.Zero ? _icon : LoadIcon(IntPtr.Zero, (IntPtr)32512), Tip = "AutoHotkey · Open workspace",
        Info = string.Empty, InfoTitle = string.Empty
    };

    /// <summary>Reports background shortcut completion through the existing native notification entry.</summary>
    internal void ShowNotification(string message, bool error)
    {
        if (_disposed) return;
        var data = CreateData();
        data.Flags = 0x10;
        data.Info = message.Length < 256 ? message : message[..252] + "...";
        data.InfoTitle = "AutoHotkey · 解压";
        data.InfoFlags = error ? 3u : 1u;
        if (!ShellNotifyIcon(1, ref data)) ServiceDiagnostics.Write("Tray", "Windows did not accept the extraction notification.");
    }

    /// <summary>Removes only this application's tray entry and native subclass.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var data = CreateData();
        ShellNotifyIcon(2, ref data);
        RemoveWindowSubclass(_window, _procedure, 1);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        internal uint Size;
        internal IntPtr Window;
        internal uint Id, Flags, Callback;
        internal IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Tip;
        internal uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string Info;
        internal uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] internal string InfoTitle;
        internal uint InfoFlags;
        internal Guid Guid;
        internal IntPtr BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { internal int X, Y; }
    private delegate IntPtr SubclassProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, nuint id, IntPtr data);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW")]
    private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("comctl32.dll", SetLastError = true)] private static extern bool SetWindowSubclass(IntPtr window, SubclassProcedure callback, nuint id, IntPtr data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(IntPtr window, SubclassProcedure callback, nuint id);
    [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "LoadIconW")] private static extern IntPtr LoadIcon(IntPtr instance, IntPtr icon);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(IntPtr menu, uint flags, nuint id, string text);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr window, IntPtr rectangle);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
}
