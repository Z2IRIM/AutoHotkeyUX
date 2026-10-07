# 创建和管理新的 AutoHotkey 脚本

当前应用使用 **AutoHotkey v2**。New Script 通过可视化流程生成独立 `.ahk` 文件和 `.ahk.flow.json` 流程数据；Scripts 管理运行、停止、重启和登录时运行。已有的资源管理器快捷键在 Settings 内配置。

## 从应用创建

1. 打开 **New Script**，输入名称，例如 `OpenDocuments`。
2. Location 保留默认的 Windows 文档目录下 `AutoHotkey` 文件夹。**Scripts 目前只管理这个目录顶层的 `.ahk` 文件**；选择其他位置创建的文件不会自动加入列表。
3. 点击固定在首位的 **Trigger**，在右侧 Properties 选择 **Keyboard shortcut**，勾选 Ctrl、Alt，选择 D；或者选择 **When script starts**。Active window 可限制为一个程序的文件名，例如 `explorer.exe`。这个条件作用于整个流程。选择 **Only this application** 后必须填写合法的程序名；清空时保留限定状态并禁用保存。
4. 点击左侧 **Action library** 的分类：Context 获取上下文，Files 操作文件、终端和剪贴板，Windows 启动/等待/激活程序，Input / logic 输入、等待和条件。共16种动作。点击动作，在 Properties 填参数；**Value source** 可选 Fixed value 或前面步骤的某个结果。程序可填写固定命令行参数，或将前序路径/文本作为一个自动加引号的参数；Working directory 可指定固定或前序目录。
5. 普通顺序可拖动或用箭头排序。含 If / Else 时，先选 **IF TRUE / ELSE · Add here** 再从库添加；箭头仅在当前分支内排序，**Append after workflow** 回到主序列。最多24个动作（含分支），条件最多3层。删除来源步骤、错误排序或跨分支引用会禁用保存并要求修正；Undo/Redo 保留最多50次修改。**Generated AutoHotkey code** 默认折叠。导航不会丢草稿，关闭整个管理器前仍需保存。
6. 点击 **Create script**。应用补 `.ahk`；源文件或流程数据重名都会另取名称。创建完成留在应用内，**不会执行动作**。返回 **Scripts** 点击 **Run**；停止用 Stop。后续点击 **Edit** 可返回此可视化流程，修改后 **Save changes**，再用 **Restart** 应用到正在运行的脚本。
7. 需要登录时运行，勾选这个脚本的 **Run at sign-in**，同时在 **Settings → Background and shortcuts** 打开 **Start with Windows**。勾选不会立即运行脚本；当管理器下次启动时才执行。关闭应用窗口会隐藏到托盘。

## 一个可复制的示例

**New Script → Examples** 提供三个可编辑的实用流程：

- **Terminal / extract at clicked object**：Alt + 左键，获取 Explorer/桌面点击对象；文件夹打开终端，支持的压缩包解压。空白文件区域表示当前目录。终端沿用 Settings 中位置/大小；解压默认放在压缩包旁的新目录，保留源文件和已有输出。
- **Copy selected object's full path**：Ctrl + Alt + C，读取 Explorer 中单个选中对象，将完整路径写到剪贴板。空选、多选、虚拟对象明确报错；桌面对象请改用点击获取。
- **Launch program, wait, activate and type**：Ctrl + Alt + N，打开记事本，最多等待10秒，激活找到的窗口后输入文本。可将等待目标改为前序 ProcessId 或其他程序名；现代记事本等重定向进程的应用适合按程序名等待。

每个示例仍需 Create script，然后在 Scripts 中 Run。Alt + 左键默认也属于内置 Explorer 快捷键；请先在 Settings 更换内置快捷键，或把示例改成 Alt + Shift + 左键。应用检测内置功能和受管完整可视化流程之间的冲突，并拒绝冲突运行/配置；无法保证识别外部或手改 AHK 的快捷键。

保存改键后，正在运行的进程仍使用启动时快照，必须 Restart 才应用新键。运行源码副本与快照跟随 PID/启动时间持久化；旧运行流程若没有可靠快照，先 Restart/Stop 才能确认快捷键不会冲突。普通或手改代码仍按原路径运行。终端设置在可视化脚本启动时读取，修改后对该流程 Restart。

