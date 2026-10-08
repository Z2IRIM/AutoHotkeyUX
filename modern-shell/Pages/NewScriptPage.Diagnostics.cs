using AutoHotkeyUX.Modern.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace AutoHotkeyUX.Modern.Pages;

public sealed partial class NewScriptPage
{
    /// <summary>Checks native spacing, panel bounds and the whole workflow's alignment inside the actual viewport.</summary>
    internal object VerifyWorkflowLayout()
    {
        UpdateLayout();
        var bounds = new[] { LibraryPanel, FlowPanel, PropertiesPanel }.Select(panel =>
        {
            var position = panel.TransformToVisual(EditorGrid).TransformPoint(default);
            if (panel.ActualWidth <= 0 || position.X < -1 || position.X + panel.ActualWidth > EditorGrid.ActualWidth + 1)
                throw new InvalidOperationException("A workflow panel overflows: " + panel.Name);
            return new { Panel = panel.Name, X = position.X, Y = position.Y, Width = panel.ActualWidth, Height = panel.ActualHeight };
        }).ToArray();
        foreach (var category in ActionLibrary.Children.OfType<Button>())
            if (category.Flyout is not MenuFlyout { Items.Count: 4 })
                throw new InvalidOperationException("An action category is missing its floating menu.");
        foreach (var container in new FrameworkElement[] { DetailsGrid, PropertiesPanel, FlowPanel, LibraryPanel })
            CheckVisibleBounds(container, container);
        if (Grid.GetRow(PropertiesPanel) != (_layoutMode == 0 ? 0 : _layoutMode == 1 ? 1 : 2))
            throw new InvalidOperationException("The property inspector has the wrong responsive position.");
        var bodyPosition = PageBody.TransformToVisual(PageScroll).TransformPoint(default);
        var editorPosition = EditorGrid.TransformToVisual(PageScroll).TransformPoint(default);
        var propertiesPosition = PropertiesPanel.TransformToVisual(PageScroll).TransformPoint(default);
        if (Math.Abs(bodyPosition.X) > 1 || bodyPosition.X + PageBody.ActualWidth > PageScroll.ViewportWidth + 1
            || editorPosition.X < PageBody.Padding.Left - 1 || editorPosition.X + EditorGrid.ActualWidth > PageScroll.ViewportWidth - PageBody.Padding.Right + 1)
            throw new InvalidOperationException($"Workflow is shifted or clipped: body X={bodyPosition.X}, width={PageBody.ActualWidth}, viewport={PageScroll.ViewportWidth}.");
        return new { Passed = true, Mode = _layoutMode, Width = EditorGrid.ActualWidth, Panels = bounds, LibraryGap = 8,
            Viewport = new { PageWidth = ActualWidth, ScrollWidth = PageScroll.ActualWidth, Width = PageScroll.ViewportWidth,
                BodyX = bodyPosition.X, BodyWidth = PageBody.ActualWidth, EditorX = editorPosition.X,
                EditorRight = editorPosition.X + EditorGrid.ActualWidth, PropertiesRight = propertiesPosition.X + PropertiesPanel.ActualWidth } };
    }

