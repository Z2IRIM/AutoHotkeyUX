using Microsoft.Web.WebView2.Core;
using Microsoft.UI.Xaml;
using AutoHotkeyUX.Modern.Services;

namespace AutoHotkeyUX.Modern.Pages;

public sealed partial class DocumentationPage
{
    /// <summary>Binds callbacks to the retained Core and page generation instead of unstable WinRT sender identity.</summary>
    private void AttachNavigation(CoreWebView2 core, Microsoft.UI.Xaml.Controls.WebView2 browser, int generation, CancellationToken token)
    {
        // Determines whether a callback still belongs to this visible browser instance.
        bool Current() => IsCurrent(generation, token) && _browser == browser && _core == core;
        Windows.Foundation.TypedEventHandler<CoreWebView2, CoreWebView2NavigationStartingEventArgs> starting = (_, args) => { if (Current()) Navigation_Starting(core, args); else args.Cancel = true; };
        Windows.Foundation.TypedEventHandler<CoreWebView2, CoreWebView2NavigationStartingEventArgs> frame = (_, args) => { if (Current()) FrameNavigation_Starting(core, args); else args.Cancel = true; };
        Windows.Foundation.TypedEventHandler<CoreWebView2, CoreWebView2NavigationCompletedEventArgs> completed = (_, args) => { if (Current()) Navigation_Completed(core, args); };
        Windows.Foundation.TypedEventHandler<CoreWebView2, object> history = (_, args) => { if (Current()) History_Changed(core, args); };
        Windows.Foundation.TypedEventHandler<CoreWebView2, CoreWebView2NewWindowRequestedEventArgs> newWindow = (_, args) => { if (Current()) NewWindow_Requested(core, args); else args.Handled = true; };
        Windows.Foundation.TypedEventHandler<CoreWebView2, CoreWebView2PermissionRequestedEventArgs> permission = (_, args) => Permission_Requested(core, args);
        Windows.Foundation.TypedEventHandler<CoreWebView2, CoreWebView2DownloadStartingEventArgs> download = (_, args) => { if (Current()) Download_Starting(core, args); else args.Cancel = true; };
        Windows.Foundation.TypedEventHandler<CoreWebView2, CoreWebView2ProcessFailedEventArgs> failed = (_, args) => { if (Current()) Process_Failed(core, args); };
        core.NavigationStarting += starting; core.FrameNavigationStarting += frame;
        core.NavigationCompleted += completed; core.HistoryChanged += history;
        core.NewWindowRequested += newWindow; core.PermissionRequested += permission;
        core.DownloadStarting += download; core.ProcessFailed += failed;
        _detachNavigation = () =>
        {
            core.NavigationStarting -= starting; core.FrameNavigationStarting -= frame;
            core.NavigationCompleted -= completed; core.HistoryChanged -= history;
            core.NewWindowRequested -= newWindow; core.PermissionRequested -= permission;
            core.DownloadStarting -= download; core.ProcessFailed -= failed;
        };
    }

    /// <summary>Rejects arbitrary protocols and offers external HTTPS links for explicit in-app navigation.</summary>
    private void Navigation_Starting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        _navigationStarted++;
        if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) || !Allowed(uri))
        {
            args.Cancel = true;
            if (uri is not null && DocumentationNavigationPolicy.IsExternalReference(uri)) OfferExternal(uri);
            else ShowError("This link cannot be opened inside Documentation.");
            return;
        }
        LoadingOverlay.Visibility = Visibility.Visible; LoadingRing.IsActive = true;
        DocumentStatusText.Text = DocumentationNavigationPolicy.IsOffline(uri)
            ? $"AutoHotkey {_location?.Version} · Offline manual" : $"External reference · {uri.Host}";
        ExternalInfoBar.IsOpen = false;
    }

    /// <summary>Applies the same protocol/origin boundary to frames created by document content.</summary>
    private void FrameNavigation_Starting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    { if (args.Uri != "about:blank" && (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) || !Allowed(uri))) args.Cancel = true; }

    /// <summary>Routes requested windows into this panel rather than allowing external popups.</summary>
    private void NewWindow_Requested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri)) return;
        if (DocumentationNavigationPolicy.IsOffline(uri)) sender.Navigate(uri.AbsoluteUri);
        else if (DocumentationNavigationPolicy.IsExternalReference(uri)) OfferExternal(uri);
        else ShowError("This link cannot be opened inside Documentation.");
    }

    /// <summary>Denies browser permissions unrelated to reading documentation.</summary>
    private void Permission_Requested(CoreWebView2 sender, CoreWebView2PermissionRequestedEventArgs args)
        => args.State = CoreWebView2PermissionState.Deny;

    /// <summary>Prevents documentation links from saving or launching executable files.</summary>
    private void Download_Starting(CoreWebView2 sender, CoreWebView2DownloadStartingEventArgs args)
    { args.Cancel = true; ShowError("File downloads are disabled in Documentation. Read or copy the example code inside the manual."); }

    /// <summary>Keeps renderer failures inside the page with an explicit retry path.</summary>
    private void Process_Failed(CoreWebView2 sender, CoreWebView2ProcessFailedEventArgs args)
        => ShowError("The documentation browser stopped. Select Retry to reload it.");

    /// <summary>Styles only bundled offline content and reveals it after theme injection.</summary>
    private async void Navigation_Completed(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        _navigationCompleted++;
        _initializationStage = $"Navigation callback (success={args.IsSuccess})";
        var generation = _generation;
        if (!IsLoaded || _core is null) return;
        if (!args.IsSuccess) { ShowError($"Document navigation failed ({args.WebErrorStatus}). Select Retry."); return; }
        try
        {
            if (Uri.TryCreate(sender.Source, UriKind.Absolute, out var uri) && DocumentationNavigationPolicy.IsOffline(uri)) await ApplyThemeAsync();
            if (generation != _generation || !IsLoaded) return;
            LoadingOverlay.Visibility = Visibility.Collapsed; LoadingRing.IsActive = false;
            _initializationStage = "Ready";
            DocumentInfoBar.IsOpen = false;
            History_Changed(sender, null!);
        }
        catch (Exception ex) { if (generation == _generation && IsLoaded) ShowError(ex.Message); }
    }

    /// <summary>Reflects browser history in native Back/Forward controls.</summary>
    private void History_Changed(CoreWebView2 sender, object args)
    { if (!IsLoaded || _core is null) return; BackButton.IsEnabled = sender.CanGoBack; ForwardButton.IsEnabled = sender.CanGoForward; }

}
