using Microsoft.UI.Xaml;
using System.Runtime.InteropServices;
using System.Text;

namespace AutoHotkeyUX.Modern;

public partial class App : Application
{
    private Window? _window;

    public static Window? MainWindowInstance { get; private set; }

    /// <summary>
    /// Initializes the WinUI 3 application.
    /// </summary>
    public App()
    {
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
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _window = new MainWindow();
            MainWindowInstance = _window;
            _window.Activate();
        }
        catch (Exception ex)
        {
            ReportStartupFailure("MainWindow startup failed", ex);
        }
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

        MessageBox(
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
