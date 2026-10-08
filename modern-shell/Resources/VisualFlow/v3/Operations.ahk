; Provides path metadata, including absence, without clipboard simulation.
FlowPath3(path) {
    if !FlowAbsolutePath(path)
        throw Error("Use an absolute filesystem path.")
    SplitPath path, &name, &parent, &extension, &stem
    baseName := RegExReplace(name, "i)\.(?:tar\.gz|tgz|zip|7z|rar|tar)$", "")
    if baseName = name
        baseName := stem
    folder := !!DirExist(path), exists := folder || !!FileExist(path)
    return {Path: path, Directory: folder ? path : parent, ParentDirectory: parent, Name: name,
        BaseName: baseName, Extension: extension, TargetKind: folder ? "Folder" : exists ? "File" : "Missing", Exists: exists}
}

; Reads operation preferences at invocation time instead of polling while idle.
FlowPreferences() {
    try return ParseShortcutPreferences(RegRead(GetShortcutRuntime().Key, "ShortcutPreferences", ""))
    catch Error
        return ParseShortcutPreferences("")
}

; Reuses the owned-window queue and exposes readiness for this launch only.
FlowTerminal3(path, context, options) {
    if !FlowAbsolutePath(path) || !DirExist(path)
        throw Error("The terminal directory does not exist.")
    preferences := options.Mode = "Inherit" ? FlowPreferences() : options
    completion := {Done: false, WindowId: 0, Error: "", TimeoutMs: options.TimeoutMs}, started := A_TickCount
    OpenTerminal(path, context.X, context.Y, "", preferences, completion)
    if !options.WaitReady
        return {Directory: path}
    while !completion.Done && A_TickCount - started <= options.TimeoutMs
        Sleep 20
    if !completion.Done
        throw Error("Terminal readiness timed out. The accepted launch was not repeated.")
    if completion.Error != ""
        throw Error(completion.Error)
    if !completion.WindowId || !WinExist("ahk_id " completion.WindowId)
        throw Error("The new terminal window is no longer available.")
    return {Directory: path, WindowId: completion.WindowId, Success: true}
}

; Combines bounded literal/result text without evaluating expressions.
FlowCompose(parts) {
    text := ""
    for part in parts {
        text .= String(part)
        if StrLen(text) > 4096
            throw Error("Composed text exceeds the supported size.")
    }
    return text
}

