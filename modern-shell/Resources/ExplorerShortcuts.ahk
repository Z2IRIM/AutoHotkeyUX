; AutoHotkeyUX Explorer shortcuts v1
#Requires AutoHotkey v2.0
#SingleInstance Ignore
#NoTrayIcon

CoordMode "Mouse", "Screen"

; Allows syntax validation without installing a live hotkey in a verification process.
if A_Args.Length && A_Args[1] = "--validate"
    ExitApp 0

#HotIf IsExplorerWindow()
~!LButton::HandleExplorerClick()
#HotIf

; Restricts the gesture to the active Explorer window rather than global Alt-clicks.
IsExplorerWindow() {
    hwnd := WinActive("ahk_class CabinetWClass")
    if !hwnd
        return false
    MouseGetPos ,, &underMouse
    return underMouse = hwnd
}

; Confirms the exact hit item and active tab before opening a terminal or extracting an archive.
HandleExplorerClick() {
    try {
        hwnd := WinActive("ahk_class CabinetWClass")
        MouseGetPos &x, &y
        hit := GetFileViewHit(x, y)
        if !hit || !KeyWait("LButton", "T0.5")
            return
        MouseGetPos &releasedX, &releasedY
        if Abs(releasedX - x) > 6 || Abs(releasedY - y) > 6 || !WinActive("ahk_id " hwnd)
            return
        Sleep 80 ; Let the native click update Explorer selection; the hit element must itself be selected.
        document := GetActiveExplorerDocument(hwnd)
        if !document
            return
        if hit.Kind = "blank" {
            directory := document.Folder.Self.Path
            if DirExist(directory)
                OpenTerminal(directory)
            return
        }
        if !IsHitSelected(hit.Element)
            return
        selected := document.SelectedItems()
        if selected.Count != 1
            return
        item := selected.Item(0)
        if StrLower(item.Name) != StrLower(hit.Name)
            return
        path := item.Path
        if DirExist(path)
            OpenTerminal(path)
        else if FileExist(path) && RegExMatch(path, "i)\.(zip|7z|rar|tar|tar\.gz|tgz)$")
            ExtractArchive(path)
    }
    catch Error as exception {
        LogShortcutError(exception)
        TrayTip "The shortcut could not complete. See manager.log for details.", "AutoHotkey Explorer shortcuts", 3
    }
}

; Gets UI Automation from Windows itself; no external AHK library is required.
GetAutomation() {
    static automation := ComObject("{FF48DBA4-60EF-4201-AA87-54103EEF594E}",
        "{30CBE57D-D9D0-452A-AB13-7AC5AC4825EE}")
    return automation
}

; Walks the hit element's ancestors and accepts only Explorer's file ItemsView, excluding navigation trees.
GetFileViewHit(x, y) {
    automation := GetAutomation()
    element := 0, walker := 0, itemElement := 0
    ComCall(7, automation, "int64", (x & 0xFFFFFFFF) | (y << 32), "ptr*", &element)
    ComCall(16, automation, "ptr*", &walker) ; IUIAutomation::get_RawViewWalker
    hitName := "", inFileView := false
    try {
        loop 16 {
            if !element
                break
            controlType := GetAutomationProperty(element, 30003)
            if controlType = 50023 ; Tree navigation is never the current directory's file area.
                break
            if !itemElement && (controlType = 50007 || controlType = 50029) {
                hitName := GetAutomationProperty(element, 30005)
                ObjAddRef(element)
                itemElement := element
            }
            if GetAutomationProperty(element, 30012) = "UIItemsView"
                || GetAutomationProperty(element, 30011) = "ItemsView" {
                inFileView := true
                break
            }
            parent := 0
            ComCall(3, walker, "ptr", element, "ptr*", &parent)
            ObjRelease(element)
            element := parent
        }
        if !inFileView
            return false
        if !itemElement
            return {Kind: "blank"}
        wrapped := ComValue(13, itemElement, 1)
        itemElement := 0 ; Ownership transferred to the ComValue.
        return {Kind: "item", Name: hitName, Element: wrapped}
    }
    finally {
        if element
            ObjRelease(element)
        if itemElement
            ObjRelease(itemElement)
        if walker
            ObjRelease(walker)
    }
}

