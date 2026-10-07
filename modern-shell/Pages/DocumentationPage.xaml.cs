using AutoHotkeyUX.Modern.Models;
using AutoHotkeyUX.Modern.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.UI.Windowing;
using System.Text.Json;

namespace AutoHotkeyUX.Modern.Pages;

public sealed partial class DocumentationPage : Page
{
    private readonly DocumentationService _documents;
    private readonly Action _goHome;
    private readonly Window _host;
    private bool _hostSubscribed;
    private Uri? _resumeOfflineUri;
    private readonly string _themeCss;
    private readonly HashSet<string> _externalOrigins = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _initializationCancellation;
    private WebView2? _browser;
    private CoreWebView2? _core;
    private Action? _detachNavigation;
    private DocumentationLocation? _location;
    private Uri? _pendingExternal;
    private int _generation;
    private Task? _initializing;
    private string _initializationStage = "Not started";
    private int _navigationStarted, _navigationCompleted;
    internal bool BrowserReady => _core is not null && LoadingOverlay.Visibility == Visibility.Collapsed;

    /// <summary>Creates a reusable tool page whose browser exists only while it remains visible.</summary>
    internal DocumentationPage(DocumentationService documents, Window host, Action goHome)
    {
        InitializeComponent(); _documents = documents; _host = host; _goHome = goHome;
        using var resource = typeof(DocumentationPage).Assembly.GetManifestResourceStream("AutoHotkeyUX.Modern.DocumentationTheme.css")
            ?? throw new InvalidDataException("The documentation theme resource is missing.");
        using var reader = new StreamReader(resource); _themeCss = reader.ReadToEnd();
        Loaded += Page_Loaded; Unloaded += Page_Unloaded; ActualThemeChanged += Theme_Changed;
    }

