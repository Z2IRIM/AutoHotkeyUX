using AutoHotkeyUX.Modern.Pages;
using AutoHotkeyUX.Modern.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
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
    /// Initializes the integrated Windows 11 shell and shared AutoHotkey services.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();

        Title = "AutoHotkey";
        AppWindow.Resize(new SizeInt32(1180, 760));
        ConfigureWindowChrome();

        var embeddedRuntime = new EmbeddedAutoHotkeyRuntime();
        _runtimeLocator = new AutoHotkeyRuntimeLocator(embeddedRuntime);
        _integration = new AutoHotkeyIntegration(_runtimeLocator);
        _settings = new AutoHotkeySettings();

        NavigateTo("home");
    }

    /// <summary>
    /// Extends content into the title bar and enables a native Mica backdrop without replacing caption buttons.
    /// </summary>
    private void ConfigureWindowChrome()
    {
        try
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            SystemBackdrop = new MicaBackdrop();

            AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        }
        catch
        {
            // Older Windows builds fall back to the standard backdrop/title-bar behavior.
        }
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

            SetSelectedNavigation(tag);

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
    /// Keeps the custom sidebar mutually exclusive even though it uses lightweight ToggleButtons.
    /// </summary>
    private void SetSelectedNavigation(string tag)
    {
        HomeNavButton.IsChecked =
            string.Equals(tag, "home", StringComparison.Ordinal);

        NewScriptNavButton.IsChecked =
            string.Equals(tag, "new", StringComparison.Ordinal);

        SettingsNavButton.IsChecked =
            string.Equals(tag, "settings", StringComparison.Ordinal);
    }

    /// <summary>
    /// Routes a sidebar button to its corresponding page and restores its checked state.
    /// </summary>
    private void SidebarNavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton button)
        {
            return;
        }

        var tag =
            ReferenceEquals(button, NewScriptNavButton)
                ? "new"
                : ReferenceEquals(button, SettingsNavButton)
                    ? "settings"
                    : "home";

        NavigateTo(tag);
    }

    /// <summary>
    /// Provides a compact command-palette-like search for the shell's primary pages and tools.
    /// </summary>
    private void GlobalSearchBox_QuerySubmitted(
        AutoSuggestBox sender,
        AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var query = (args.QueryText ?? string.Empty)
            .Trim()
            .ToLowerInvariant();

        if (query.Length == 0)
        {
            return;
        }

        if (query.Contains("setting", StringComparison.Ordinal)
            || query.Contains("preference", StringComparison.Ordinal))
        {
            NavigateTo("settings");
        }
        else if (query.Contains("new", StringComparison.Ordinal)
                 || query.Contains("create", StringComparison.Ordinal))
        {
            NavigateTo("new");
        }
        else if (query.Contains("window", StringComparison.Ordinal)
                 && query.Contains("spy", StringComparison.Ordinal))
        {
            _integration.OpenWindowSpy();
        }
        else if (query.Contains("compile", StringComparison.Ordinal))
        {
            _integration.OpenCompiler();
        }
        else if (query.Contains("doc", StringComparison.Ordinal)
                 || query.Contains("help", StringComparison.Ordinal))
        {
            _integration.OpenDocumentation();
        }
        else
        {
            NavigateTo("home");
        }

        sender.Text = string.Empty;
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
}
