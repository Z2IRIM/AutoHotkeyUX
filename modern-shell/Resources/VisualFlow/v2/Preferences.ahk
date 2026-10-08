; AutoHotkeyUX Explorer shortcuts v3 Preferences

; Provides one cached snapshot; hooks never poll settings or enumerate files.
GetShortcutRuntime() {
    static runtime := {Key: "HKCU\Software\AutoHotkey\Modern", Token: "", Snapshot: false, Registered: Map()}
    return runtime
}

; Matches the immutable default snapshot and schema shared with the manager codec.
DefaultPreferenceText() {
    return "schema=1`nrevision=00000000000000000000000000000000`nterminal-enabled=1`nterminal-key=alt-left"
        . "`nprogram=auto`nposition=above`nwidth=800`nheight=440`ngap=12`narchive-enabled=1"
        . "`narchive-key=alt-left`ndestination=beside`nfolder="
}

; Rejects partial, duplicate, unknown and out-of-range snapshots before changing any hotkey.
ParseShortcutPreferences(text) {
    if text = ""
        text := DefaultPreferenceText()
    if StrLen(text) > 8192 || InStr(text, "`r")
        throw Error("The shortcut preference snapshot is malformed or too large.")
    allowed := Map(), values := Map()
    allowed.CaseSense := "On", values.CaseSense := "On"
    for key in ["schema", "revision", "terminal-enabled", "terminal-key", "program", "position", "width", "height", "gap",
        "archive-enabled", "archive-key", "destination", "folder"]
        allowed[key] := true
    for line in StrSplit(text, "`n") {
        separator := InStr(line, "=")
        key := SubStr(line, 1, separator - 1)
        if separator < 2 || !allowed.Has(key) || values.Has(key)
            throw Error("The shortcut preference snapshot contains an unknown or duplicate field.")
        values[key] := SubStr(line, separator + 1)
    }
    if values.Count != 13 || values["schema"] != "1" || !RegExMatch(values["revision"], "^[0-9a-fA-F]{32}$")
        throw Error("The shortcut preference schema or revision is unsupported.")
    for key in ["terminal-enabled", "archive-enabled"]
        if values[key] != "0" && values[key] != "1"
            throw Error("Invalid shortcut boolean.")
    for key in ["terminal-key", "archive-key"]
        if !RegExMatch(values[key], "^(alt-left|ctrl-alt-left|alt-middle|ctrl-alt-middle)$")
            throw Error("Choose a supported shortcut; Ctrl + left click is reserved for Explorer selection.")
    if !RegExMatch(values["program"], "^(auto|wt|powershell)$") || !RegExMatch(values["position"], "^(above|center)$")
        throw Error("Choose a supported terminal program and position.")
    for key in ["width", "height", "gap"]
        if !RegExMatch(values[key], "^\d+$")
            throw Error("Invalid shortcut dimension.")
    width := Integer(values["width"]), height := Integer(values["height"]), gap := Integer(values["gap"])
    if width < 480 || width > 1600 || height < 240 || height > 1000 || gap < 0 || gap > 64
        throw Error("Shortcut window dimensions are outside their supported ranges.")
    folder := values["folder"]
    if !RegExMatch(values["destination"], "^(beside|custom)$") || StrLen(folder) > 2048 || RegExMatch(folder, "[\x00-\x1f\x7f]")
        throw Error("The extraction destination is invalid.")
    if values["destination"] = "custom" && (!RegExMatch(folder, "i)^(?:[a-z]:[\\/]|\\\\[^\\/]+[\\/][^\\/]+)")
        || SubStr(folder, 1, 4) = "\\?\" || SubStr(folder, 1, 4) = "\\.\" || folder != Trim(folder))
        throw Error("Choose an absolute destination folder.")
    return {Revision: values["revision"], Serialized: text, TerminalEnabled: values["terminal-enabled"] = "1",
        TerminalKey: values["terminal-key"], Program: values["program"], Position: values["position"],
        Width: width, Height: height, Gap: gap, ArchiveEnabled: values["archive-enabled"] = "1",
        ArchiveKey: values["archive-key"], Destination: values["destination"], Folder: folder}
}

; Loads the snapshot once, registers only the supported context variants and publishes a transient endpoint.
InitializeShortcutPreferences(key := "HKCU\Software\AutoHotkey\Modern") {
    runtime := GetShortcutRuntime(), runtime.Key := key
    try runtime.Snapshot := ParseShortcutPreferences(RegRead(key, "ShortcutPreferences", ""))
    catch Error as exception {
        LogShortcut("preferences", "Using defaults for invalid saved configuration: " exception.Message)
        runtime.Snapshot := ParseShortcutPreferences("")
    }
    SetShortcutBindings(runtime.Snapshot)
    guid := Buffer(16), text := Buffer(78)
    if DllCall("ole32\CoCreateGuid", "ptr", guid, "int") != 0
        throw Error("Could not create a shortcut endpoint token.")
    DllCall("ole32\StringFromGUID2", "ptr", guid, "ptr", text, "int", 39)
    runtime.Token := RegExReplace(StrGet(text), "[{}-]", "")
    OnMessage 0x4A, ReceiveShortcutPreferences
    OnExit ClearShortcutEndpoint
    RegWrite A_ScriptHwnd, "REG_SZ", key, "ShortcutScriptWindow"
    RegWrite DllCall("GetCurrentProcessId"), "REG_SZ", key, "ShortcutScriptPid"
    RegWrite runtime.Token, "REG_SZ", key, "ShortcutScriptToken"
    Persistent true
}

; Adapts HotIf's callback arguments to the existing lightweight Shell context predicate.
IsShortcutContext(*) => GetShellGestureContext()

; Shares one native variant for actions with the same key; action routing later checks the target type.
SetShortcutBindings(preferences) {
    runtime := GetShortcutRuntime()
    HotIf IsShortcutContext
    try {
        for key, symbol in Map("alt-left", "~!LButton", "ctrl-alt-left", "~^!LButton", "alt-middle", "~!MButton", "ctrl-alt-middle", "~^!MButton") {
            enabled := (preferences.TerminalEnabled && preferences.TerminalKey = key) || (preferences.ArchiveEnabled && preferences.ArchiveKey = key)
            button := InStr(key, "middle") ? "MButton" : "LButton"
            Hotkey symbol, HandleShellClick.Bind(key, button), enabled ? "On" : "Off"
            runtime.Registered[key] := true
        }
    }
    finally HotIf
}

; Commits a validated snapshot under Critical so a click cannot observe partially changed bindings.
ApplyShortcutPreferences(candidate) {
    runtime := GetShortcutRuntime(), previous := runtime.Snapshot
    if candidate.Revision = previous.Revision
        return candidate.Serialized = previous.Serialized ? 1 : 2
    Critical true
    try {
        SetShortcutBindings(candidate)
        RegWrite candidate.Serialized, "REG_SZ", runtime.Key, "ShortcutPreferences"
        runtime.Snapshot := candidate
        return 1
    }
    catch Error as exception {
        try SetShortcutBindings(previous)
        catch Error as rollbackError
            LogShortcut("preferences-rollback", rollbackError.Message)
        try RegWrite "Could not apply preferences; saved settings were kept. " exception.Message, "REG_SZ", runtime.Key, "ShortcutConfigError"
        LogShortcut("preferences-rejected", exception.Message)
        return 3
    }
    finally Critical false
}

; Validates transient token and packet bounds, then acknowledges configure or read-only revision confirmation.
ReceiveShortcutPreferences(wParam, lParam, *) {
    try {
        if NumGet(lParam, 0, "uptr") != 0x41584B33
            return 0
        length := NumGet(lParam, A_PtrSize, "uint"), pointer := NumGet(lParam, A_PtrSize * 2, "ptr")
        if !pointer || length < 2 || length > 32768 || NumGet(pointer, length - 1, "uchar") != 0
            return 0
        body := StrGet(pointer, length - 1, "UTF-8")
        if StrLen(body) > 8296
            return 0
        parts := StrSplit(body, "`n",, 3), runtime := GetShortcutRuntime()
        if parts.Length != 3 || parts[2] != runtime.Token
            return 0
        if parts[1] = "query"
            return parts[3] = runtime.Snapshot.Revision ? 1 : 2
        if parts[1] != "configure"
            return 0
        return ApplyShortcutPreferences(ParseShortcutPreferences(parts[3]))
    }
    catch Error as exception {
        try RegWrite exception.Message, "REG_SZ", GetShortcutRuntime().Key, "ShortcutConfigError"
        return 2
    }
}

; Removes only this script's endpoint, never a replacement process's published token.
ClearShortcutEndpoint(*) {
    runtime := GetShortcutRuntime()
    try {
        if RegRead(runtime.Key, "ShortcutScriptToken", "") = runtime.Token {
            RegWrite "", "REG_SZ", runtime.Key, "ShortcutScriptToken"
            RegWrite "0", "REG_SZ", runtime.Key, "ShortcutScriptWindow"
            RegWrite "0", "REG_SZ", runtime.Key, "ShortcutScriptPid"
        }
    }
}
