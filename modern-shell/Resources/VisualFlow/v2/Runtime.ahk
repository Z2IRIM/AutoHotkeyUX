; AutoHotkey UX v2 visual runtime. No polling is installed while waiting for a trigger.

; Captures a click before waiting for release, preserving native selection and rejecting drags.
FlowCapture(button) {
    MouseGetPos &x, &y
    shell := FlowShellContext(button != "")
    if button != "" {
        if !KeyWait(button, "T0.5")
            return false
        MouseGetPos &endX, &endY
        if Abs(endX - x) > 6 || Abs(endY - y) > 6
            return false
        Sleep 15
    }
    return {X: x, Y: y, Shell: shell, ExpectedWindow: 0}
}

; Uses the shared inactive-click resolver or the foreground Explorer/desktop for keyboard workflows.
FlowShellContext(mouse := false) {
    if mouse
        return GetShellGestureContext()
    hwnd := WinExist("A")
    if !hwnd
        return false
    class := WinGetClass(hwnd)
    return class = "CabinetWClass" ? {Window: hwnd, Desktop: false}
        : class = "Progman" || class = "WorkerW" ? {Window: hwnd, Desktop: true} : false
}

; Resolves one existing Shell target without clipboard simulation or guesses for hidden extensions.
FlowAcquire(mode, context) {
    if !context.Shell || !(view := GetShellView(context.Shell))
        throw Error("File Explorer or desktop context is unavailable.")
    if mode = "directory"
        return FlowPathInfo(view.Directory)
    if mode = "clicked" {
        hit := GetFileViewHit(context.X, context.Y, context.Shell.Desktop)
        if !hit
            throw Error("The mouse was outside the file area.")
        return FlowPathInfo(hit.Kind = "blank" ? view.Directory : ResolveHitPath(view, hit))
    }
    if !view.Document
        throw Error("Selected-object acquisition requires a File Explorer window; use clicked object on desktop.")
    selection := view.Document.SelectedItems
    if selection.Count != 1
        throw Error("Select exactly one file or folder. Multiple or empty selection is not accepted.")
    return FlowPathInfo(selection.Item(0).Path)
}

; Produces stable path fields only for real filesystem targets.
FlowPathInfo(path) {
    if !FlowAbsolutePath(path) || (!DirExist(path) && !FileExist(path))
        throw Error("No unique existing filesystem target was resolved.")
    SplitPath path, &name, &directory, &extension
    return {Path: path, Directory: DirExist(path) ? path : directory, Name: name, Extension: extension}
}

; Launches one quoted program/file with optional literal arguments and returns its process identity.
FlowProgram(target, arguments, directory, singleArgument := false) {
    if !FlowAbsolutePath(target) && !RegExMatch(target, "i)^[a-z0-9_.-]+\.exe$")
        throw Error("Invalid program/file target.")
    if directory != "" && (!FlowAbsolutePath(directory) || !DirExist(directory))
        throw Error("The working directory does not exist.")
    if RegExMatch(arguments, "[\x00-\x1f]") || StrLen(arguments) > 4096
        throw Error("The program argument is invalid or too large.")
    if singleArgument
        arguments := FlowQuoteArgument(arguments)
    pid := 0
    Run QuotePath(target) (arguments = "" ? "" : " " arguments), directory,, &pid
    return {ProcessId: pid}
}

; Quotes one result as a Windows argument, including embedded quotes and trailing backslashes.
FlowQuoteArgument(value) {
    return '"' RegExReplace(RegExReplace(value, '(\\*)"', '$1$1\"'), '(\\+)$', '$1$1') '"'
}

; Rejects relative, device and invalid paths before passing dynamic results to filesystem actions.
FlowAbsolutePath(path) {
    return StrLen(path) <= 2048 && !RegExMatch(path, '[\x00-\x1f"<>|?*]') && SubStr(path, 1, 4) != "\\.\"
        && RegExMatch(path, "i)^(?:[a-z]:[\\/]|\\\\[^\\/]+[\\/][^\\/]+)")
}

; Refuses empty launch identities rather than letting a selector match an unrelated window.
FlowPositiveId(value) {
    if !IsInteger(value) || value <= 0
        throw Error("The launch did not return a usable process/window ID. Choose an executable-name window target for redirecting applications.")
    return value
}

; Returns one HWND or fails after a bounded timeout so dependent input never runs prematurely.
FlowWaitWindow(selector, timeout) {
    hwnd := WinWait(selector,, timeout / 1000)
    if !hwnd
        throw Error("Timed out waiting for the target window.")
    return {WindowId: hwnd}
}

; Pins the activated HWND for subsequent input and confirms that focus was actually obtained.
FlowActivate(selector, timeout, context) {
    hwnd := WinExist(selector)
    if !hwnd
        throw Error("The target window no longer exists.")
    WinActivate hwnd
    if !WinWaitActive(hwnd,, timeout / 1000)
        throw Error("The target window could not be activated.")
    context.ExpectedWindow := hwnd
}

