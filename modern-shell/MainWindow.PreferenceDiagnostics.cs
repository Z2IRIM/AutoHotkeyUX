namespace AutoHotkeyUX.Modern;

public sealed partial class MainWindow
{
    /// <summary>Checks the approved secondary Settings route in a fully isolated native verification instance.</summary>
    internal async Task<object> VerifyShortcutPreferencesAsync()
    {
        var shell = await VerifyPagesAsync();
        NavigateTo("shortcuts");
        await Task.Delay(150);
        if (_shortcutSettingsPage is null || !ReferenceEquals(PageHost.Content, _shortcutSettingsPage) || SettingsNavButton.IsChecked != true)
            throw new InvalidOperationException("Explorer preferences must open inside Settings.");
        var form = await _shortcutSettingsPage.VerifyPreferencesAsync();
        NavigateTo("settings"); await Task.Delay(60);
        NavigateTo("shortcuts"); await Task.Delay(100);
        var originalSize = AppWindow.Size;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(MinimumWindowWidth, MinimumWindowHeight));
        await Task.Delay(150);
        var layout = _shortcutSettingsPage.VerifyRetainedDraftAndLayout();
        AppWindow.Resize(originalSize);
        return new { Passed = true, SettingsRoute = true, Shell = shell, Form = form, MinimumLayout = layout,
            IsolatedStateDirectory = _services.StateDirectory };
    }
}
