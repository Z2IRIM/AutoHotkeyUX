; AutoHotkeyUX Explorer shortcuts v3 Shell

; Accepts Explorer or desktop under the cursor, including the first click on an inactive window.
GetShellGestureContext() {
    MouseGetPos ,, &window
    window := DllCall("GetAncestor", "ptr", window, "uint", 2, "ptr")
    if !window
        return false
    className := WinGetClass("ahk_id " window)
    return className = "CabinetWClass" ? {Window: window, Desktop: false}
        : className = "Progman" || className = "WorkerW" ? {Window: window, Desktop: true} : false
}

; Reuses the Shell COM connection rather than creating one for every click.
GetShellApplication() {
    static shell := ComObject("Shell.Application")
    return shell
}

; Reads the live folder from the visible Explorer tab or the combined public/user desktop.
GetShellView(context) {
    if context.Desktop
        return {Folder: GetShellApplication().Namespace(0), Directory: A_Desktop, Document: false}
    document := GetActiveExplorerDocument(context.Window)
    return document ? {Folder: document.Folder, Directory: document.Folder.Self.Path, Document: document} : false
}

; Caches the tab's browser, but reacquires its Document after every folder navigation.
GetActiveExplorerDocument(hwnd) {
    static lastWindow := 0, lastTab := 0, lastBrowser := false
    activeTab := 0
    for control in WinGetControlsHwnd("ahk_id " hwnd) {
        if WinGetClass("ahk_id " control) = "ShellTabWindowClass" && DllCall("IsWindowVisible", "ptr", control) {
            if activeTab
                return false
            activeTab := control
        }
    }
    if lastBrowser && lastWindow = hwnd && lastTab = activeTab {
        try {
            if lastBrowser.HWND = hwnd
                return lastBrowser.Document
        }
        catch Error
            lastBrowser := false
    }
    matches := []
    for window in GetShellApplication().Windows {
        if window.HWND != hwnd
            continue
        if activeTab {
            browser := ComObjQuery(window, "{4C96BE40-915C-11CF-99D3-00AA004AE837}",
                "{000214E2-0000-0000-C000-000000000046}")
            tab := 0
            ComCall(3, browser, "ptr*", &tab)
            if tab != activeTab
                continue
        }
        matches.Push(window)
    }
    lastWindow := hwnd, lastTab := activeTab
    lastBrowser := matches.Length = 1 ? matches[1] : false
    return lastBrowser ? lastBrowser.Document : false
}

; Caches Windows UI Automation and its raw walker; temporary element references remain scoped to each hit.
GetAutomation() {
    static automation := ComObject("{FF48DBA4-60EF-4201-AA87-54103EEF594E}",
        "{30CBE57D-D9D0-452A-AB13-7AC5AC4825EE}")
    static walker := 0
    if !walker {
        pointer := 0
        ComCall(16, automation, "ptr*", &pointer)
        walker := ComValue(13, pointer, 1)
    }
    return {Automation: automation, Walker: walker}
}

; Identifies the actual item under the click, excluding navigation trees, column headers and other controls.
GetFileViewHit(x, y, desktop := false) {
    uia := GetAutomation(), element := 0
    ComCall(7, uia.Automation, "int64", (x & 0xFFFFFFFF) | (y << 32), "ptr*", &element)
    name := "", selected := false
    try {
        loop 16 {
            if !element
                break
            controlType := GetAutomationProperty(element, 30003)
            if controlType = 50023 || controlType = 50024 || controlType = 50034 || controlType = 50035
                return false
            if name = "" && (controlType = 50007 || controlType = 50029) {
                name := GetAutomationProperty(element, 30005)
                selected := GetAutomationProperty(element, 30079)
            }
            if controlType = 50008 || controlType = 50025 || controlType = 50028 || controlType = 50033 {
                className := GetAutomationProperty(element, 30012)
                if className = "UIItemsView" || GetAutomationProperty(element, 30011) = "ItemsView"
                    || (desktop && className = "SysListView32")
                    return {Kind: name = "" ? "blank" : "item", Name: name, Selected: selected}
                if className = "SHELLDLL_DefView"
                    return GetAccessibleFileHit(x, y)
            }
            parent := 0
            ComCall(3, uia.Walker, "ptr", element, "ptr*", &parent)
            ObjRelease(element)
            element := parent
        }
        return false
    }
    finally {
        if element
            ObjRelease(element)
    }
}