; Checks a prior activation immediately before sending literal text.
FlowSendText(text, context) {
    if StrLen(text) > 4096 || InStr(text, Chr(0))
        throw Error("Text input exceeds the supported size.")
    if context.ExpectedWindow && !WinActive(context.ExpectedWindow)
        throw Error("The target lost focus; text was not sent.")
    SendText text
}

; Shares the bounded key vocabulary and the activated-window guard with text input.
FlowSendKeys(key, context) {
    if context.ExpectedWindow && !WinActive(context.ExpectedWindow)
        throw Error("The target lost focus; keys were not sent.")
    keys := Map("Enter", "{Enter}", "Tab", "{Tab}", "Escape", "{Escape}", "Ctrl+C", "^c", "Ctrl+V", "^v")
    Send keys[key]
}

; Opens an existing absolute folder through Explorer without command interpretation.
FlowFolder(path) {
    if !FlowAbsolutePath(path) || !DirExist(path)
        throw Error("The folder does not exist.")
    Run "explorer.exe " QuotePath(path)
}

; Restricts dynamic website results to the same schemes as fixed addresses.
FlowWebsite(url) {
    if !RegExMatch(url, "i)^https?://[^\s/]+") || RegExMatch(url, "\s") || StrLen(url) > 2048
        throw Error("The result is not an HTTP or HTTPS address.")
    Run url
}

; Reuses the existing bounded terminal placement queue and mouse-above position.
FlowTerminal(path, context) {
    if !FlowAbsolutePath(path) || !DirExist(path)
        throw Error("The terminal directory does not exist.")
    OpenTerminal(path, context.X, context.Y)
}

; Invokes the headless manager's existing non-overwriting extraction service and consumes its exact output.
FlowExtract(path, destination) {
    executable := RegRead("HKCU\Software\AutoHotkey\Modern", "ExecutablePath", "")
    if !FileExist(executable)
        throw Error("AutoHotkeyUX is required for extraction. Open the application once to refresh its path.")
    if !FlowAbsolutePath(path) || !FileExist(path) || !RegExMatch(path, "i)\.(zip|7z|rar|tar|tar\.gz|tgz)$")
        throw Error("Choose a supported existing archive.")
    if destination != "" && (!FlowAbsolutePath(destination) || !DirExist(destination))
        throw Error("The extraction destination does not exist.")
    guid := Buffer(16), name := Buffer(78)
    if DllCall("ole32\CoCreateGuid", "ptr", guid, "int") != 0
        throw Error("Could not allocate an extraction request.")
    DllCall("ole32\StringFromGUID2", "ptr", guid, "ptr", name, "int", 39)
    temporary := A_Temp "\AutoHotkeyUX.Flow-" RegExReplace(StrGet(name), "[{}]", "")
    DirCreate temporary
    report := temporary "\result.txt"
    try {
        exitCode := RunWait(QuotePath(executable) " --flow-extract " QuotePath(path) " " QuotePath(destination) " " QuotePath(report),, "Hide")
        if !FileExist(report) || FileGetSize(report) > 8192
            throw Error("The extraction helper did not return a result. See application diagnostics.")
        result := FileRead(report, "UTF-8")
        if exitCode != 0 || SubStr(result, 1, 3) != "ok`n"
            throw Error("Extraction failed: " SubStr(result, 1, 2048))
        return FlowPathInfo(SubStr(result, 4))
    }
    finally {
        if FileExist(report)
            FileDelete report
        DirDelete temporary
    }
}

; Evaluates only the declared comparisons, never an AHK expression supplied by the user.
FlowCondition(value, condition, comparison) {
    switch condition {
        case "IsFolder": return !!DirExist(value)
        case "IsFile": return !!FileExist(value) && !DirExist(value)
        case "IsArchive": return !!FileExist(value) && !DirExist(value) && !!RegExMatch(value, "i)\.(zip|7z|rar|tar|tar\.gz|tgz)$")
        case "ExtensionEquals": return StrLower(LTrim(value, ".")) = StrLower(LTrim(comparison, "."))
            || !!RegExMatch(value, "i)\." StrReplace(LTrim(comparison, "."), ".", "\.") "$")
        case "IsEmpty": return value = ""
        case "IsNotEmpty": return value != ""
    }
    throw Error("Unsupported condition.")
}

; Records bounded runtime failures with the responsible action, then reports without a blocking error dialog.
FlowFailure(step, failure) {
    try {
        directory := EnvGet("LOCALAPPDATA") "\AutoHotkeyUX.Modern\state"
        DirCreate directory
        path := directory "\workflows.log"
        if FileExist(path) && FileGetSize(path) > 262144
            FileMove path, path ".previous", 1
        FileAppend FormatTime(, "yyyy-MM-dd HH:mm:ss") " [" step "] " failure.Message "`n", path, "UTF-8"
    }
    catch Error as logFailure
        OutputDebug "Workflow log failed: " logFailure.Message
    TrayTip failure.Message, "AutoHotkey workflow stopped"
}
