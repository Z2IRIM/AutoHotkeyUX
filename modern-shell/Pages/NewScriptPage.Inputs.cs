using AutoHotkeyUX.Modern.Models;
using AutoHotkeyUX.Modern.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AutoHotkeyUX.Modern.Pages;

public sealed partial class NewScriptPage
{
    /// <summary>Creates only the typed fields that belong to the currently selected action.</summary>
    private void RenderExtendedProperties(FlowAction action)
    {
        var accepts = action.Kind is FlowActionKind.OpenProgram or FlowActionKind.OpenWebsite or FlowActionKind.SendText or FlowActionKind.OpenTerminal
            or FlowActionKind.ExtractArchive or FlowActionKind.SetClipboard or FlowActionKind.WaitForWindow or FlowActionKind.ActivateWindow or FlowActionKind.IfElse
            || action.Kind == FlowActionKind.OpenFolder && action.Folder == FlowFolderKind.Custom;
        if (accepts) AddInputEditor(action, "Value source", action.Parameters?.Input, false, "Input", action.Value);
        if (action.Kind == FlowActionKind.OpenProgram)
        {
            AddInputEditor(action, "Arguments source", action.Parameters?.ArgumentInput, false, "Arguments", action.Parameters?.Arguments ?? "");
            AddInputEditor(action, "Working directory", action.Parameters?.WorkingDirectory, true, "WorkingDirectory");
        }
        if (action.Kind == FlowActionKind.ExtractArchive) AddInputEditor(action, "Destination", action.Parameters?.Destination, true, "Destination");
        if (action.Kind is FlowActionKind.WaitForWindow or FlowActionKind.ActivateWindow)
        {
            var timeout = new NumberBox { Header = "Timeout (ms)", Minimum = 1, Maximum = 60000, Value = action.Parameters!.TimeoutMs, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
            timeout.ValueChanged += (_, e) => ChangeAction(current => current with { Parameters = current.Parameters! with { TimeoutMs = double.IsFinite(e.NewValue) && e.NewValue == Math.Truncate(e.NewValue) ? (int)e.NewValue : -1 } });
            ExtendedFields.Children.Add(timeout);
        }
        if (action.Kind == FlowActionKind.IfElse)
        {
            var parameters = action.Parameters ?? throw new InvalidDataException("This condition has no parameters.");
            var conditions = new[] { FlowConditionKind.IsFolder, FlowConditionKind.IsFile, FlowConditionKind.IsArchive, FlowConditionKind.ExtensionEquals, FlowConditionKind.IsEmpty, FlowConditionKind.IsNotEmpty }
                .Select(kind => new FlowConditionChoice(FlowActionCard.ConditionLabel(kind), kind)).ToArray();
            var condition = new ComboBox { Header = "Condition", ItemsSource = conditions, DisplayMemberPath = "Label", SelectedItem = conditions.FirstOrDefault(choice => choice.Kind == parameters.Condition), HorizontalAlignment = HorizontalAlignment.Stretch };
            condition.SelectionChanged += (_, _) => { if (condition.SelectedItem is FlowConditionChoice choice) ChangeAction(current => current with { Parameters = current.Parameters! with { Condition = choice.Kind, Comparison = choice.Kind == FlowConditionKind.ExtensionEquals ? "zip" : "" } }, true); };
            ExtendedFields.Children.Add(condition);
            if (parameters.Condition == FlowConditionKind.ExtensionEquals)
            {
                var extension = new TextBox { Header = "Extension", Text = parameters.Comparison, PlaceholderText = "zip or tar.gz", MaxLength = 64 };
                extension.TextChanged += (_, _) => ChangeAction(current => current with { Parameters = current.Parameters! with { Comparison = extension.Text } });
                ExtendedFields.Children.Add(extension);
            }
        }
        var outputs = VisualFlowTree.Outputs(action.Kind);
        if (outputs.Length > 0) ExtendedFields.Children.Add(new TextBlock { Text = "Results: " + string.Join(", ", outputs), TextWrapping = TextWrapping.Wrap });
    }

    /// <summary>Offers fixed values or valid earlier output fields while retaining visibly broken references for repair.</summary>
    private void AddInputEditor(FlowAction action, string header, FlowInput? current, bool optional, string slot, string fallback = "")
    {
        var options = new List<FlowSourceChoice>();
        if (optional) options.Add(new(slot == "Destination" ? "Beside the archive" : "Program default", null));
        var literal = current?.Kind == FlowInputKind.Literal ? current : new FlowInput { Literal = fallback };
        options.Add(new("Fixed value", literal));
        foreach (var producer in VisualFlowTree.Available(_session.Document.Actions, action.Id))
            foreach (var field in VisualFlowTree.Outputs(producer.Kind).Where(field => slot == "Input" ? AllowsInput(action.Kind, field) : slot == "Arguments" ? field is not (FlowResultField.ProcessId or FlowResultField.WindowId) : field is FlowResultField.Path or FlowResultField.Directory or FlowResultField.Text))
                options.Add(new(FlowActionCard.StepLabel(_session.Document, producer.Id) + " → " + field, FlowInput.Reference(producer.Id, field)));
        var selected = optional && current is null ? options[0] : options.FirstOrDefault(choice => choice.Input == (current ?? literal));
        if (selected is null) { selected = new("Missing / unavailable result · choose again", current); options.Add(selected); }
        var source = new ComboBox { Header = header, ItemsSource = options, DisplayMemberPath = "Label", SelectedItem = selected, HorizontalAlignment = HorizontalAlignment.Stretch };
        source.SelectionChanged += (_, _) => { if (source.SelectedItem is FlowSourceChoice choice) SetInput(slot, choice.Input, true); };
        ExtendedFields.Children.Add(source);
        var effective = current ?? (!optional ? literal : null);
        // Existing literal TextBoxes retain their native events and v1 semantics.
        if (effective?.Kind == FlowInputKind.Literal && (slot != "Input" || action.Parameters?.Input is not null || action.Kind > FlowActionKind.Wait))
        {
            var text = new TextBox { Header = slot == "Arguments" ? "Arguments (optional command line)" : slot == "Input" ? "Value" : "Folder path", Text = effective.Literal, MaxLength = action.Kind is FlowActionKind.SendText or FlowActionKind.SetClipboard ? 4096 : 2048,
                TextWrapping = TextWrapping.Wrap, AcceptsReturn = action.Kind == FlowActionKind.SendText, MinHeight = action.Kind == FlowActionKind.SendText ? 100 : 32 };
            text.TextChanged += (_, _) => SetInput(slot, effective with { Literal = text.Text }, false);
            ExtendedFields.Children.Add(text);
        }
    }
    /// <summary>Writes one parameter source without exposing an expression field or disturbing unrelated branch data.</summary>
    private void SetInput(string slot, FlowInput? input, bool render) => ChangeAction(action =>
    {
        var p = action.Parameters ?? new();
        if (slot == "Arguments") return action with { Parameters = p with { Arguments = input?.Kind == FlowInputKind.Literal ? input.Literal : "", ArgumentInput = input?.Kind == FlowInputKind.Result ? input : null } };
        if (slot == "WorkingDirectory") return action with { Parameters = p with { WorkingDirectory = input } };
        if (slot == "Destination") return action with { Parameters = p with { Destination = input } };
        if (input?.Kind == FlowInputKind.Literal && action.Kind <= FlowActionKind.Wait)
            return action with { Value = input.Literal, Parameters = action.Parameters is null ? null : p with { Input = null } };
        return action with { Value = "", Parameters = p with { Input = input } };
    }, render);
    /// <summary>Filters available fields according to the receiving operation's path, text or window type.</summary>
    private static bool AllowsInput(FlowActionKind kind, FlowResultField field) => kind is FlowActionKind.WaitForWindow or FlowActionKind.ActivateWindow
        ? field is FlowResultField.ProcessId or FlowResultField.WindowId
        : kind is FlowActionKind.OpenFolder or FlowActionKind.OpenTerminal or FlowActionKind.ExtractArchive or FlowActionKind.OpenProgram
            ? field is FlowResultField.Path or FlowResultField.Directory or FlowResultField.Text
            : field is not (FlowResultField.ProcessId or FlowResultField.WindowId);
    /// <summary>Provides a native display label for a closed typed source option.</summary>
    private sealed record FlowSourceChoice(string Label, FlowInput? Input);
    /// <summary>Keeps human-readable condition labels separate from persisted enum values.</summary>
    private sealed record FlowConditionChoice(string Label, FlowConditionKind Kind);
}
