# 编码记录：可视化流程实用动作

## 本次目标和关键假设

将只有六个固定值动作的可视化创建器扩展为可组合的实际工作流。用户确认获取上下文、文件/终端/解压/剪贴板/窗口动作、前序结果引用和简单 If / Else，并授权实现；先提供了可交互 UI 预览，再修改产品代码。项目保持 WinUI 3/C# 与 AHK v2 架构。

基线 `0eb68d203fb23ff513b67e980cda5ce8e303d68f`。使用 `codex/visual-workflow-capabilities` 隔离分支；施工前保存完整跟踪源码 ZIP 和正式 EXE，保留主目录的 `RelayPrompt.md`、`modern-shell/debug.log`。无需建立 agent 文件（此前用户已明确）。

## 问题分析和业务决策

动作数量少只是表面；旧模型不能传递获取对象的结果，也不能按对象类型选择不同动作。此次采用带身份的结果引用和显式分支，避免自由表达式以及按序号引用造成重排错连。

- 动作从6种增加到16种，4个折叠分类，提供三个实用示例。所有动作仍通过原来的保存/运行 owner，保存不执行，保存后不自动重启。
- v1 字节和 JSON 保持兼容；引入新能力才升级 v2。打开不改文件。v2 验证所有结果字段、顺序、删除和分支作用域；最多24动作、3层条件。深比较/复制维护撤销和草稿正确性。
- 条件分支支持明确添加位置及本分支排序；含分支关闭跨分支拖拽，以避免含义模糊的隐式移动。可回到根序列追加动作。
- 程序固定参数保留目标程序命令行格式；结果作为一个参数时自动加引号，避免路径空格或引号破坏边界。工作目录可引用结果。窗口等待返回 HWND，支持超时；激活后输入再次检查窗口焦点。
- Explorer 和桌面点击复用既有 Shell/UIA/MSAA 函数，不模拟复制、不猜隐藏扩展名；单选获取限定 Explorer，桌面使用点击获取。空选、多选、虚拟对象明确失败。
- 终端复用现有队列、DPI/工作区夹取及鼠标上方定位，使用已有终端偏好。解压复用 `ArchiveExtractionService`，保持源文件、已有输出和档案路径保护。显式流程解压不受内置快捷解压开关控制；新增 headless 命令返回提取目录或失败报告，依赖本机 AutoHotkeyUX。
- 内置和完整受管可视化流程发生快捷键冲突时拒绝新运行、内置启用或冲突配置；不静默关闭既有脚本。一个共享事务门串行化配置提交与执行预检，防止并发穿透。同一文件重启先检查再停止。外部/手改 AHK 和绕过管理器运行不保证检测。
- 运行完整可视化流程时，将已验证的源码写到会话状态目录 `visual-runs/<GUID>/<原脚本名>.ahk` 并持有只读共享文件句柄，避免解释器加载前后被保存替换。工作目录仍是原脚本目录；保存/编辑针对原文件。会话持久化启动触发器、源码副本路径/哈希及 PID/启动时间；重开管理器恢复同一快照，成功 Restart 后更新。无可靠快照的旧运行流程要求显式 Restart/Stop，不猜测磁盘新版的旧注册键。源码副本使生成脚本的 `A_ScriptDir` 指向缓存；当前支持的动作没有依赖该目录。脚本退出清理精确文件；管理器退出时保留仍运行的副本。
- 共享 v3 支持资源是已发布 v2 生成器的版本化输入；未来核心升级必须提供新资源/语言版本，避免改变既有流程的确定性输出。任意旧 AHK 转译、循环、多个文件批量处理留到后续。

