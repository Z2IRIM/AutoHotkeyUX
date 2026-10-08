using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AutoHotkeyUX.Modern.Pages;

public sealed partial class SettingsPage
{
    internal sealed record LayoutSnapshot(int SwitchMask, bool Feedback, double PageX, double PageWidth,
        double BodyX, double BodyWidth, double BodyDesiredWidth, double ViewportWidth, double ExtentWidth, double HorizontalOffset, double HeadingX,
        double[] ToggleWidths, double[] ToggleColumnWidths, int RowOverflowCount);

    /// <summary>Measures actual native layout while suppressing all settings writes in the isolated diagnostic instance.</summary>
    internal async Task<IReadOnlyList<LayoutSnapshot>> MeasureSwitchLayoutsAsync(FrameworkElement owner)
    {
        var scroll = (ScrollViewer)Content;
        var body = (StackPanel)scroll.Content;
        var heading = body.Children[0];
        var toggles = new[] { StartWithWindowsToggle, ExplorerShortcutsToggle, UseLauncherToggle, CheckUpdatesToggle };
        var originalStates = toggles.Select(toggle => toggle.IsOn).ToArray();
        var previousLoaded = _loadedSettings;
        var originalStatus = ExplorerStatusText.Text;
        var originalRuntime = RuntimePathText.Text;
        var originalMessage = SettingsInfoBar.Message;
        var originalSeverity = SettingsInfoBar.Severity;
        var originalOpen = SettingsInfoBar.IsOpen;
        var measurements = new List<LayoutSnapshot>();
        _loadedSettings = false;
        try
        {
            // The diagnostic workspace path is much longer than the real runtime path and masks natural-width changes.
            RuntimePathText.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"AutoHotkeyUX.Modern\runtime\2.0.29\v2\AutoHotkey64.exe") + " · 64-bit · Built-in runtime";
            for (var mask = 0; mask < 16; mask++)
            {
                for (var index = 0; index < toggles.Length; index++) toggles[index].IsOn = (mask & (1 << index)) != 0;
                ExplorerStatusText.Text = ExplorerShortcutsToggle.IsOn ? "Shortcuts: Running" : "Shortcuts are stopped.";
                SettingsInfoBar.IsOpen = mask != 0;
                SettingsInfoBar.Message = (mask % 3) switch
                {
                    0 => "Windows sign-in startup disabled.",
                    1 => "The workspace will start silently at Windows sign-in.",
                    _ => "Explorer shortcuts enabled with your saved action preferences."
                };
                await Task.Delay(60);
                UpdateLayout();
                measurements.Add(new(mask, SettingsInfoBar.IsOpen, TransformToVisual(owner).TransformPoint(default).X,
                    ActualWidth, body.TransformToVisual(scroll).TransformPoint(default).X, body.ActualWidth, body.DesiredSize.Width,
                    scroll.ViewportWidth, scroll.ExtentWidth, scroll.HorizontalOffset, heading.TransformToVisual(owner).TransformPoint(default).X,
                    toggles.Select(toggle => toggle.ActualWidth).ToArray(),
                    toggles.Select(toggle => ((Grid)toggle.Parent).ColumnDefinitions[1].ActualWidth).ToArray(),
                    CountSettingRowOverflows(toggles.Select(toggle => (Grid)toggle.Parent)
                        .Append((Grid)InterpreterComboBox.Parent).Append((Grid)((Grid)EditorCommandTextBox.Parent).Parent))));
            }
            return measurements;
        }
        finally
        {
            for (var index = 0; index < toggles.Length; index++) toggles[index].IsOn = originalStates[index];
            ExplorerStatusText.Text = originalStatus;
            RuntimePathText.Text = originalRuntime;
            SettingsInfoBar.Message = originalMessage;
            SettingsInfoBar.Severity = originalSeverity;
            SettingsInfoBar.IsOpen = originalOpen;
            _loadedSettings = previousLoaded;
        }
    }

    /// <summary>Checks that both setting labels and controls fit within their row after responsive layout.</summary>
    private static int CountSettingRowOverflows(IEnumerable<Grid> rows)
    {
        var count = 0;
        foreach (var row in rows)
            foreach (var child in row.Children.OfType<FrameworkElement>())
            {
                var x = child.TransformToVisual(row).TransformPoint(default).X;
                if (x < row.Padding.Left - 1 || x + child.ActualWidth > row.ActualWidth - row.Padding.Right + 1) count++;
            }
        return count;
    }
}
