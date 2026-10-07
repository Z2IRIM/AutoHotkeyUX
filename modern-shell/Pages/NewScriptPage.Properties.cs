using AutoHotkeyUX.Modern.Models;
using Microsoft.UI.Xaml;

namespace AutoHotkeyUX.Modern.Pages;

public sealed partial class NewScriptPage
{
    /// <summary>Populates the selected step's native fields under the render event guard.</summary>
    private void RenderProperties()
    {
        var action = _session.Document.Actions.FirstOrDefault(step => step.Id == _session.Selection);
        TriggerFields.Visibility = action is null ? Visibility.Visible : Visibility.Collapsed;
        ActionFields.Visibility = action is null ? Visibility.Collapsed : Visibility.Visible;
        SelectedTitleText.Text = action is null ? "Workflow trigger" : FlowActionCard.Label(action.Kind);
        if (action is null)
        {
            var trigger = _session.Document.Trigger;
            TriggerKindCombo.SelectedIndex = trigger.Kind == FlowTriggerKind.Startup ? 1 : 0; TriggerKeyCombo.SelectedItem = trigger.Key;
            CtrlCheck.IsChecked = trigger.Modifiers.HasFlag(FlowModifiers.Ctrl); AltCheck.IsChecked = trigger.Modifiers.HasFlag(FlowModifiers.Alt);
            ShiftCheck.IsChecked = trigger.Modifiers.HasFlag(FlowModifiers.Shift); WinCheck.IsChecked = trigger.Modifiers.HasFlag(FlowModifiers.Win);
            ScopeCombo.SelectedIndex = trigger.Scope == FlowScopeKind.ActiveApplication ? 1 : 0; ApplicationBox.Text = trigger.Application;
            ApplicationBox.Visibility = ScopeCombo.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            HotkeyFields.Visibility = trigger.Kind == FlowTriggerKind.Hotkey ? Visibility.Visible : Visibility.Collapsed;
            return;
        }
        FolderCombo.Visibility = action.Kind == FlowActionKind.OpenFolder ? Visibility.Visible : Visibility.Collapsed; FolderCombo.SelectedIndex = (int)action.Folder;
        SendKeysCombo.Visibility = action.Kind == FlowActionKind.SendKeys ? Visibility.Visible : Visibility.Collapsed;
        SendKeysCombo.SelectedItem = action.Kind == FlowActionKind.SendKeys ? action.Value : null;
        WaitBox.Visibility = action.Kind == FlowActionKind.Wait ? Visibility.Visible : Visibility.Collapsed; WaitBox.Value = action.Kind == FlowActionKind.Wait ? action.DelayMs : 0;
        var hasText = action.Kind is FlowActionKind.OpenProgram or FlowActionKind.OpenWebsite or FlowActionKind.SendText
            || action.Kind == FlowActionKind.OpenFolder && action.Folder == FlowFolderKind.Custom;
        ActionValueBox.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
        ActionValueBox.Header = action.Kind switch
        { FlowActionKind.OpenProgram => "Program or file", FlowActionKind.OpenWebsite => "Website address", FlowActionKind.SendText => "Text to send", _ => "Folder path" };
        ActionValueBox.MaxLength = action.Kind == FlowActionKind.SendText ? 4096 : 2048;
        ActionValueBox.AcceptsReturn = action.Kind == FlowActionKind.SendText; ActionValueBox.MinHeight = action.Kind == FlowActionKind.SendText ? 120 : 32;
        ActionValueBox.Text = action.Value;
        ParameterHintText.Text = action.Kind switch
        {
            FlowActionKind.OpenProgram => "Use a full file path or an executable name, for example notepad.exe. Command arguments are not supported in this action.",
            FlowActionKind.OpenFolder => "The folder opens in File Explorer when the script runs.",
            FlowActionKind.OpenWebsite => "HTTP and HTTPS addresses open in your default browser.",
            FlowActionKind.SendText => "Sends literal text to the focused window. AHK expressions are treated as text.",
            FlowActionKind.SendKeys => "Sends this key combination to the focused window.",
            _ => "Pause before the next action. Use a whole number from 0 to 60000 ms."
        };
    }
}
