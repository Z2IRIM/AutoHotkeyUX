using AutoHotkeyUX.Modern.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AutoHotkeyUX.Modern.Pages;

public sealed partial class SettingsPage : Page
{
    private readonly AutoHotkeyIntegration _integration;
    private readonly AutoHotkeySettings _settings;
    private bool _loadedSettings;

    /// <summary>
    /// Creates the Settings page over the existing AutoHotkey registry contract.
    /// </summary>
    internal SettingsPage(
        AutoHotkeyIntegration integration,
        AutoHotkeySettings settings)
    {
        InitializeComponent();
        _integration = integration;
        _settings = settings;
        Loaded += SettingsPage_Loaded;
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

        _loadedSettings = true;
    }

    private void SettingsPage_Loaded(object sender, RoutedEventArgs e) => Refresh();

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
