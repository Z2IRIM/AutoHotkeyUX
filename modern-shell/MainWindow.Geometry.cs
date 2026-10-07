using AutoHotkeyUX.Modern.Services;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace AutoHotkeyUX.Modern;

public sealed partial class MainWindow
{
    private const int MinimumWindowWidth = 980;
    private const int MinimumWindowHeight = 680;
    private const double InitialWindowScale = 0.80;
    private RectInt32 _initialDisplayBounds;
    private RectInt32 _initialWorkArea;
    private RectInt32 _initialWindowBounds;

    /// <summary>Centers a larger startup window with the monitor's aspect ratio inside its usable area.</summary>
    private void ConfigureInitialWindowBounds()
    {
        try
        {
            var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest)
                ?? DisplayArea.Primary;
            var screen = display.OuterBounds;
            var work = display.WorkArea;
            var bounds = CalculateInitialBounds(screen, work);
            // WorkArea coordinates are display-relative; the overload applies the monitor's screen offset.
            AppWindow.MoveAndResize(bounds, display);
            _initialDisplayBounds = screen;
            _initialWorkArea = work;
            _initialWindowBounds = bounds;
            ServiceDiagnostics.Write("Window", $"Startup size {bounds.Width}x{bounds.Height}; display {screen.Width}x{screen.Height}; scale {InitialWindowScale:P0}.");
        }
        catch (Exception ex)
        {
            ServiceDiagnostics.Write("Window", "Screen placement failed; using the previous startup size.", ex);
            AppWindow.Resize(new SizeInt32(1280, 820));
        }
    }

    /// <summary>Fits one uniform screen scale to the work area, honoring usable minima when space permits.</summary>
    internal static RectInt32 CalculateInitialBounds(RectInt32 screen, RectInt32 work)
    {
        if (screen.Width <= 0 || screen.Height <= 0 || work.Width <= 0 || work.Height <= 0)
            throw new ArgumentException("The display must have positive screen and work-area dimensions.");
        var maximum = Math.Min((double)work.Width / screen.Width, (double)work.Height / screen.Height);
        var minimum = Math.Max((double)MinimumWindowWidth / screen.Width, (double)MinimumWindowHeight / screen.Height);
        var scale = Math.Min(maximum, Math.Max(maximum * InitialWindowScale, minimum));
        var width = Math.Min(work.Width, Math.Max(1, (int)Math.Round(screen.Width * scale)));
        var height = Math.Min(work.Height, Math.Max(1, (int)Math.Round(screen.Height * scale)));
        return new RectInt32(work.X + (work.Width - width) / 2, work.Y + (work.Height - height) / 2, width, height);
    }

    /// <summary>Enforces usable manual-resize minima without pushing a small monitor's window off screen.</summary>
    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidSizeChange || sender.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized }) return;
        var display = DisplayArea.GetFromWindowId(sender.Id, DisplayAreaFallback.Nearest) ?? DisplayArea.Primary;
        var work = display.WorkArea;
        var screen = display.OuterBounds;
        var fit = Math.Min((double)work.Width / screen.Width, (double)work.Height / screen.Height);
        var width = Math.Max(sender.Size.Width, Math.Min(MinimumWindowWidth, (int)Math.Round(screen.Width * fit)));
        var height = Math.Max(sender.Size.Height, Math.Min(MinimumWindowHeight, (int)Math.Round(screen.Height * fit)));
        if (width != sender.Size.Width || height != sender.Size.Height) sender.Resize(new SizeInt32(width, height));
    }

    /// <summary>Checks native startup placement and representative monitor layouts for the explicit UI diagnostic.</summary>
    private object VerifyStartupPlacement()
    {
        var screen = _initialDisplayBounds;
        var work = _initialWorkArea;
        var bounds = _initialWindowBounds;
        if (screen.Width <= 0) throw new InvalidOperationException("Monitor-based startup placement was not applied.");
        var position = AppWindow.Position;
        var size = AppWindow.Size;
        if (size.Width != bounds.Width || size.Height != bounds.Height
            || position.X != screen.X + bounds.X || position.Y != screen.Y + bounds.Y)
            throw new InvalidOperationException("The native startup window differs from the requested monitor placement.");
        foreach (var layout in new[]
        {
            (screen, work),
            (new RectInt32(-1920, 0, 1920, 1080), new RectInt32(0, 0, 1920, 1040)),
            (new RectInt32(0, 0, 2560, 1600), new RectInt32(60, 0, 2500, 1600)),
            (new RectInt32(0, 0, 3440, 1440), new RectInt32(0, 0, 3440, 1392)),
            (new RectInt32(0, 0, 1080, 1920), new RectInt32(0, 0, 1080, 1872)),
            (new RectInt32(0, 0, 800, 600), new RectInt32(0, 0, 800, 560))
        })
        {
            var fitted = CalculateInitialBounds(layout.Item1, layout.Item2);
            var ratio = (double)layout.Item1.Width / layout.Item1.Height;
            if (fitted.X < layout.Item2.X || fitted.Y < layout.Item2.Y
                || fitted.X + fitted.Width > layout.Item2.X + layout.Item2.Width
                || fitted.Y + fitted.Height > layout.Item2.Y + layout.Item2.Height
                || Math.Abs(fitted.Width - fitted.Height * ratio) > 1 + ratio
                || Math.Abs((fitted.X - layout.Item2.X) * 2 + fitted.Width - layout.Item2.Width) > 1
                || Math.Abs((fitted.Y - layout.Item2.Y) * 2 + fitted.Height - layout.Item2.Height) > 1)
                throw new InvalidOperationException("Screen-ratio placement failed for a representative display layout.");
        }
        return new { Screen = new { screen.X, screen.Y, screen.Width, screen.Height },
            WorkArea = new { work.X, work.Y, work.Width, work.Height }, Bounds = new { bounds.X, bounds.Y, bounds.Width, bounds.Height },
            NativePosition = new { position.X, position.Y }, NativeSize = new { size.Width, size.Height },
            RequestedScale = InitialWindowScale, RepresentativeLayouts = 6 };
    }
}
