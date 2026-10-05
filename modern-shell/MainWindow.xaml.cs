using AutoHotkeyUX.Modern.Pages;
using AutoHotkeyUX.Modern.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace AutoHotkeyUX.Modern;

public sealed partial class MainWindow : Window
{
    private readonly AutoHotkeyRuntimeLocator _runtimeLocator;
    private readonly AutoHotkeyIntegration _integration;
    private readonly AutoHotkeySettings _settings;

    private HomePage? _homePage;
    private NewScriptPage? _newScriptPage;
    private SettingsPage? _settingsPage;

    /// <summary>
    /// Initializes the WinUI 3 shell using the standard system title bar for maximum compatibility.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();

        Title = "AutoHotkey";
        AppWindow.Resize(new SizeInt32(1120, 720));

        var embeddedRuntime = new EmbeddedAutoHotkeyRuntime();
        _runtimeLocator = new AutoHotkeyRuntimeLocator(embeddedRuntime);
        _integration = new AutoHotkeyIntegration(_runtimeLocator);
        _settings = new AutoHotkeySettings();

        RootNavigation.SelectedItem = RootNavigation.MenuItems[0];
        NavigateTo("home");
    }

    /// <summary>
    /// Lazily creates pages so a page-level XAML failure can be reported without taking down the whole process.
    /// </summary>
    private void NavigateTo(string tag)
    {
        try
        {
            PageHost.Content = tag switch
            {
                "new" => _newScriptPage ??= new NewScriptPage(_integration),
                "settings" => _settingsPage ??= new SettingsPage(_integration, _settings),
                _ => _homePage ??= new HomePage(
                    _integration,
                    () => NavigateTo("new"),
                    () => NavigateTo("settings"))
            };

            foreach (var item in RootNavigation.MenuItems.OfType<NavigationViewItem>())
            {
                if (string.Equals(item.Tag?.ToString(), tag, StringComparison.Ordinal))
                {
                    RootNavigation.SelectedItem = item;
                    break;
                }
            }

            if (tag == "home")
            {
                _homePage?.RefreshRuntime();
            }
            else if (tag == "settings")
            {
                _settingsPage?.Refresh();
            }
        }
        catch (Exception ex)
        {
            App.ReportStartupFailure($"Page '{tag}' failed to load", ex);
            PageHost.Content = CreateFallbackContent(tag, ex);
        }
    }

    /// <summary>
    /// Builds a code-only fallback panel that does not depend on the failed page XAML.
    /// </summary>
    private static UIElement CreateFallbackContent(string tag, Exception exception)
    {
        var panel = new StackPanel
        {
            Padding = new Thickness(36),
            Spacing = 12
        };

        panel.Children.Add(new TextBlock
        {
            Text = $"Failed to load {tag}",
            FontSize = 26,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });

        panel.Children.Add(new TextBlock
        {
            Text = exception.Message,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.72
        });

        panel.Children.Add(new TextBlock
        {
            Text = @"Full details were written to %LOCALAPPDATA%\AutoHotkeyUX.Modern\startup-error.log",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.72
        });

        return panel;
    }

    /// <summary>
    /// Handles navigation selection using each item's stable tag.
    /// </summary>
    private void RootNavigation_SelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is string tag)
        {
            NavigateTo(tag);
        }
    }
}