AHK 等待窗口超时单位和 HWND 返回值依据官方 [WinWait 文档](https://raw.githubusercontent.com/AutoHotkey/AutoHotkeyDocs/v2/docs/lib/WinWait.htm)；启动 PID 和重定向进程限制依据官方 [Run 文档](https://raw.githubusercontent.com/AutoHotkey/AutoHotkeyDocs/v2/docs/lib/Run.htm)，并以实际解释器验证。

## 文件与主要改动

- 模型：`Models/VisualFlowDocument.cs`、`FlowParameters.cs`。
- 语言/历史/存储：`Services/VisualFlowCodec.cs`、`VisualFlowValidation.cs`、`VisualFlowGenerator*.cs`、`VisualFlowTree.cs`、`VisualEditorSession.cs`、`VisualFlowStore.cs`。
- 核心复用及执行：`Resources/VisualFlow.Runtime.ahk`、`VisualExtractionCommand.cs`、`VisualFlowExamples.cs`、`VisualHotkeyConflicts.cs`、`ScriptExecutionService.cs`、`ShortcutPreferencesService.cs`、`ApplicationServices.cs`、`Program.cs`、项目资源声明。原有 Explorer 核心四个文件未修改。
- UI：`Pages/NewScriptPage.xaml` 及原有职责 partial，新增 `FlowActionCard.cs`、`NewScriptPage.Inputs.cs`、`NewScriptPage.Examples.cs`。保持填满内容视口及三种响应式布局。
- 诊断：原生能力 partial、`MainWindow.VisualDiagnostics.cs`；扩展 SmokeTests 专项入口和资源。使用说明、设计和施工计划同步更新。没有删除源码文件。

## 已取得验证证据

- RED：当前 v1 不能接收 v2，`Unsupported workflow version or identity`；实现后独立版本生成通过。
- `dotnet run --project tools/ModernShell.SmokeTests -- <runtime> <isolated-root> --capabilities-only`：9/9 边界组通过。覆盖三个示例重开、引用失效/前向/跨分支、嵌套历史、快捷键范围、显式解压成功/重试/失败、v1 字节兼容与外部编辑保护、实际 AHK 解析、真实测试 PID/窗口等待、Unicode 参数与引号、UNC/相对路径和超时日志。
- `--visual-only`：11/11 原有成对存储、失败回滚、pending、保存队列和真实解释器组通过。
- `--capability-review-only`：最终审阅发现的3个场景分别 RED；一轮修复后3/3 GREEN，覆盖启动源不可写、保存改键、保存中冲突检查、会话恢复、成功重启释放旧键、旧会话缺少快照保守处理、三层普通动作与历史、超深草稿不变、非活动 Explorer 鼠标作用域。
- 现有服务 smoke 入口（隔离目录/测试解释器）：42项通过，包含监视、生命周期和档案失败路径。
- 发布构建已成功；新增 nullable 诊断已处理。最终构建、审阅、交付哈希记录见本机 `.verification/visual-workflow-capabilities`。
- 回归首轮曾出现一次普通启动/重启探针的5秒标记超时；保留失败日志。只读检查源文件/会话、同一脚本两次独立启动、窄启动/重启诊断均成功；没有因此修改普通执行逻辑。最终相同实现完整11/11与42项验证通过，单次超时原因未确定。
- `AutoHotkeyUX.Modern.exe --verify-visual-creation <report>`：真实 WinUI 验证通过，16动作/4分类、分支添加/排序/撤销、输入及程序参数/工作目录引用、条件修改、v2 保存重开；ExecutionCount=0。宽/中/窄 Mode=0/1/2，整体 BodyX=0，两侧28 DIP，无视口错位。原生像素证据保存在同一目录。
- 交互预览通过2048/1024/320宽度及前序 PID、分支添加检查。

## 未验证事项和风险

本阶段没有触发用户真实热键、读写真实剪贴板或自动向用户应用输入内容；物理鼠标拖动、不同 Explorer 插件/虚拟目录及外部 AHK 组合仍需真实使用反馈。按程序名等待可能匹配已有窗口；需要精确身份的直接进程应引用 ProcessId/WindowId。操作系统焦点变化与输入之间存在极小竞态，现有检查不能取代操作系统提供的完整输入事务。

headless 解压每次创建一个短命进程，存在冷启动开销；既有内置快捷解压仍走暖管理器队列。v2 运行失败写入有界 `workflows.log` 并通知，提取写入既有 `manager.log`。

## 独立审阅与施工判断

一次 fresh-context 只读审阅：3项 Important，无 Critical/Minor。运行键从磁盘推断、第四层条件破坏草稿、鼠标/活动应用作用域交集均已在同一轮修复；不另派第二次审阅。审阅报告和 RED/GREEN 输出保存在 `.verification/visual-workflow-capabilities`。

本轮所有 Ruling 及代价：

1. 用户已确认范围和三列 UI 并授权“做吧”，展示交互预览后持续完成实施，不重复请求同一授权；若理解不符，代价是可逆的 UI 调整，保存本身不执行。
2. 显式流程解压采用 headless 既有服务，以获得确定的输出/失败结果并独立于快捷键开关；代价是每次解压的短命进程开销。
3. 采用最小充分验证，避免无变化的重复硬件/真实热键测试；代价是没有覆盖的实际设备/外部交互须由使用反馈补充。
4. 不将静态复用与解析检查称为真实 Explorer 首次点击/UIA/MSAA/物理拖动验证；代价是这些环境差异仍可能需要后续修正。
5. 不将测试自有窗口等待或发送前焦点检查称为用户窗口完整输入事务、真实终端位置验证；代价是焦点变化及实际终端环境仍有实用边界。
6. 外部自行启动或手改 AHK 不加入可靠快捷键保证，不能静默认领/停止它们；代价是用户应自行避免这类脚本与管理器快捷键冲突。
7. 当前生成器使用不可变版本输入，未来升级必须另行迁移验证；不能提前声称未知升级已兼容，代价是后续维护若改变 v2 输出须保留旧生成版本。

没有延期的 Minor。

## 兼容性、迁移和回滚

无数据库迁移和外部 API 变化；新增进程参数 `--flow-extract <source> <destination-or-empty> <new-report-path>`。v1 不批量迁移；用户编辑新能力并保存时才生成 v2，仍保留旧字节的失败恢复保护。

最终源码和正式 EXE 本地交付后，使用施工前 `.verification/visual-workflow-capabilities/source-before.zip` / `app-before.exe` 可恢复基线；恢复操作前备份当前文档和新生成 v2 脚本，旧应用无法编辑 v2 文档。不得删除用户创建的脚本、自动改回设置或批量停止 AHK。增量源码 ZIP 不含测试、依赖、构建产物及用户临时文件。
