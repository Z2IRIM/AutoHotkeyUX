using AutoHotkeyUX.Modern.Models;
using AutoHotkeyUX.Modern.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AutoHotkeyUX.Modern.Pages;

public sealed partial class NewScriptPage
{
    /// <summary>Proves category, branch, source-picker and paired persistence behavior in the isolated native app.</summary>
    internal async Task<object> VerifyCapabilitiesAsync()
    {
        if (_libraryItems.Count != 21 || ActionLibrary.Children.OfType<Button>().Count() != 5)
            throw new InvalidOperationException("The categorized action library is incomplete.");
        _opened = null; _session.Load(VisualFlowExamples.Explorer()); _insertion = default; _branchSelection = null;
        _rendering = true; ScriptNameTextBox.Text = "Native practical flow"; ScriptLocationTextBox.Text = _services.Catalog.RootDirectory; _rendering = false;
        _baseline = _session.Document; _baselineName = ScriptNameTextBox.Text; _baselineDirectory = ScriptLocationTextBox.Text; RenderDocument();
        var branch = _cards.First(card => card.IsBranch && !card.Branch.IsElse);
        ActionList.SelectedItem = branch; await Task.Delay(60);
        var count = VisualFlowTree.Sequence(_session.Document.Actions, branch.Branch).Length;
        Library_Click(_libraryItems.Single(button => (FlowActionKind)button.Tag == FlowActionKind.Wait), new RoutedEventArgs());
        if (VisualFlowTree.Sequence(_session.Document.Actions, branch.Branch).Length != count + 1 || _session.Document.Actions.Length != 2)
            throw new InvalidOperationException("A native branch insertion went into the root sequence.");
        Up_Click(this, new RoutedEventArgs()); Undo_Click(this, new RoutedEventArgs());
        if (VisualFlowTree.Sequence(_session.Document.Actions, branch.Branch)[^1].Kind != FlowActionKind.Wait)
            throw new InvalidOperationException("Native nested reorder/undo failed.");
        RootInsertion_Click(this, new RoutedEventArgs()); AddAction(FlowActionKind.SetClipboard);
        var source = ExtendedFields.Children.OfType<ComboBox>().First();
        var option = source.Items.Cast<FlowSourceChoice>().Single(choice => choice.Input?.Kind == FlowInputKind.Result && choice.Input.Field == FlowResultField.Path);
        source.SelectedItem = option; await Task.Delay(60);
        var added = VisualFlowTree.Find(_session.Document.Actions, _session.Selection)!;
        if (added.Parameters?.Input != option.Input || !SaveButton.IsEnabled || ActionList.CanReorderItems)
            throw new InvalidOperationException("Native result selection or branch drag boundary failed.");
        RootInsertion_Click(this, new RoutedEventArgs()); AddAction(FlowActionKind.OpenProgram);
        var arguments = ExtendedFields.Children.OfType<ComboBox>().ElementAt(1);
        arguments.SelectedItem = arguments.Items.Cast<FlowSourceChoice>().Single(choice => choice.Input?.Kind == FlowInputKind.Result && choice.Input.Field == FlowResultField.Path);
        var working = ExtendedFields.Children.OfType<ComboBox>().Last();
        working.SelectedItem = working.Items.Cast<FlowSourceChoice>().Single(choice => choice.Input?.Kind == FlowInputKind.Result && choice.Input.Field == FlowResultField.Directory);
        await Task.Delay(60);
        var program = VisualFlowTree.Find(_session.Document.Actions, _session.Selection)!;
        if (program.Parameters?.ArgumentInput?.Field != FlowResultField.Path || program.Parameters.WorkingDirectory?.Field != FlowResultField.Directory || !SaveButton.IsEnabled)
            throw new InvalidOperationException("Native program argument/working-directory result pickers failed.");
        await SaveDraftAsync();
        if (_opened is null || _opened.Document.SchemaVersion != 3 || _services.Execution.Snapshot().Count != 0)
            throw new InvalidOperationException("Practical flow did not save or ran automatically.");
        var reopened = _services.VisualFlows.Open(_opened.ScriptPath); _session.Load(reopened.Document); _opened = reopened; RememberSaved(); RenderDocument();
        var condition = reopened.Document.Actions[1]; _session.Selection = condition.Id; RenderDocument();
        var conditions = ExtendedFields.Children.OfType<ComboBox>().Last(); conditions.SelectedItem = conditions.Items.Cast<FlowConditionChoice>().Single(choice => choice.Kind == FlowConditionKind.ExtensionEquals);
        var extension = ExtendedFields.Children.OfType<TextBox>().Last(); extension.Text = "tar.gz"; await Task.Delay(60);
        if (VisualFlowTree.Find(_session.Document.Actions, condition.Id)?.Parameters?.Comparison != "tar.gz")
            throw new InvalidOperationException("Native condition editing did not reach the draft.");
        await SaveDraftAsync(); _session.Selection = condition.Id; RenderDocument();
        var savedDocument = _session.Document;
        _session.Load(savedDocument with { Actions = [NestedCondition(3)] }); _insertion = default; _branchSelection = null; RenderDocument();
        var deepest = _cards.Last(card => card.IsBranch && !card.Branch.IsElse); ActionList.SelectedItem = deepest;
        var ifButton = _libraryItems.Single(button => (FlowActionKind)button.Tag == FlowActionKind.IfElse);
        if (ifButton.IsEnabled) throw new InvalidOperationException("Native editor allows a fourth nested condition.");
        var beforeDepth = VisualFlowTree.Walk(_session.Document.Actions).Count(); AddAction(FlowActionKind.IfElse);
        if (VisualFlowTree.Walk(_session.Document.Actions).Count() != beforeDepth) throw new InvalidOperationException("Over-depth insertion changed the native draft.");
        AddAction(FlowActionKind.ReadClipboard); Up_Click(this, new RoutedEventArgs()); Undo_Click(this, new RoutedEventArgs()); Redo_Click(this, new RoutedEventArgs());
        VisualFlowCodec.Validate(_session.Document);
        _session.Load(savedDocument); _session.Selection = condition.Id; _insertion = default; _branchSelection = null; RenderDocument();
        return new { Passed = true, Actions = 21, Categories = 4, BranchInsertion = true, ResultPicker = true, NestedUndo = true,
            ConditionEditor = true, ArgumentResult = true, WorkingDirectoryResult = true, DepthGuard = true, SavedSchema = 3, ScriptPath = _opened!.ScriptPath, ExecutionCount = _services.Execution.Snapshot().Count };
    }
    /// <summary>Provides a three-layer native fixture for the insertion boundary without executing it.</summary>
    private static FlowAction NestedCondition(int depth) => new() { Kind = FlowActionKind.IfElse, Parameters = new() { Input = new() { Literal = "x" }, Condition = FlowConditionKind.IsNotEmpty,
        Then = [depth == 1 ? new() { Kind = FlowActionKind.Wait, DelayMs = 1 } : NestedCondition(depth - 1)] } };
}
