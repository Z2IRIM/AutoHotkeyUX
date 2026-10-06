using AutoHotkeyUX.Modern.Pages;
using Microsoft.UI.Xaml.Controls;

namespace AutoHotkeyUX.Modern;

public sealed partial class MainWindow
{
    /// <summary>Loads real XAML pages and checks navigation/search for the explicit --verify-ui diagnostic command.</summary>
    internal async Task<object> VerifyPagesAsync()
    {
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
        AppWindow.Resize(new Windows.Graphics.SizeInt32(MinimumWindowWidth, MinimumWindowHeight));
        await Task.Delay(120);
        if (_scriptsPage.VerifyActionLayout() != true) throw new InvalidOperationException("Scripts action row overflows at the minimum desktop size.");
        var minimumPageWidth = _scriptsPage.ActualWidth;
        AppWindow.Resize(originalSize);
        NavigateTo("home");
        return new { Passed = true, Checks = checks, ScriptsSearch = true, MinimumSizeActionLayout = true,
            MinimumPageWidth = minimumPageWidth, CatalogCount = _services.Catalog.Snapshot().Count, RuntimePath = _integration.FindRuntime()?.Path };
    }
}
