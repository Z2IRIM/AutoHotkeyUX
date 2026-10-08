using AutoHotkeyUX.Modern.Models;
using System.Diagnostics.CodeAnalysis;

namespace AutoHotkeyUX.Modern.Services;

/// <summary>Checks typed v3 operation ownership, bounds and composed result visibility.</summary>
internal static class VisualFlowValidationV3
{
    /// <summary>Rejects irrelevant options and invalid literals before generating or writing code.</summary>
    internal static void Validate(VisualFlowDocument flow, FlowAction action)
    {
        if (action.DisplayName is { } name && (name.Length is < 1 or > 80 || name.Any(char.IsControl))) Fail("Use an action name of 1 to 80 characters.");
        var p = action.Parameters;
        if (p is null) { if (action.Kind > FlowActionKind.Wait) Fail("This action is missing its parameters."); return; }
        if (p.Then is null || p.Else is null) Fail("Invalid branch parameters.");
        if (p.Terminal is not null && action.Kind != FlowActionKind.OpenTerminal
            || p.Extraction is not null && action.Kind != FlowActionKind.ExtractArchive
            || p.Context is not null && action.Kind is not (FlowActionKind.GetClickedObject or FlowActionKind.GetSelectedObject or FlowActionKind.GetCurrentDirectory or FlowActionKind.GetPathProperties)
            || p.JoinPath is not null && action.Kind != FlowActionKind.JoinPath
            || p.Directory is not null && action.Kind != FlowActionKind.CreateDirectory
            || p.Stop is not null && action.Kind != FlowActionKind.StopWorkflow
            || p.Notification is not null && action.Kind != FlowActionKind.Notify) Fail("An action contains options belonging to another action.");
        if (p.Context is { } context && !Enum.IsDefined(context.Missing)) Fail("Choose a supported missing-target policy.");
        if (action.Kind <= FlowActionKind.IfElse)
        {
            VisualFlowValidation.ValidateAction(flow, action with { Parameters = p with
            { Terminal = null, Extraction = null, Context = null, JoinPath = null, Directory = null, Stop = null, Notification = null, Failure = null } }, legacyRules: true);
        }
        else
        {
            if (action.Value.Length != 0 || action.DelayMs != 0 || action.Folder != FlowFolderKind.Documents
                || p.Then!.Length != 0 || p.Else!.Length != 0 || p.Condition != FlowConditionKind.None || p.Comparison != ""
                || p.Arguments != "" || p.ArgumentInput is not null || p.WorkingDirectory is not null || p.Destination is not null || p.TimeoutMs != 0)
                Fail("A v3 action contains unrelated legacy parameters.");
            if (action.Kind is FlowActionKind.GetPathProperties or FlowActionKind.JoinPath or FlowActionKind.CreateDirectory)
            {
                if (p.Input is null) Fail("Choose an absolute path or earlier path result.");
                VisualFlowValidation.ValidatePathInput(p.Input!, "path");
            }
            else if (p.Input is not null) Fail("This action uses composed text or stop options instead of a path input.");
        }
        foreach (var input in VisualFlowSchema.Inputs(action)) VisualFlowValidation.ValidateInput(flow, action, input);
        if (p.Input is { Kind: FlowInputKind.Result } target && target.Field is FlowResultField.Exists or FlowResultField.Success or FlowResultField.MouseX or FlowResultField.MouseY)
            Fail("Choose a path or text input rather than a boolean or coordinate result.");
        if (p.Terminal is { } terminal && (!Enum.IsDefined(terminal.Mode) || !Enum.IsDefined(terminal.Program) || !Enum.IsDefined(terminal.Position)
            || terminal.Width is < 320 or > 2400 || terminal.Height is < 200 or > 1600 || terminal.Gap is < 0 or > 100 || terminal.TimeoutMs is < 500 or > 10000))
            Fail("Use a supported terminal program/position, width 320–2400, height 200–1600, gap 0–100 DIP and timeout 500–10000 ms.");
        if (p.Extraction is { } archive)
        {
            if (!Enum.IsDefined(archive.Mode) || !Enum.IsDefined(archive.Destination) || !Enum.IsDefined(archive.Naming) || !Enum.IsDefined(archive.Collision)) Fail("Choose supported extraction options.");
            ValidateExpression(archive.Name, false, "archive folder name");
            if (archive.Mode == FlowConfigurationMode.Custom && (archive.Destination == FlowArchiveDestination.Custom) != (p.Destination is not null)) Fail("Choose a custom destination source, or use the archive parent folder.");
            if (archive.Naming == FlowArchiveNaming.ArchiveName && archive.Name.Parts.Length > 0) Fail("Archive-name mode cannot carry a custom name expression.");
            if (archive.Naming == FlowArchiveNaming.Composition && archive.Name.Parts.Length == 0) Fail("Add at least one name part.");
            foreach (var input in archive.Name.Parts)
                if (input.Kind == FlowInputKind.Literal && (input.Literal.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || input.Literal.Any(char.IsControl))) Fail("Folder-name parts cannot contain separators or reserved characters.");
        }
        if (p.JoinPath is { } join)
        {
            if (join.Segments is null || join.Segments.Length is < 1 or > 8) Fail("Add one to eight relative path segments.");
            foreach (var segment in join.Segments)
            {
                ValidateTextInput(segment, "path segment");
                if (segment.Kind == FlowInputKind.Literal && (string.IsNullOrWhiteSpace(segment.Literal) || Path.IsPathRooted(segment.Literal)
                    || segment.Literal.Split(['\\', '/']).Any(part => part is "" or "." or ".."))) Fail("Use relative path segments without traversal.");
            }
        }
        if (action.Kind == FlowActionKind.JoinPath && p.JoinPath is null) Fail("Add path segments.");
        if (action.Kind == FlowActionKind.Notify)
        {
            if (p.Notification is not { } notification) Fail("Add notification title and message.");
            else { ValidateExpression(notification.Title, true, "notification title"); ValidateExpression(notification.Message, true, "notification message"); }
        }
        if (action.Kind == FlowActionKind.StopWorkflow && (p.Stop is not { } stop || stop.Reason is null || stop.Reason.Length > 2048 || stop.Reason.Any(char.IsControl))) Fail("Enter a bounded stop reason without line breaks.");
    }

    /// <summary>Bounds text parts and excludes identity/boolean fields from ordinary text composition.</summary>
    private static void ValidateExpression(FlowTextExpression? value, bool required, string label)
    {
        if (value?.Parts is null || value.Parts.Length > 8 || required && value.Parts.Length == 0) Fail("Add one to eight " + label + " parts.");
        foreach (var input in value!.Parts) ValidateTextInput(input, label);
        if (value.Parts.Sum(input => input.Literal.Length) > 4096) Fail("The " + label + " is too long.");
    }

    /// <summary>Keeps composed data literal and human-readable rather than accepting code expressions.</summary>
    private static void ValidateTextInput(FlowInput? value, string label)
    {
        if (value is null || value.Literal is null || value.Literal.Length > 2048 || value.Literal.Contains('\0')
            || value.Kind == FlowInputKind.Result && value.Field is FlowResultField.ProcessId or FlowResultField.WindowId or FlowResultField.Exists or FlowResultField.Success or FlowResultField.MouseX or FlowResultField.MouseY)
            Fail("Choose literal text or an earlier text/path field for " + label + ".");
    }

    /// <summary>Uses the workflow validation exception for all operation contract failures.</summary>
    [DoesNotReturn] private static void Fail(string message) => throw new InvalidDataException(message);
}
