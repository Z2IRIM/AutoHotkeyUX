using AutoHotkeyUX.Modern.Pages;
using AutoHotkeyUX.Modern.Models;
using AutoHotkeyUX.Modern.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AutoHotkeyUX.Modern;

public sealed partial class MainWindow : Window
{
    private readonly AutoHotkeyIntegration _integration;
    private readonly AutoHotkeySettings _settings;
    private readonly ApplicationServices _services;

    private HomePage? _homePage;
    private NewScriptPage? _newScriptPage;
    private SettingsPage? _settingsPage;
    private ScriptsPage? _scriptsPage;
    private WindowSpyPage? _windowSpyPage;
    private CompilePage? _compilePage;
    private DocumentationPage? _documentationPage;
    private ShortcutSettingsPage? _shortcutSettingsPage;
    internal bool HasCustomIcon { get; private set; }

    /// <summary>
    /// Initializes the screen-proportioned Windows 11 shell, approved icon and shared services.
    /// </summary>
    internal MainWindow(ApplicationServices services, ApplicationIconService? icon)
    {
        InitializeComponent();

        Title = "AutoHotkey";
        ConfigureInitialWindowBounds();
        AppWindow.Changed += AppWindow_Changed;
        ConfigureWindowChrome();
        ConfigureWindowIcon(icon);

        _services = services;
        _integration = services.Integration;
        _settings = services.Settings;

        NavigateTo("home");
    }

    /// <summary>Applies the embedded H icon to native window/taskbar surfaces while keeping the workspace usable on failure.</summary>
    private void ConfigureWindowIcon(ApplicationIconService? icon)
    {
        if (icon is null) return;
        try
        {
            AppWindow.SetIcon(Win32Interop.GetIconIdFromIcon(icon.WindowIcon));
            HasCustomIcon = true;
        }
        catch (Exception ex) { ServiceDiagnostics.Write("Icon", "The custom window icon could not be applied.", ex); }
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
                "spy" => _windowSpyPage ??= new WindowSpyPage(_services.WindowSpy, this, () => NavigateTo("home")),
                "compile" => _compilePage ??= new CompilePage(_services.Compiler, _integration, () => NavigateTo("home")),
                "docs" => _documentationPage ??= new DocumentationPage(_services.Documentation, this, () => NavigateTo("home")),
                "new" => _newScriptPage ??= new NewScriptPage(_services, () => NavigateTo("scripts")),
                "settings" => _settingsPage ??= new SettingsPage(_integration, _settings, _services.WindowsStartup, _services.ScriptStartup, _services.Execution, () => NavigateTo("shortcuts")),
                "shortcuts" => _shortcutSettingsPage ??= new ShortcutSettingsPage(_services, () => NavigateTo("settings")),
                "scripts" => _scriptsPage ??= new ScriptsPage(_services.Catalog, _services.Execution, _services.ScriptStartup, _integration, _settings, OpenVisualScriptAsync),
                _ => _homePage ??= new HomePage(
                    _integration,
                    () => NavigateTo("new"),
                    () => NavigateTo("settings"),
                    _services.Catalog,
                    OpenTool)
            };

            SetSelectedNavigation(tag is "spy" or "compile" or "docs" ? "home" : tag == "shortcuts" ? "settings" : tag);

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

    /// <summary>Translates a Home tool intent into a route owned by the existing content host.</summary>
    private void OpenTool(WorkspaceTool tool) => NavigateTo(tool switch
    { WorkspaceTool.WindowSpy => "spy", WorkspaceTool.Compile => "compile", WorkspaceTool.Documentation => "docs", _ => "home" });

    /// <summary>Routes generated workflows into the cached native editor while leaving ordinary AHK in the configured editor.</summary>
    private async Task<bool> OpenVisualScriptAsync(string path)
    {
        if (!await Task.Run(() => VisualFlowStore.IsVisualCandidate(path))) return false;
        _newScriptPage ??= new NewScriptPage(_services, () => NavigateTo("scripts"));
        await _newScriptPage.OpenAsync(path, RootLayout.XamlRoot);
        NavigateTo("new");
        return true;
    }

    /// <summary>
    /// Keeps the custom sidebar mutually exclusive using the native RadioButton group state.
    /// </summary>
    private void SetSelectedNavigation(string tag)
    {
        HomeNavButton.IsChecked =
            string.Equals(tag, "home", StringComparison.Ordinal);

        NewScriptNavButton.IsChecked =
            string.Equals(tag, "new", StringComparison.Ordinal);

        SettingsNavButton.IsChecked =
            string.Equals(tag, "settings", StringComparison.Ordinal);
        ScriptsNavButton.IsChecked = string.Equals(tag, "scripts", StringComparison.Ordinal);
    }

    /// <summary>
    /// Routes a sidebar button to its corresponding page and restores its checked state.
    /// </summary>
    private void SidebarNavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton button)
        {
            return;
        }

        var tag = ReferenceEquals(button, NewScriptNavButton) ? "new"
            : ReferenceEquals(button, SettingsNavButton) ? "settings"
            : ReferenceEquals(button, ScriptsNavButton) ? "scripts" : "home";

        NavigateTo(tag);
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
