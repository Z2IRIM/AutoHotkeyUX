using AutoHotkeyUX.Modern.Models;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace AutoHotkeyUX.Modern.Services;

/// <summary>Applies one snapshot through the actual owned v3 AHK process and confirms its cached revision.</summary>
internal sealed class ShortcutPreferencesRuntime(AutoHotkeySettings settings, Func<RunningScriptSession?> session)
{
    private const nuint Protocol = 0x41584B33;

    /// <summary>Confirms both persistent values and runtime cache; a missing reply never implies rollback.</summary>
    internal void Apply(ShortcutPreferenceSnapshot snapshot)
    {
        var owned = session() ?? throw new InvalidOperationException("Shortcuts are stopped. Enable Explorer shortcuts before applying live preferences.");
        var window = new IntPtr(long.TryParse(settings.Read("Modern", "ShortcutScriptWindow"), out var handle) ? handle : 0);
        var token = settings.Read("Modern", "ShortcutScriptToken");
        if (owned.State != ScriptState.Running || owned.ProcessId is null || owned.ProcessStartUtc is null
            || window == IntPtr.Zero || token.Length != 32 || !IsWindow(window)) throw Incompatible();
        GetWindowThreadProcessId(window, out var pid);
        if (pid != owned.ProcessId || !int.TryParse(settings.Read("Modern", "ShortcutScriptPid"), out var endpointPid)
            || endpointPid != owned.ProcessId) throw Incompatible();
        using var process = Process.GetProcessById(owned.ProcessId.Value);
        if (process.HasExited || process.StartTime.ToUniversalTime() != owned.ProcessStartUtc) throw Incompatible();
        var encoded = ShortcutPreferenceCodec.Encode(snapshot);
        var delivered = Send(window, "configure\n" + token + "\n" + encoded, 800, out var result);
        if (delivered && result != 1)
        {
            var reason = settings.Read("Modern", "ShortcutConfigError");
            throw new InvalidOperationException(string.IsNullOrEmpty(reason) ? "The shortcut script rejected the update; saved preferences were kept." : reason);
        }
        // A timeout can occur after dispatch. Query the immutable revision instead of replaying a mutation.
        if (Send(window, "query\n" + token + "\n" + snapshot.Revision, 800, out var cached) && cached == 1
            && settings.Read("Modern", ShortcutPreferenceCodec.SettingName) == encoded) return;
        throw new UnconfirmedShortcutPreferencesException("The script did not confirm the saved revision. Current saved values were reloaded; check the shortcut process before retrying.");
    }

    /// <summary>Copies a bounded UTF-8 request into synchronous native IPC without invoking a shell.</summary>
    private static bool Send(nint window, string body, uint timeout, out nuint result)
    {
        var bytes = Encoding.UTF8.GetBytes(body + '\0');
        var memory = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, memory, bytes.Length);
            var data = new CopyData { Tag = Protocol, Length = bytes.Length, Data = memory };
            return SendMessageTimeout(window, 0x4A, 0, ref data, 0x23, timeout, out result) != 0;
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    /// <summary>Explains compatibility without replacing or terminating a custom user script.</summary>
    private static InvalidOperationException Incompatible() => new("This running script does not expose the v3 preferences endpoint. Edited scripts are preserved; use a compatible built-in copy before configuring these actions.");

    [StructLayout(LayoutKind.Sequential)] private struct CopyData { internal nuint Tag; internal int Length; internal nint Data; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SendMessageTimeout(nint window, uint message,
        nuint wParam, ref CopyData data, uint flags, uint timeout, out nuint result);
}

/// <summary>Separates an unknown acknowledgement from a confirmed rejected transaction.</summary>
internal sealed class UnconfirmedShortcutPreferencesException(string message) : Exception(message);