    /// <summary>Starts one initialization for this visible page generation.</summary>
    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_hostSubscribed) { _host.AppWindow.Changed += Host_Changed; _hostSubscribed = true; }
        await InitializeAsync();
    }

    /// <summary>Cancels extraction/initialization and releases the browser when leaving the tool.</summary>
    private void Page_Unloaded(object sender, RoutedEventArgs e)
    { _host.AppWindow.Changed -= Host_Changed; _hostSubscribed = false; CloseBrowser(); }

    /// <summary>Releases hidden/minimized browser resources and restores the last offline topic when shown.</summary>
    private async void Host_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!IsLoaded) return;
        if (!HostVisible) { if (_browser is not null || _initializationCancellation is not null) CloseBrowser(); }
        else await InitializeAsync();
    }

    private bool HostVisible => _host.AppWindow.IsVisible && _host.AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Minimized };

    /// <summary>Prepares offline files and creates WebView2 on the WinUI thread without blocking it.</summary>
    private async Task InitializeAsync()
    {
        if (!IsLoaded || !HostVisible || _browser is not null || _initializing is { IsCompleted: false }) return;
        _initializationCancellation = new CancellationTokenSource();
        var token = _initializationCancellation.Token;
        var generation = _generation;
        _initializing = InitializeBrowserAsync(generation, token);
        await _initializing;
    }

    /// <summary>Attaches scoped navigation handlers only to the current generation's browser.</summary>
    private async Task InitializeBrowserAsync(int generation, CancellationToken cancellationToken)
    {
        try
        {
            LoadingOverlay.Visibility = Visibility.Visible; LoadingRing.IsActive = true;
            LoadingText.Text = "Loading offline manual…"; DocumentInfoBar.IsOpen = false;
            _initializationStage = "Preparing cache";
            _location = await _documents.EnsureReadyAsync(cancellationToken);
            if (!IsCurrent(generation, cancellationToken)) return;
            _initializationStage = "Checking browser runtime";
            try { CoreWebView2Environment.GetAvailableBrowserVersionString(null); }
            catch (Exception runtimeError) { throw new InvalidOperationException("WebView2 is unavailable. Install the official Microsoft WebView2 runtime, then select Retry.", runtimeError); }
            var userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoHotkeyUX.Modern", "documentation-browser");
            _initializationStage = "Creating environment";
            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, userData, null);
            if (!IsCurrent(generation, cancellationToken)) return;
            var browser = new WebView2(); _browser = browser; BrowserHost.Children.Add(browser);
            _initializationStage = "Creating controller";
            await browser.EnsureCoreWebView2Async(environment, null);
            if (!IsCurrent(generation, cancellationToken) || _browser != browser) return;
            var core = browser.CoreWebView2;
            _core = core;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsWebMessageEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.SetVirtualHostNameToFolderMapping(DocumentationNavigationPolicy.HostName, _location.RootDirectory, CoreWebView2HostResourceAccessKind.DenyCors);
            AttachNavigation(core, browser, generation, cancellationToken);
            ContentsButton.IsEnabled = true;
            _initializationStage = "Navigating manual";
            if (_resumeOfflineUri is { } resume) core.Navigate(resume.AbsoluteUri); else NavigateHome();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!IsCurrent(generation, cancellationToken)) return;
            CloseBrowser();
            ShowError($"The offline manual could not load: {ex.Message} Select Retry.");
            ServiceDiagnostics.Write("Documentation", "Offline browser initialization failed.", ex);
        }
        finally
        {
            if (generation == _generation && IsLoaded) LoadingRing.IsActive = false;
        }
    }

    /// <summary>Checks the epoch before asynchronous initialization touches any page state.</summary>
    private bool IsCurrent(int generation, CancellationToken token) => generation == _generation && IsLoaded && HostVisible && !token.IsCancellationRequested;

    /// <summary>Accepts only the offline manual or origins explicitly selected in this page.</summary>
    private bool Allowed(Uri uri) => DocumentationNavigationPolicy.IsOffline(uri)
        || (DocumentationNavigationPolicy.IsExternalReference(uri) && _externalOrigins.Contains(uri.GetLeftPart(UriPartial.Authority)));

    /// <summary>Shows the actual external destination before granting it in-panel navigation.</summary>
    private void OfferExternal(Uri uri)
    { _pendingExternal = uri; ExternalInfoBar.Message = uri.AbsoluteUri; ExternalInfoBar.IsOpen = true; }

    /// <summary>Grants the user-selected HTTPS origin only for this browser generation.</summary>
    private void External_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingExternal is not { } uri || _core is not { } core) return;
        _externalOrigins.Add(uri.GetLeftPart(UriPartial.Authority)); _pendingExternal = null;
        core.Navigate(uri.AbsoluteUri);
    }

    /// <summary>Updates offline typography and colors when the host appearance changes.</summary>
    private async void Theme_Changed(FrameworkElement sender, object args)
    {
        try { if (IsLoaded && _core is { } core && Uri.TryCreate(core.Source, UriKind.Absolute, out var uri) && DocumentationNavigationPolicy.IsOffline(uri)) await ApplyThemeAsync(); }
        catch (Exception ex) { if (IsLoaded) ShowError(ex.Message); }
    }

    /// <summary>Injects fixed CSS with serialized theme values; no user text is executed as script.</summary>
    private async Task ApplyThemeAsync()
    {
        if (_core is not { } core) return;
        var dark = ActualTheme == ElementTheme.Dark;
        var tokens = dark
            ? ":root{--ahk-bg:#2e3638;--ahk-text:#f4f6f6;--ahk-code:#394143;--ahk-border:#465052;--ahk-link:#80c8d7;--ahk-selection:#365d66;color-scheme:dark;}"
            : ":root{--ahk-bg:#fcfcfd;--ahk-text:#23272c;--ahk-code:#f2f4f7;--ahk-border:#dadddf;--ahk-link:#217d91;--ahk-selection:#d6e9ed;color-scheme:light;}";
        var css = JsonSerializer.Serialize(tokens + _themeCss);
        await core.ExecuteScriptAsync($$"""
            (() => {
              const css = {{css}};
              const key = '__ahkWorkspaceTheme';
              let state = window[key];
              if (!state) {
                state = window[key] = { css, frames: new WeakSet() };
                state.apply = doc => {
                  if (!doc?.head) return;
                  let style = doc.getElementById('ahk-workspace-theme');
                  if (!style) { style = doc.createElement('style'); style.id = 'ahk-workspace-theme'; doc.head.appendChild(style); }
                  if (style.textContent !== state.css) style.textContent = state.css;
                  doc.querySelectorAll('iframe').forEach(frame => {
                    const applyFrame = () => { try { state.apply(frame.contentDocument); } catch {} };
                    if (!state.frames.has(frame)) { state.frames.add(frame); frame.addEventListener('load', applyFrame); }
                    applyFrame();
                  });
                };
                new MutationObserver(changes => {
                  if (changes.some(change => [...change.addedNodes].some(node => node.nodeType === 1 && (node.tagName === 'IFRAME' || node.querySelector?.('iframe'))))) state.apply(document);
                }).observe(document.documentElement, { childList: true, subtree: true });
              }
              state.css = css; state.apply(document);
            })()
            """);
    }

    /// <summary>Navigates to the bundled manual's verified entry page.</summary>
    private void NavigateHome()
    { if (_location is not null) _core?.Navigate($"https://{DocumentationNavigationPolicy.HostName}/{_location.HomeRelativePath}"); }
    /// <summary>Returns to the workspace Home route.</summary>
    private void Home_Click(object sender, RoutedEventArgs e) => _goHome();
    /// <summary>Uses browser history inside the same panel.</summary>
    private void Back_Click(object sender, RoutedEventArgs e) { if (_core is { CanGoBack: true } core) core.GoBack(); }
    /// <summary>Uses forward history inside the same panel.</summary>
    private void Forward_Click(object sender, RoutedEventArgs e) { if (_core is { CanGoForward: true } core) core.GoForward(); }
    /// <summary>Returns to the verified offline contents entry.</summary>
    private void Contents_Click(object sender, RoutedEventArgs e) => NavigateHome();
    /// <summary>Retries using a fresh browser rather than reviving a closed control.</summary>
    private async void Retry_Click(object sender, RoutedEventArgs e) { CloseBrowser(); await InitializeAsync(); }

    /// <summary>Releases browser resources and makes outstanding callbacks stale.</summary>
    private void CloseBrowser()
    {
        if (_core is { } active && Uri.TryCreate(active.Source, UriKind.Absolute, out var address) && DocumentationNavigationPolicy.IsOffline(address)) _resumeOfflineUri = address;
        _generation++; _initializationCancellation?.Cancel(); _initializationCancellation?.Dispose(); _initializationCancellation = null;
        _initializing = null; _externalOrigins.Clear(); _pendingExternal = null;
        _detachNavigation?.Invoke(); _detachNavigation = null; _core = null;
        if (_browser is { } browser)
        {
            _browser = null; BrowserHost.Children.Clear(); browser.Close();
        }
        BackButton.IsEnabled = false; ForwardButton.IsEnabled = false; ContentsButton.IsEnabled = false;
    }

    /// <summary>Shows a readable load failure while keeping the page and retry button usable.</summary>
    private void ShowError(string message)
    { if (!IsLoaded) return; DocumentInfoBar.Message = message; DocumentInfoBar.IsOpen = true; LoadingText.Text = "Manual unavailable — select Retry"; LoadingRing.IsActive = false; }
}
