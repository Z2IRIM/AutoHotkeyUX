using System.Windows;
using System.Windows.Threading;

namespace AutoHotkeyUX.Modern;

public partial class App : Application
{
    /// <summary>
    /// Installs a final UI exception guard so unexpected failures are visible instead of silently closing the shell.
    /// </summary>
    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    /// <summary>
    /// Reports an unhandled UI exception and keeps the application from crashing without context.
    /// </summary>
    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            e.Exception.Message,
            "AutoHotkey",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }
}
