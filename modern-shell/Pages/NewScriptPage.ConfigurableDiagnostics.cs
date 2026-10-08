using AutoHotkeyUX.Modern.Models;
using AutoHotkeyUX.Modern.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;

namespace AutoHotkeyUX.Modern.Pages;

public sealed partial class NewScriptPage
{
    /// <summary>Builds the target dispatcher from a blank draft using real library, branch and property-control events.</summary>
    internal async Task<object> VerifyConfigurableActionsAsync()
    {
        _opened = null; _insertion = default; _branchSelection = null; _session.Load(VisualFlowExamples.Blank());
        _rendering = true; ScriptNameTextBox.Text = "Visual Explorer actions"; ScriptLocationTextBox.Text = _services.Catalog.RootDirectory; _rendering = false;
        RenderDocument(); if (SaveButton.IsEnabled) throw new InvalidOperationException("A blank draft must require actions.");
        CtrlCheck.IsChecked = false; AltCheck.IsChecked = true; Modifiers_Click(this, new RoutedEventArgs());
        TriggerKeyCombo.SelectedItem = "Left mouse button"; ScopeCombo.SelectedIndex = 2;
        AddAction(FlowActionKind.GetClickedObject); var target = _session.Selection!.Value;
        AddAction(FlowActionKind.IfElse); var folderCondition = _session.Selection!.Value;
        SelectDiagnosticBranch(folderCondition, false); AddAction(FlowActionKind.OpenTerminal); var terminal = _session.Selection!.Value;
        SelectDiagnosticOption("Configuration", FlowConfigurationMode.Custom);
        SelectDiagnosticOption("Program", FlowTerminalProgram.WindowsPowerShell);
        SelectDiagnosticOption("Position", FlowTerminalPosition.Above);
        ExtendedFields.Children.OfType<NumberBox>().Single(box => (string)box.Header == "Width (DIP)").Value = 920;
        ExtendedFields.Children.OfType<NumberBox>().Single(box => (string)box.Header == "Height (DIP)").Value = 460;
        ExtendedFields.Children.OfType<NumberBox>().Single(box => (string)box.Header == "Cursor gap (DIP)").Value = 16;
        var configured = VisualFlowTree.Find(_session.Document.Actions, terminal)!;
        if (configured.Parameters?.Input?.Field != FlowResultField.Directory || configured.Parameters.Terminal is not { Width: 920, Height: 460, Gap: 16 })
            throw new InvalidOperationException("The terminal form did not retain its source and independent geometry.");
        SelectDiagnosticBranch(folderCondition, true); AddAction(FlowActionKind.IfElse); var archiveCondition = _session.Selection!.Value;
        var condition = ExtendedFields.Children.OfType<ComboBox>().Single(box => Equals(box.Header, "Condition"));
        condition.SelectedItem = condition.Items.Cast<FlowConditionChoice>().Single(choice => choice.Kind == FlowConditionKind.IsArchive);
        SelectDiagnosticBranch(archiveCondition, false); AddAction(FlowActionKind.ExtractArchive); var extraction = _session.Selection!.Value;
        SelectDiagnosticOption("Configuration", FlowConfigurationMode.Custom);
        SelectDiagnosticOption("Subfolder name", FlowArchiveNaming.Composition);
        var nameSource = ExtendedFields.Children.OfType<ComboBox>().Single(box => Equals(box.Header, "Part 1"));
        nameSource.SelectedItem = nameSource.Items.Cast<FlowSourceChoice>().Single(choice => choice.Input?.StepId == target && choice.Input.Field == FlowResultField.BaseName);
        var addPart = ExtendedFields.Children.OfType<Button>().Single(button => Equals(button.Content, "Add part"));
        ((IInvokeProvider)new ButtonAutomationPeer(addPart)).Invoke(); await Task.Delay(60);
        ExtendedFields.Children.OfType<TextBox>().Single(box => Equals(box.Header, "Text")).Text = " unpacked";
        AddAction(FlowActionKind.OpenFolder);
        if (VisualFlowTree.Find(_session.Document.Actions, _session.Selection)?.Parameters?.Input != FlowInput.Reference(extraction, FlowResultField.Directory))
            throw new InvalidOperationException("Open folder did not bind the committed extraction directory.");
        SelectDiagnosticBranch(archiveCondition, true); AddAction(FlowActionKind.StopWorkflow);
        if (!SaveButton.IsEnabled) throw new InvalidOperationException("The from-scratch flow is invalid: " + FieldErrorsText.Text);
        await SaveDraftAsync();
        if (_opened is null || _services.Execution.Snapshot().Count != 0) throw new InvalidOperationException("Visual save failed or unexpectedly started an interpreter.");
        var reopened = _services.VisualFlows.Open(_opened.ScriptPath);
        if (!VisualFlowTree.Same(reopened.Document.Actions, _session.Document.Actions) || reopened.Document.SchemaVersion != 3)
            throw new InvalidOperationException("The configurable file pair did not reopen with identical settings.");
        _session.Load(reopened.Document); _opened = reopened; RememberSaved(); _session.Selection = terminal; RenderDocument();
        var preset = VisualActionPresets.Capture(reopened.Document, terminal, "Cursor terminal");
        await Task.Run(() => _services.ActionPresets.Save(preset)); RefreshPresetMenu();
        if (_presetFlyout?.Items.OfType<MenuFlyoutItem>().All(item => item.Text != "Cursor terminal") != false)
            throw new InvalidOperationException("The saved action is absent from the native My actions menu.");
        return new { Passed = true, BlankToDispatcher = true, TypedSources = true, IndependentGeometry = true, ComposedExtractionName = true,
            BranchStop = true, PairedReopen = true, MyActionsMenu = true, ScriptPath = reopened.ScriptPath, FlowId = reopened.Document.Id,
            ExecutionCount = _services.Execution.Snapshot().Count, PhysicalPointerAndPresetDialog = "unvalidated" };
    }

    /// <summary>Selects an actual rendered branch entrance before invoking the next library action.</summary>
    private void SelectDiagnosticBranch(Guid parent, bool otherwise)
    { ActionList.SelectedItem = _cards.Single(card => card.IsBranch && card.Branch.ParentId == parent && card.Branch.IsElse == otherwise); }

    /// <summary>Uses the exact native option picker so property events and rerendering are exercised.</summary>
    private void SelectDiagnosticOption<T>(string header, T value) where T : struct, Enum
    {
        var box = ExtendedFields.Children.OfType<ComboBox>().Single(box => Equals(box.Header, header));
        box.SelectedItem = box.Items.Cast<OptionChoice<T>>().Single(choice => choice.Value.Equals(value));
    }
}
