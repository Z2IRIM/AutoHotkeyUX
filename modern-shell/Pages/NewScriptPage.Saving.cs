using AutoHotkeyUX.Modern.Models;
using AutoHotkeyUX.Modern.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace AutoHotkeyUX.Modern.Pages;

public sealed partial class NewScriptPage
{
    /// <summary>Serializes opening, retains unchanged-file drafts and guards replacement by a newer saved pair.</summary>
    internal async Task OpenAsync(string path, XamlRoot root)
    {
        if (_busy) throw new InvalidOperationException("Finish the current workflow operation before opening another workflow.");
        _busy = true; SetEditingEnabled(false); RefreshValidation();
        try
        {
            var opened = await Task.Run(() => _services.VisualFlows.Open(path));
            if (_opened?.ScriptPath.Equals(opened.ScriptPath, StringComparison.OrdinalIgnoreCase) == true
                && _opened.SourceHash == opened.SourceHash && _opened.SidecarHash == opened.SidecarHash) return;
            if (!await CanReplaceDraftAsync(root)) return;
            _opened = opened; _session.Load(opened.Document); _insertion = default; _branchSelection = null;
            _rendering = true; ScriptNameTextBox.Text = Path.GetFileNameWithoutExtension(opened.ScriptPath); ScriptLocationTextBox.Text = Path.GetDirectoryName(opened.ScriptPath)!; _rendering = false;
            RememberSaved(); ShowCreateMessage("Opened the visual workflow. Saving does not start or restart it.", InfoBarSeverity.Informational);
        }
        finally { _busy = false; SetEditingEnabled(true); RenderDocument(); }
    }

    /// <summary>Uses an in-app discard choice only when switching would lose an actual unsaved draft.</summary>
    private async Task<bool> CanReplaceDraftAsync(XamlRoot root)
    {
        if (!HasChanges()) return true;
        var dialog = new ContentDialog { XamlRoot = root, Title = "Keep your current changes?", Content = "Loading a saved workflow or starting a new one replaces this unsaved draft.",
            PrimaryButtonText = "Discard changes", CloseButtonText = "Keep editing", DefaultButton = ContentDialogButton.Close };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <summary>Disables repeat submissions and sends accepted writes through the shared save queue.</summary>
    private async void Save_Click(object sender, RoutedEventArgs e) => await SaveDraftAsync();
    /// <summary>Captures a validated draft, writes off the UI thread, then records the exact saved file identity.</summary>
    private async Task SaveDraftAsync()
    {
        if (_busy || !SaveButton.IsEnabled) return;
        _busy = true; SetEditingEnabled(false); RefreshValidation();
        try
        {
            var saved = await _services.VisualFlows.SaveAsync(ScriptLocationTextBox.Text.Trim(), ScriptNameTextBox.Text, _session.Document, _opened);
            _opened = saved; _session.Load(saved.Document); _rendering = true; ScriptNameTextBox.Text = Path.GetFileNameWithoutExtension(saved.ScriptPath); _rendering = false;
            RememberSaved(); _services.Catalog.Refresh();
            var outside = !string.Equals(Path.GetDirectoryName(saved.ScriptPath), _services.Catalog.RootDirectory, StringComparison.OrdinalIgnoreCase);
            ShowCreateMessage("Saved " + saved.ScriptPath + (outside ? ". This location is outside the Scripts workspace; continue editing here." : ". Open Scripts when you want to run it."), InfoBarSeverity.Success);
        }
        catch (Exception ex) { ServiceDiagnostics.Write("VisualFlow", "Workflow save failed; retained the current draft.", ex); ShowCreateMessage(ex.Message, InfoBarSeverity.Error); }
        finally { _busy = false; SetEditingEnabled(true); RenderDocument(); }
    }

    /// <summary>Sets the saved baseline and keeps the existing workflow destination immutable.</summary>
    private void RememberSaved()
    {
        _baseline = _session.Document; _baselineName = ScriptNameTextBox.Text; _baselineDirectory = ScriptLocationTextBox.Text;
        PageTitle.Text = "Edit your workflow"; ScriptNameTextBox.IsEnabled = false; ScriptLocationTextBox.IsEnabled = false; BrowseButton.IsEnabled = false; SavedActions.Visibility = Visibility.Visible;
    }
    /// <summary>Blocks draft changes in flight while allowing navigation to other app pages.</summary>
    private void SetEditingEnabled(bool enabled)
    {
        if (!enabled) CloseLibraryMenus();
        EditorGrid.IsHitTestVisible = enabled; ActionList.IsEnabled = enabled; TriggerButton.IsEnabled = enabled;
        foreach (var control in new Control[] { TriggerKindCombo, TriggerKeyCombo, ScopeCombo, ApplicationBox, CtrlCheck, AltCheck, ShiftCheck, WinCheck,
            FolderCombo, ActionValueBox, SendKeysCombo, WaitBox }) control.IsEnabled = enabled;
        ActionList.CanDragItems = enabled; ActionList.CanReorderItems = enabled;
        ScriptNameTextBox.IsEnabled = enabled && _opened is null; ScriptLocationTextBox.IsEnabled = enabled && _opened is null; BrowseButton.IsEnabled = enabled && _opened is null;
        CodeEditorButton.IsEnabled = enabled; NewWorkflowButton.IsEnabled = enabled;
    }

    /// <summary>Starts another workflow after protecting edits to the previous one.</summary>
    private async void NewWorkflow_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !await CanReplaceDraftAsync(XamlRoot)) return;
        _opened = null; _session.Load(VisualEditorSession.DefaultDocument()); _insertion = default; _branchSelection = null;
        _rendering = true; ScriptNameTextBox.Text = "QuickActions"; ScriptLocationTextBox.Text = _services.Catalog.RootDirectory; _rendering = false;
        _baseline = _session.Document; _baselineName = ScriptNameTextBox.Text; _baselineDirectory = ScriptLocationTextBox.Text;
        PageTitle.Text = "Build a workflow"; SavedActions.Visibility = Visibility.Collapsed; CreateInfoBar.IsOpen = false; SetEditingEnabled(true); RenderDocument();
    }
    /// <summary>Shows Scripts without implicitly running newly generated actions.</summary>
    private void Scripts_Click(object sender, RoutedEventArgs e) => _openScripts();
    /// <summary>Explicitly opens saved source in the configured editor and explains manual edit protection.</summary>
    private void CodeEditor_Click(object sender, RoutedEventArgs e)
    {
        if (_opened is null || _busy) return;
        try { _services.Integration.EditScript(_opened.ScriptPath, _services.Settings); ShowCreateMessage("Code editor opened. Manual source changes are preserved and prevent visual overwrite.", InfoBarSeverity.Informational); }
        catch (Exception ex) { ShowCreateMessage(ex.Message, InfoBarSeverity.Error); }
    }
    /// <summary>Uses the established native folder picker without creating a parallel workspace scanner.</summary>
    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FolderPicker(); picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance!));
            var folder = await picker.PickSingleFolderAsync();
            if (folder is not null && !_busy && _opened is null) ScriptLocationTextBox.Text = folder.Path;
        }
        catch (Exception ex) { ShowCreateMessage(ex.Message, InfoBarSeverity.Error); }
    }
    /// <summary>Shows validation, creation and recovery feedback inside the app.</summary>
    private void ShowCreateMessage(string message, InfoBarSeverity severity) { CreateInfoBar.Message = message; CreateInfoBar.Severity = severity; CreateInfoBar.IsOpen = true; }
}
