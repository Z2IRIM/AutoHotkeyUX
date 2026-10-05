using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace AutoHotkeyUX.Modern;

public partial class MainWindow : Window
{
    private readonly AutoHotkeyIntegration _integration = new();
    private readonly AutoHotkeySettings _settings = new();
    private string _selectedTemplate = "Blank";
    private bool _settingsLoaded;

    /// <summary>
    /// Initializes the modern shell and hydrates it from the existing AutoHotkey installation and registry settings.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();

        ScriptLocationTextBox.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "AutoHotkey");

        RefreshRuntime();
        LoadSettings();
        UpdateScriptPreview();
        ShowPage(HomePage);
    }

    /// <summary>
    /// Displays exactly one application page and updates navigation emphasis.
    /// </summary>
    private void ShowPage(Grid page)
    {
        HomePage.Visibility = page == HomePage ? Visibility.Visible : Visibility.Collapsed;
        NewScriptPage.Visibility = page == NewScriptPage ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == SettingsPage ? Visibility.Visible : Visibility.Collapsed;

        HomeNavButton.Background = page == HomePage
            ? (System.Windows.Media.Brush)FindResource("AccentSoftBrush")
            : (System.Windows.Media.Brush)FindResource("CardBackgroundBrush");
        NewScriptNavButton.Background = page == NewScriptPage
            ? (System.Windows.Media.Brush)FindResource("AccentSoftBrush")
            : (System.Windows.Media.Brush)FindResource("CardBackgroundBrush");
        SettingsNavButton.Background = page == SettingsPage
            ? (System.Windows.Media.Brush)FindResource("AccentSoftBrush")
            : (System.Windows.Media.Brush)FindResource("CardBackgroundBrush");
    }

    /// <summary>
    /// Refreshes the Home page runtime state from the detected AutoHotkey installation.
    /// </summary>
    private void RefreshRuntime()
    {
        RuntimeDetailsText.Text = _integration.GetRuntimeDescription();
        var detected = _integration.GetAutoHotkeyExecutable() is not null;
        RuntimeStatusText.Text = detected ? "● AutoHotkey ready" : "○ Runtime not detected";
        SidebarVersionText.Text = detected ? "AutoHotkey v2" : "AutoHotkey";
    }

    /// <summary>
    /// Loads launcher, editor and update preferences from the existing AutoHotkey registry schema.
    /// </summary>
    private void LoadSettings()
    {
        _settingsLoaded = false;

        var openCommand = Registry.ClassesRoot
            .OpenSubKey(@"AutoHotkeyScript\shell\open\command")
            ?.GetValue(null)
            ?.ToString() ?? string.Empty;

        UseLauncherCheckBox.IsChecked = openCommand.Contains(
            @"UX\launcher.ahk",
            StringComparison.OrdinalIgnoreCase);

        var build = _settings.Read(@"Launcher\v2", "Build", "");
        InterpreterComboBox.SelectedIndex = build switch
        {
            "64-bit" => 1,
            "32-bit" => 2,
            _ => 0
        };

        EditorCommandTextBox.Text = _settings.ReadEditorCommand();
        CheckUpdatesCheckBox.IsChecked = _settings.ReadBoolean("Dash", "CheckForUpdates", false);

        _settingsLoaded = true;
    }

    /// <summary>
    /// Updates the code preview and destination description from the current New Script form state.
    /// </summary>
    private void UpdateScriptPreview()
    {
        ScriptPreviewTextBox.Text = GetTemplateContent(_selectedTemplate);

        var name = string.IsNullOrWhiteSpace(ScriptNameTextBox.Text)
            ? "Untitled"
            : ScriptNameTextBox.Text.Trim();

        if (!name.EndsWith(".ahk", StringComparison.OrdinalIgnoreCase))
        {
            name += ".ahk";
        }

        CreateScriptStatusText.Text = $"Creates {name}";
    }

    /// <summary>
    /// Returns the starter content for one of the built-in modern-shell templates.
    /// </summary>
    private static string GetTemplateContent(string template)
    {
        return template switch
        {
            "Hotkeys" =>
                "#Requires AutoHotkey v2.0\r\n\r\n" +
                "; Ctrl + Alt + E\r\n" +
                "^!e:: {\r\n" +
                "    ; Add your hotkey action here.\r\n" +
                "}\r\n",
            "Automation" =>
                "#Requires AutoHotkey v2.0\r\n\r\n" +
                "; Add your desktop automation here.\r\n" +
                "Run \"explorer.exe\"\r\n",
            _ => "#Requires AutoHotkey v2.0\r\n\r\n"
        };
    }

    /// <summary>
    /// Keeps only the selected template visually emphasized.
    /// </summary>
    private void SelectTemplate(string template)
    {
        _selectedTemplate = template;
        BlankTemplateButton.Background = template == "Blank"
            ? (System.Windows.Media.Brush)FindResource("AccentSoftBrush")
            : (System.Windows.Media.Brush)FindResource("CardBackgroundBrush");
        HotkeysTemplateButton.Background = template == "Hotkeys"
            ? (System.Windows.Media.Brush)FindResource("AccentSoftBrush")
            : (System.Windows.Media.Brush)FindResource("CardBackgroundBrush");
        AutomationTemplateButton.Background = template == "Automation"
            ? (System.Windows.Media.Brush)FindResource("AccentSoftBrush")
            : (System.Windows.Media.Brush)FindResource("CardBackgroundBrush");

        UpdateScriptPreview();
    }

        Process.Start(new ProcessStartInfo(exe, $"\"{script}\"")
        {
            WorkingDirectory = _integration.UxDirectory,
            UseShellExecute = true
        });
    }

    private void HomeNavButton_Click(object sender, RoutedEventArgs e) => ShowPage(HomePage);
    private void NewScriptNavButton_Click(object sender, RoutedEventArgs e) => ShowPage(NewScriptPage);
    private void SettingsNavButton_Click(object sender, RoutedEventArgs e) => ShowPage(SettingsPage);
    private void CreateScriptHero_Click(object sender, RoutedEventArgs e) => ShowPage(NewScriptPage);
    private void CancelCreateButton_Click(object sender, RoutedEventArgs e) => ShowPage(HomePage);

    /// <summary>
    /// Handles the template card selection from its Tag value.
    /// </summary>
    private void TemplateButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string template)
        {
            SelectTemplate(template);
        }
    }

    private void ScriptInput_Changed(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded)
        {
            UpdateScriptPreview();
        }
    }

    /// <summary>
    /// Lets the user choose a script destination folder without changing the global default.
    /// </summary>
    private void BrowseScriptLocationButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select script folder",
            InitialDirectory = Directory.Exists(ScriptLocationTextBox.Text)
                ? ScriptLocationTextBox.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dialog.ShowDialog(this) == true)
        {
            ScriptLocationTextBox.Text = dialog.FolderName;
        }
    }

    /// <summary>
    /// Creates a non-overwriting UTF-8 script and selects it in File Explorer.
    /// </summary>
    private void CreateScriptButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var directory = ScriptLocationTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(directory))
            {
                CreateScriptStatusText.Text = "Choose a location first.";
                return;
            }

            var path = _integration.CreateScript(
                directory,
                ScriptNameTextBox.Text,
                GetTemplateContent(_selectedTemplate));

            CreateScriptStatusText.Text = $"Created {Path.GetFileName(path)}";
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            CreateScriptStatusText.Text = ex.Message;
        }
    }

    private void WindowSpyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _integration.OpenWindowSpy();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Window Spy", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CompileButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _integration.OpenCompiler();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Compile", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void DocumentationButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _integration.OpenDocumentation();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Documentation", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ManageRuntimeButton_Click(object sender, RoutedEventArgs e) => ShowPage(SettingsPage);

    /// <summary>
    /// Applies launcher enable/disable state through the existing per-user AutoHotkey shell association.
    /// </summary>
    private void UseLauncherCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!_settingsLoaded)
        {
            return;
        }

        try
        {
            var build = InterpreterComboBox.SelectedItem is ComboBoxItem item
                ? item.Content?.ToString() ?? string.Empty
                : string.Empty;

            _integration.SetLauncherMode(UseLauncherCheckBox.IsChecked == true, build);
            SettingsStatusText.Text = UseLauncherCheckBox.IsChecked == true
                ? "Automatic version detection enabled."
                : "Specific v2 interpreter enabled.";
        }
        catch (Exception ex)
        {
            SettingsStatusText.Text = ex.Message;
            LoadSettings();
        }
    }

    /// <summary>
    /// Stores the preferred v2 build using the existing Launcher\v2 Build setting.
    /// </summary>
    private void InterpreterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_settingsLoaded || InterpreterComboBox.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        var value = item.Content?.ToString() switch
        {
            "64-bit" => "64-bit",
            "32-bit" => "32-bit",
            _ => string.Empty
        };

        _settings.Write(@"Launcher\v2", "Build", value);
        if (UseLauncherCheckBox.IsChecked != true)
        {
            _integration.SetLauncherMode(false, value);
        }
        SettingsStatusText.Text = "Interpreter preference saved.";
    }

    /// <summary>
    /// Saves the per-user Edit Script command exactly as entered.
    /// </summary>
    private void SaveEditorButton_Click(object sender, RoutedEventArgs e)
    {
        var command = EditorCommandTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(command))
        {
            SettingsStatusText.Text = "Editor command cannot be empty.";
            return;
        }

        _settings.WriteEditorCommand(command);
        SettingsStatusText.Text = "Editor command saved.";
    }

    /// <summary>
    /// Persists the existing Dashboard update-check preference.
    /// </summary>
    private void CheckUpdatesCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!_settingsLoaded)
        {
            return;
        }

        _settings.WriteBoolean("Dash", "CheckForUpdates", CheckUpdatesCheckBox.IsChecked == true);
        SettingsStatusText.Text = "Update preference saved.";
    }
}
