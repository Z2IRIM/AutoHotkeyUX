using AutoHotkeyUX.Modern.Models;
using AutoHotkeyUX.Modern.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace AutoHotkeyUX.Modern.Pages;

public sealed partial class ShortcutSettingsPage : Page
{
    private readonly ShortcutPreferencesService _preferences;
    private readonly ShortcutActivityService _activity;
    private readonly ScriptStartupService _startup;
    private readonly ScriptExecutionService _execution;
    private readonly Action _goSettings;
    private ShortcutPreferences _draft;
    private ShortcutPreferences _lastSaved;
    private bool _ready, _rendering, _saving, _subscribed;

    /// <summary>Creates the approved settings subpage over existing shared runtime owners.</summary>
    internal ShortcutSettingsPage(ApplicationServices services, Action goSettings)
    {
        _preferences = services.ShortcutPreferences; _activity = services.ShortcutActivity;
        _startup = services.ScriptStartup; _execution = services.Execution; _goSettings = goSettings;
        _draft = _lastSaved = _preferences.Saved;
        InitializeComponent();
        _ready = true;
        Loaded += Page_Loaded; Unloaded += Page_Unloaded;
        RenderForm();
    }

    /// <summary>Refreshes saved state without replacing a retained draft and subscribes only while visible.</summary>
    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_draft == _lastSaved) _draft = _preferences.Saved;
        _lastSaved = _preferences.Saved;
        if (!_subscribed)
        {
            _preferences.Changed += Shared_Changed; _execution.Changed += Shared_Changed;
            _startup.Changed += Shared_Changed; _activity.Changed += Activity_Changed; _subscribed = true;
        }
        RenderForm(); RenderActivity();
        if (_preferences.Warning is { } warning) ShowFeedback(warning, InfoBarSeverity.Warning);
    }

    /// <summary>Detaches all background notifications when the user navigates away.</summary>
    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _subscribed = false;
        _preferences.Changed -= Shared_Changed; _execution.Changed -= Shared_Changed;
        _startup.Changed -= Shared_Changed; _activity.Changed -= Activity_Changed;
    }

    /// <summary>Updates status on the XAML dispatcher without overwriting edited controls.</summary>
    private void Shared_Changed(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(() =>
    { if (_subscribed) { _lastSaved = _preferences.Saved; RefreshDraftState(); } });

    /// <summary>Renders one immutable draft and prevents programmatic field changes from becoming edits.</summary>
    private void RenderForm()
    {
        _rendering = true;
        try
        {
            TerminalEnabledToggle.IsOn = _draft.TerminalEnabled; ArchiveEnabledToggle.IsOn = _draft.ArchiveEnabled;
            Select(TerminalKeyCombo, _draft.TerminalShortcut); Select(ArchiveKeyCombo, _draft.ArchiveShortcut);
            Select(ProgramCombo, _draft.TerminalProgram); Select(PositionCombo, _draft.TerminalPosition);
            Select(DestinationCombo, _draft.ArchiveDestination);
            WidthBox.Value = _draft.TerminalWidth; HeightBox.Value = _draft.TerminalHeight; GapBox.Value = _draft.PointerGap;
            FolderTextBox.Text = _draft.ArchiveFolder;
        }
        finally { _rendering = false; }
        RefreshDraftState();
    }

    /// <summary>Copies current controls into a typed draft without changing stored or running preferences.</summary>
    private void ReadDraft()
    {
        if (!_ready || _rendering || _saving) return;
        _draft = new(TerminalEnabledToggle.IsOn, Selected(TerminalKeyCombo), Selected(ProgramCombo), Selected(PositionCombo),
            Integer(WidthBox.Value), Integer(HeightBox.Value), Integer(GapBox.Value), ArchiveEnabledToggle.IsOn,
            Selected(ArchiveKeyCombo), Selected(DestinationCombo), FolderTextBox.Text.Trim());
        PreferencesInfoBar.IsOpen = false;
        RefreshDraftState();
    }

    /// <summary>Recomputes validation, dirty state, actual runtime status and placement from the draft.</summary>
    private void RefreshDraftState()
    {
        if (!_ready) return;
        var errors = ShortcutPreferenceCodec.Validate(_draft, false);
        var dirty = _draft != _preferences.Saved || _preferences.Warning is not null;
        FieldErrorsText.Text = string.Join("\n", errors.Values);
        FieldErrorsText.Visibility = errors.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        SaveStateText.Text = _saving ? "Applying preferences…" : errors.Count > 0 ? "Check the highlighted fields"
            : dirty ? "Unsaved changes" : "All changes saved";
        SaveButton.IsEnabled = !_saving && dirty && errors.Count == 0;
        DiscardButton.IsEnabled = !_saving && dirty;
        foreach (Control control in new Control[] { TerminalEnabledToggle, ArchiveEnabledToggle, TerminalKeyCombo,
            ArchiveKeyCombo, ProgramCombo, PositionCombo, DestinationCombo, WidthBox, HeightBox, FolderTextBox, BrowseButton })
            control.IsEnabled = !_saving;
        SaveProgress.IsActive = _saving; SaveProgress.Visibility = _saving ? Visibility.Visible : Visibility.Collapsed;
        GapBox.IsEnabled = !_saving && _draft.TerminalPosition == "above";
        CustomFolderPanel.Visibility = _draft.ArchiveDestination == "custom" ? Visibility.Visible : Visibility.Collapsed;
        ContextText.Text = !_startup.ExplorerEnabled ? "Explorer shortcuts are paused. You can still save preferences."
            : _draft.TerminalShortcut == _draft.ArchiveShortcut ? "One shortcut, two targets: folders open a terminal; archives extract."
            : "Each shortcut applies only to its target type.";
        EngineStatusText.Text = IsExplorerRunning() ? "● Running" : "Stopped";
        OutputExampleText.Text = _draft.ArchiveDestination == "beside" ? @"C:\Downloads\Toolkit" :
            string.IsNullOrWhiteSpace(_draft.ArchiveFolder) ? "Choose a destination folder" : _draft.ArchiveFolder.TrimEnd('\\', '/') + @"\Toolkit";
        SetFieldHelp(TerminalKeyCombo, errors, nameof(_draft.TerminalShortcut));
        SetFieldHelp(ArchiveKeyCombo, errors, nameof(_draft.ArchiveShortcut));
        SetFieldHelp(FolderTextBox, errors, nameof(_draft.ArchiveFolder));
        RenderPlacement();
    }

    /// <summary>Attaches exact field validation to accessibility output without an extra storage mechanism.</summary>
    private static void SetFieldHelp(DependencyObject control, IReadOnlyDictionary<string, string> errors, string field)
        => Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(control, errors.GetValueOrDefault(field) ?? string.Empty);

    /// <summary>Chooses a known ComboBox item by its protocol key.</summary>
    private static void Select(ComboBox combo, string value) => combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().First(item => (string)item.Tag == value);
    /// <summary>Reads the selected protocol key independently of visible copy.</summary>
    private static string Selected(ComboBox combo) => (combo.SelectedItem as ComboBoxItem)?.Tag as string ?? string.Empty;
    /// <summary>Rejects blank, fractional and non-finite input rather than silently truncating it.</summary>
    private static int Integer(double value) => double.IsFinite(value) && value == Math.Truncate(value) && value is >= 0 and <= 1600 ? (int)value : -1;
    /// <summary>Reports the actual managed interpreter rather than treating its startup preference as running proof.</summary>
    private bool IsExplorerRunning() => _execution.Snapshot().Any(item => _startup.IsExplorerScript(item.ScriptPath) && item.State == ScriptState.Running);
    /// <summary>Updates the draft when a toggle changes.</summary>
    private void Draft_Toggled(object sender, RoutedEventArgs e) => ReadDraft();
    /// <summary>Updates the draft when a program, shortcut or destination changes.</summary>
    private void Draft_SelectionChanged(object sender, SelectionChangedEventArgs e) => ReadDraft();
    /// <summary>Updates the draft from integral window dimensions.</summary>
    private void Draft_NumberChanged(NumberBox sender, NumberBoxValueChangedEventArgs e) => ReadDraft();
    /// <summary>Updates the draft from the custom output path.</summary>
    private void Draft_TextChanged(object sender, TextChangedEventArgs e) => ReadDraft();

    /// <summary>Submits the draft once, retaining it and showing accurate feedback if the live transaction fails.</summary>
    private async Task SaveDraftAsync()
    {
        if (_saving || !SaveButton.IsEnabled) return;
        _saving = true; PreferencesInfoBar.IsOpen = false; RefreshDraftState();
        try
        {
            await _preferences.SaveAsync(_draft);
            _draft = _lastSaved = _preferences.Saved;
            if (IsLoaded)
            {
                RenderForm();
                ShowFeedback(IsExplorerRunning() ? "Shortcut preferences saved. New settings are active."
                    : "Shortcut preferences saved. They will apply when Explorer shortcuts next start.", InfoBarSeverity.Success);
            }
        }
        catch (Exception ex)
        {
            ServiceDiagnostics.Write("Shortcut", "Saving preferences failed or was not confirmed.", ex);
            if (IsLoaded) ShowFeedback(ex.Message, ex is UnconfirmedShortcutPreferencesException ? InfoBarSeverity.Warning : InfoBarSeverity.Error);
        }
        finally { _saving = false; if (IsLoaded) RefreshDraftState(); }
    }

    /// <summary>Starts the shared draft save from the native button.</summary>
    private async void Save_Click(object sender, RoutedEventArgs e) => await SaveDraftAsync();
    /// <summary>Restores saved preferences without restarting the script or modifying storage.</summary>
    private void Discard_Click(object sender, RoutedEventArgs e)
    { _draft = _lastSaved = _preferences.Saved; PreferencesInfoBar.IsOpen = false; RenderForm(); }

    /// <summary>Chooses an existing folder through the window-owned native picker.</summary>
    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.Downloads };
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance!));
            var folder = await picker.PickSingleFolderAsync();
            if (IsLoaded && !_saving && folder is not null) FolderTextBox.Text = folder.Path;
        }
        catch (Exception ex) { ShowFeedback(ex.Message, InfoBarSeverity.Error); }
    }

    /// <summary>Switches in-page content while preserving every draft value.</summary>
    private void SettingsTabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (!_ready) return;
        var activity = sender.SelectedItem == ActivityTab;
        PreferencesPanel.Visibility = activity ? Visibility.Collapsed : Visibility.Visible;
        ActivityPanel.Visibility = activity ? Visibility.Visible : Visibility.Collapsed;
        if (activity) RenderActivity();
    }

    /// <summary>Shows non-modal feedback in the active configuration page.</summary>
    private void ShowFeedback(string message, InfoBarSeverity severity)
    { PreferencesInfoBar.Message = message; PreferencesInfoBar.Severity = severity; PreferencesInfoBar.IsOpen = true; }
    /// <summary>Returns to Settings with the draft retained in the cached subpage.</summary>
    private void Back_Click(object sender, RoutedEventArgs e) => _goSettings();
}
