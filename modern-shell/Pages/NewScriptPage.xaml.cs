using AutoHotkeyUX.Modern.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;

namespace AutoHotkeyUX.Modern.Pages;

public sealed partial class NewScriptPage : Page
{
    private readonly AutoHotkeyIntegration _integration;
    private string _selectedTemplate = "Blank";

    /// <summary>
    /// Initializes script creation with the user's Documents\AutoHotkey folder as the default destination.
    /// </summary>
    internal NewScriptPage(AutoHotkeyIntegration integration)
    {
        InitializeComponent();
        _integration = integration;

        ScriptLocationTextBox.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "AutoHotkey");

        UpdatePreview();
    }

    /// <summary>
    /// Updates the selected template card and generated starter code.
    /// </summary>
    private void SelectTemplate(string template)
    {
        _selectedTemplate = template;

        SetTemplateSelected(BlankTemplateButton, template == "Blank");
        SetTemplateSelected(HotkeysTemplateButton, template == "Hotkeys");
        SetTemplateSelected(AutomationTemplateButton, template == "Automation");

        UpdatePreview();
    }

    /// <summary>
    /// Applies the WinUI accent border only to the selected template.
    /// </summary>
    private static void SetTemplateSelected(Button button, bool selected)
    {
        button.BorderThickness = selected ? new Thickness(2) : new Thickness(1);
        button.BorderBrush = (Brush)Application.Current.Resources[
            selected ? "AccentFillColorDefaultBrush" : "CardStrokeColorDefaultBrush"];
    }

    /// <summary>
    /// Refreshes the preview and non-destructive destination description.
    /// </summary>
    private void UpdatePreview()
    {
        ScriptPreviewTextBox.Text = GetTemplateContent(_selectedTemplate);

        var name = string.IsNullOrWhiteSpace(ScriptNameTextBox.Text)
            ? "Untitled"
            : ScriptNameTextBox.Text.Trim();

        if (!name.EndsWith(".ahk", StringComparison.OrdinalIgnoreCase))
        {
            name += ".ahk";
        }

        CreateScriptStatusText.Text = $"Creates {name}; existing files are never overwritten.";
    }

    /// <summary>
    /// Returns the starter code associated with a template card.
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

    private void ScriptInput_Changed(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded)
        {
            UpdatePreview();
        }
    }

    private void TemplateButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string template })
        {
            SelectTemplate(template);
        }
    }

    /// <summary>
    /// Opens the WinUI folder picker and initializes it with the native desktop window handle.
    /// </summary>
    private async void BrowseScriptLocationButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");

        if (App.MainWindowInstance is not null)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            ScriptLocationTextBox.Text = folder.Path;
        }
    }

    /// <summary>
    /// Creates the script, suffixing on collisions, and reveals it in File Explorer.
    /// </summary>
    private void CreateScriptButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var directory = ScriptLocationTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(directory))
            {
                ShowCreateMessage("Choose a location first.", InfoBarSeverity.Warning);
                return;
            }

            var path = _integration.CreateScript(
                directory,
                ScriptNameTextBox.Text,
                GetTemplateContent(_selectedTemplate));

            _integration.RevealFile(path);
            ShowCreateMessage($"Created {Path.GetFileName(path)}", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowCreateMessage(ex.Message, InfoBarSeverity.Error);
        }
    }

    /// <summary>
    /// Shows script-creation feedback without modal message boxes.
    /// </summary>
    private void ShowCreateMessage(string message, InfoBarSeverity severity)
    {
        CreateInfoBar.Message = message;
        CreateInfoBar.Severity = severity;
        CreateInfoBar.IsOpen = true;
    }
}