; Uses the native Shell file view's MSAA hit when its UIA provider exposes no ItemsView children.
GetAccessibleFileHit(x, y) {
    pointer := 0, child := Buffer(24, 0)
    if DllCall("oleacc\AccessibleObjectFromPoint", "int64", (x & 0xFFFFFFFF) | (y << 32),
        "ptr*", &pointer, "ptr", child, "int") < 0 || !pointer
        return false
    accessible := ComValue(9, pointer, 1)
    try {
        if NumGet(child, 0, "ushort") != 3
            return false
        childId := NumGet(child, 8, "int")
        role := accessible.accRole(childId)
        if role = 34
            return {Kind: "item", Name: accessible.accName(childId), Selected: (accessible.accState(childId) & 2) != 0}
        return role = 33 ? {Kind: "blank", Name: "", Selected: false} : false
    }
    finally DllCall("oleaut32\VariantClear", "ptr", child)
}

; Reads string, integer and boolean UIA properties and always releases their VARIANT resources.
GetAutomationProperty(element, propertyId) {
    value := Buffer(24, 0)
    ComCall(10, element, "int", propertyId, "ptr", value)
    try {
        type := NumGet(value, 0, "ushort")
        if type = 8 {
            pointer := NumGet(value, 8, "ptr")
            return pointer ? StrGet(pointer, "UTF-16") : ""
        }
        if type = 11
            return NumGet(value, 8, "short") != 0
        return type = 3 ? NumGet(value, 8, "int") : ""
    }
    finally DllCall("oleaut32\VariantClear", "ptr", value)
}

; Uses a selected hit first, then a bounded unique lookup so hidden extensions cannot select a same-name folder.
ResolveHitPath(view, hit) {
    if hit.Selected && view.Document {
        selected := view.Document.SelectedItems()
        if selected.Count = 1 && MatchesHitName(selected.Item(0), hit.Name)
            return selected.Item(0).Path
    }
    result := "", started := A_TickCount
    for item in view.Folder.Items {
        if MatchesHitName(item, hit.Name) {
            if result != "" && StrLower(result) != StrLower(item.Path)
                return ""
            result := item.Path
        }
        if A_TickCount - started > 180
            return ""
    }
    return result
}

; Compares Shell display names without constructing or guessing a filesystem path.
MatchesHitName(item, name) {
    itemName := item.Name
    if StrLower(itemName) = StrLower(name)
        return true
    SplitPath itemName,,, &extension, &stem
    if extension = "" || StrLower(stem) != StrLower(name)
        return false
    try return StrLower(item.ExtendedProperty("System.ItemNameDisplay")) = StrLower(name)
    catch Error
        return false
}

; Measures read-only Shell resolution and cache reuse without generating input events.
WriteShellDiagnostics(report) {
    text := "version=3`n"
    for window in GetShellApplication().Windows {
        started := A_TickCount
        document := GetActiveExplorerDocument(window.HWND)
        cold := A_TickCount - started, started := A_TickCount
        loop 20
            GetActiveExplorerDocument(window.HWND)
        text .= "explorer=" window.HWND " resolved=" (document ? 1 : 0) " coldMs=" cold " warm20Ms=" (A_TickCount - started) "`n"
    }
    text .= "desktopItems=" GetShellApplication().Namespace(0).Items.Count "`n"
    FileAppend text, report, "UTF-8"
}