; Joins relative segments without permitting traversal or drive changes.
FlowJoinPath(base, segments) {
    if !FlowAbsolutePath(base) || FileExist(base) && !DirExist(base)
        throw Error("The base path must be an absolute directory path.")
    path := RTrim(base, "\/")
    for segment in segments {
        if segment = "" || RegExMatch(segment, '^[\\/]|[\x00-\x1f:"<>|?*]')
            throw Error("Use a relative path segment without reserved characters.")
        for part in StrSplit(StrReplace(segment, "/", "\"), "\")
            if part = "" || part = "." || part = ".." || RegExMatch(part, 'i)[. ]$|^(?:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)')
                throw Error("Path segments cannot contain traversal or ambiguous names.")
        path .= "\" segment
    }
    return FlowPath3(path)
}

; Creates a directory idempotently and rejects an existing file target.
FlowCreateDirectory(path, failIfExists) {
    if !FlowAbsolutePath(path) || FileExist(path) && !DirExist(path)
        throw Error("The directory path is invalid or already names a file.")
    if failIfExists && DirExist(path)
        throw Error("The directory already exists.")
    DirCreate path
    result := FlowPath3(path), result.Success := true
    return result
}

; Extends closed comparisons with filesystem existence.
FlowCondition3(value, condition, comparison) {
    if condition = "PathExists"
        return !!DirExist(value) || !!FileExist(value)
    return FlowCondition(value, condition, comparison)
}

; Ends only the current invocation, with an optional configured notification.
FlowStop(reason, notify) {
    if notify
        TrayTip reason, "AutoHotkey workflow"
    throw FlowStopped(reason)
}

; Displays composed result text after successful preceding actions.
FlowNotify(title, message) {
    TrayTip message, title
}

; Enqueues immutable archive options and consumes this request's committed result.
FlowExtract3(path, destination, options, context) {
    global FlowIdentity
    if RegRead(GetShortcutRuntime().Key, "WorkflowProtocolVersion", "0") != "3"
        throw Error("Open the updated AutoHotkeyUX application before running configurable extraction.")
    if !FlowAbsolutePath(path) || !FileExist(path) || DirExist(path) || !RegExMatch(path, "i)\.(zip|7z|rar|tar|tar\.gz|tgz)$")
        throw Error("Choose a supported existing archive.")
    if options.Mode = "Inherit" || options.Destination = "Inherit" {
        preferences := FlowPreferences()
        destination := preferences.Destination = "custom" ? preferences.Folder : ""
    } else if options.Destination = "BesideArchive"
        destination := ""
    if destination != "" && (!FlowAbsolutePath(destination) || !DirExist(destination))
        throw Error("The extraction destination does not exist.")
    requestId := FlowGuid(), endpoint := FlowEndpoint()
    root := endpoint ? endpoint.ResultRoot : RegRead(GetShortcutRuntime().Key, "WorkflowResultRoot", EnvGet("LOCALAPPDATA") "\AutoHotkeyUX.Modern\state\workflow-results")
    if !FlowAbsolutePath(root)
        throw Error("The manager result directory is invalid.")
    report := root "\" StrReplace(requestId, "-", "") ".result"
    name := options.Mode = "Inherit" ? "" : options.Name
    if options.Mode = "Custom" && options.Naming = "Composition" && Trim(name) = ""
        throw Error("The custom extraction folder name is empty.")
    collision := options.Mode = "Inherit" ? "AutoSuffix" : options.Collision
    request := '{"requestId":' FlowJson(requestId) ',"flowId":' FlowJson(FlowIdentity)
        . ',"runId":' FlowJson(context.RunId) ',"stepId":' FlowJson(context.StepId)
        . ',"source":' FlowJson(path) ',"destination":' FlowJson(destination) ',"name":' FlowJson(name)
        . ',"collision":' FlowJson(collision) '}'
    if endpoint {
        acknowledgement := FlowSend("flow-extract`n" endpoint.Token "`n" request, endpoint)
        if !acknowledgement.Delivered
            throw Error("Manager acknowledgement timed out. The request may be accepted; it was not repeated. Request " requestId)
        if acknowledgement.Result != 1 && acknowledgement.Result != 2
            throw Error(acknowledgement.Result = 3 ? "The extraction queue is full." : "The manager rejected the extraction request.")
    } else {
        executable := RegRead(GetShortcutRuntime().Key, "ExecutablePath", "")
        if !FileExist(executable)
            throw Error("Open AutoHotkeyUX once to register its extraction helper.")
        Run QuotePath(executable) " --flow-extract-v3 " FlowQuoteArgument(request),, "Hide", &helperPid
    }
    started := A_TickCount, queried := started
    while !FileExist(report) && A_TickCount - started < 600000 {
        if endpoint && !FlowEndpointAlive(endpoint) {
            if FileExist(report)
                break
            throw Error("Manager exited before reporting this request; it was not repeated. Request " requestId)
        }
        if !endpoint && !ProcessExist(helperPid) {
            if FileExist(report)
                break
            throw Error("The extraction helper exited without a result. Request " requestId)
        }
        if endpoint && A_TickCount - queried >= 1000 {
            status := FlowSend("flow-status`n" endpoint.Token "`n" requestId, endpoint), queried := A_TickCount
            if status.Delivered && status.Result = 5
                throw Error("The manager could not publish the extraction result; it was not repeated. Request " requestId)
            if status.Delivered && status.Result = 6
                throw Error("The request identity belongs to different extraction options; it was not repeated. Request " requestId)
        }
        Sleep 50
    }
    if !FileExist(report) || FileGetSize(report) > 8192
        throw Error("No bounded result was returned. Check request " requestId " in diagnostics; it was not repeated.")
    response := FileRead(report, "UTF-8")
    if SubStr(response, 1, 3) != "ok`n"
        throw Error("Extraction failed: " SubStr(response, 7, 2048))
    result := FlowPath3(SubStr(response, 4))
    if !DirExist(result.Path)
        throw Error("The committed extraction directory is unavailable.")
    result.Success := true
    return result
}
