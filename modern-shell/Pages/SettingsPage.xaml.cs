using AutoHotkeyUX.Modern.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AutoHotkeyUX.Modern.Pages;

public sealed partial class SettingsPage : Page
{
    private readonly AutoHotkeyIntegration _integration;
    private readonly AutoHotkeySettings _settings;
    private bool _loadedSettings;
    private readonly WindowsStartupService _windowsStartup;
    private readonly ScriptStartupService _scriptStartup;
    private readonly ScriptExecutionService _execution;
    private bool _subscribed;
    private bool _changingBackground;
    private readonly Action _goShortcuts;

    /// <summary>
    /// Creates the Settings page over the existing AutoHotkey registry contract.
    /// </summary>
    internal SettingsPage(
        AutoHotkeyIntegration integration,
        AutoHotkeySettings settings,
        WindowsStartupService windowsStartup,
        ScriptStartupService scriptStartup,
        ScriptExecutionService execution,
        Action goShortcuts)
    {
        InitializeComponent();
        _integration = integration;
        _settings = settings;
        _windowsStartup = windowsStartup;
        _scriptStartup = scriptStartup;
        _execution = execution;
        _goShortcuts = goShortcuts;
        Loaded += SettingsPage_Loaded;
        Unloaded += SettingsPage_Unloaded;
    }

    /// <summary>
    /// Reloads settings and runtime status from Windows.
    /// </summary>
    internal void Refresh()
    {
        _loadedSettings = false;

        var runtime = _integration.FindRuntime();
        RuntimePathText.Text = runtime is null
            ? "No AutoHotkey v2 runtime detected."
            : $"{runtime.Path} · {runtime.Architecture} · {runtime.DiscoverySource}";

        UseLauncherToggle.IsOn = _integration.IsLauncherEnabled();

        var build = _settings.Read(@"Launcher\v2", "Build", "");
        InterpreterComboBox.SelectedIndex = build switch
        {
            "64-bit" => 1,
            "32-bit" => 2,
            _ => 0
        };

        EditorCommandTextBox.Text = _settings.ReadEditorCommand();
        CheckUpdatesToggle.IsOn = _settings.ReadBoolean("Dash", "CheckForUpdates", false);
        StartWithWindowsToggle.IsOn = _windowsStartup.IsEnabled;
        ExplorerShortcutsToggle.IsOn = _scriptStartup.ExplorerEnabled;
        UpdateBackgroundStatus();
        if (_scriptStartup.LastWarning is not null) ShowStatus(_scriptStartup.LastWarning, InfoBarSeverity.Warning);

        _loadedSettings = true;
    }

