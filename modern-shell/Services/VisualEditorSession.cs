using AutoHotkeyUX.Modern.Models;

namespace AutoHotkeyUX.Modern.Services;

/// <summary>Keeps draft edits and bounded history independent of page navigation and generated-file revisions.</summary>
internal sealed class VisualEditorSession
{
    private readonly List<VisualFlowDocument> _undo = [];
    private readonly List<VisualFlowDocument> _redo = [];
    internal VisualFlowDocument Document { get; private set; } = DefaultDocument();
    internal Guid? Selection { get; set; }
    internal bool CanUndo => _undo.Count > 0;
    internal bool CanRedo => _redo.Count > 0;

    /// <summary>Creates the approved quick-actions starter without writing or running it.</summary>
    internal static VisualFlowDocument DefaultDocument() => new() { Actions =
        [new() { Kind = FlowActionKind.OpenFolder }, new() { Kind = FlowActionKind.Wait, DelayMs = 500 }] };

    /// <summary>Loads a new editing identity and clears history so undo cannot cross into another file.</summary>
    internal void Load(VisualFlowDocument value)
    { Document = Copy(value); _undo.Clear(); _redo.Clear(); Selection = null; }

    /// <summary>Records one semantic edit, including temporarily invalid form input, for later validation.</summary>
    internal void Replace(VisualFlowDocument value)
    {
        if (Same(value, Document)) return;
        _undo.Add(Copy(Document));
        if (_undo.Count > 50) _undo.RemoveAt(0);
        _redo.Clear(); Document = Copy(value);
        RestoreSelection();
    }

    /// <summary>Moves a stable action identity to a final zero-based index, leaving the trigger outside this list.</summary>
    internal void Move(Guid id, int target)
    {
        var actions = Document.Actions.ToList();
        var from = actions.FindIndex(action => action.Id == id);
        if (from < 0 || target < 0 || target >= actions.Count || from == target) return;
        var action = actions[from]; actions.RemoveAt(from); actions.Insert(target, action);
        Replace(Document with { Actions = actions.ToArray() }); Selection = id;
    }

    /// <summary>Restores the most recent draft without writing either file.</summary>
    internal void Undo()
    { if (!CanUndo) return; _redo.Add(Copy(Document)); Document = _undo[^1]; _undo.RemoveAt(_undo.Count - 1); RestoreSelection(); }

    /// <summary>Reapplies an undone edit until a new edit replaces the redo branch.</summary>
    internal void Redo()
    { if (!CanRedo) return; _undo.Add(Copy(Document)); Document = _redo[^1]; _redo.RemoveAt(_redo.Count - 1); RestoreSelection(); }

    /// <summary>Falls back to the trigger if a selected action was removed or undone.</summary>
    private void RestoreSelection() { if (Selection.HasValue && !Document.Actions.Any(action => action.Id == Selection)) Selection = null; }
    /// <summary>Copies mutable array storage at the history boundary; individual records are immutable.</summary>
    private static VisualFlowDocument Copy(VisualFlowDocument value) => value with { Actions = [.. value.Actions] };
    /// <summary>Compares semantic records instead of treating a replacement array as a new edit.</summary>
    private static bool Same(VisualFlowDocument a, VisualFlowDocument b) => a.Id == b.Id && a.SchemaVersion == b.SchemaVersion
        && a.Revision == b.Revision && a.SourceSha256 == b.SourceSha256 && a.Trigger == b.Trigger && a.Actions.SequenceEqual(b.Actions);
}
