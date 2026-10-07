#Requires AutoHotkey v2.0
#SingleInstance Ignore

; Ctrl + Alt + D opens the Windows Documents known folder.
^!d::OpenDocuments()

; Uses the redirected Documents folder and keeps the Explorer argument safe at a drive root.
OpenDocuments() {
    try Run 'explorer.exe "' RTrim(A_MyDocuments, "\") '\."'
    catch Error as exception {
        OutputDebug "[OpenDocuments] " exception.Message
        MsgBox "无法打开文档文件夹：`n" exception.Message, "Open Documents"
    }
}
