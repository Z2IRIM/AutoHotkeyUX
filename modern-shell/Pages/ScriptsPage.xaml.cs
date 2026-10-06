using AutoHotkeyUX.Modern.Models;
using AutoHotkeyUX.Modern.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AutoHotkeyUX.Modern.Pages;

public sealed partial class ScriptsPage : Page
{
    private readonly ScriptCatalogService _catalog;
    private readonly ScriptExecutionService _execution;
    private readonly ScriptStartupService _startup;
    private readonly AutoHotkeyIntegration _integration;
    private readonly AutoHotkeySettings _settings;
    private bool _subscribed;
    private bool _busy;

    /// <summary>Renders application-owned services without scanning files or owning interpreter handles.</summary>
    internal ScriptsPage(ScriptCatalogService catalog, ScriptExecutionService execution,
        ScriptStartupService startup, AutoHotkeyIntegration integration, AutoHotkeySettings settings)
    {
        InitializeComponent();
        _catalog = catalog;
        _execution = execution;
        _startup = startup;
        _integration = integration;
        _settings = settings;
        ScriptRootText.Text = catalog.RootDirectory;
        ToolTipService.SetToolTip(ScriptRootText, catalog.RootDirectory);
        Loaded += Page_Loaded;
        Unloaded += Page_Unloaded;
    }

    /// <summary>Subscribes once per visible lifetime and applies an up-to-date shared snapshot.</summary>
    private void Page_Loaded(object sender, RoutedEventArgs args)
    {
        if (!_subscribed)
        {
            _catalog.Changed += Service_Changed;
            _execution.Changed += Service_Changed;
            _startup.Changed += Service_Changed;
            _subscribed = true;
        }
        RefreshRows();
    }

    /// <summary>Releases subscriptions on navigation so background callbacks cannot update an unloaded page.</summary>
    private void Page_Unloaded(object sender, RoutedEventArgs args)
    {
        _subscribed = false;
        _catalog.Changed -= Service_Changed;
        _execution.Changed -= Service_Changed;
        _startup.Changed -= Service_Changed;
    }

    /// <summary>Marshals filesystem and interpreter notifications onto the page dispatcher.</summary>
    private void Service_Changed(object? sender, EventArgs args)
        => DispatcherQueue.TryEnqueue(() => { if (_subscribed) RefreshRows(); });

    /// <summary>Searches the shared catalog and keeps deleted-but-running scripts available for Stop.</summary>
    private void RefreshRows()
    {
        var sessions = _execution.Snapshot().ToDictionary(session => session.ScriptPath, StringComparer.OrdinalIgnoreCase);
        var entries = _catalog.Snapshot().ToDictionary(entry => entry.Path, StringComparer.OrdinalIgnoreCase);
        foreach (var session in sessions.Values.Where(session => session.State is ScriptState.Running or ScriptState.Stopping))
            entries.TryAdd(session.ScriptPath, new ScriptEntry(Path.GetFileName(session.ScriptPath), session.ScriptPath, session.StartedUtc, 0));
        var query = SearchBox.Text.Trim();
        var rows = entries.Values.Where(entry => entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .Select(entry => new ScriptRow(entry, sessions.GetValueOrDefault(entry.Path), _startup, _busy)).ToArray();
        ScriptsList.ItemsSource = rows;
        EmptyState.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyTitle.Text = query.Length == 0 ? "No scripts yet" : "No matching scripts";
        EmptyDescription.Text = query.Length == 0 ? "Create a script in Documents\\AutoHotkey to get started." : "Try another script name.";
        var warning = _catalog.LastError ?? _execution.LastWarning ?? _startup.LastWarning;
        if (warning is not null) ShowStatus(warning, InfoBarSeverity.Warning);
    }

    /// <summary>Updates the visible name filter without touching filesystem or process lifetimes.</summary>
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs args)
    { if (_subscribed) RefreshRows(); }

    /// <summary>Checks the live search/empty-state behavior using real catalog entries during explicit UI diagnostics.</summary>
    internal async Task<bool> VerifySearchAsync()
    {
        var original = SearchBox.Text;
        SearchBox.Text = string.Empty;
        await Task.Delay(80);
        var count = (ScriptsList.ItemsSource as ScriptRow[])?.Length ?? 0;
        SearchBox.Text = Guid.NewGuid().ToString("N");
        await Task.Delay(80);
        var empty = (ScriptsList.ItemsSource as ScriptRow[])?.Length == 0 && EmptyState.Visibility == Visibility.Visible;
        SearchBox.Text = string.Empty;
        await Task.Delay(80);
        var restored = (ScriptsList.ItemsSource as ScriptRow[])?.Length == count;
        SearchBox.Text = original;
        await Task.Delay(80);
        return empty && restored;
    }

    /// <summary>Checks rendered row actions against their actual available width, including high-DPI minimum-size windows.</summary>
    internal bool VerifyActionLayout()
    {
        var pending = new Stack<DependencyObject>();
        pending.Push(ScriptsList);
        while (pending.Count > 0)
        {
            var element = pending.Pop();
            if (element is Grid { Name: "ScriptActionsGrid" } grid)
            {
                foreach (var child in grid.Children.OfType<FrameworkElement>())
                {
                    var position = child.TransformToVisual(grid).TransformPoint(default);
                    if (position.X + child.ActualWidth > grid.ActualWidth + 1) return false;
                }
            }
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
                pending.Push(VisualTreeHelper.GetChild(element, index));
        }
        return true;
    }

