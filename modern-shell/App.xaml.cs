using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
using System.Text;
using AutoHotkeyUX.Modern.Services;
using Microsoft.UI.Dispatching;
using System.Text.Json;

namespace AutoHotkeyUX.Modern;

public partial class App : Application
{
    private MainWindow? _window;
    private ApplicationServices? _services;
    private TrayIconService? _tray;
    private ApplicationIconService? _icon;
    private ShortcutCommandService? _shortcuts;
    private readonly string[] _arguments;
    private readonly SingleInstanceService _singleInstance;
    private readonly DispatcherQueue _dispatcher;
    private bool _quitting;
    private bool _closeAllowed;
    private static bool _silent;

    public static Window? MainWindowInstance { get; private set; }

    /// <summary>
    /// Initializes the WinUI 3 application.
    /// </summary>
    internal App(string[] arguments, SingleInstanceService singleInstance)
    {
        _arguments = arguments;
        _singleInstance = singleInstance;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _silent = arguments.Contains("--background") || arguments.Contains("--verify-ui") || arguments.Contains("--verify-tools") || arguments.Contains("--verify-preferences") || arguments.Contains("--verify-visual-creation");
        try
        {
            InitializeComponent();
            UnhandledException += App_UnhandledException;
        }
        catch (Exception ex)
        {
            ReportStartupFailure("App XAML initialization failed", ex);
            throw;
        }
    }

