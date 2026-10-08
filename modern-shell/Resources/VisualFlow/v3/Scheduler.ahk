; Owns a bounded invocation queue; no timer is installed while idle.
FlowQueueState() {
    static state := {Pending: [], Busy: false}
    return state
}

; Captures targets before queueing so later selection or pointer changes cannot redirect a job.
FlowDispatch(button, modes) {
    context := FlowCapture(button)
    if !context
        return
    context.RunId := FlowGuid(), context.Started := A_TickCount
    context.StepId := "00000000-0000-0000-0000-000000000000", context.StepName := "Trigger"
    context.NotifyErrors := false, context.Captured := Map()
    for mode in modes {
        try context.Captured[mode] := {Value: FlowResolveTarget(mode, context), Error: ""}
        catch Error as failure
            context.Captured[mode] := {Value: false, Error: failure.Message}
    }
    Critical "On"
    try {
        queue := FlowQueueState()
        if queue.Pending.Length >= 4 {
            FlowEvent(context, context.StepId, context.StepName, "failed", "The workflow queue is full.")
            return
        }
        queue.Pending.Push(context)
        if !queue.Busy
            SetTimer FlowDrain, -15
    }
    finally Critical "Off"
}

; Processes one invocation at a time while allowing the hotkey to capture later requests.
FlowDrain() {
    Critical "On"
    queue := FlowQueueState()
    if queue.Busy || !queue.Pending.Length {
        Critical "Off"
        return
    }
    queue.Busy := true, context := queue.Pending.RemoveAt(1)
    Critical "Off"
    try FlowRun(context)
    finally {
        Critical "On"
        queue.Busy := false
        if queue.Pending.Length
            SetTimer FlowDrain, -1
        Critical "Off"
    }
}

; Applies a missing-target policy to this invocation's previously captured target.
FlowCaptured(mode, context, stopSilently) {
    if !context.Captured.Has(mode)
        throw Error("The requested context was not captured.")
    captured := context.Captured[mode]
    if captured.Error != "" {
        if stopSilently
            throw FlowStopped(captured.Error)
        throw Error(captured.Error)
    }
    return captured.Value
}

; Reuses Shell resolution with one hit test and augments the target with v3 fields.
FlowResolveTarget(mode, context) {
    if !context.Shell || !(view := GetShellView(context.Shell))
        throw Error("File Explorer or desktop context is unavailable.")
    if mode = "directory"
        return FlowPath3(view.Directory)
    if mode = "clicked" {
        hit := GetFileViewHit(context.X, context.Y, context.Shell.Desktop)
        if !hit
            throw Error("The mouse was outside the file area.")
        result := FlowPath3(hit.Kind = "blank" ? view.Directory : ResolveHitPath(view, hit))
        if !result.Exists
            throw Error("No existing filesystem target was resolved.")
        if hit.Kind = "blank"
            result.TargetKind := "Background"
        result.MouseX := context.X, result.MouseY := context.Y
        return result
    }
    if !view.Document
        throw Error("Selected-object acquisition needs File Explorer; use clicked object on desktop.")
    selection := view.Document.SelectedItems
    if selection.Count != 1
        throw Error("Select exactly one file or folder.")
    result := FlowPath3(selection.Item(0).Path)
    if !result.Exists
        throw Error("The selected filesystem target no longer exists.")
    return result
}

; Distinguishes deliberate termination from a failed operation.
class FlowStopped extends Error {
}
