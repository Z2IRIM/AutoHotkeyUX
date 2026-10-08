using AutoHotkeyUX.Modern.Models;

namespace AutoHotkeyUX.Modern.Services;

/// <summary>Owns version boundaries, typed source enumeration and explicit v3 upgrades.</summary>
internal static class VisualFlowSchema
{
    /// <summary>Enumerates every dynamic source, including composed names and notification content.</summary>
    internal static IEnumerable<FlowInput> Inputs(FlowAction action)
    {
        if (action.Parameters is not { } p) yield break;
        foreach (var input in new[] { p.Input, p.ArgumentInput, p.WorkingDirectory, p.Destination })
            if (input is not null) yield return input;
        foreach (var input in (p.Extraction?.Name?.Parts ?? []).Concat(p.JoinPath?.Segments ?? [])
            .Concat(p.Notification?.Title?.Parts ?? []).Concat(p.Notification?.Message?.Parts ?? []))
            yield return input ?? throw new InvalidDataException("Empty composed input.");
    }

    /// <summary>Detects semantics that cannot be silently written as an older workflow version.</summary>
    internal static bool NeedsV3(FlowAction action) => action.Kind > FlowActionKind.IfElse || action.DisplayName is not null
        || action.Parameters is { } p && (p.Terminal is not null || p.Extraction is not null || p.Context is not null
            || p.JoinPath is not null || p.Directory is not null || p.Stop is not null || p.Notification is not null || p.Failure is not null
            || p.Condition > FlowConditionKind.IsNotEmpty || Inputs(action).Any(input => input.Field > FlowResultField.WindowId));

    /// <summary>Adds operation defaults only when an editing action explicitly introduces v3 semantics.</summary>
    internal static VisualFlowDocument Upgrade(VisualFlowDocument flow) => flow with { SchemaVersion = 3, Actions = UpgradeActions(flow.Actions, flow.SchemaVersion < 3) };

    /// <summary>Transforms sources in the same stable order used by portable preset slots.</summary>
    internal static FlowAction MapInputs(FlowAction action, Func<FlowInput, FlowInput> map)
    {
        if (action.Parameters is not { } p) return action;
        FlowInput? Map(FlowInput? input) => input is null ? null : map(input);
        var input = Map(p.Input); var argument = Map(p.ArgumentInput); var working = Map(p.WorkingDirectory); var destination = Map(p.Destination);
        var extraction = p.Extraction is { } archive ? archive with { Name = archive.Name with { Parts = archive.Name.Parts.Select(map).ToArray() } } : null;
        var join = p.JoinPath is { } path ? path with { Segments = path.Segments.Select(map).ToArray() } : null;
        var notification = p.Notification is { } notice ? notice with { Title = notice.Title with { Parts = notice.Title.Parts.Select(map).ToArray() },
            Message = notice.Message with { Parts = notice.Message.Parts.Select(map).ToArray() } } : null;
        return action with { Parameters = p with { Input = input, ArgumentInput = argument, WorkingDirectory = working, Destination = destination,
            Extraction = extraction, JoinPath = join, Notification = notification } };
    }

    /// <summary>Preserves older extraction destinations while adding immutable per-operation defaults.</summary>
    private static FlowAction[] UpgradeActions(FlowAction[] actions, bool legacy) => actions.Select(action =>
    {
        var p = action.Parameters;
        if (p is null && action.Kind <= FlowActionKind.Wait && !legacy) return action;
        p ??= new();
        p = p with { Then = UpgradeActions(p.Then, legacy), Else = UpgradeActions(p.Else, legacy) };
        if (legacy) p = p with { Failure = p.Failure ?? new() { Notify = true } };
        if (action.Kind is FlowActionKind.GetClickedObject or FlowActionKind.GetSelectedObject or FlowActionKind.GetCurrentDirectory)
            p = p with { Context = p.Context ?? new() { Missing = legacy ? FlowMissingTarget.Error : FlowMissingTarget.StopSilently } };
        if (action.Kind == FlowActionKind.OpenTerminal) p = p with { Terminal = p.Terminal ?? new() { WaitReady = !legacy } };
        if (action.Kind == FlowActionKind.ExtractArchive) p = p with { Extraction = p.Extraction ?? new()
            { Mode = legacy || p.Destination is not null ? FlowConfigurationMode.Custom : FlowConfigurationMode.Inherit,
                Destination = p.Destination is null ? FlowArchiveDestination.BesideArchive : FlowArchiveDestination.Custom } };
        return action with { Parameters = p };
    }).ToArray();

    /// <summary>Shares human-readable output field names across source pickers and results.</summary>
    internal static string FieldLabel(FlowResultField field) => field switch
    {
        FlowResultField.Directory => "Usable directory", FlowResultField.ParentDirectory => "Parent directory",
        FlowResultField.BaseName => "Base name", FlowResultField.TargetKind => "Target type",
        FlowResultField.ProcessId => "Process ID", FlowResultField.WindowId => "Owned window ID",
        FlowResultField.MouseX => "Trigger X", FlowResultField.MouseY => "Trigger Y", _ => field.ToString()
    };
}
