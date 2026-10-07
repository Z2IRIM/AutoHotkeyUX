using AutoHotkeyUX.Modern.Models;
using System.Diagnostics;

namespace AutoHotkeyUX.Modern.Services;

/// <summary>Builds one bounded snapshot without retaining remote process or GDI resources.</summary>
internal sealed class WindowSpyService
{
    /// <summary>Captures an external target, retaining the last external window while the pointer is over the manager.</summary>
    internal WindowSpySnapshot? Capture(WindowSpyOptions options, nint excludedWindow, nint previousTarget, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (options.FreezeWithModifiers && WindowInspectionNative.ModifiersHeld) return null;
        if (!WindowInspectionNative.GetCursorPos(out var pointer)) return null;
        var control = WindowInspectionNative.WindowFromPoint(pointer);
        var target = options.FollowMouse ? WindowInspectionNative.GetAncestor(control, 2) : WindowInspectionNative.GetForegroundWindow();
        if (!IsExternal(target, excludedWindow)) target = previousTarget;
        if (!IsExternal(target, excludedWindow)) return null;
        WindowInspectionNative.GetWindowThreadProcessId(target, out var pid);
        var bounds = WindowInspectionNative.Bounds(target);
        var clientBounds = WindowInspectionNative.Bounds(target, client: true);
        var clientPointer = pointer;
        WindowInspectionNative.ScreenToClient(target, ref clientPointer);
        var budget = Stopwatch.StartNew();
        var children = WindowInspectionNative.Children(target, budget, cancellationToken);
        var underTarget = control != target && WindowInspectionNative.GetAncestor(control, 2) == target;
        var className = underTarget ? WindowInspectionNative.Metadata(control) : string.Empty;
        var classNN = WindowInspectionNative.ClassNN(control, className, children, budget);
        var controlText = underTarget ? WindowInspectionNative.Text(control, Remaining(budget), out _) : string.Empty;
        var visible = new List<string>();
        var all = new List<string>();
        var warning = controlText == "Unavailable" || classNN == "Unavailable";
        var status = string.Empty;
        var characterCount = 0;
        if (options.IncludeWindowText)
        {
            foreach (var child in children)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Remaining(budget) <= 0 || characterCount >= 16000) { warning = true; break; }
                var text = WindowInspectionNative.Text(child, Remaining(budget), out var readable);
                if (WindowInspectionNative.Metadata(child) == "msctls_statusbar32") status = text;
                if (!readable) { warning = true; continue; }
                if (text.Length == 0) continue;
                all.Add(text);
                characterCount += text.Length;
                if (WindowInspectionNative.IsWindowVisible(child)) visible.Add(text);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (!WindowInspectionNative.IsWindow(target)) return null;
        return new(target, WindowInspectionNative.Metadata(target, caption: true), WindowInspectionNative.Metadata(target),
            ProcessName(pid), pid, new(pointer.X, pointer.Y), new(pointer.X - bounds.X, pointer.Y - bounds.Y),
            new(clientPointer.X, clientPointer.Y), WindowInspectionNative.Pixel(pointer), classNN, controlText,
            underTarget ? WindowInspectionNative.Bounds(control) : null, bounds, clientBounds, status,
            string.Join("\n", visible.Distinct()), string.Join("\n", all.Distinct()),
            warning ? "Some control text is unavailable or exceeded the capture time budget." : null);
    }

    /// <summary>Limits every remote text query to the remaining shared 100ms budget.</summary>
    private static int Remaining(Stopwatch budget) => (int)Math.Clamp(100 - budget.ElapsedMilliseconds, 0, 20);

    /// <summary>Rejects stale handles and every window owned by this manager.</summary>
    private static bool IsExternal(nint window, nint excluded)
    {
        if (window == 0 || window == excluded || !WindowInspectionNative.IsWindow(window)) return false;
        WindowInspectionNative.GetWindowThreadProcessId(window, out var pid);
        return pid != Environment.ProcessId;
    }

    /// <summary>Reads a process name while tolerating privilege boundaries and process exit races.</summary>
    private static string ProcessName(uint pid)
    {
        try { using var process = Process.GetProcessById((int)pid); return process.ProcessName + ".exe"; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return "Unavailable"; }
    }
}
