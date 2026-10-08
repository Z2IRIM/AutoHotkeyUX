using AutoHotkeyUX.Modern.Services;
using Windows.Graphics;

namespace AutoHotkeyUX.Modern;

public sealed partial class MainWindow
{
    /// <summary>Checks settings page stability across switch states at real desktop sizes without running interpreters.</summary>
    internal async Task<object> VerifySettingsLayoutAsync(string reportPath)
    {
        NavigateTo("settings");
        await Task.Delay(120);
        var scale = RootLayout.XamlRoot.RasterizationScale;
        var sizes = new[] { AppWindow.Size,
            new SizeInt32((int)Math.Round(1100 * scale), (int)Math.Round(900 * scale)),
            new SizeInt32(MinimumWindowWidth, MinimumWindowHeight) };
        var checks = new List<object>();
        var failures = new List<string>();
        for (var sizeIndex = 0; sizeIndex < sizes.Length; sizeIndex++)
        {
            AppWindow.Resize(sizes[sizeIndex]);
            await Task.Delay(120);
            var measurements = await _settingsPage!.MeasureSwitchLayoutsAsync(RootLayout);
            var baseline = measurements[0];
            foreach (var measurement in measurements)
            {
                if (Math.Abs(measurement.PageX - baseline.PageX) > 1 || Math.Abs(measurement.BodyX - baseline.BodyX) > 1
                    || Math.Abs(measurement.BodyWidth - baseline.BodyWidth) > 1 || Math.Abs(measurement.HeadingX - baseline.HeadingX) > 1)
                    failures.Add($"Size {sizeIndex}, switch mask {measurement.SwitchMask}: page or heading moved horizontally.");
                if (measurement.BodyX < -1 || measurement.BodyX + measurement.BodyWidth > measurement.ViewportWidth + 1
                    || measurement.ExtentWidth > measurement.ViewportWidth + 1)
                    failures.Add($"Size {sizeIndex}, switch mask {measurement.SwitchMask}: content exceeds its horizontal viewport.");
                var expectedX = Math.Max(0, (measurement.ViewportWidth - measurement.BodyWidth) / 2);
                if (Math.Abs(measurement.BodyX - expectedX) > 1)
                    failures.Add($"Size {sizeIndex}, switch mask {measurement.SwitchMask}: centering follows natural content width instead of actual page width.");
                if (measurement.RowOverflowCount != 0)
                    failures.Add($"Size {sizeIndex}, switch mask {measurement.SwitchMask}: a setting label or control exceeds its row.");
            }
            checks.Add(new { Width = sizes[sizeIndex].Width, Height = sizes[sizeIndex].Height, Measurements = measurements });
            await CaptureVisualAsync(Path.ChangeExtension(reportPath, sizeIndex == 0 ? "wide.png" : sizeIndex == 1 ? "medium.png" : "narrow.png"));
        }
        if (failures.Count != 0)
        {
            Program.ExitCode = 1;
            ServiceDiagnostics.Write("SettingsLayout", string.Join(Environment.NewLine, failures));
        }
        return new { Passed = failures.Count == 0, Checks = checks, Failures = failures,
            RasterizationScale = scale, SwitchCombinationsPerSize = 16, InterpreterExecuted = false,
            ActualUserSettingsChanged = false, StateDirectory = _services.StateDirectory,
            Capture = "Actual WinUI RenderTargetBitmap; native caption and Mica excluded" };
    }
}
