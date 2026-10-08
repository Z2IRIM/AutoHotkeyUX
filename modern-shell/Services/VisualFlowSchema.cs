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
    internal static VisualFlowDocument Upgrade(VisualFlowDocument flow) => flow with { SchemaVersion = 3, Actions = UpgradeActions(flow.Actions) };

    /// <summary>Preserves older extraction destinations while adding immutable per-operation defaults.</summary>
    private static FlowAction[] UpgradeActions(FlowAction[] actions) => actions.Select(action =>
    {
        var p = action.Parameters;
        if (p is null && action.Kind <= FlowActionKind.Wait) return action;
        p ??= new();
        p = p with { Then = UpgradeActions(p.Then), Else = UpgradeActions(p.Else) };
        if (action.Kind == FlowActionKind.OpenTerminal) p = p with { Terminal = p.Terminal ?? new() };
        if (action.Kind == FlowActionKind.ExtractArchive) p = p with { Extraction = p.Extraction ?? new()
            { Mode = p.Destination is null ? FlowConfigurationMode.Inherit : FlowConfigurationMode.Custom,
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
