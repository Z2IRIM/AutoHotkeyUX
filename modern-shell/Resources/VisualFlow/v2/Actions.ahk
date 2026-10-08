; AutoHotkeyUX Explorer shortcuts v3 Actions

; Quotes filesystem arguments with Windows trailing-backslash rules without invoking a command shell.
QuotePath(path) {
    return '"' RegExReplace(path, "(\\+)$", "$1$1") '"'
}

; Places the window bottom above the pointer and clamps the rectangle to the clicked monitor's work area.
GetPopupRectangle(x, y, width, height, work, gap := 12, position := "above") {
    width := Min(width, work.Right - work.Left), height := Min(height, work.Bottom - work.Top)
    desiredX := position = "center" ? work.Left + (work.Right - work.Left - width) / 2 : x - width / 2
    desiredY := position = "center" ? work.Top + (work.Bottom - work.Top - height) / 2 : y - height - gap
    return {X: Max(work.Left, Min(Round(desiredX), work.Right - width)),
        Y: Max(work.Top, Min(Round(desiredY), work.Bottom - height)), Width: width, Height: height}
}

; Reads the monitor containing the click, including monitors with negative screen coordinates.
GetClickMonitor(x, y) {
    monitor := DllCall("MonitorFromPoint", "int64", (x & 0xFFFFFFFF) | (y << 32), "uint", 2, "ptr")
    info := Buffer(40, 0), NumPut("uint", 40, info)
    if !DllCall("GetMonitorInfo", "ptr", monitor, "ptr", info)
        throw Error("The clicked monitor's work area is unavailable.")
    return {Left: NumGet(info, 20, "int"), Top: NumGet(info, 24, "int"),
        Right: NumGet(info, 28, "int"), Bottom: NumGet(info, 32, "int")}
}

; Creates a distinct terminal window at the pointer, then briefly watches only that newly created window.
OpenTerminal(directory, x, y, report := "", preferences := false) {
    launch := GetTerminalLaunchState()
    if !preferences
        preferences := GetShortcutRuntime().Snapshot
    if launch.Pending {
        if launch.Queue.Length >= 4
            throw Error("The terminal launch queue is full. Wait for the current window to open.")
        launch.Queue.Push({Directory: directory, X: x, Y: y, Report: report, Preferences: preferences})
        return
    }
    if !DirExist(directory)
        throw Error("The terminal directory no longer exists: " directory)
    work := GetClickMonitor(x, y)
    clickedWindow := DllCall("WindowFromPoint", "int64", (x & 0xFFFFFFFF) | (y << 32), "ptr")
    dpi := DllCall("GetDpiForWindow", "ptr", clickedWindow, "uint")
    scale := (dpi ? dpi : DllCall("GetDpiForSystem", "uint")) / 96
    rectangle := GetPopupRectangle(x, y, Round(preferences.Width * scale), Round(preferences.Height * scale), work,
        Round(preferences.Gap * scale), preferences.Position)
    existing := Map()
    for className in ["CASCADIA_HOSTING_WINDOW_CLASS", "ConsoleWindowClass"]
        for window in WinGetList("ahk_class " className)
            existing[window] := true
    title := "AutoHotkey · " directory
    job := {X: x, Y: y, Work: work, Rectangle: rectangle, Existing: existing, Title: title,
        Started: A_TickCount, Report: report, Pid: 0, Terminal: false, Directory: directory,
        Gap: Round(preferences.Gap * scale), Position: preferences.Position}
    terminal := EnvGet("LOCALAPPDATA") "\Microsoft\WindowsApps\wt.exe"
    if preferences.Program = "wt" && !FileExist(terminal)
        throw Error("Windows Terminal is unavailable. Install it or choose Auto / Windows PowerShell.")
    if preferences.Program != "powershell" && FileExist(terminal) {
        try {
            name := "AutoHotkeyUX-" DllCall("GetCurrentProcessId") "-" A_TickCount
            command := QuotePath(terminal) " -w " name " --pos " rectangle.X "," rectangle.Y
                . " --size 96,24 new-tab -d " QuotePath(StrReplace(directory, ";", "\;"))
                . " --title " QuotePath(StrReplace(title, ";", "\;")) " --suppressApplicationTitle"
            Run command, directory
            job.Terminal := true
        }
        catch Error as exception {
            if preferences.Program = "wt"
                throw exception
            LogShortcut("terminal-fallback", exception.Message)
        }
    }
    if !job.Terminal {
        powershell := A_WinDir "\System32\WindowsPowerShell\v1.0\powershell.exe"
        conhost := A_WinDir "\System32\conhost.exe"
        Run QuotePath(conhost) " " QuotePath(powershell) " -NoLogo -NoExit -NoProfile", directory,, &pid
        job.Pid := pid
    }
    job.Timer := PositionTerminal.Bind(job)
    launch.Pending := true
    SetTimer job.Timer, 30
}

