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
    private readonly string[] _arguments;
    private readonly SingleInstanceService _singleInstance;
    private readonly DispatcherQueue _dispatcher;
    private bool _quitting;
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
        _silent = arguments.Contains("--background") || arguments.Contains("--verify-ui");
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
    /// Creates and activates the single main application window with startup diagnostics.
    /// </summary>
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _services = new ApplicationServices();
            await Task.Run(_services.Initialize);
            _window = new MainWindow(_services);
            MainWindowInstance = _window;
            _window.AppWindow.Closing += (_, closing) =>
            {
                if (!_quitting)
                {
                    closing.Cancel = true;
                    _window.AppWindow.Hide();
                    WriteStartupStatus();
                }
            };
            try
            {
                _tray = new TrayIconService(WinRT.Interop.WindowNative.GetWindowHandle(_window), ShowWindow, ExitManager);
            }
            catch (Exception trayError)
            {
                ServiceDiagnostics.Write("Tray", "Tray initialization failed; showing the workspace so it remains accessible.", trayError);
                ShowWindow();
            }
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
            if (verifyIndex >= 0 && verifyIndex + 1 < _arguments.Length)
            {
                ShowWindow();
                var report = await _window.VerifyPagesAsync();
                File.WriteAllText(_arguments[verifyIndex + 1], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
                ExitManager();
            }
        }
        catch (Exception ex)
        {
            Program.ExitCode = 1;
            ReportStartupFailure(_arguments.Contains("--verify-ui") ? "UI verification failed" : "MainWindow startup failed", ex);
            _tray?.Dispose();
            _services?.Dispose();
            Exit();
        }
    }

    /// <summary>Restores the existing workspace when its tray icon or a second launch requests it.</summary>
    private void ShowWindow()
    {
        if (_quitting || _window is null) return;
        if (!_arguments.Contains("--verify-ui")) _silent = false;
        _window.AppWindow.Show();
        if (_window.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter
            && presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Minimized)
            presenter.Restore();
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

    /// <summary>Exits the manager intentionally while persisting and retaining running scripts.</summary>
    private void ExitManager()
    {
        if (_quitting) return;
        _quitting = true;
        _tray?.Dispose();
        _services?.Dispose();
        _window?.Close();
        Exit();
    }

    /// <summary>Records actual window visibility and interpreter states for silent-start troubleshooting.</summary>
    private void WriteStartupStatus()
    {
        try
        {
            Directory.CreateDirectory(ServiceDiagnostics.StateDirectory);
            File.WriteAllText(Path.Combine(ServiceDiagnostics.StateDirectory, "manager-startup.json"), JsonSerializer.Serialize(new
            {
                ProcessId = Environment.ProcessId,
                ExecutablePath = Environment.ProcessPath,
                WindowShown = _window?.AppWindow.IsVisible ?? false,
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
