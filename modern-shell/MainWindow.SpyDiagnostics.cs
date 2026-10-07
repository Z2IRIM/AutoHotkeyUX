using AutoHotkeyUX.Modern.Models;
using AutoHotkeyUX.Modern.Services;
using Microsoft.UI.Windowing;

namespace AutoHotkeyUX.Modern;

public sealed partial class MainWindow
{
    /// <summary>Checks real shell capture plus pause, visibility and navigation ownership.</summary>
    private async Task<object> VerifyWindowSpyAsync()
    {
        var external = WindowInspectionNative.FindWindow("CabinetWClass", null);
        var desktopFallback = external == 0;
        if (desktopFallback) external = WindowInspectionNative.GetShellWindow();
        if (external == 0) throw new InvalidOperationException("No Explorer or shell desktop window is available for inspection.");
        var snapshot = await Task.Run(() => _services.WindowSpy.Capture(new WindowSpyOptions(false, true, false),
            WinRT.Interop.WindowNative.GetWindowHandle(this), external, CancellationToken.None));
        if (snapshot is null || snapshot.TargetWindow == 0 || snapshot.ProcessId == 0 || snapshot.WindowBounds.Width <= 0
            || snapshot.MouseWindow.X != snapshot.MouseScreen.X - snapshot.WindowBounds.X)
            throw new InvalidOperationException("Native external-window capture or coordinate conversion failed.");
        NavigateTo("spy");
        await Task.Delay(100);
        _windowSpyPage!.RetainDiagnosticTarget(external);
        await WaitForToolAsync(() => _windowSpyPage.HasDiagnosticSnapshot, "live inspection fields");
        _windowSpyPage.ToggleDiagnosticPause();
        var pausedCount = _windowSpyPage.CaptureAttempts;
        await Task.Delay(400);
        if (_windowSpyPage.SamplerRunning || pausedCount != _windowSpyPage.CaptureAttempts) throw new InvalidOperationException("Paused capture continued running.");
        _windowSpyPage.ToggleDiagnosticPause();
        await WaitForToolAsync(() => _windowSpyPage.CaptureAttempts > pausedCount, "capture resume");
        AppWindow.Hide();
        await Task.Delay(80);
        var hiddenCount = _windowSpyPage.CaptureAttempts;
        await Task.Delay(350);
        if (_windowSpyPage.SamplerRunning || hiddenCount != _windowSpyPage.CaptureAttempts) throw new InvalidOperationException("Hidden capture continued running.");
        AppWindow.Show(); Activate();
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Minimize(); await Task.Delay(100);
            if (_windowSpyPage.SamplerRunning) throw new InvalidOperationException("Minimized capture continued running.");
            presenter.Restore(); Activate();
        }
        for (var cycle = 0; cycle < 3; cycle++)
        {
            NavigateTo("home"); await Task.Delay(80);
            if (_windowSpyPage.SamplerRunning) throw new InvalidOperationException("Unloaded capture continued running.");
            NavigateTo("spy"); await Task.Delay(100);
            if (!_windowSpyPage.SamplerRunning) throw new InvalidOperationException("Returning to Window Spy did not restore one sampler.");
        }
        NavigateTo("home");
        var boundedRead = await VerifyBoundedSpyReadAsync();
        return new { Target = desktopFallback ? "Shell desktop" : "File Explorer", snapshot.ProcessId, Class = snapshot.Class,
            Screen = snapshot.MouseScreen, Window = snapshot.MouseWindow, Client = snapshot.MouseClient,
            PauseResume = true, HiddenStopped = true, MinimizedStopped = true, NavigationCycles = 3, BoundedRead = boundedRead };
    }

    /// <summary>Checks a genuinely unresponsive external Win32 control without moving or clicking desktop input.</summary>
    private async Task<object> VerifyBoundedSpyReadAsync()
    {
        var directory = Path.Combine(Environment.CurrentDirectory, ".verification", $"spy-budget-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var ready = Path.Combine(directory, "ready.txt");
        var script = Path.Combine(directory, "fixture.ahk");
        await File.WriteAllTextAsync(script, $"#Requires AutoHotkey v2.0\n#SingleInstance Off\n#NoTrayIcon\ng := Gui()\ng.AddEdit(, \"Budget test\")\ng.AddText(, \"Bounded read\")\ng.Show(\"Hide\")\nFileAppend(Format(\"{{:X}}\", g.Hwnd), \"{ready}\")\nDllCall(\"Sleep\", \"UInt\", 8000)\nExitApp\n");
        var start = new System.Diagnostics.ProcessStartInfo(_integration.FindRuntime()!.Path)
        { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("/ErrorStdOut"); start.ArgumentList.Add(script);
        using var process = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("The bounded-read fixture could not start.");
        try
        {
            await WaitForToolAsync(() => File.Exists(ready), "unresponsive control fixture", 5000);
            var target = (nint)Convert.ToInt64(await File.ReadAllTextAsync(ready), 16);
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            var captured = await Task.Run(() => _services.WindowSpy.Capture(new WindowSpyOptions(false, true, false),
                WindowInspectionNative.GetForegroundWindow(), target, CancellationToken.None));
            if (captured is null || captured.TargetWindow != target || captured.ReadWarning is null || elapsed.ElapsedMilliseconds > 250)
                throw new InvalidOperationException("Unresponsive control text exceeded its capture budget or lost unavailable-field feedback.");
            return new { ElapsedMilliseconds = elapsed.ElapsedMilliseconds, UnavailableReported = true };
        }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
    }
}
