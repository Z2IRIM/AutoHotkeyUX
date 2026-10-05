using Microsoft.UI.Xaml;

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
        InitializeComponent();
    }

    /// <summary>
    /// Creates and activates the single main application window.
    /// </summary>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        MainWindowInstance = _window;
        _window.Activate();
    }
}
