using AutoHotkeyUX.Modern.Models;
using AutoHotkeyUX.Modern.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace AutoHotkeyUX.Modern.Pages;

public sealed partial class WindowSpyPage : Page
{
    private readonly WindowSpyService _service;
    private readonly Window _host;
    private readonly Action _goHome;
    private readonly DispatcherQueueTimer _timer;
    private CancellationTokenSource? _captureCancellation;
    private int _generation;
    private bool _capturing, _paused, _subscribed, _reportedError;
    private bool? _stacked;
    private nint _lastTarget;
    private WindowSpySnapshot? _snapshot;
    internal long CaptureAttempts { get; private set; }
    internal bool SamplerRunning => _timer.IsRunning;

    /// <summary>Creates one page-owned sampler over the shared read-only inspection service.</summary>
    internal WindowSpyPage(WindowSpyService service, Window host, Action goHome)
    {
        InitializeComponent();
        _service = service; _host = host; _goHome = goHome;
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(250);
        _timer.Tick += CaptureTimer_Tick;
        Loaded += Page_Loaded;
        Unloaded += Page_Unloaded;
    }

    /// <summary>Attaches visibility notifications once when the tool returns to view.</summary>
    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed) { _host.AppWindow.Changed += Host_Changed; _subscribed = true; }
        SynchronizeSampler();
    }

    /// <summary>Stops all capture work before the page leaves the content host.</summary>
    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _host.AppWindow.Changed -= Host_Changed; _subscribed = false;
        StopSampler();
    }

    /// <summary>Stops sampling for hidden or minimized workspaces and resumes only when usable.</summary>
    private void Host_Changed(AppWindow sender, AppWindowChangedEventArgs args) => SynchronizeSampler();

    /// <summary>Maintains a single timer and cancellation generation for the page's visible state.</summary>
    private void SynchronizeSampler()
    {
        var visible = IsLoaded && _host.AppWindow.IsVisible
            && _host.AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Minimized };
        if (!visible || _paused) { StopSampler(); CaptureStatusText.Text = _paused ? "Paused" : "Capture stopped"; return; }
        if (_timer.IsRunning) return;
        _captureCancellation = new CancellationTokenSource();
        _timer.Start();
        _ = CaptureAsync();
    }

    /// <summary>Cancels the current generation so late callbacks cannot update this or a newly loaded page.</summary>
    private void StopSampler()
    {
        _timer.Stop(); _generation++;
        _captureCancellation?.Cancel(); _captureCancellation?.Dispose(); _captureCancellation = null;
    }

    /// <summary>Starts a bounded read without overlapping a prior capture.</summary>
    private async void CaptureTimer_Tick(DispatcherQueueTimer sender, object args) => await CaptureAsync();

    /// <summary>Reads native data on a worker and applies it only to the still-active page generation.</summary>
    private async Task CaptureAsync()
    {
        if (_capturing || !_timer.IsRunning || _captureCancellation is null) return;
        if (WindowInspectionNative.ModifiersHeld) { CaptureStatusText.Text = "Frozen · Ctrl / Shift"; return; }
        var generation = _generation;
        var cancellationToken = _captureCancellation.Token;
        var options = new WindowSpyOptions(FollowMouseCheckBox.IsChecked == true, WindowTextExpander.IsExpanded);
        var excluded = WinRT.Interop.WindowNative.GetWindowHandle(_host);
        var previous = _lastTarget;
        _capturing = true; CaptureAttempts++;
        try
        {
            var snapshot = await Task.Run(() => _service.Capture(options, excluded, previous, cancellationToken), cancellationToken);
            if (generation != _generation || !IsLoaded || cancellationToken.IsCancellationRequested) return;
            CaptureStatusText.Text = snapshot is null ? "Move the pointer to an external window." : "Live";
            if (snapshot is not null) { ApplySnapshot(snapshot); _reportedError = false; }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (generation != _generation || !IsLoaded) return;
            CaptureInfoBar.Message = "Window inspection failed. Move to another window and retry.";
            CaptureInfoBar.IsOpen = true;
            if (!_reportedError) { ServiceDiagnostics.Write("WindowSpy", "Native capture failed.", ex); _reportedError = true; }
        }
        finally { _capturing = false; }
    }

    /// <summary>Updates inspection fields without logging external window contents.</summary>
    private void ApplySnapshot(WindowSpySnapshot snapshot)
    {
        _snapshot = snapshot; _lastTarget = snapshot.TargetWindow;
        WindowTitleText.Text = EmptyText(snapshot.Title);
        WindowClassText.Text = $"Class: {snapshot.Class}";
        ProcessText.Text = $"{snapshot.ProcessName} · PID {snapshot.ProcessId} · HWND 0x{snapshot.TargetWindow:X}";
        SelectorsTextBox.Text = snapshot.Selectors; CopyButton.IsEnabled = true;
        MouseScreenText.Text = $"Screen: {snapshot.MouseScreen}";
        MouseWindowText.Text = $"Window: {snapshot.MouseWindow}";
        MouseClientText.Text = $"Client: {snapshot.MouseClient}";
        PixelColorText.Text = $"Color: {snapshot.PixelColor}";
        ControlClassText.Text = $"ClassNN: {EmptyText(snapshot.ControlClassNN)}";
        ControlText.Text = $"Text: {EmptyText(snapshot.ControlText)}";
        ControlBoundsText.Text = $"Bounds: {snapshot.ControlBounds?.ToString() ?? "—"}";
        WindowBoundsText.Text = $"Screen: {snapshot.WindowBounds}";
        ClientBoundsText.Text = $"Client: {snapshot.ClientBounds}";
        StatusBarText.Text = EmptyText(snapshot.StatusText);
        VisibleText.Text = EmptyText(snapshot.VisibleText);
        AllWindowText.Text = EmptyText(snapshot.AllText);
        CaptureInfoBar.Message = snapshot.ReadWarning ?? string.Empty;
        CaptureInfoBar.IsOpen = snapshot.ReadWarning is not null;
    }

    /// <summary>Distinguishes empty text from a read that explicitly failed.</summary>
    private static string EmptyText(string value) => string.IsNullOrWhiteSpace(value) ? "(no text)" : value;

    /// <summary>Toggles user-requested freezing through the same sampler lifecycle.</summary>
    private void Pause_Click(object sender, RoutedEventArgs e)
    { _paused = !_paused; PauseButton.Content = _paused ? "Resume" : "Pause"; SynchronizeSampler(); }

    /// <summary>Copies the actual captured AHK selectors after a user click.</summary>
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (_snapshot is null) return;
        try
        {
            var data = new DataPackage(); data.SetText(_snapshot.Selectors); Clipboard.SetContent(data);
            CaptureStatusText.Text = "Selectors copied";
        }
        catch (Exception ex) { CaptureInfoBar.Message = ex.Message; CaptureInfoBar.IsOpen = true; }
    }

    /// <summary>Returns to the existing Home route.</summary>
    private void Back_Click(object sender, RoutedEventArgs e) => _goHome();

    /// <summary>Stacks inspection cards when the content area cannot fit two readable columns.</summary>
    private void InspectionGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var stacked = e.NewSize.Width < 640;
        if (_stacked == stacked) return;
        _stacked = stacked;
        var cards = new[] { WindowCard, MouseCard, ControlCard, PositionCard };
        for (var index = 0; index < cards.Length; index++)
        {
            Grid.SetRow(cards[index], stacked ? index : index / 2);
            Grid.SetColumn(cards[index], stacked ? 0 : index % 2);
            Grid.SetColumnSpan(cards[index], stacked ? 2 : 1);
        }
    }
}
