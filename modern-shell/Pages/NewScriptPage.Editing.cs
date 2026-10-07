using AutoHotkeyUX.Modern.Models;
using AutoHotkeyUX.Modern.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AutoHotkeyUX.Modern.Pages;

public sealed partial class NewScriptPage
{
    /// <summary>Adds a supported action with useful starter parameters and selects its property editor.</summary>
    private void Library_Click(object sender, RoutedEventArgs e)
    { if (_busy || sender is not Button { Tag: FlowActionKind kind } || _session.Document.Actions.Length >= VisualFlowCodec.MaximumActions) return; AddAction(kind); }

    /// <summary>Appends one action through the shared history boundary without executing it.</summary>
    private void AddAction(FlowActionKind kind)
    {
        var action = new FlowAction { Kind = kind, DelayMs = kind == FlowActionKind.Wait ? 500 : 0, Value = kind switch
        { FlowActionKind.OpenProgram => "notepad.exe", FlowActionKind.OpenWebsite => "https://example.com", FlowActionKind.SendText => "Hello!", FlowActionKind.SendKeys => "Enter", _ => "" } };
        _session.Replace(_session.Document with { Actions = [.. _session.Document.Actions, action] });
        _session.Selection = action.Id; RenderDocument();
    }

    /// <summary>Shows the undeletable trigger's properties without changing workflow data.</summary>
    private void Trigger_Click(object sender, RoutedEventArgs e) { if (_busy) return; _session.Selection = null; RenderDocument(); }