    /// <summary>Checks actual native descendant control widths while ignoring collapsed property editors.</summary>
    private static void CheckVisibleBounds(DependencyObject node, FrameworkElement container)
    {
        if (node is FrameworkElement element && element.Visibility == Visibility.Collapsed) return;
        if (node is ListViewItem item && ItemsControl.ItemsControlFromItemContainer(item)?.IndexFromContainer(item) is not >= 0) return;
        if (node is Control control && control.IsLoaded && control.ActualWidth > 0)
        {
            var point = control.TransformToVisual(container).TransformPoint(new Point(0, 0));
            if (point.X < -1 || point.X + control.ActualWidth > container.ActualWidth + 1)
                throw new InvalidOperationException($"A workflow control overflows: {control.GetType().Name} '{control.Name}' X={point.X} Width={control.ActualWidth}, container={container.Name} Width={container.ActualWidth}");
        }
        for (var index = 0; index < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node); index++)
            CheckVisibleBounds(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(node, index), container);
    }

    /// <summary>Exercises real XAML events, paired saving, history, validation and reopening in the isolated app instance.</summary>
    internal async Task<string> VerifyWorkflowAsync()
    {
        if (!IsLoaded || !SaveButton.IsEnabled || CodeExpander.IsExpanded || _cards.Count != 2)
            throw new InvalidOperationException("The starter workflow did not load.");
        ScriptNameTextBox.Text = "Native workflow 测试";
        Library_Click(_libraryItems.Single(button => (FlowActionKind)button.Tag == FlowActionKind.OpenWebsite), new RoutedEventArgs());
        ActionValueBox.Text = "file:///C:/invalid";
        await Task.Delay(80);
        if (SaveButton.IsEnabled || FieldErrorsText.Visibility != Visibility.Visible) throw new InvalidOperationException("The native form accepted an invalid URL.");
        ActionValueBox.Text = "https://example.com/test";
        await Task.Delay(80);
        if (!SaveButton.IsEnabled || _session.Document.Actions[^1].Value != "https://example.com/test") throw new InvalidOperationException("Native URL editing did not reach the draft.");
        Up_Click(this, new RoutedEventArgs());
        if (_session.Document.Actions[1].Kind != FlowActionKind.OpenWebsite) throw new InvalidOperationException("Native up did not reorder.");
        Undo_Click(this, new RoutedEventArgs());
        if (_session.Document.Actions[^1].Kind != FlowActionKind.OpenWebsite) throw new InvalidOperationException("Native undo lost the action.");
        Redo_Click(this, new RoutedEventArgs());
        // Exercises the drag completion boundary with the same native ObservableCollection used by CanReorderItems.
        _cards.Move(1, 2); CommitCardOrder();
        if (_session.Document.Actions[^1].Kind != FlowActionKind.OpenWebsite) throw new InvalidOperationException("Native collection reorder was not committed.");
        Trigger_Click(this, new RoutedEventArgs());
        await VerifyPropertyEditorsAsync();
        ScopeCombo.SelectedIndex = 1;
        await Task.Delay(60);
        if (_session.Document.Trigger.Application != "explorer.exe" || !ScriptPreviewTextBox.Text.Contains("#HotIf")) throw new InvalidOperationException("Native active-application scope failed.");
        await SaveDraftAsync();
        if (_opened is null || SaveButton.IsEnabled || _services.Execution.Snapshot().Count != 0) throw new InvalidOperationException("Native create failed or implicitly ran the workflow.");
        var path = _opened.ScriptPath;
        var loaded = _services.VisualFlows.Open(path);
        if (loaded.Document.Actions.Length != 3 || loaded.Document.Actions[^1].Kind != FlowActionKind.OpenWebsite) throw new InvalidOperationException("Native saved order did not reopen.");
        _session.Load(loaded.Document); _opened = loaded; RememberSaved(); RenderDocument();
        ActionList.SelectedIndex = 1;
        await Task.Delay(80);
        WaitBox.Value = 750;
        await Task.Delay(80);
        if (!SaveButton.IsEnabled || _session.Document.Actions[1].DelayMs != 750) throw new InvalidOperationException("Reopened properties are not editable.");
        return path;
    }

    /// <summary>Checks distinct native parameter editors so queued TextChanged events cannot corrupt another action kind.</summary>
    private async Task VerifyPropertyEditorsAsync()
    {
        foreach (var kind in new[] { FlowActionKind.SendText, FlowActionKind.OpenProgram, FlowActionKind.SendKeys, FlowActionKind.OpenFolder, FlowActionKind.Wait })
        {
            AddAction(kind); await Task.Delay(60);
            switch (kind)
            {
                case FlowActionKind.SendText:
                    ActionValueBox.Text = "测试`\"value"; await Task.Delay(60);
                    if (_session.Document.Actions[^1].Value != "测试`\"value") throw new InvalidOperationException("Native literal text did not reach its action.");
                    break;
                case FlowActionKind.OpenProgram:
                    ActionValueBox.Text = @"C:\Windows\System32\notepad.exe"; await Task.Delay(60);
                    if (_session.Document.Actions[^1].Value != @"C:\Windows\System32\notepad.exe") throw new InvalidOperationException("Native program path did not reach its action.");
                    break;
                case FlowActionKind.SendKeys:
                    SendKeysCombo.SelectedItem = "Ctrl+V"; await Task.Delay(60);
                    if (_session.Document.Actions[^1].Value != "Ctrl+V") throw new InvalidOperationException("Native keys selection did not reach its action.");
                    break;
                case FlowActionKind.OpenFolder:
                    FolderCombo.SelectedIndex = 2; ActionValueBox.Text = _services.Catalog.RootDirectory; await Task.Delay(60);
                    if (_session.Document.Actions[^1].Folder != FlowFolderKind.Custom || _session.Document.Actions[^1].Value != _services.Catalog.RootDirectory)
                        throw new InvalidOperationException("Native custom folder did not reach its action.");
                    FolderCombo.SelectedIndex = 1; await Task.Delay(60);
                    if (_session.Document.Actions[^1].Value != "") throw new InvalidOperationException("Predefined folder retained a custom path.");
                    break;
                case FlowActionKind.Wait:
                    WaitBox.Value = 1.5; await Task.Delay(60);
                    if (SaveButton.IsEnabled) throw new InvalidOperationException("Native fractional wait was accepted.");
                    WaitBox.Value = 25; await Task.Delay(60);
                    break;
            }
            if (!SaveButton.IsEnabled) throw new InvalidOperationException("Native property editors produced an invalid action: " + kind + ": " + FieldErrorsText.Text);
            Delete_Click(this, new RoutedEventArgs()); await Task.Delay(60);
        }
        Trigger_Click(this, new RoutedEventArgs());
    }

    /// <summary>Checks a retained draft and writes its updated revision through the actual Save command.</summary>
    internal async Task<object> VerifyRetainedWorkflowAsync()
    {
        if (WaitBox.Value != 750 || !SaveButton.IsEnabled) throw new InvalidOperationException("Navigation lost the workflow draft.");
        await SaveDraftAsync();
        if (_opened?.Document.Revision != 2 || _services.VisualFlows.Open(_opened.ScriptPath).Document.Actions[1].DelayMs != 750)
            throw new InvalidOperationException("The edited workflow did not save and reopen.");
        return new { Passed = true, SavedPath = _opened.ScriptPath, Revision = 2, NavigationRetainsDraft = true, SaveDoesNotRun = _services.Execution.Snapshot().Count == 0 };
    }
}
