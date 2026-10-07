using AutoHotkeyUX.Modern.Pages;
using Microsoft.UI.Xaml.Controls;

namespace AutoHotkeyUX.Modern;

public sealed partial class MainWindow
{
    /// <summary>Checks startup placement, icon, navigation and real template geometry for the explicit UI command.</summary>
    internal async Task<object> VerifyPagesAsync()
    {
        if (!HasCustomIcon) throw new InvalidOperationException("The embedded application icon was not applied to the window.");
        var startupPlacement = VerifyStartupPlacement();
        if (AppTitleBar.FindName("GlobalSearchBox") is not null) throw new InvalidOperationException("The removed title-bar search is still present.");
        var checks = new List<object>();
        foreach (var tag in new[] { "home", "new", "settings", "scripts", "home", "scripts" })
        {
            NavigateTo(tag);
            // Allow Loaded and dispatcher-bound service notifications to settle in the real WinUI loop.
            await Task.Delay(80);
            var expected = tag switch
            {
                "new" => typeof(NewScriptPage), "settings" => typeof(SettingsPage),
                "scripts" => typeof(ScriptsPage), _ => typeof(HomePage)
            };
            var page = PageHost.Content as Page;
            var selected = new[] { HomeNavButton, NewScriptNavButton, ScriptsNavButton, SettingsNavButton }
                .Count(button => button.IsChecked == true);
            if (page?.GetType() != expected || !page.IsLoaded || page.ActualWidth <= 0 || selected != 1)
                throw new InvalidOperationException($"UI diagnostic failed for '{tag}'. See startup-error.log.");
            checks.Add(new { Page = tag, Loaded = page.IsLoaded, Width = page.ActualWidth, SelectedNavigationCount = selected });
        }
        if (_scriptsPage is null || !await _scriptsPage.VerifySearchAsync()) throw new InvalidOperationException("Scripts search diagnostic failed.");
        var originalSize = AppWindow.Size;
        var scale = RootLayout.XamlRoot.RasterizationScale;
        AppWindow.Resize(new Windows.Graphics.SizeInt32((int)Math.Round(1280 * scale), (int)Math.Round(820 * scale)));
        NavigateTo("new");
        await Task.Delay(120);
        var wideTemplates = _newScriptPage!.VerifyTemplateLayout(false);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(MinimumWindowWidth, MinimumWindowHeight));
        await Task.Delay(120);
        var narrowTemplates = _newScriptPage.VerifyTemplateLayout(RootLayout.ActualWidth < 960);
        NavigateTo("scripts");
        await Task.Delay(80);
        if (_scriptsPage.VerifyActionLayout() != true) throw new InvalidOperationException("Scripts action row overflows at the minimum desktop size.");
        var minimumPageWidth = _scriptsPage.ActualWidth;
        AppWindow.Resize(originalSize);
        NavigateTo("home");
        return new { Passed = true, StartupPlacement = startupPlacement, TitleBarSearchRemoved = true,
            WideTemplates = wideTemplates, NarrowTemplates = narrowTemplates, CustomWindowIconApplied = HasCustomIcon, Checks = checks, ScriptsSearch = true, MinimumSizeActionLayout = true,
            MinimumPageWidth = minimumPageWidth, CatalogCount = _services.Catalog.Snapshot().Count, RuntimePath = _integration.FindRuntime()?.Path };
    }
}
