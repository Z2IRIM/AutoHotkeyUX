using Microsoft.UI.Xaml;

namespace AutoHotkeyUX.Modern.Pages;

public sealed partial class ShortcutSettingsPage
{
    /// <summary>Exercises actual XAML draft, validation, save and activity using the explicit isolated verification instance.</summary>
    internal async Task<object> VerifyPreferencesAsync()
    {
        if (!IsLoaded || !SaveButton.IsLoaded || _draft != _preferences.Saved)
            throw new InvalidOperationException("Shortcut preferences did not load a saved draft.");
        TerminalKeyCombo.SelectedIndex = 4;
        if (SaveButton.IsEnabled || !FieldErrorsText.Text.Contains("Explorer selection"))
            throw new InvalidOperationException("Reserved selection shortcut was not blocked in the native UI.");
        Discard_Click(this, new RoutedEventArgs());
        if (TerminalKeyCombo.SelectedIndex != 0 || SaveButton.IsEnabled)
            throw new InvalidOperationException("Discard did not restore the saved controls.");
        WidthBox.Value = 960;
        if (!SaveButton.IsEnabled) throw new InvalidOperationException("The native draft did not become saveable.");
        var originalX = Microsoft.UI.Xaml.Controls.Canvas.GetLeft(MiniTerminal);
        Placement_Click(this, new RoutedEventArgs());
        if (Microsoft.UI.Xaml.Controls.Canvas.GetLeft(MiniTerminal) == originalX)
            throw new InvalidOperationException("Placement preview did not react to the click point.");
        await SaveDraftAsync();
        if (_preferences.Saved.TerminalWidth != 960 || SaveButton.IsEnabled)
            throw new InvalidOperationException("Saved width did not round-trip through the actual settings owner.");
        DestinationCombo.SelectedIndex = 1;
        if (SaveButton.IsEnabled || FieldErrorsText.Visibility != Visibility.Visible)
            throw new InvalidOperationException("Missing custom destination was not blocked.");
        var folder = Path.Combine(Path.GetDirectoryName(_startup.ExplorerScriptPath)!, "Chosen output");
        Directory.CreateDirectory(folder);
        FolderTextBox.Text = folder;
        await Task.Delay(60); // Native TextBox dispatches TextChanged after the property setter returns.
        if (!SaveButton.IsEnabled || _draft.ArchiveFolder != folder)
            throw new InvalidOperationException("The native folder field did not update its draft.");
        await SaveDraftAsync();
        if (_preferences.Saved.ArchiveFolder != folder || SaveButton.IsEnabled)
            throw new InvalidOperationException("The chosen folder did not save in the native UI.");
        _activity.Record(new(DateTimeOffset.Now, "Extract", false, Path.Combine(folder, "Protected.zip"), 25,
            "Password-protected archives are not supported by quick extraction."));
        SettingsTabs.SelectedItem = ActivityTab;
        RenderActivity();
        if (ActivityPanel.Visibility != Visibility.Visible || ActivityList.Children.Count != 1)
            throw new InvalidOperationException("Actual activity did not reach the in-page view.");
        SettingsTabs.SelectedItem = PreferencesTab;
        WidthBox.Value = 1100;
        return new { Passed = true, ReservedShortcutBlocked = true, Discard = true, SavedWidth = 960,
            CustomDestination = folder, PlacementReacts = true, ActivityCount = ActivityList.Children.Count };
    }

    /// <summary>Confirms navigation keeps the pending draft and checks narrow fields/actions stay inside their containers.</summary>
    internal object VerifyRetainedDraftAndLayout()
    {
        if (WidthBox.Value != 1100 || !SaveButton.IsEnabled || _preferences.Saved.TerminalWidth != 960)
            throw new InvalidOperationException("Navigating away replaced the unsaved draft.");
        foreach (var control in new FrameworkElement[] { WidthBox, HeightBox, GapBox })
        {
            var point = control.TransformToVisual(DimensionsGrid).TransformPoint(default);
            if (point.X < -1 || point.X + control.ActualWidth > DimensionsGrid.ActualWidth + 1)
                throw new InvalidOperationException("Numeric preferences overflow at the minimum window width.");
        }
        var actions = SaveButtons.TransformToVisual(SaveBar).TransformPoint(default);
        if (actions.X + SaveButtons.ActualWidth > SaveBar.ActualWidth + 1)
            throw new InvalidOperationException("Preference actions overflow at the minimum window width.");
        return new { RetainedDraft = true, PageWidth = ActualWidth, FieldsWidth = DimensionsGrid.ActualWidth,
            SaveBarWidth = SaveBar.ActualWidth, SaveButtonsWidth = SaveButtons.ActualWidth };
    }
}
