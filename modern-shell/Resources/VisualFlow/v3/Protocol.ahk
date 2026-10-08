; Confirms parsing only to the exact private snapshot receipt requested by its manager.
FlowAnnounceReady() {
    global FlowIdentity
    if A_Args.Length != 2 || A_Args[1] != "--signal-ready"
        return
    path := A_ScriptFullPath ".ready"
    if A_Args[2] != path
        throw Error("The ready receipt does not belong to this script snapshot.")
    FileAppend ProcessExist() "`n" FlowIdentity, path, "UTF-8-RAW"
}

; Allocates independent invocation and request identities.
FlowGuid() {
    guid := Buffer(16), text := Buffer(78)
    if DllCall("ole32\CoCreateGuid", "ptr", guid, "int") != 0
        throw Error("Could not allocate a workflow request identity.")
    DllCall("ole32\StringFromGUID2", "ptr", guid, "ptr", text, "int", 39)
    return Trim(StrGet(text), "{}")
}

; Escapes one JSON string without inserting script or command syntax.
FlowJson(value) {
    value := String(value)
    value := StrReplace(value, "\", "\\"), value := StrReplace(value, '"', '\"')
    value := StrReplace(value, "`r", "\r"), value := StrReplace(value, "`n", "\n"), value := StrReplace(value, "`t", "\t")
    Loop 31
        value := StrReplace(value, Chr(A_Index), Format("\u{:04x}", A_Index))
    return '"' value '"'
}

; Accepts only the currently published manager HWND and live owning PID.
FlowEndpoint() {
    key := GetShortcutRuntime().Key
    window := Integer(RegRead(key, "ShortcutWindow", "0")), pid := Integer(RegRead(key, "ShortcutPid", "0"))
    token := RegRead(key, "ShortcutToken", ""), owner := 0
    if !window || !RegExMatch(token, "i)^[a-f0-9]{32}$") || !DllCall("IsWindow", "ptr", window)
        return false
    DllCall("GetWindowThreadProcessId", "ptr", window, "uint*", &owner)
    if owner != pid
        return false
    root := RegRead(key, "WorkflowResultRoot", "")
    if !FlowAbsolutePath(root)
        return false
    return {Window: window, Pid: pid, Token: token, ResultRoot: root}
}

; Confirms that a remembered HWND has not been reused by a different process.
FlowEndpointAlive(endpoint) {
    if !DllCall("IsWindow", "ptr", endpoint.Window)
        return false
    owner := 0
    DllCall("GetWindowThreadProcessId", "ptr", endpoint.Window, "uint*", &owner)
    return owner = endpoint.Pid
}

; Copies bounded UTF-8 data and distinguishes acknowledgement from ambiguous delivery.
FlowSend(body, endpoint) {
    payload := Buffer(StrPut(body, "UTF-8")), data := Buffer(A_PtrSize * 3, 0)
    if payload.Size > 32768
        throw Error("The workflow request exceeds the protocol size limit.")
    StrPut body, payload, "UTF-8"
    NumPut("uptr", 0x41584B32, "uint", payload.Size, data), NumPut("ptr", payload.Ptr, data, A_PtrSize * 2)
    result := 0
    delivered := DllCall("SendMessageTimeout", "ptr", endpoint.Window, "uint", 0x4A, "ptr", 0, "ptr", data,
        "uint", 0xA, "uint", 250, "uptr*", &result, "ptr")
    return {Delivered: !!delivered, Result: result}
}

; Reports exact per-step state to bounded manager activity without idle polling.
FlowEvent(context, stepId, stepName, state, detail) {
    global FlowIdentity
    detail := SubStr(String(detail), 1, 2048)
    try {
        if endpoint := FlowEndpoint() {
            payload := '{"flowId":' FlowJson(FlowIdentity) ',"runId":' FlowJson(context.RunId)
                . ',"stepId":' FlowJson(stepId) ',"stepName":' FlowJson(SubStr(stepName, 1, 80))
                . ',"state":' FlowJson(state) ',"detail":' FlowJson(detail)
                . ',"elapsedMs":' Min(A_TickCount - context.Started, 3600000) '}'
            FlowSend("flow-event`n" endpoint.Token "`n" payload, endpoint)
        }
    }
    catch Error as reportError
        OutputDebug "Workflow activity failed: " reportError.Message
    if state = "failed" || state = "stopped" {
        try {
            directory := EnvGet("LOCALAPPDATA") "\AutoHotkeyUX.Modern\state"
            DirCreate directory
            path := directory "\workflows.log"
            if FileExist(path) && FileGetSize(path) > 262144
                FileMove path, path ".previous", 1
            FileAppend FormatTime(, "yyyy-MM-dd HH:mm:ss") " [" context.RunId "/" stepId "] " state " " detail "`n", path, "UTF-8"
        }
        catch Error as logError
            OutputDebug "Workflow log failed: " logError.Message
    }
}