    /// <summary>
    /// Creates the embedded icons and single main application window with startup diagnostics.
    /// </summary>
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            var verifyPreferencesIndex = Array.IndexOf(_arguments, "--verify-preferences");
            var verifyVisualIndex = Array.IndexOf(_arguments, "--verify-visual-creation");
            var isolatedIndex = Math.Max(verifyPreferencesIndex, verifyVisualIndex);
            var diagnosticRoot = isolatedIndex < 0 ? null : Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(_arguments[isolatedIndex + 1]))!, "native-workspace-" + Guid.NewGuid().ToString("N"));
            _services = new ApplicationServices(diagnosticRoot);
            await Task.Run(_services.Initialize);
            try { _icon = new ApplicationIconService(); }
            catch (Exception iconError) { ServiceDiagnostics.Write("Icon", "Using the default application icon after loading failed.", iconError); }
            _window = new MainWindow(_services, _icon);
            MainWindowInstance = _window;
            _window.AppWindow.Closing += (_, closing) =>
            {
                if (!_closeAllowed)
                {
                    closing.Cancel = true;
                    if (!_quitting)
                    {
                        _window.AppWindow.Hide();
                        WriteStartupStatus();
                    }
                }
            };
            try
            {
                _tray = new TrayIconService(WinRT.Interop.WindowNative.GetWindowHandle(_window), ShowWindow, ExitManager, _icon?.TrayIcon ?? IntPtr.Zero);
            }
            catch (Exception trayError)
            {
                ServiceDiagnostics.Write("Tray", "Tray initialization failed; showing the workspace so it remains accessible.", trayError);
                ShowWindow();
            }
            _shortcuts = new ShortcutCommandService(WinRT.Interop.WindowNative.GetWindowHandle(_window),
                _services.Settings, (message, error) => _dispatcher.TryEnqueue(() => _tray?.ShowNotification(message, error)),
                preferences: _services.ShortcutPreferences, activity: _services.ShortcutActivity,
                workflowResultRoot: Path.Combine(_services.StateDirectory, "workflow-results"));
            if (_arguments.Contains("--enable-core")) await EnableCoreAsync();
            if (!_silent) ShowWindow();
            WriteStartupStatus();
            _singleInstance.SetHandler(command => _dispatcher.TryEnqueue(async () =>
            {
                try
                {
                    if (command == "exit") ExitManager();
                    else if (command == "enable-core") await EnableCoreAsync();
                    else ShowWindow();
                }
                catch (Exception ex) { ReportStartupFailure("Activation failed", ex); }
            }));
            var verifyIndex = Array.IndexOf(_arguments, "--verify-ui");
            var verifyToolsIndex = Array.IndexOf(_arguments, "--verify-tools");
            if (verifyToolsIndex >= 0) verifyIndex = verifyToolsIndex;
            if (verifyPreferencesIndex >= 0) verifyIndex = verifyPreferencesIndex;
            if (verifyVisualIndex >= 0) verifyIndex = verifyVisualIndex;
            if (verifyIndex >= 0 && verifyIndex + 1 < _arguments.Length)
            {
                ShowWindow();
                var report = verifyVisualIndex >= 0 ? (_arguments.Contains("--settings-layout-only")
                    ? await _window.VerifySettingsLayoutAsync(_arguments[verifyIndex + 1]) : _arguments.Contains("--placement-only")
                    ? await _window.VerifyWindowPlacementAsync(ShowWindow) : _arguments.Contains("--configurable-only")
                    ? await _window.VerifyConfigurableActionsAsync(_arguments[verifyIndex + 1], _arguments.Contains("--with-runtime")) : _arguments.Contains("--layout-only")
                    ? await _window.VerifyWorkflowLayoutOnlyAsync(_arguments[verifyIndex + 1])
                    : await _window.VerifyVisualCreationAsync(_arguments[verifyIndex + 1]))
                    : verifyPreferencesIndex >= 0 ? await _window.VerifyShortcutPreferencesAsync()
                    : _arguments.Contains("--verify-docs-only") ? await _window.VerifyDocumentationAsync()
                    : verifyToolsIndex >= 0 ? await _window.VerifyToolsAsync() : await _window.VerifyPagesAsync();
                File.WriteAllText(_arguments[verifyIndex + 1], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
                ExitManager();
            }
        }
        catch (Exception ex)
        {
            Program.ExitCode = 1;
            ReportStartupFailure(_arguments.Contains("--verify-tools") ? "Tool verification failed"
                : _arguments.Contains("--verify-ui") ? "UI verification failed" : "MainWindow startup failed", ex);
            _quitting = true;
            if (_shortcuts is not null) await _shortcuts.DrainAsync();
            if (_services is not null) await Task.WhenAll(_services.Compiler.CancelAndDrainAsync(), _services.Documentation.CancelAndDrainAsync(), _services.ShortcutPreferences.DrainAsync(), _services.VisualFlows.DrainAsync());
            _closeAllowed = true;
            _tray?.Dispose();
            _services?.Dispose();
            _window?.Close();
            _icon?.Dispose();
            Exit();
        }
    }

    /// <summary>Restores the existing workspace when its tray icon or a second launch requests it.</summary>
    private void ShowWindow()
    {
        if (_quitting || _window is null) return;
        if (!_arguments.Contains("--verify-ui") && !_arguments.Contains("--verify-tools") && !_arguments.Contains("--verify-preferences") && !_arguments.Contains("--verify-visual-creation")) _silent = false;
        _window.AppWindow.Show();
        if (_window.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter
            && presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Minimized)
            presenter.Restore();
        _window.EnsureWindowVisible();
        _window.Activate();
        WriteStartupStatus();
    }

    /// <summary>Supports the explicitly requested initial setup through the same services as the Settings toggles.</summary>
    private async Task EnableCoreAsync()
    {
        if (_services is null) return;
        await Task.Run(() =>
        {
            var previous = _services.WindowsStartup.IsEnabled;
            var previousExplorer = _services.ScriptStartup.ExplorerEnabled;
            try
            {
                _services.ScriptStartup.SetExplorerEnabled(true);
                _services.WindowsStartup.SetEnabled(true);
            }
            catch
            {
                _services.WindowsStartup.SetEnabled(previous);
                _services.ScriptStartup.SetExplorerEnabled(previousExplorer);
                throw;
            }
        });
        WriteStartupStatus();
    }

    /// <summary>Drains accepted extractions and owned tools before exit, retaining user scripts and releasing UI last.</summary>
    private async void ExitManager()
    {
        if (_quitting) return;
        _quitting = true;
        if (_shortcuts is not null) await _shortcuts.DrainAsync();
        if (_services is not null) await Task.WhenAll(_services.Compiler.CancelAndDrainAsync(), _services.Documentation.CancelAndDrainAsync(), _services.ShortcutPreferences.DrainAsync(), _services.VisualFlows.DrainAsync());
        _closeAllowed = true;
        _tray?.Dispose();
        _services?.Dispose();
        _window?.Close();
        _icon?.Dispose();
        Exit();
    }

    /// <summary>Records actual window visibility, icon initialization and interpreter states for troubleshooting.</summary>
    private void WriteStartupStatus()
    {
        try
        {
            var stateDirectory = _services?.StateDirectory ?? ServiceDiagnostics.StateDirectory;
            Directory.CreateDirectory(stateDirectory);
            File.WriteAllText(Path.Combine(stateDirectory, "manager-startup.json"), JsonSerializer.Serialize(new
            {
                ProcessId = Environment.ProcessId,
                ExecutablePath = Environment.ProcessPath,
                WindowShown = _window?.AppWindow.IsVisible ?? false,
                CustomWindowIconApplied = _window?.HasCustomIcon ?? false,
                CustomTrayIconRegistered = _tray?.UsesCustomIcon ?? false,
                StartWithWindows = _services?.WindowsStartup.IsEnabled ?? false,
                ExplorerShortcuts = _services?.ScriptStartup.ExplorerEnabled ?? false,
                Sessions = _services?.Execution.Snapshot(),
                StartupWarning = _services?.ScriptStartup.LastWarning
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { ServiceDiagnostics.Write("App", "Could not write startup status.", ex); }
    }

    /// <summary>
    /// Records otherwise silent WinUI exceptions and shows a native fallback message.
    /// </summary>
    private void App_UnhandledException(
        object sender,
        Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        ReportStartupFailure("Unhandled WinUI exception", e.Exception);
        e.Handled = true;
    }

    /// <summary>
    /// Persists full exception details to LocalAppData and shows the log path without relying on XAML.
    /// </summary>
    internal static void ReportStartupFailure(string stage, Exception exception)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AutoHotkeyUX.Modern");

        Directory.CreateDirectory(directory);

        var logPath = Path.Combine(directory, "startup-error.log");
        var text = new StringBuilder()
            .AppendLine($"Time: {DateTimeOffset.Now:O}")
            .AppendLine($"Stage: {stage}")
            .AppendLine($"OS: {Environment.OSVersion}")
            .AppendLine($".NET: {Environment.Version}")
            .AppendLine()
            .AppendLine(exception.ToString())
            .ToString();

        File.WriteAllText(logPath, text, Encoding.UTF8);

        ServiceDiagnostics.Write("App", stage, exception);
        if (!_silent) MessageBox(
            IntPtr.Zero,
            $"{stage}\n\n{exception.Message}\n\nDetails:\n{logPath}",
            "AutoHotkey Modern UX",
            0x00000010);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBox(
        IntPtr hWnd,
        string lpText,
        string lpCaption,
        uint uType);
}