; Reads a small UIA VARIANT property and always releases BSTR/VARIANT resources.
GetAutomationProperty(element, propertyId) {
    value := Buffer(24, 0)
    ComCall(10, element, "int", propertyId, "ptr", value)
    try {
        type := NumGet(value, 0, "ushort")
        if type = 8 {
            text := NumGet(value, 8, "ptr")
            return text ? StrGet(text, "UTF-16") : ""
        }
        return type = 3 ? NumGet(value, 8, "int") : ""
    }
    finally DllCall("oleaut32\VariantClear", "ptr", value)
}

; Checks the clicked element, not merely a possibly stale SelectedItems snapshot.
IsHitSelected(element) {
    pattern := 0
    ComCall(16, element, "int", 10010, "ptr*", &pattern) ; SelectionItemPattern
    if !pattern
        return false
    try {
        selected := 0
        ComCall(6, pattern, "int*", &selected)
        return selected != 0
    }
    finally ObjRelease(pattern)
}

; Resolves the visible ShellTabWindowClass on Win11 and matches it against each shell browser's own window.
GetActiveExplorerDocument(hwnd) {
    activeTab := 0
    for control in WinGetControlsHwnd("ahk_id " hwnd) {
        if WinGetClass("ahk_id " control) = "ShellTabWindowClass" && DllCall("IsWindowVisible", "ptr", control) {
            if activeTab
                return false ; Ambiguous tab ownership is intentionally refused.
            activeTab := control
        }
    }
    matches := []
    for window in ComObject("Shell.Application").Windows {
        if window.HWND != hwnd
            continue
        if activeTab {
            browser := ComObjQuery(window, "{4C96BE40-915C-11CF-99D3-00AA004AE837}",
                "{000214E2-0000-0000-C000-000000000046}")
            tab := 0
            ComCall(3, browser, "ptr*", &tab) ; IShellBrowser::GetWindow
            if tab != activeTab
                continue
        }
        matches.Push(window.Document)
    }
    return matches.Length = 1 ? matches[1] : false
}

; Quotes a filesystem argument with Windows trailing-backslash rules and without a command shell.
QuotePath(path) {
    return '"' RegExReplace(path, "(\\+)$", "$1$1") '"'
}

; Opens Windows Terminal when its app execution alias exists, otherwise opens Windows PowerShell in the directory.
OpenTerminal(directory) {
    terminal := EnvGet("LOCALAPPDATA") "\Microsoft\WindowsApps\wt.exe"
    if FileExist(terminal) {
        try {
            Run QuotePath(terminal) " -d " QuotePath(directory), directory
            return
        }
        catch Error as exception {
            LogShortcutError(exception)
        }
    }
    powershell := A_WinDir "\System32\WindowsPowerShell\v1.0\powershell.exe"
    Run QuotePath(powershell) " -NoLogo -NoExit", directory
}

; Uses the modern EXE's bounded extraction service; a nonzero exit produces visible action feedback.
ExtractArchive(path) {
    executable := RegRead("HKCU\Software\AutoHotkey\Modern", "ExecutablePath", "")
    if !FileExist(executable)
        throw Error("The modern manager EXE cannot be found. Open AutoHotkeyUX once to refresh its path.")
    result := RunWait(QuotePath(executable) " --extract " QuotePath(path),, "Hide")
    if result
        throw Error("Extraction failed with exit code " result ". Source archive was preserved.")
    TrayTip "Extracted into a new folder beside the archive.", "AutoHotkey Explorer shortcuts"
}

; Keeps shortcut errors in the same per-user diagnostic log as the manager.
LogShortcutError(exception) {
    try {
        directory := EnvGet("LOCALAPPDATA") "\AutoHotkeyUX.Modern\state"
        DirCreate directory
        FileAppend FormatTime(, "yyyy-MM-dd HH:mm:ss") " [ExplorerShortcut] " exception.Message "`n", directory "\manager.log", "UTF-8"
    }
    catch Error as logError {
        OutputDebug "AutoHotkeyUX diagnostic write failed: " logError.Message
    }
}
