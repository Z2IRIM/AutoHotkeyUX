using AutoHotkeyUX.Modern.Models;

namespace AutoHotkeyUX.Modern.Services;

/// <summary>Shares recursive sequence editing and result visibility between validation, history and UI.</summary>
internal static class VisualFlowTree
{
    /// <summary>Enumerates every action in display order with its containing branch and indentation.</summary>
    internal static IEnumerable<(FlowAction Action, FlowBranch Branch, int Depth)> Walk(FlowAction[] actions, FlowBranch branch = default, int depth = 0)
    {
        if (depth > 4 || actions is null) throw new InvalidDataException("Invalid or excessively nested workflow branches.");
        foreach (var action in actions)
        {
            if (action is null) throw new InvalidDataException("The workflow contains an empty action.");
            yield return (action, branch, depth);
            if (action.Parameters is not { } p) continue;
            foreach (var child in Walk(p.Then, new(action.Id), depth + 1)) yield return child;
            foreach (var child in Walk(p.Else, new(action.Id, true), depth + 1)) yield return child;
        }
    }
    /// <summary>Finds a stable identity without confusing its location with its visible row index.</summary>
    internal static FlowAction? Find(FlowAction[] actions, Guid? id) => Walk(actions).FirstOrDefault(item => item.Action.Id == id).Action;
    /// <summary>Bounds insertion before a draft or native card projection can enter unsupported branch depth.</summary>
    internal static bool CanInsert(FlowAction[] actions, FlowBranch branch, FlowActionKind kind)
    {
        var rows = Walk(actions).ToArray();
        if (rows.Length >= VisualFlowCodec.MaximumActions) return false;
        if (branch.ParentId is null) return true;
        var parent = rows.FirstOrDefault(item => item.Action.Id == branch.ParentId);
        return parent.Action?.Kind == FlowActionKind.IfElse && (kind != FlowActionKind.IfElse || parent.Depth + 1 < 3);
    }
    /// <summary>Retrieves a branch sequence, returning empty for a removed branch.</summary>
    internal static FlowAction[] Sequence(FlowAction[] actions, FlowBranch branch) => branch.ParentId is null ? actions
        : Find(actions, branch.ParentId)?.Parameters is { } p ? branch.IsElse ? p.Else : p.Then : [];
    /// <summary>Replaces exactly one sequence while preserving all other immutable branch nodes.</summary>
    internal static FlowAction[] SetSequence(FlowAction[] actions, FlowBranch branch, FlowAction[] replacement) => branch.ParentId is null ? replacement
        : Update(actions, branch.ParentId.Value, action => action with { Parameters = branch.IsElse ? action.Parameters! with { Else = replacement } : action.Parameters! with { Then = replacement } });
    /// <summary>Transforms a single selected node without changing its identity or siblings.</summary>
    internal static FlowAction[] Update(FlowAction[] actions, Guid id, Func<FlowAction, FlowAction> update) => actions.Select(action => action.Id == id ? update(action)
        : action.Parameters is not { } p ? action : action with { Parameters = p with { Then = Update(p.Then, id, update), Else = Update(p.Else, id, update) } }).ToArray();
    /// <summary>Compares nested semantic records for dirty-state and undo boundaries.</summary>
    internal static bool Same(FlowAction[] left, FlowAction[] right) => left.Length == right.Length && left.Zip(right).All(pair =>
        (pair.First with { Parameters = null }) == (pair.Second with { Parameters = null }) && SameParameters(pair.First.Parameters, pair.Second.Parameters));
    /// <summary>Ignores replacement array identities while checking every parameter and both branch sequences.</summary>
    private static bool SameParameters(FlowParameters? left, FlowParameters? right) => left is null || right is null ? left == right
        : left.Input == right.Input && left.Arguments == right.Arguments && left.ArgumentInput == right.ArgumentInput && left.WorkingDirectory == right.WorkingDirectory && left.Destination == right.Destination
          && left.TimeoutMs == right.TimeoutMs && left.Condition == right.Condition && left.Comparison == right.Comparison && Same(left.Then, right.Then) && Same(left.Else, right.Else);
    /// <summary>Copies arrays recursively so retained history cannot be mutated by a native collection.</summary>
    internal static FlowAction[] Copy(FlowAction[] actions) => actions.Select(action => action.Parameters is not { } p ? action
        : action with { Parameters = p with { Then = Copy(p.Then), Else = Copy(p.Else) } }).ToArray();
    /// <summary>Lists only earlier outputs definitely available on the selected action's execution path.</summary>
    internal static IReadOnlyList<FlowAction> Available(FlowAction[] actions, Guid id) => FindAvailable(actions, id, []) ?? [];
    /// <summary>Carries ancestor inputs into each branch and discards branch-only outputs at the merge.</summary>
    private static List<FlowAction>? FindAvailable(FlowAction[] actions, Guid id, List<FlowAction> known)
    {
        var local = new List<FlowAction>(known);
        foreach (var action in actions)
        {
            if (action.Id == id) return local;
            if (action.Parameters is { } p)
            {
                var found = FindAvailable(p.Then, id, local) ?? FindAvailable(p.Else, id, local);
                if (found is not null) return found;
            }
            if (Outputs(action.Kind).Length > 0) local.Add(action);
        }
        return null;
    }
    /// <summary>Defines the closed output vocabulary shared by validation and dropdowns.</summary>
    internal static FlowResultField[] Outputs(FlowActionKind kind) => kind switch
    {
        FlowActionKind.GetClickedObject or FlowActionKind.GetSelectedObject or FlowActionKind.GetCurrentDirectory =>
            [FlowResultField.Path, FlowResultField.Directory, FlowResultField.Name, FlowResultField.Extension],
        FlowActionKind.ReadClipboard => [FlowResultField.Text], FlowActionKind.OpenProgram => [FlowResultField.ProcessId],
        FlowActionKind.WaitForWindow => [FlowResultField.WindowId], FlowActionKind.ExtractArchive => [FlowResultField.Path, FlowResultField.Directory], _ => []
    };
}