无需写代码的例子：保留默认 **Ctrl + Alt + D** 触发器，添加 **Open folder → Documents**，再添加 **Wait → 500 ms**，保存后在 Scripts 点击 Run。按快捷键即可打开文档目录。

仓库也保留普通代码示例 [`examples/OpenDocuments.ahk`](examples/OpenDocuments.ahk)。普通 `.ahk` 的 Edit 使用当前配置的外部编辑器，未配置时使用记事本。下面的手写示例独立于可视化创建；如果将其内容覆盖到生成的文件，应用会识别源代码已变动并保护它，不再静默重生成。

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

资源管理器的“识别点击文件 → 判断类型 → 解压或打开终端”可从上面的示例修改，也可自行搭建：Get clicked object → If / Else 的 Is folder → Open terminal；Else 中再判断 Is archive → Extract archive。各输入选择第一次获取对象的 Path 结果。核心 Shell 识别和终端定位复用已有实现，无需修改 `Explorer Shortcuts.ahk`。

等待窗口用1–60000毫秒；超时、解压失败或目标丢失会停止当前触发，后续步骤不执行。激活后输入会再次检查目标是否仍活动。解压动作是显式流程操作，不受内置快捷解压开关控制；需要 AutoHotkeyUX 安装在本机并至少打开过一次。其他基本动作可用标准 v2 解释器运行。当前不支持循环、多个文件批处理或任意旧 AHK 转译。

以后可以直接描述四点：**触发方式、在哪个程序里触发、要执行的动作、失败时怎么办**。例如：“在浏览器按 Ctrl + Alt + C，把当前选中文字复制成 Markdown 引用”。据此可以生成独立 v2 脚本，再按上面的流程加入应用。

## 脚本与现代界面的关系

添加 `.ahk` 文件会加入现有的脚本管理流程，**不会自动生成专属 WinUI 设置页**。脚本如果需要少量参数，可以先在文件顶部定义变量；需要与当前应用一致的交互设置页时，再为这个功能添加配置模型和页面，复用现有管理服务。普通脚本不必编译成 EXE，也不必更改应用核心。

可视化编辑依靠旁边的 `.ahk.flow.json`。移动或备份可视化脚本时，应一起保留这两个文件。当前支持回编本功能生成的完整文件对；**任意已有 AHK 转可视化、循环和任意条件分支尚未实现**，后续单独开发。没有流程数据的普通脚本继续使用代码编辑器。

保存时核对源文件字节及流程文件版本/字节。发现手工修改、损坏数据、未知版本或未完成保存时会保留文件并说明原因。Scripts 的提示提供 **Edit code**，可继续手工编辑。不要手动修改流程数据来绕过保护。

另一个实例更新了合法文件对后，再从 Scripts 点击 **Edit** 会载入新版本；当前有未保存草稿时先选择保留或丢弃。文件内容没有变化时，重新 Edit 会继续保留当前草稿。

两个文件不是系统级原子事务。更新期间发生普通写入失败会尝试恢复旧字节；断电或恢复失败会保留 `<name>.ahk.flow.pending` 目录，包含 `source.next`/`workflow.next`，更新时还包含 `source.before`/`workflow.before`。先备份这些材料，再比对并手动恢复一对匹配的源文件和流程数据；确认恢复后移走 pending 目录。应用不会猜测哪一份应覆盖用户文件；pending 未处理前拒绝运行/重启这个流程。新建中断后的再次创建会另取名称。

## 出错时

- Scripts 中查看真实运行状态与错误，检查文件是否仍在原路径。
- 使用 **Home → Documentation** 打开应用内离线 v2 文档；旧版 v1 的命令语法不能直接照搬到 v2。
- 管理器诊断：`%LOCALAPPDATA%\AutoHotkeyUX.Modern\state\manager.log`。
- 内置资源管理器动作诊断：同目录 `shortcuts.log`，以及 **Settings → Configure shortcuts → Recent activity**。
- 新版可视化流程的运行失败：同目录 `workflows.log`，包含失败步骤；提取过程还写入 `manager.log`。失败会显示非阻塞通知。
- 示例失败会显示原因，并向 `OutputDebug` 写入简短错误。示例只随源码提供，不会自动安装、运行或选择开机启动。
