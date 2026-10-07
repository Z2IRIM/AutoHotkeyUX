# 创建和管理新的 AutoHotkey 脚本

当前应用使用 **AutoHotkey v2**。新脚本是独立的 `.ahk` 文件；可以通过应用创建、运行、停止、重启和选择登录时运行。已有的资源管理器快捷键在 Settings 内配置。

## 从应用创建

1. 打开 **New Script**，输入名称，例如 `OpenDocuments`。
2. Location 保留默认的 Windows 文档目录下 `AutoHotkey` 文件夹。**Scripts 目前只管理这个目录顶层的 `.ahk` 文件**；选择其他位置创建的文件不会自动加入列表。
3. 选择 **Hotkeys** 模板来创建快捷键；选择 **Automation** 创建一次执行的自动化；Blank 从空白开始。
4. 点击 **Create script**。应用自动补 `.ahk`，重名时使用新文件名，不覆盖已有文件，并在资源管理器中选中它。
5. 返回 **Scripts**，点击这个脚本的 **Edit**。使用当前配置的编辑器，未配置时使用记事本。
6. 写好动作并保存文件，点击 **Run**。改过代码后点击 **Restart**，新代码才会进入运行中的脚本；Stop 结束该脚本。
7. 需要登录时运行，勾选这个脚本的 **Run at sign-in**，同时在 **Settings → Background and shortcuts** 打开 **Start with Windows**。勾选不会立即运行脚本；当管理器下次启动时才执行。关闭应用窗口会隐藏到托盘。

## 一个可复制的示例

仓库附带 [`examples/OpenDocuments.ahk`](examples/OpenDocuments.ahk)。将其内容复制到新建脚本即可。按 **Ctrl + Alt + D** 打开 Windows 文档文件夹，避免与内置 Alt + 鼠标快捷键重叠。

```ahk
#Requires AutoHotkey v2.0
#SingleInstance Ignore

; Ctrl + Alt + D 打开文档文件夹。
^!d::OpenDocuments()

; 使用 Windows 已知文档目录，支持目录重定向和路径中的空格。
OpenDocuments() {
    try Run 'explorer.exe "' RTrim(A_MyDocuments, "\") '\."'
    catch Error as exception {
        OutputDebug "[OpenDocuments] " exception.Message
        MsgBox "无法打开文档文件夹：`n" exception.Message, "Open Documents"
    }
}
```

`^` 表示 Ctrl，`!` 表示 Alt，`+` 表示 Shift，`#` 表示 Windows 键。把 `^!d` 改成其他组合即可改变快捷键；同时运行的脚本应避免占用相同组合。语法参见 [AutoHotkey v2 快捷键说明](https://www.autohotkey.com/docs/v2/Hotkeys.htm) 和 [Run 说明](https://www.autohotkey.com/docs/v2/lib/Run.htm)。这些链接对应内置离线文档的 Hotkeys 与 Run 主题。

## 创建类似资源管理器快捷键的功能

简单动作只需一个脚本，例如启动程序、输入固定文本、按组合键处理当前应用。需要“只在某个程序里触发”，可使用 v2 的 `#HotIf` 限定上下文。

资源管理器的“识别点击文件 → 判断类型 → 解压或打开终端”还涉及 Shell 目标解析、程序定位和失败处理。不要直接修改 `Explorer Shortcuts.ahk` 来加入无关功能：创建独立文件，并为它选一个不冲突的快捷键。修改内置脚本或其辅助模块后，应用会保留这些修改，可能无法继续自动升级或应用内置配置。

以后可以直接描述四点：**触发方式、在哪个程序里触发、要执行的动作、失败时怎么办**。例如：“在浏览器按 Ctrl + Alt + C，把当前选中文字复制成 Markdown 引用”。据此可以生成独立 v2 脚本，再按上面的流程加入应用。

## 脚本与现代界面的关系

添加 `.ahk` 文件会加入现有的脚本管理流程，**不会自动生成专属 WinUI 设置页**。脚本如果需要少量参数，可以先在文件顶部定义变量；需要与当前应用一致的交互设置页时，再为这个功能添加配置模型和页面，复用现有管理服务。普通脚本不必编译成 EXE，也不必更改应用核心。

## 出错时

- Scripts 中查看真实运行状态与错误，检查文件是否仍在原路径。
- 使用 **Home → Documentation** 打开应用内离线 v2 文档；旧版 v1 的命令语法不能直接照搬到 v2。
- 管理器诊断：`%LOCALAPPDATA%\AutoHotkeyUX.Modern\state\manager.log`。
- 内置资源管理器动作诊断：同目录 `shortcuts.log`，以及 **Settings → Configure shortcuts → Recent activity**。
- 示例失败会显示原因，并向 `OutputDebug` 写入简短错误。示例只随源码提供，不会自动安装、运行或选择开机启动。