    /// <summary>Refreshes directory metadata off the UI thread.</summary>
    private async void RefreshButton_Click(object sender, RoutedEventArgs args)
        => await RunActionAsync(() => { _catalog.Refresh(); return null; });

    /// <summary>Opens the real source-of-truth workspace.</summary>
    private async void OpenFolderButton_Click(object sender, RoutedEventArgs args)
        => await RunActionAsync(() => { _integration.OpenScriptsFolder(_catalog.RootDirectory); return null; });

    /// <summary>Starts the selected script via the embedded interpreter.</summary>
    private async void RunButton_Click(object sender, RoutedEventArgs args)
    { if (sender is Button { Tag: string path }) await RunActionAsync(() => _execution.Run(path)); }

    /// <summary>Stops only the tracked interpreter selected by the row.</summary>
    private async void StopButton_Click(object sender, RoutedEventArgs args)
    { if (sender is Button { Tag: string path }) await RunActionAsync(() => { _execution.Stop(path); return null; }); }

    /// <summary>Executes the manager's atomic stop-then-run operation.</summary>
    private async void RestartButton_Click(object sender, RoutedEventArgs args)
    { if (sender is Button { Tag: string path }) await RunActionAsync(() => _execution.Restart(path)); }

    /// <summary>Reuses the user's existing editor command or the non-mutating Notepad fallback.</summary>
    private async void EditButton_Click(object sender, RoutedEventArgs args)
    { if (sender is Button { Tag: string path }) await RunActionAsync(() => { _integration.EditScript(path, _settings); return null; }); }

    /// <summary>Reveals the script with the shared Explorer select action.</summary>
    private async void RevealButton_Click(object sender, RoutedEventArgs args)
    { if (sender is Button { Tag: string path }) await RunActionAsync(() => { _integration.RevealFile(path); return null; }); }

    /// <summary>Saves a per-script login preference without starting it as a side effect.</summary>
    private void StartupCheckBox_Click(object sender, RoutedEventArgs args)
    {
        if (sender is not CheckBox { Tag: string path } checkBox) return;
        try { _startup.SetRunAtSignIn(path, checkBox.IsChecked == true); ShowStatus("Startup choice saved.", InfoBarSeverity.Success); }
        catch (Exception ex) { ShowStatus(ex.Message, InfoBarSeverity.Error); RefreshRows(); }
    }

    /// <summary>Disables repeated actions and reports boundary failures while keeping interpreter waits off the UI thread.</summary>
    private async Task RunActionAsync(Func<RunningScriptSession?> action)
    {
        if (_busy) return;
        _busy = true;
        RefreshRows();
        try
        {
            var result = await Task.Run(action);
            if (result?.State == ScriptState.Failed) ShowStatus(result.Error ?? "The script failed.", InfoBarSeverity.Error);
            else ActionInfoBar.IsOpen = false;
        }
        catch (Exception ex) { ShowStatus(ex.Message, InfoBarSeverity.Error); }
        finally { _busy = false; if (_subscribed) RefreshRows(); }
    }

    /// <summary>Displays non-modal action feedback in the existing WinUI visual system.</summary>
    private void ShowStatus(string message, InfoBarSeverity severity)
    { ActionInfoBar.Message = message; ActionInfoBar.Severity = severity; ActionInfoBar.IsOpen = true; }
}

/// <summary>Provides a disposable presentation snapshot; service state remains independent of XAML.</summary>
internal sealed class ScriptRow
{
    public string Name { get; }
    public string Path { get; }
    public string Details { get; }
    public string StateText { get; }
    public string? Error { get; }
    public Visibility ErrorVisibility => string.IsNullOrEmpty(Error) ? Visibility.Collapsed : Visibility.Visible;
    public Brush StateBackground { get; }
    public Brush StateForeground { get; }
    public bool FileExists { get; }
    public bool CanRun { get; }
    public bool CanStop { get; }
    public bool CanRestart { get; }
    public bool RunAtSignIn { get; }
    public bool CanChangeStartup { get; }
    public string StartupHint { get; }

    /// <summary>Derives button states from confirmed ownership and the script's current availability.</summary>
    internal ScriptRow(ScriptEntry entry, RunningScriptSession? session, ScriptStartupService startup, bool busy)
    {
        Name = entry.Name;
        Path = entry.Path;
        FileExists = File.Exists(entry.Path);
        Details = FileExists ? $"Modified {entry.ModifiedText} · {entry.Size:N0} bytes" : "File removed; the managed interpreter is still running.";
        var state = session?.State ?? ScriptState.Stopped;
        StateText = state.ToString();
        Error = session?.Error;
        CanRun = !busy && FileExists && state is ScriptState.Stopped or ScriptState.Failed;
        CanStop = !busy && state == ScriptState.Running;
        CanRestart = CanStop && FileExists;
        RunAtSignIn = startup.IsRunAtSignIn(entry.Path);
        CanChangeStartup = FileExists && !startup.IsExplorerScript(entry.Path);
        StartupHint = startup.IsExplorerScript(entry.Path) ? "Controlled by Explorer shortcuts in Settings." : "Starts when the manager starts at Windows sign-in.";
        StateBackground = (Brush)Application.Current.Resources[state == ScriptState.Running ? "SystemFillColorSuccessBackgroundBrush" : "SubtleFillColorSecondaryBrush"];
        StateForeground = (Brush)Application.Current.Resources[state == ScriptState.Running ? "SystemFillColorSuccessBrush" : "TextFillColorSecondaryBrush"];
    }
}
