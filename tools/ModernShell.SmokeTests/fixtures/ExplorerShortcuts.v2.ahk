; AutoHotkeyUX Explorer shortcuts v2
#Requires AutoHotkey v2.0
#SingleInstance Ignore
#NoTrayIcon
#Include %A_ScriptDir%\ExplorerShortcuts\v2\Shell.ahk
#Include %A_ScriptDir%\ExplorerShortcuts\v2\Actions.ahk

CoordMode "Mouse", "Screen"
SetWinDelay 0
OnError ReportShortcutFailure

; Diagnostic entry points exercise the same actions without installing a live verification hotkey.
if A_Args.Length {
    switch A_Args[1] {
        case "--validate": ExitApp 0
        case "--extract":
            FileAppend RequestExtraction(A_Args[2]), A_Args[3], "UTF-8"
            ExitApp 0
        case "--verify-terminal":
            OpenTerminal(A_Args[2], Integer(A_Args[3]), Integer(A_Args[4]), A_Args[5])
            return
        case "--diagnose":
            WriteShellDiagnostics(A_Args[2])
            ExitApp 0
    }
}

#HotIf GetShellGestureContext()
~!LButton::HandleShellClick()
#HotIf

; Keeps verification failures observable and prevents unattended shortcut errors from opening a blocking dialog.
ReportShortcutFailure(exception, mode) {
    LogShortcut("error", exception.Message " | " exception.File ":" exception.Line)
    if A_Args.Length {
        FileAppend exception.Message " | " exception.File ":" exception.Line "`n", "*", "UTF-8"
        ExitApp 1
    }
    return true
}

; Keeps native selection intact and rejects drags before scheduling the action outside the mouse hook.
HandleShellClick() {
    try {
        context := GetShellGestureContext()
        MouseGetPos &x, &y
        if !context || !KeyWait("LButton", "T0.5")
            return
        MouseGetPos &releasedX, &releasedY
        if Abs(releasedX - x) > 6 || Abs(releasedY - y) > 6
            return
        SetTimer ResolveShellClick.Bind(context, x, y), -15
    }
    catch Error as exception {
        LogShortcut("error", exception.Message)
    }
}

; Resolves the clicked item or file-area background, including the real merged desktop namespace.
ResolveShellClick(context, x, y) {
    started := A_TickCount
    try {
        hit := GetFileViewHit(x, y, context.Desktop)
        view := GetShellView(context)
        if !hit || !view {
            LogShortcut("skip", !hit ? "outside file area" : "shell view unavailable")
            return
        }
        path := hit.Kind = "blank" ? view.Directory : ResolveHitPath(view, hit)
        if DirExist(path) {
            OpenTerminal(path, x, y)
            LogShortcut("terminal", path " | resolveMs=" (A_TickCount - started))
        }
        else if FileExist(path) && RegExMatch(path, "i)\.(zip|7z|rar|tar|tar\.gz|tgz)$") {
            result := RequestExtraction(path)
            LogShortcut("extract", path " | ack=" result " | dispatchMs=" (A_TickCount - started))
        }
        else LogShortcut("skip", "no unique supported filesystem target: " hit.Name)
    }
    catch Error as exception {
        LogShortcut("error", exception.Message)
    }
}
