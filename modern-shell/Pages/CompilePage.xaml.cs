using AutoHotkeyUX.Modern.Models;
using AutoHotkeyUX.Modern.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace AutoHotkeyUX.Modern.Pages;

public sealed partial class CompilePage : Page
{
    private readonly CompilerService _compiler;
    private readonly AutoHotkeyIntegration _integration;
    private readonly Action _goHome;
    private string? _pendingReplacePath;
    private int _refreshQueued;
    private bool _subscribed;

    /// <summary>Creates the compiler form while its shared service retains jobs across navigation.</summary>
    internal CompilePage(CompilerService compiler, AutoHotkeyIntegration integration, Action goHome)
    {
        InitializeComponent(); _compiler = compiler; _integration = integration; _goHome = goHome;
        Loaded += Page_Loaded; Unloaded += Page_Unloaded;
    }

    /// <summary>Restores the latest service state and subscribes once when the page becomes visible.</summary>
    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed) { _compiler.Changed += Compiler_Changed; _subscribed = true; }
        RefreshState();
    }

    /// <summary>Detaches UI notifications without cancelling an accepted build.</summary>
    private void Page_Unloaded(object sender, RoutedEventArgs e)
    { _compiler.Changed -= Compiler_Changed; _subscribed = false; }

    /// <summary>Coalesces worker output into at most one pending UI refresh.</summary>
    private void Compiler_Changed(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _refreshQueued, 1) != 0) return;
        if (!DispatcherQueue.TryEnqueue(() => { Interlocked.Exchange(ref _refreshQueued, 0); if (IsLoaded) RefreshState(); }))
            Interlocked.Exchange(ref _refreshQueued, 0);
    }

    /// <summary>Updates controls and bounded logs from the authoritative shared job state.</summary>
    private void RefreshState()
    {
        var state = _compiler.State;
        foreach (var control in new Control[] { SourceTextBox, OutputTextBox, IconTextBox, ArchitectureComboBox,
                     SourceBrowseButton, OutputBrowseButton, IconBrowseButton, CompileButton }) control.IsEnabled = !state.IsRunning;
        CancelButton.IsEnabled = state.IsRunning;
        BuildProgressRing.IsActive = state.IsRunning;
        BuildProgressRing.Visibility = state.IsRunning ? Visibility.Visible : Visibility.Collapsed;
        BuildStatusText.Text = $"AutoHotkey {_integration.FindRuntime()?.Version ?? "unavailable"} · {state.Status}";
        BuildLogTextBox.Text = state.Log;
        OpenOutputButton.Visibility = state.Succeeded ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Suggests a free output name when the user chooses a new source.</summary>
    private void Source_Changed(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _pendingReplacePath = null; ReplaceInfoBar.IsOpen = false;
        try
        {
            var source = SourceTextBox.Text.Trim();
            if (!source.EndsWith(".ahk", StringComparison.OrdinalIgnoreCase)) return;
            var output = Path.ChangeExtension(Path.GetFullPath(source), ".exe");
            var candidate = output;
            for (var index = 1; File.Exists(candidate) || Directory.Exists(candidate); index++)
                candidate = Path.Combine(Path.GetDirectoryName(output)!, $"{Path.GetFileNameWithoutExtension(output)}-{index}.exe");
            OutputTextBox.Text = candidate;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { }
    }

    /// <summary>Invalidates overwrite approval whenever the current target changes.</summary>
    private void Output_Changed(object sender, TextChangedEventArgs e)
    { _pendingReplacePath = null; if (ReplaceInfoBar is not null) ReplaceInfoBar.IsOpen = false; }

    /// <summary>Uses the native file picker for source or optional icon without opening a tool window.</summary>
    private async void BrowseInput_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string kind }) return;
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.FileTypeFilter.Add(kind == "source" ? ".ahk" : ".ico");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance!));
            var file = await picker.PickSingleFileAsync();
            if (!IsLoaded || file is null) return;
            if (kind == "source") SourceTextBox.Text = file.Path; else IconTextBox.Text = file.Path;
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    /// <summary>Chooses an output directory without letting a save picker create or truncate the target file.</summary>
    private async void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance!));
            var folder = await picker.PickSingleFolderAsync();
            if (!IsLoaded || folder is null) return;
            var name = Path.GetFileName(OutputTextBox.Text);
            if (string.IsNullOrWhiteSpace(name)) name = "CompiledScript.exe";
            OutputTextBox.Text = Path.Combine(folder.Path, name);
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    /// <summary>Starts a new build or asks for explicit in-page replacement of this exact target.</summary>
    private async void Compile_Click(object sender, RoutedEventArgs e) => await StartBuildAsync(false);

    /// <summary>Consumes the current target's one-use replacement approval.</summary>
    private async void Replace_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_pendingReplacePath is null || !string.Equals(_pendingReplacePath, Path.GetFullPath(OutputTextBox.Text.Trim()), StringComparison.OrdinalIgnoreCase)) return;
            _pendingReplacePath = null; ReplaceInfoBar.IsOpen = false;
            await StartBuildAsync(true);
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    /// <summary>Submits immutable form values and leaves all execution and cancellation ownership in the service.</summary>
    private async Task StartBuildAsync(bool replace)
    {
        if (_compiler.State.IsRunning) return;
        try
        {
            var output = Path.GetFullPath(OutputTextBox.Text.Trim());
            if (File.Exists(output) && !replace)
            {
                _pendingReplacePath = output;
                ReplaceInfoBar.Message = $"{output}\nChoose another filename or replace this file after a successful build.";
                ReplaceInfoBar.IsOpen = true;
                return;
            }
            BuildInfoBar.IsOpen = false;
            var request = new CompilerRequest(SourceTextBox.Text, output, IconTextBox.Text,
                ArchitectureComboBox.SelectedIndex == 1 ? CompilerArchitecture.Bit32 : CompilerArchitecture.Bit64, replace);
            var job = _compiler.CompileAsync(request, null, CancellationToken.None);
            RefreshState();
            var result = await job;
            if (!IsLoaded) return;
            RefreshState();
            if (!result.Succeeded && !result.Cancelled) ShowError("Build failed. See the output below for details.");
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    /// <summary>Cancels only the job accepted by the shared compiler service.</summary>
    private void Cancel_Click(object sender, RoutedEventArgs e) => _compiler.CancelCurrent();

    /// <summary>Reveals the completed output through the existing integration action.</summary>
    private void OpenOutput_Click(object sender, RoutedEventArgs e)
    {
        try { if (_compiler.State.OutputPath is { } path) _integration.RevealFile(path); }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    /// <summary>Shows errors only while this page remains the active view.</summary>
    private void ShowError(string message)
    { if (!IsLoaded) return; BuildInfoBar.Message = message; BuildInfoBar.IsOpen = true; }

    /// <summary>Returns to Home without terminating the user's build.</summary>
    private void Back_Click(object sender, RoutedEventArgs e) => _goHome();
}
