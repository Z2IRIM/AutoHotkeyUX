using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AutoHotkeyUX.Modern.Pages;

public sealed partial class ShortcutSettingsPage
{
    /// <summary>Rebuilds the bounded activity list only on a completed action while the page is visible.</summary>
    private void Activity_Changed(object? sender, EventArgs e)
        => DispatcherQueue.TryEnqueue(() => { if (_subscribed) RenderActivity(); });

    /// <summary>Displays real per-session results with details expanded inside the workspace.</summary>
    private void RenderActivity()
    {
        if (!_ready) return;
        var entries = _activity.Snapshot();
        EmptyActivityText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ActivityList.Children.Clear();
        foreach (var entry in entries)
        {
            var heading = new StackPanel { Spacing = 5 };
            heading.Children.Add(new TextBlock
            {
                Text = entry.Action == "Terminal" ? entry.Succeeded ? "Terminal opened" : "Terminal failed"
                    : entry.Succeeded ? "Archive extracted" : "Extraction failed",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap
            });
            heading.Children.Add(new TextBlock { Text = entry.Source, FontSize = 13, TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
            var details = new StackPanel { Spacing = 8 };
            details.Children.Add(new TextBlock { Text = $"{entry.Time:HH:mm:ss} · {entry.DurationMilliseconds:N0} ms · {(entry.Succeeded ? "Completed" : "Failed")}", TextWrapping = TextWrapping.Wrap });
            if (entry.Destination is { } destination) details.Children.Add(new TextBlock { Text = "Output: " + destination, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
            details.Children.Add(new TextBlock { Text = entry.Detail, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
            ActivityList.Children.Add(new Expander { Header = heading, Content = details, HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch });
        }
    }
}