    /// <summary>Subscribes to live shortcut state only while the settings page is visible.</summary>
    private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed) { _execution.Changed += Execution_Changed; _subscribed = true; }
        Refresh();
    }

    /// <summary>Prevents duplicate notifications when navigating away and back.</summary>
    private void SettingsPage_Unloaded(object sender, RoutedEventArgs e)
    { _subscribed = false; _execution.Changed -= Execution_Changed; }

    /// <summary>Dispatches script exit events to settings without accessing XAML from a process callback.</summary>
    private void Execution_Changed(object? sender, EventArgs e)
        => DispatcherQueue.TryEnqueue(() => { if (_subscribed) UpdateBackgroundStatus(); });

    /// <summary>Shows the actual shortcut process state independently of the saved enable preference.</summary>
    private void UpdateBackgroundStatus()
    {
        var session = _execution.Snapshot().FirstOrDefault(session => _scriptStartup.IsExplorerScript(session.ScriptPath));
        ExplorerStatusText.Text = session is null ? "Shortcuts are stopped." : $"Shortcuts: {session.State}{(session.Error is null ? "" : $" · {session.Error}")}";
    }

    /// <summary>Writes or removes only the current user's silent login startup entry.</summary>
    private void StartWithWindowsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loadedSettings || _changingBackground) return;
        try
        {
            _windowsStartup.SetEnabled(StartWithWindowsToggle.IsOn);
            ShowStatus(StartWithWindowsToggle.IsOn ? "The workspace will start silently at Windows sign-in." : "Windows sign-in startup disabled.", InfoBarSeverity.Success);
        }
        catch (Exception ex) { ShowStatus(ex.Message, InfoBarSeverity.Error); Refresh(); }
    }

    /// <summary>Installs/starts or stops the editable built-in script through the same process manager as user scripts.</summary>
    private async void ExplorerShortcutsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loadedSettings || _changingBackground) return;
        _changingBackground = true;
        ExplorerShortcutsToggle.IsEnabled = false;
        StartWithWindowsToggle.IsEnabled = false;
        try
        {
            var enabled = ExplorerShortcutsToggle.IsOn;
            await Task.Run(() => _scriptStartup.SetExplorerEnabled(enabled));
            ShowStatus(enabled ? "Explorer shortcuts enabled with your saved action preferences." : "Explorer shortcuts stopped.", InfoBarSeverity.Success);
        }
        catch (Exception ex) { ShowStatus(ex.Message, InfoBarSeverity.Error); }
        finally
        {
            _changingBackground = false;
            ExplorerShortcutsToggle.IsEnabled = true;
            StartWithWindowsToggle.IsEnabled = true;
            Refresh();
        }
    }

    /// <summary>Opens the configuration subpage in the existing Settings content host.</summary>
    private void ConfigureShortcuts_Click(object sender, RoutedEventArgs e) => _goShortcuts();

    /// <summary>
    /// Applies launcher mode using the currently selected runtime preference.
    /// </summary>
    private void UseLauncherToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loadedSettings)
        {
            return;
        }

        try
        {
            _integration.SetLauncherMode(
                UseLauncherToggle.IsOn,
                GetSelectedBuild());

            ShowStatus(
                UseLauncherToggle.IsOn
                    ? "Automatic version detection enabled."
                    : "Specific v2 interpreter enabled.",
                InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, InfoBarSeverity.Error);
            Refresh();
        }
    }

    /// <summary>
    /// Stores and, when launcher mode is disabled, immediately applies the preferred runtime architecture.
    /// </summary>
    private void InterpreterComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_loadedSettings)
        {
            return;
        }

        var build = GetSelectedBuild();
        _settings.Write(@"Launcher\v2", "Build", build);

        try
        {
            if (!UseLauncherToggle.IsOn)
            {
                _integration.SetLauncherMode(false, build);
            }

            ShowStatus("Interpreter preference saved.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
    }

    /// <summary>
    /// Saves the per-user Edit Script shell command.
    /// </summary>
    private void SaveEditorButton_Click(object sender, RoutedEventArgs e)
    {
        var command = EditorCommandTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(command))
        {
            ShowStatus("Editor command cannot be empty.", InfoBarSeverity.Warning);
            return;
        }

        _settings.WriteEditorCommand(command);
        ShowStatus("Editor command saved.", InfoBarSeverity.Success);
    }

    /// <summary>
    /// Persists the legacy Dashboard update-check preference.
    /// </summary>
    private void CheckUpdatesToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loadedSettings)
        {
            return;
        }

        _settings.WriteBoolean(
            "Dash",
            "CheckForUpdates",
            CheckUpdatesToggle.IsOn);

        ShowStatus("Update preference saved.", InfoBarSeverity.Success);
    }

    /// <summary>
    /// Maps the native ComboBox selection to the existing AutoHotkey build setting.
    /// </summary>
    private string GetSelectedBuild()
    {
        return InterpreterComboBox.SelectedIndex switch
        {
            1 => "64-bit",
            2 => "32-bit",
            _ => string.Empty
        };
    }

    /// <summary>
    /// Shows non-modal settings feedback.
    /// </summary>
    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        SettingsInfoBar.Message = message;
        SettingsInfoBar.Severity = severity;
        SettingsInfoBar.IsOpen = true;
    }
}
