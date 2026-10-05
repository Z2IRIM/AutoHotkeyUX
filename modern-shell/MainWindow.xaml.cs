using AutoHotkeyUX.Modern.Pages;
using AutoHotkeyUX.Modern.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace AutoHotkeyUX.Modern;

public sealed partial class MainWindow : Window
{
    private readonly AutoHotkeyRuntimeLocator _runtimeLocator;
    private readonly AutoHotkeyIntegration _integration;
    private readonly AutoHotkeySettings _settings;
    private readonly HomePage _homePage;
    private readonly NewScriptPage _newScriptPage;
    private readonly SettingsPage _settingsPage;

    /// <summary>
    /// Initializes the WinUI 3 shell and its three primary pages.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();

        Title = "AutoHotkey";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        try
        {
            SystemBackdrop = new MicaBackdrop();
        }
        catch
        {
            // Mica is optional; WinUI falls back to the normal window background.
        }

        AppWindow.Resize(new SizeInt32(1120, 720));

        _runtimeLocator = new AutoHotkeyRuntimeLocator();
        _integration = new AutoHotkeyIntegration(_runtimeLocator);
        _settings = new AutoHotkeySettings();

        _homePage = new HomePage(
            _integration,
            () => NavigateTo("new"),
            () => NavigateTo("settings"));

        _newScriptPage = new NewScriptPage(_integration);
        _settingsPage = new SettingsPage(_integration, _settings);

        RootNavigation.SelectedItem = RootNavigation.MenuItems[0];
        NavigateTo("home");
    }

    /// <summary>
    /// Switches the NavigationView content without creating duplicate page instances.
    /// </summary>
    private void NavigateTo(string tag)
    {
        PageHost.Content = tag switch
        {
            "new" => _newScriptPage,
            "settings" => _settingsPage,
            _ => _homePage
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
            _homePage.RefreshRuntime();
        }
        else if (tag == "settings")
        {
            _settingsPage.Refresh();
        }
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