; Serializes window discovery so rapid clicks in the same folder cannot move a previous newly created terminal.
GetTerminalLaunchState() {
    static state := {Pending: false, Queue: []}
    return state
}

; Verifies the console client belongs to our launch; console HWNDs report the child rather than conhost's PID.
IsOwnedConsoleProcess(processId, hostId) {
    if processId = hostId
        return true
    snapshot := DllCall("CreateToolhelp32Snapshot", "uint", 2, "uint", 0, "ptr")
    if snapshot = -1
        return false
    try {
        entry := Buffer(A_PtrSize = 8 ? 568 : 556, 0), NumPut("uint", entry.Size, entry)
        found := DllCall("Process32FirstW", "ptr", snapshot, "ptr", entry, "int")
        while found {
            if NumGet(entry, 8, "uint") = processId
                return NumGet(entry, A_PtrSize = 8 ? 32 : 24, "uint") = hostId
            found := DllCall("Process32NextW", "ptr", snapshot, "ptr", entry, "int")
        }
        return false
    }
    finally DllCall("CloseHandle", "ptr", snapshot)
}

; Releases one launch and schedules at most one next job, avoiding one retry timer per rapid click.
FinishTerminalLaunch(job) {
    SetTimer job.Timer, 0
    job.Timer := 0
    launch := GetTerminalLaunchState(), launch.Pending := false
    if launch.Queue.Length
        SetTimer OpenQueuedTerminal, -1
}

; Isolates a queued launch error so later accepted terminal jobs still execute.
OpenQueuedTerminal() {
    launch := GetTerminalLaunchState()
    if launch.Pending || !launch.Queue.Length
        return
    queued := launch.Queue.RemoveAt(1)
    try OpenTerminal(queued.Directory, queued.X, queued.Y, queued.Report, queued.Preferences)
    catch Error as exception {
        ReportTerminalResult(queued.Directory, false, 0, exception.Message)
        LogShortcut("terminal-error", exception.Message)
        if launch.Queue.Length
            SetTimer OpenQueuedTerminal, -1
    }
}

; Finishes placement after creation and stops polling immediately; existing terminal windows are never moved.
PositionTerminal(job) {
    try {
        className := job.Terminal ? "CASCADIA_HOSTING_WINDOW_CLASS" : "ConsoleWindowClass"
        for window in WinGetList("ahk_class " className) {
            if job.Existing.Has(window)
                continue
            if job.Terminal ? !InStr(WinGetTitle("ahk_id " window), job.Title) : !IsOwnedConsoleProcess(WinGetPID("ahk_id " window), job.Pid)
                continue
            WinMove job.Rectangle.X, job.Rectangle.Y, job.Rectangle.Width, job.Rectangle.Height, "ahk_id " window
            WinGetPos &actualX, &actualY, &width, &height, "ahk_id " window
            rectangle := GetPopupRectangle(job.X, job.Y, width, height, job.Work, job.Gap, job.Position)
            WinMove rectangle.X, rectangle.Y,,, "ahk_id " window
            WinGetPos &actualX, &actualY, &width, &height, "ahk_id " window
            FinishTerminalLaunch(job)
            LogShortcut("terminal-ready", "readyMs=" (A_TickCount - job.Started) " x=" actualX " y=" actualY)
            if job.Report != "" {
                FileAppend "passed=1`nx=" actualX "`ny=" actualY "`nwidth=" width "`nheight=" height
                    . "`nanchorX=" job.X "`nanchorY=" job.Y "`nworkLeft=" job.Work.Left "`nworkTop=" job.Work.Top
                    . "`nworkRight=" job.Work.Right "`nworkBottom=" job.Work.Bottom
                    . "`nreadyMs=" (A_TickCount - job.Started) "`n", job.Report, "UTF-8"
                WinClose "ahk_id " window
                ExitApp 0
            }
            ReportTerminalResult(job.Directory, true, A_TickCount - job.Started,
                (job.Terminal ? "Windows Terminal" : "Windows PowerShell") " opened at " actualX ", " actualY ".")
            return
        }
        if A_TickCount - job.Started > 4000 {
            FinishTerminalLaunch(job)
            LogShortcut("terminal-position", "Timed out locating the new terminal window; initial position was requested.")
            if job.Report != "" {
                FileAppend "passed=0`nreason=window-timeout`n", job.Report, "UTF-8"
                ExitApp 1
            }
            ReportTerminalResult(job.Directory, false, A_TickCount - job.Started, "The new terminal window could not be located for placement.")
        }
    }
    catch Error as exception {
        FinishTerminalLaunch(job)
        LogShortcut("terminal-position", exception.Message)
        if job.Report != "" {
            FileAppend "passed=0`nreason=" exception.Message "`n", job.Report, "UTF-8"
            ExitApp 1
        }
        ReportTerminalResult(job.Directory, false, A_TickCount - job.Started, exception.Message)
    }
}

