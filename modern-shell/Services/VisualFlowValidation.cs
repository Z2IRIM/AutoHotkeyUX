using AutoHotkeyUX.Modern.Models;
using System.Text.RegularExpressions;

namespace AutoHotkeyUX.Modern.Services;

/// <summary>Validates v2 parameter ownership and references before any source or file write.</summary>
internal static class VisualFlowValidation
{
    /// <summary>Checks closed parameters and branch scope independently of the editor's source picker.</summary>
    internal static void ValidateAction(VisualFlowDocument flow, FlowAction action)
    {
        var p = action.Parameters;
        if (p is null)
        {
            if (action.Kind > FlowActionKind.Wait) Fail("This action is missing its v2 parameters.");
            return;
        }
        if (p.Then is null || p.Else is null || p.Arguments is null || p.Comparison is null || !Enum.IsDefined(p.Condition)) Fail("Invalid action parameters.");
        if (action.Kind != FlowActionKind.IfElse && (p.Then!.Length != 0 || p.Else!.Length != 0 || p.Condition != FlowConditionKind.None || p.Comparison!.Length != 0)) Fail("Only If / Else can own branches and conditions.");
        if (action.Kind != FlowActionKind.OpenProgram && (p.Arguments!.Length != 0 || p.ArgumentInput is not null || p.WorkingDirectory is not null)) Fail("Only a program action can specify arguments or its working directory.");
        if (action.Kind != FlowActionKind.ExtractArchive && p.Destination is not null) Fail("Only extraction can specify a destination.");
        if (action.Kind is not (FlowActionKind.WaitForWindow or FlowActionKind.ActivateWindow) && p.TimeoutMs != 0) Fail("This action does not use a window timeout.");
        if (action.Kind is FlowActionKind.WaitForWindow or FlowActionKind.ActivateWindow && p.TimeoutMs is < 1 or > 60000) Fail("Window timeout must be 1 to 60000 ms.");
        if (p.Arguments!.Length > 2048 || p.Arguments.Any(char.IsControl)) Fail("Use at most 2048 argument characters without line breaks.");
        if (action.Kind is FlowActionKind.GetClickedObject or FlowActionKind.GetSelectedObject or FlowActionKind.GetCurrentDirectory or FlowActionKind.ReadClipboard or FlowActionKind.Wait or FlowActionKind.SendKeys)
        { if (p.Input is not null) Fail("This action does not accept an input source."); }
        else if (action.Kind > FlowActionKind.Wait && p.Input is null) Fail("Choose an input value or earlier result.");
        if (p.Input is not null && action.Value.Length != 0) Fail("A result/source action cannot also contain an old literal value.");
        if (action.Kind > FlowActionKind.Wait && (action.Value.Length != 0 || action.DelayMs != 0 || action.Folder != FlowFolderKind.Documents)) Fail("This action contains legacy parameters.");
        if (action.Kind == FlowActionKind.GetClickedObject && (flow.Trigger.Kind != FlowTriggerKind.Hotkey || !flow.Trigger.Key.EndsWith("Button", StringComparison.Ordinal))) Fail("Get clicked object needs a mouse shortcut trigger.");
        foreach (var input in new[] { p.Input, p.ArgumentInput, p.WorkingDirectory, p.Destination })
            if (input is not null) ValidateInput(flow, action, input);
        if (p.ArgumentInput is not null && (p.Arguments.Length != 0 || p.ArgumentInput.Kind != FlowInputKind.Result || p.ArgumentInput.Field is FlowResultField.ProcessId or FlowResultField.WindowId)) Fail("Choose a previous path/text as one program argument, or use a fixed command line.");
        if (p.WorkingDirectory is not null) ValidatePathInput(p.WorkingDirectory, "working directory");
        if (p.Destination is not null) ValidatePathInput(p.Destination, "extraction destination");
        if (p.Input is { } target)
        {
            if (action.Kind is FlowActionKind.OpenFolder or FlowActionKind.OpenTerminal or FlowActionKind.ExtractArchive) ValidatePathInput(target, "path");
            if (action.Kind == FlowActionKind.OpenProgram && target.Kind == FlowInputKind.Literal && !VisualFlowCodec.IsPath(target.Literal)
                && !Regex.IsMatch(target.Literal, "^[a-zA-Z0-9_.-]+\\.exe$", RegexOptions.CultureInvariant)) Fail("Use an absolute program path or executable name.");
            if (action.Kind == FlowActionKind.OpenWebsite && target.Kind == FlowInputKind.Literal && (!Uri.TryCreate(target.Literal, UriKind.Absolute, out var url)
                || url.Scheme is not ("http" or "https") || url.Host.Length == 0 || target.Literal.Any(char.IsWhiteSpace))) Fail("Use an HTTP or HTTPS website address.");
            if (action.Kind == FlowActionKind.SendText && target.Kind == FlowInputKind.Literal && target.Literal.Length == 0) Fail("Enter text to send.");
            if (action.Kind is FlowActionKind.WaitForWindow or FlowActionKind.ActivateWindow)
            {
                if (target.Kind == FlowInputKind.Literal && !Regex.IsMatch(target.Literal, "^[a-zA-Z0-9_ .-]+\\.exe$", RegexOptions.CultureInvariant)) Fail("Use an executable name or a previous process/window ID.");
                if (target.Kind == FlowInputKind.Result && target.Field is not (FlowResultField.ProcessId or FlowResultField.WindowId)) Fail("Choose a process ID or window ID result.");
            }
            else if (target.Kind == FlowInputKind.Result && target.Field is FlowResultField.ProcessId or FlowResultField.WindowId) Fail("A path or text action cannot use a process/window ID.");
        }
        if (action.Kind == FlowActionKind.IfElse && (p.Condition == FlowConditionKind.None || p.Comparison!.Length > 64
            || (p.Condition != FlowConditionKind.ExtensionEquals && p.Comparison.Length != 0)
            || (p.Condition == FlowConditionKind.ExtensionEquals && !Regex.IsMatch(p.Comparison, "^\\.?[a-zA-Z0-9]+(?:\\.[a-zA-Z0-9]+)*$", RegexOptions.CultureInvariant)))) Fail("Choose a condition and a valid extension when applicable.");
    }
    /// <summary>Rejects unknown fields and values outside the previous-step visibility boundary.</summary>
    private static void ValidateInput(VisualFlowDocument flow, FlowAction action, FlowInput input)
    {
        if (!Enum.IsDefined(input.Kind) || !Enum.IsDefined(input.Field) || input.Literal is null || input.Literal.Contains('\0') || input.Literal.Length > 4096) Fail("Invalid input source.");
        _ = VisualFlowCodec.Utf8.GetByteCount(input.Literal!);
        if (input.Kind == FlowInputKind.Literal)
        { if (input.StepId != Guid.Empty || input.Field != FlowResultField.Path) Fail("Fixed values cannot carry a result reference."); return; }
        var producer = VisualFlowTree.Available(flow.Actions, action.Id).FirstOrDefault(step => step.Id == input.StepId);
        if (input.Literal!.Length != 0 || producer is null || !VisualFlowTree.Outputs(producer.Kind).Contains(input.Field)) Fail("This result is missing, later, or outside this branch. Choose an earlier result.");
    }
    /// <summary>Bounds recursive conditions without requiring a nonempty branch.</summary>
    internal static void ValidateTree(FlowAction[] actions, int conditions = 0)
    {
        foreach (var action in actions)
        {
            if (action.Kind != FlowActionKind.IfElse) continue;
            if (conditions >= 3) Fail("Use at most three nested If / Else conditions.");
            var parameters = action.Parameters ?? throw new InvalidDataException("This condition is missing its parameters.");
            ValidateTree(parameters.Then, conditions + 1); ValidateTree(parameters.Else, conditions + 1);
        }
    }
    /// <summary>Checks fixed absolute paths and limits reference fields to path-bearing outputs.</summary>
    private static void ValidatePathInput(FlowInput input, string label)
    {
        if (input.Kind == FlowInputKind.Literal ? !VisualFlowCodec.IsPath(input.Literal) : input.Field is not (FlowResultField.Path or FlowResultField.Directory or FlowResultField.Text)) Fail("Choose an absolute " + label + " or an earlier path result.");
    }
    /// <summary>Uses one recognizable validation exception at every v2 language boundary.</summary>
    private static void Fail(string message) => throw new InvalidDataException(message);
}
