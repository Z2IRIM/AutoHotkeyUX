using AutoHotkeyUX.Modern.Services;

namespace AutoHotkeyUX.Modern.Pages;

public sealed partial class DocumentationPage
{
    internal bool HasBrowserForDiagnostics => _browser is not null;
    internal string? DiagnosticError => DocumentInfoBar.IsOpen ? DocumentInfoBar.Message : null;
    internal string? DiagnosticAddress => _core?.Source;
    internal string DiagnosticInitialization => $"{_initializationStage}; started={_navigationStarted}, completed={_navigationCompleted}; address={DiagnosticAddress}; page={ActualWidth}x{ActualHeight}; host={BrowserHost.ActualWidth}x{BrowserHost.ActualHeight}; browserLoaded={_browser?.IsLoaded}; browser={_browser?.ActualWidth}x{_browser?.ActualHeight}; core={_browser?.CoreWebView2 is not null}; loaded={IsLoaded}";

    /// <summary>Uses the actual scoped browser to navigate a bundled topic during explicit verification.</summary>
    internal void NavigateDiagnosticTopic(string relativePath)
        => _core?.Navigate($"https://{DocumentationNavigationPolicy.HostName}/docs/{relativePath}");

    /// <summary>Reads rendered document state without depending on external browser automation.</summary>
    internal async Task<string> ReadDiagnosticDocumentAsync()
    {
        if (_core is not { } core) throw new InvalidOperationException("The documentation browser is unavailable.");
        return await core.ExecuteScriptAsync("JSON.stringify({title:document.title,theme:!!document.getElementById('ahk-workspace-theme'),frameTitle:document.querySelector('.lyt_frame')?.contentDocument?.title,frameTheme:!!document.querySelector('.lyt_frame')?.contentDocument?.getElementById('ahk-workspace-theme')})");
    }

    /// <summary>Exercises native Back using the page's real handler.</summary>
    internal void DiagnosticBack() => Back_Click(this, new Microsoft.UI.Xaml.RoutedEventArgs());
}