; Enqueues extraction in the warm manager; cold fallback runs asynchronously only when no endpoint exists.
RequestExtraction(path) {
    key := GetShortcutRuntime().Key
    window := Integer(RegRead(key, "ShortcutWindow", "0")), pid := Integer(RegRead(key, "ShortcutPid", "0"))
    token := RegRead(key, "ShortcutToken", ""), owner := 0
    if window && token != "" && DllCall("IsWindow", "ptr", window) {
        DllCall("GetWindowThreadProcessId", "ptr", window, "uint*", &owner)
        if owner = pid {
            body := "extract`n" token "`n" path
            payload := Buffer(StrPut(body, "UTF-8"))
            StrPut body, payload, "UTF-8"
            data := Buffer(A_PtrSize * 3, 0)
            NumPut("uptr", 0x41584B32, "uint", payload.Size, data)
            NumPut("ptr", payload.Ptr, data, A_PtrSize * 2)
            result := 0
            delivered := DllCall("SendMessageTimeout", "ptr", window, "uint", 0x4A, "ptr", 0,
                "ptr", data, "uint", 0xA, "uint", 250, "uptr*", &result, "ptr")
            if !delivered
                throw Error("Manager did not acknowledge extraction in 250 ms. See shortcuts.log; retry after checking the output folder.")
            if result = 1 || result = 2
                return result
            throw Error(result = 3 ? "The extraction queue is full. Try again after the current jobs finish." : "Manager rejected the extraction request.")
        }
    }
    executable := RegRead(key, "ExecutablePath", "")
    if !FileExist(executable)
        throw Error("Open AutoHotkeyUX once to refresh the manager executable path.")
    Run QuotePath(executable) " --extract " QuotePath(path),, "Hide"
    return 4
}

; Publishes a completed terminal result to the warm manager without filesystem polling or starting a new manager.
ReportTerminalResult(directory, succeeded, elapsed, detail) {
    try {
        key := GetShortcutRuntime().Key
        window := Integer(RegRead(key, "ShortcutWindow", "0")), pid := Integer(RegRead(key, "ShortcutPid", "0")), owner := 0
        token := RegRead(key, "ShortcutToken", "")
        if !window || token = "" || !DllCall("IsWindow", "ptr", window)
            return
        DllCall("GetWindowThreadProcessId", "ptr", window, "uint*", &owner)
        if owner != pid
            return
        body := "terminal`n" token "`n" (succeeded ? "1" : "0") "`n" Min(elapsed, 60000) "`n" directory "`n"
            . SubStr(StrReplace(StrReplace(detail, "`n", " | "), "`r", ""), 1, 2048)
        payload := Buffer(StrPut(body, "UTF-8")), data := Buffer(A_PtrSize * 3, 0)
        StrPut body, payload, "UTF-8"
        NumPut("uptr", 0x41584B32, "uint", payload.Size, data)
        NumPut("ptr", payload.Ptr, data, A_PtrSize * 2)
        result := 0
        DllCall("SendMessageTimeout", "ptr", window, "uint", 0x4A, "ptr", 0, "ptr", data,
            "uint", 0xA, "uint", 250, "uptr*", &result, "ptr")
    }
    catch Error as exception
        LogShortcut("activity", exception.Message)
}

; Keeps bounded shortcut timing/error diagnostics separate from the manager's concurrent log writer.
LogShortcut(action, message) {
    try {
        directory := EnvGet("LOCALAPPDATA") "\AutoHotkeyUX.Modern\state"
        DirCreate directory
        path := directory "\shortcuts.log"
        if FileExist(path) && FileGetSize(path) > 262144
            FileMove path, path ".previous", 1
        FileAppend FormatTime(, "yyyy-MM-dd HH:mm:ss") " [" action "] " message "`n", path, "UTF-8"
    }
    catch Error as exception {
        OutputDebug "AutoHotkeyUX shortcut log failed: " exception.Message
    }
}