    /// <summary>Changes selection without rebuilding the collection during a native drag operation.</summary>
    private void ActionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_rendering || _busy || _dragging) return;
        _session.Selection = (ActionList.SelectedItem as FlowActionCard)?.Action.Id;
        _rendering = true;
        try { RenderProperties(); }
        finally { _rendering = false; }
        TriggerButton.BorderThickness = new Thickness(_session.Selection is null ? 2 : 1); RefreshValidation();
    }

    /// <summary>Protects the live reorder operation from rerenders and accepted saves.</summary>
    private void ActionList_DragItemsStarting(object sender, DragItemsStartingEventArgs e) { e.Cancel = _busy; _dragging = !e.Cancel; }
    /// <summary>Commits the native collection's actual order to the saved workflow and history.</summary>
    private void ActionList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs e) { _dragging = false; CommitCardOrder(); }
    /// <summary>Applies one completed reorder while retaining the stable selection.</summary>
    private void CommitCardOrder()
    { if (_busy) { RenderDocument(); return; } _session.Replace(_session.Document with { Actions = _cards.Select(card => card.Action).ToArray() }); RenderDocument(); }
    /// <summary>Provides a keyboard-accessible alternative to dragging upward.</summary>
    private void Up_Click(object sender, RoutedEventArgs e) => MoveSelected(-1);
    /// <summary>Provides a keyboard-accessible alternative to dragging downward.</summary>
    private void Down_Click(object sender, RoutedEventArgs e) => MoveSelected(1);
    /// <summary>Moves only an action; the trigger is outside the reorder range.</summary>
    private void MoveSelected(int direction)
    { if (_busy || !_session.Selection.HasValue) return; var index = Array.FindIndex(_session.Document.Actions, action => action.Id == _session.Selection); _session.Move(_session.Selection.Value, index + direction); RenderDocument(); }
    /// <summary>Removes the selected action and returns the inspector to the trigger.</summary>
    private void Delete_Click(object sender, RoutedEventArgs e)
    { if (_busy || !_session.Selection.HasValue) return; _session.Replace(_session.Document with { Actions = _session.Document.Actions.Where(action => action.Id != _session.Selection).ToArray() }); _session.Selection = null; RenderDocument(); }
    /// <summary>Undoes the latest edit without saving files.</summary>
    private void Undo_Click(object sender, RoutedEventArgs e) { if (_busy) return; _session.Undo(); RenderDocument(); }
    /// <summary>Reapplies the latest undone edit.</summary>
    private void Redo_Click(object sender, RoutedEventArgs e) { if (_busy) return; _session.Redo(); RenderDocument(); }
    /// <summary>Refreshes destination validation without changing the workflow language.</summary>
    private void ScriptInput_Changed(object sender, TextChangedEventArgs e) { if (!_rendering) RefreshValidation(); }

    /// <summary>Updates exactly the selected action while retaining its TextBox focus.</summary>
    private void ChangeAction(Func<FlowAction, FlowAction> update, bool renderProperties = false)
    {
        if (_rendering || _busy || !_session.Selection.HasValue) return;
        _session.Replace(_session.Document with { Actions = _session.Document.Actions.Select(action => action.Id == _session.Selection ? update(action) : action).ToArray() });
        RenderDocument(renderProperties);
    }

    /// <summary>Includes trigger and key selections in the same draft.</summary>
    private void TriggerInput_Changed(object sender, SelectionChangedEventArgs e) => ChangeTrigger();
    /// <summary>Includes modifier checkbox changes in the trigger draft.</summary>
    private void Modifiers_Click(object sender, RoutedEventArgs e) => ChangeTrigger();
    /// <summary>Reads trigger controls as one closed modifier/key vocabulary.</summary>
    private void ChangeTrigger()
    {
        if (_rendering || _busy) return;
        var modifiers = (CtrlCheck.IsChecked == true ? FlowModifiers.Ctrl : 0) | (AltCheck.IsChecked == true ? FlowModifiers.Alt : 0)
            | (ShiftCheck.IsChecked == true ? FlowModifiers.Shift : 0) | (WinCheck.IsChecked == true ? FlowModifiers.Win : 0);
        _session.Replace(_session.Document with { Trigger = _session.Document.Trigger with
        { Kind = TriggerKindCombo.SelectedIndex == 1 ? FlowTriggerKind.Startup : FlowTriggerKind.Hotkey, Key = TriggerKeyCombo.SelectedItem as string ?? "D", Modifiers = modifiers } });
        RenderDocument(false); HotkeyFields.Visibility = TriggerKindCombo.SelectedIndex == 1 ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Applies or clears the whole-workflow active-application condition.</summary>
    private void Scope_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_rendering || _busy) return;
        _session.Replace(_session.Document with { Trigger = _session.Document.Trigger with
        { Scope = ScopeCombo.SelectedIndex == 1 ? FlowScopeKind.ActiveApplication : FlowScopeKind.AnyApplication,
            Application = ScopeCombo.SelectedIndex == 1 ? (ApplicationBox.Text.Length == 0 ? "explorer.exe" : ApplicationBox.Text) : "" } });
        RenderDocument();
    }
    /// <summary>Preserves incomplete application input until validation permits saving.</summary>
    private void Application_Changed(object sender, TextChangedEventArgs e)
    { if (_rendering || _busy || ScopeCombo.SelectedIndex != 1) return; _session.Replace(_session.Document with { Trigger = _session.Document.Trigger with { Application = ApplicationBox.Text } }); RenderDocument(false); }
    /// <summary>Switches predefined and custom folders without carrying an irrelevant path.</summary>
    private void Folder_Changed(object sender, SelectionChangedEventArgs e)
        => ChangeAction(action => action with { Folder = (FlowFolderKind)Math.Max(0, FolderCombo.SelectedIndex), Value = FolderCombo.SelectedIndex == 2 ? ActionValueBox.Text : "" }, true);
    /// <summary>Stores literal action text, whose escaping belongs to the generator.</summary>
    private void ActionValue_Changed(object sender, TextChangedEventArgs e) => ChangeAction(action => action with { Value = ActionValueBox.Text });
    /// <summary>Maps supported keys instead of accepting free AHK Send expressions.</summary>
    private void SendKeys_Changed(object sender, SelectionChangedEventArgs e) => ChangeAction(action => action with { Value = SendKeysCombo.SelectedItem as string ?? "" });
    /// <summary>Rejects missing or fractional waits instead of silently truncating them.</summary>
    private void Wait_Changed(NumberBox sender, NumberBoxValueChangedEventArgs e)
        => ChangeAction(action => action with { DelayMs = double.IsFinite(e.NewValue) && e.NewValue == Math.Truncate(e.NewValue) && e.NewValue is >= 0 and <= 60000 ? (int)e.NewValue : -1 });
}
