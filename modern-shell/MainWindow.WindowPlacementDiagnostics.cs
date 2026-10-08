using AutoHotkeyUX.Modern.Services;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace AutoHotkeyUX.Modern;

public sealed partial class MainWindow
{
    /// <summary>Checks real monitor placement and the app's restore path without running any user scripts.</summary>
    internal async Task<object> VerifyWindowPlacementAsync(Action showWindow)
    {
        await Task.Delay(80);
        var startup = VerifyStartupPlacement();
        var displays = DisplayArea.FindAll();
        var screens = new List<RectInt32>();
        var checks = new List<object>();
        for (var index = 0; index < displays.Count; index++)
        {
            var display = displays[index];
            var screen = display.OuterBounds;
            var work = display.WorkArea;
            screens.Add(screen);
            var bounds = CalculateInitialBounds(screen, work);
            AppWindow.MoveAndResize(bounds);
            await Task.Delay(80);
            var actual = new RectInt32(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
            ServiceDiagnostics.Write("WindowVerification", $"Display {screen.X},{screen.Y} {screen.Width}x{screen.Height}; work {work.X},{work.Y} {work.Width}x{work.Height}; native {actual.X},{actual.Y} {actual.Width}x{actual.Height}.");
            if (!ContainsRectangle(screen, actual))
                throw new InvalidOperationException("The startup placement falls outside its selected display. See the isolated manager.log.");
            checks.Add(new { Screen = PlacementRectangle(screen), WorkArea = PlacementRectangle(work), Bounds = PlacementRectangle(actual) });
        }
        var original = new RectInt32(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
        AppWindow.Move(new PointInt32(30000, -30000));
        AppWindow.Hide();
        showWindow();
        await Task.Delay(80);
        var restored = new RectInt32(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
        if (!screens.Any(screen => ContainsRectangle(screen, restored)))
            throw new InvalidOperationException("Showing an offscreen window did not recover it onto a connected display.");
        AppWindow.MoveAndResize(original);
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Minimize(); showWindow(); await Task.Delay(80);
            if (presenter.State != OverlappedPresenterState.Restored)
                throw new InvalidOperationException("Showing a minimized window did not restore it.");
            presenter.Maximize(); showWindow(); await Task.Delay(80);
            if (presenter.State != OverlappedPresenterState.Maximized)
                throw new InvalidOperationException("Showing a maximized window lost its user-selected state.");
            presenter.Restore();
        }
        return new { Passed = true, Startup = startup, Displays = checks, OffscreenRecovery = true,
            MinimizedRestore = true, MaximizedPreserved = true, StateDirectory = _services.StateDirectory,
            InterpreterExecuted = false };
    }

    /// <summary>Checks the complete native rectangle rather than accepting a taskbar-only or offscreen window.</summary>
    private static bool ContainsRectangle(RectInt32 outer, RectInt32 inner) => inner.Width > 0 && inner.Height > 0
        && inner.X >= outer.X && inner.Y >= outer.Y
        && (long)inner.X + inner.Width <= (long)outer.X + outer.Width
        && (long)inner.Y + inner.Height <= (long)outer.Y + outer.Height;

    /// <summary>Includes WinRT rectangle fields in the explicit diagnostic JSON.</summary>
    private static object PlacementRectangle(RectInt32 rectangle) => new { rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height };
}
