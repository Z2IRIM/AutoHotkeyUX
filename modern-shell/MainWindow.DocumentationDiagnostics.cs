using AutoHotkeyUX.Modern.Services;
using System.Security.Cryptography;

namespace AutoHotkeyUX.Modern;

public sealed partial class MainWindow
{
    /// <summary>Verifies offline cache bytes, real WebView navigation and release across page changes.</summary>
    internal async Task<object> VerifyDocumentationAsync()
    {
        var chm = Path.Combine(Path.GetDirectoryName(_integration.FindRuntime()!.Path)!, "AutoHotkey.chm");
        var originalHash = SHA256.HashData(await File.ReadAllBytesAsync(chm));
        var originalLength = new FileInfo(chm).Length;
        var location = await _services.Documentation.EnsureReadyAsync(CancellationToken.None);
        if (!DocumentationService.ValidateCache(location.RootDirectory, Convert.ToHexString(originalHash), CancellationToken.None))
            throw new InvalidOperationException("Offline manual cache hashes failed.");
        var cacheEntry = Path.Combine(location.RootDirectory, "docs", "index.htm");
        await File.WriteAllTextAsync(cacheEntry, "controlled incomplete cache fixture");
        if (DocumentationService.ValidateCache(location.RootDirectory, Convert.ToHexString(originalHash), CancellationToken.None))
            throw new InvalidOperationException("A corrupted offline cache was accepted.");
        location = await _services.Documentation.EnsureReadyAsync(CancellationToken.None);
        if (!DocumentationService.ValidateCache(location.RootDirectory, Convert.ToHexString(originalHash), CancellationToken.None))
            throw new InvalidOperationException("A corrupted offline cache could not rebuild.");
        if (DocumentationService.ValidateCache(Path.Combine(Environment.CurrentDirectory, ".verification", "missing-doc-cache"), Convert.ToHexString(originalHash), CancellationToken.None))
            throw new InvalidOperationException("An incomplete manual cache was accepted.");
        var blocked = new[] { "file:///C:/Windows/win.ini", "javascript:alert(1)", "http://example.com/", "ahk://run", "https://ahk-docs.invalid/other-file", "https://user@ahk-docs.invalid/docs/index.htm" };
        if (blocked.Any(address => DocumentationNavigationPolicy.IsOffline(new Uri(address))))
            throw new InvalidOperationException("Unsafe documentation navigation was accepted.");
        NavigateTo("docs");
        try { await WaitForToolAsync(() => _documentationPage!.BrowserReady || _documentationPage.DiagnosticError is not null, "offline browser initialization", 15000); }
        catch (TimeoutException) { throw new TimeoutException($"Documentation: {_documentationPage!.DiagnosticInitialization}; content alignment={PageHost.VerticalContentAlignment}"); }
        if (!_documentationPage!.BrowserReady) throw new InvalidOperationException(_documentationPage.DiagnosticError);
        await Task.Delay(400);
        var homeDocument = await _documentationPage.ReadDiagnosticDocumentAsync();
        if (!homeDocument.Contains("\\\"theme\\\":true", StringComparison.Ordinal))
            throw new InvalidOperationException($"The offline manual theme was not applied: {homeDocument}");
        _documentationPage.NavigateDiagnosticTopic("lib/Run.htm");
        await WaitForToolAsync(() => _documentationPage.BrowserReady && _documentationPage.DiagnosticAddress?.Contains("lib/Run.htm", StringComparison.OrdinalIgnoreCase) == true, "Run topic");
        var runDocument = await _documentationPage.ReadDiagnosticDocumentAsync();
        _documentationPage.NavigateDiagnosticTopic("Hotkeys.htm");
        await WaitForToolAsync(() => _documentationPage.BrowserReady && _documentationPage.DiagnosticAddress?.Contains("Hotkeys.htm", StringComparison.OrdinalIgnoreCase) == true, "Hotkeys topic");
        _documentationPage.DiagnosticBack();
        await WaitForToolAsync(() => _documentationPage.BrowserReady && _documentationPage.DiagnosticAddress?.Contains("lib/Run.htm", StringComparison.OrdinalIgnoreCase) == true, "native document Back");
        NavigateTo("home"); await Task.Delay(100);
        if (_documentationPage.HasBrowserForDiagnostics) throw new InvalidOperationException("Leaving Documentation retained its browser control.");
        NavigateTo("docs");
        await WaitForToolAsync(() => _documentationPage.BrowserReady || _documentationPage.DiagnosticError is not null, "documentation re-entry", 30000);
        if (!_documentationPage.BrowserReady) throw new InvalidOperationException(_documentationPage.DiagnosticError);
        AppWindow.Hide(); await Task.Delay(100);
        if (_documentationPage.HasBrowserForDiagnostics) throw new InvalidOperationException("Hidden Documentation retained its browser control.");
        AppWindow.Show(); Activate();
        await WaitForToolAsync(() => _documentationPage.BrowserReady || _documentationPage.DiagnosticError is not null, "documentation visibility restore", 15000);
        if (!_documentationPage.BrowserReady) throw new InvalidOperationException(_documentationPage.DiagnosticError);
        NavigateTo("home");
        if (originalLength != new FileInfo(chm).Length || !originalHash.SequenceEqual(SHA256.HashData(await File.ReadAllBytesAsync(chm))))
            throw new InvalidOperationException("Offline help extraction changed the bundled CHM.");
        return new { OfflineCache = true, SourceChmUnchanged = true, HomeRendered = homeDocument, RunRendered = runDocument,
            InternalLinks = true, Back = true, BrowserReleased = true, HiddenReleased = true, ReEntry = true, CorruptCacheRebuilt = true, UnsafeProtocolsBlocked = true,
            Version = location.Version, CacheDirectory = location.RootDirectory };
    }
}
