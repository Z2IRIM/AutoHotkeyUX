# Script Manager v1 与后台核心功能编码记录

日期：2026-10-07。基线：`88647ee`。开发分支：`codex/script-manager-core`。

## Goal / 达成的功能

完成真实脚本目录、运行/停止/重启、编辑与打开目录、搜索、运行状态更新及会话恢复；增加当前用户 Windows 登录静默启动、托盘恢复和资源管理器 Alt + 左键操作。已生成并启动单 EXE，登录启动与 Explorer Shortcuts 已为本机用户启用。

最终 EXE：`modern-shell/artifacts/win-x64/AutoHotkeyUX.Modern.exe`。

最终 EXE SHA-256：`4775A158D5DCCED9F9F8657ACF4E68DDC654678FC9ABD747E7F5DC154A3E0FAF`。

## 关键假设、业务语义与决定

- 交接文档原本推迟 Start with Windows；本次聊天明确要求，将登录静默启动纳入当前 Phase。
- 两个动作共用 Alt + 左键，按点击对象区分：目录打开该目录终端，支持的压缩文件解压到旁边的同名新目录，文件列表空白打开当前目录终端。导航树、地址栏、其他程序和虚拟目录不执行。
- “开机”落实为当前用户登录后启动，符合交互式桌面热键需要 Explorer/用户会话的前提；不是 Windows 服务或登录前任务。
- 终端优先使用 Windows Terminal 的明确 app alias 路径；不可用时使用系统 Windows PowerShell，并设置工作目录；不拼接路径到 shell 执行代码。
- 解压保留源包和已有目标，重名目标用 `(1)` 等新目录。先解压到本操作独有的临时目录，成功后提交；失败清理本次临时产物。ZIP、7z、RAR、固实 RAR、TAR、TAR.GZ/TGZ 都使用随 EXE 分发的托管库，无需外部解压程序。
- 仅管理本应用拥有的脚本进程。Stop 结束一个解释器，不终止它启动的终端、浏览器等子应用。
- 关闭窗口隐藏到托盘；退出管理器保留正在运行的脚本。再次启动按 PID + 精确开始时间 + runtime 可执行路径恢复所有权；不猜测外部脚本。
- 普通脚本的 Run at sign-in 仅保存选择，设置该选项不会立即执行脚本。内置 Explorer 脚本通过 Settings 独立控制，首次创建后允许编辑，不自动覆盖用户文件。
- 用户明确不需要 agent.md；按已有交接说明施工。用户原有未跟踪的 RelayPrompt.md 保留且未纳入本次 Git 提交。

## Architecture / 数据和生命周期

`ApplicationServices` 在应用级组合原有 runtime/integration/settings 与共享 catalog/execution/startup。Home 和 Scripts 读取同一个文件元数据 snapshot；页面不拥有扫描、解释器或 JSON 存储。

- `ScriptEntry`：Name、Path、LastModifiedUtc、Size，不缓存文件内容。
- `RunningScriptSession`：路径、PID、进程开始 UTC、管理器开始 UTC、Starting/Running/Stopping/Stopped/Failed、错误信息。
- `ScriptSessionStore`：仅保存 scriptPath、pid、processStartUtc；临时文件写入并 flush 后原子替换；坏 JSON 记录警告并返回空状态。
- `ScriptCatalogService`：单 watcher，300 ms debounce；目录不存在时监控最近现存父目录，目录重建或 watcher error 后恢复。Home/Scripts Loaded/Unloaded 成对订阅，回调通过 DispatcherQueue 更新 XAML。
- `ScriptExecutionService`：复用 AutoHotkeyIntegration → AutoHotkeyRuntimeLocator → EmbeddedAutoHotkeyRuntime；ArgumentList 传参，UTF-8 标准错误捕获；Run/Stop/Restart 串行化。Exited 回调只异步投递，不在 Process 自身事件锁内等待服务锁；退出完成幂等。
- `ScriptStartupService`：使用原有 registry settings 的 Modern 区保存路径选择与开关。逐项隔离启动失败，失效脚本不会阻止管理器或其他脚本启动。
- `SingleInstanceService`：当前用户/Windows 会话的 Mutex 和 EventWaitHandle，第二次启动唤回已有窗口，后台重复启动不弹窗。
- `TrayIconService`：原生 Windows notification area，打开窗口/退出管理器；Explorer 重建通知后重新添加图标。
- `ArchiveExtractionService`：独立 `--extract` 入口；验证路径穿越、Windows 特殊名称、链接、大小写文件冲突和 ZIP CRC；输出上限 20 GiB / 100000 条目。最终目录重命名遇到 Windows 分享/访问冲突时最多 4 次有限重试，不重新解压、不覆盖目标。

冻结的 EmbeddedAutoHotkeyRuntime、AutoHotkeyRuntimeLocator、runtime 打包准备脚本、App.xaml 视觉系统和原有工具实现均未重构。快捷脚本为独立 embedded resource，不修改 frozen runtime ZIP 结构。

## Modified / Added / Deleted files

新增：

- `.gitignore`、`package-source.ps1`
- `modern-shell/SCRIPT_MANAGER_V1_IMPLEMENTATION_PLAN.md`、本记录
- `modern-shell/Models/ScriptEntry.cs`、`RunningScriptSession.cs`
- `modern-shell/Services/ServiceDiagnostics.cs`、`ScriptCatalogService.cs`、`ScriptExecutionService.cs`、`ScriptSessionStore.cs`、`ScriptStartupService.cs`
- `modern-shell/Services/ApplicationServices.cs`、`WindowsStartupService.cs`、`SingleInstanceService.cs`、`TrayIconService.cs`、`WindowsCommandLine.cs`、`ArchiveExtractionService.cs`
- `modern-shell/Program.cs`、`MainWindow.Diagnostics.cs`
- `modern-shell/Pages/ScriptsPage.xaml`、`ScriptsPage.xaml.cs`
- `modern-shell/Resources/ExplorerShortcuts.ahk`
- `tools/ModernShell.SmokeTests/ModernShell.SmokeTests.csproj`、`Program.cs`

修改：

- `modern-shell/App.xaml.cs`、`AutoHotkeyUX.Modern.csproj`
- `modern-shell/MainWindow.xaml`、`MainWindow.xaml.cs`
- `modern-shell/Pages/HomePage.xaml.cs`、`SettingsPage.xaml`、`SettingsPage.xaml.cs`
- `modern-shell/Services/AutoHotkeyIntegration.cs`、`AutoHotkeySettings.cs`
- `modern-shell/README.md`、`THIRD_PARTY_NOTICES.md`

删除：没有删除项目源文件；移除了 Home 内重复扫描代码。

## Tests / Build / Run / Publish

| 检查 | 命令或证据 | 结果 |
| --- | --- | --- |
| Release build | `dotnet build .\modern-shell\AutoHotkeyUX.Modern.csproj -c Release -p:Platform=x64 --no-restore` | PASS，0 warning / 0 error |
| 风险驱动集成探针 | `dotnet run --project .\tools\ModernShell.SmokeTests\ModernShell.SmokeTests.csproj -c Release --no-restore -- .\.verification\ahk-runtime\AutoHotkey64.exe .\.verification\smoke-20261007-reviewed .\modern-shell\Resources\ExplorerShortcuts.ahk .\.verification\archive-fixtures` | PASS，45 项 |
| 真实 WinUI 源码运行 | `dotnet run --project .\modern-shell\AutoHotkeyUX.Modern.csproj -c Release -p:Platform=x64 --no-restore -- --verify-ui C:\DESKTOP\AutoHotkeyUX\.verification\ui-with-scripts.json` | PASS，真实数据、四页面、往返导航和搜索 |
| 单 EXE publish | `.\modern-shell\build-single-exe.cmd` | PASS，self-contained / unpackaged / single EXE |
| 发布包 runtime | `AutoHotkeyUX.Modern.exe --verify-runtime <report>` | PASS，runtime 2.0.29 与快捷脚本 embedded resource 均存在 |
| 发布包实际 UI | `AutoHotkeyUX.Modern.exe --verify-ui <report>` | PASS，CatalogCount=1、搜索、6 次页面加载、每次仅 1 个 sidebar selected |
| 小窗口动作布局 | 发布包 UI 报告 `MinimumSizeActionLayout=true` | PASS，150% 系统 DPI 下最小窗口页面宽约 397 DIP，动作无横向溢出 |
| 后台启动 | 最终 EXE `--background`，实际 AppWindow.IsVisible 状态 | PASS，WindowShown=false，Explorer 脚本 Running |
| 单实例唤回 | 再次运行同一 EXE，读取实际状态与进程列表 | PASS，仍为 PID 29624，WindowShown=true，仅 1 个 manager；脚本 PID 34180 未重复启动 |
| 当前用户启动项 | HKCU Run AutoHotkeyUX.Modern | PASS，指向最终 artifacts EXE + `--background` |
| 打包脚本 | Windows PowerShell 5.1.26100.9444 实际执行、PowerShell 7 执行与源码 ZIP 条目检查 | PASS；33 个源码/文档文件，排除项为 0；增量包在本机指定目录交付 |

45 项覆盖：空/缺失根目录、创建/修改/重命名/删除/重建、debounce、多个脚本、缺脚本/缺 runtime、中文空格路径、重复 Run、Stop/Restart、应用退出保留进程、精确恢复、错误开始时间/错误 runtime/失效 PID、坏 JSON、正常/非零/语法错误立即退出、12 次自然退出与 Stop 竞态、子应用保留、单个失效 startup 不阻塞后续脚本、AHK v2 语法、编辑命令引号、常用格式真实内容、重名目标、越界、ADS、保留名称、大小写冲突、CRC 和失败回滚。

过程中的 FAIL 与修复：

1. 沙箱 NuGet TLS 还原失败；正常网络权限还原成功，没有关闭证书验证。
2. 初始编译引用了错误的 AppWindow 属性和 reader overload；按 SDK/库实际 API 修正。
3. AHK 的 Error/error 大小写不敏感冲突；异常变量改名，真实解释器验证通过。
4. 恢复的 Process 不支持读取新启动对象的 StartInfo；恢复路径不读取该属性，并清理注册失败的句柄。
5. ExtractAllEntries 仅适合固实包/7z；非固实包使用逐 entry stream；ZIP 使用库的 CRC32Stream 验证。
6. 固实 RAR 提交目录遇到短暂 Windows 分享/访问冲突；只重试提交，实测 retry 后通过。
7. 独立只读审阅发现 Process 锁死锁/同步重入、失效 startup 阻断启动、关联开关忙碌状态吞操作；均已修复并针对性验证。
8. 有真实脚本时 UI 诊断未等待异步 TextChanged；修正诊断等待后，源代码及发布包 UI 均通过。诊断失败现在返回非零且不会弹出阻塞对话框。
9. Windows PowerShell 5.1 不会由 FileSystem 自动加载 ZipArchive 所在程序集；显式加载 System.IO.Compression 后实际执行通过，异常时释放句柄并清理本次不完整的 ZIP。

## Validation boundaries / 已知限制

- 真实鼠标 Alt + 左键在 Explorer 多标签页、不同列表视图、隐藏扩展名下的行为仍为 **UNVALIDATED**。代码已接入，AHK 解释器与 COM 索引静态核对通过；不能把它们等同于实际鼠标验收。
- Windows 真正重新登录/重启为 **UNVALIDATED**；Run 项、同等 --background 启动及窗口隐藏已验证。
- 实际托盘鼠标菜单、Explorer 重启后的托盘重建、最小化后点击恢复仍需人工交互验收。普通第二次启动恢复已验证。
- Window Spy、compiler 安装/编译、新建脚本按钮的实际创建、用户自选 editor 启动未在本次逐个执行；页面加载与 shared runtime 已验证，相关旧实现未改。
- 不支持密码输入或专门的分卷解压流程；路径/链接/资源限制拒绝后原包保留。
- 原始编译源码中的长期大文件没有无关拆分；新服务按职责分离。应用 UI 是原生 WinUI，不引入 Electron/WebView、插件或数据库。
- 便携 EXE 移动后需重新启动应用以更新 helper/启动命令；Windows 自身禁用 startup 的策略不由本应用绕过。
- 历史 startup-error.log 保留失败诊断；本次最终成功以最新验证 JSON 和运行状态为准。

## 数据库 / API / 兼容性 / 迁移

无数据库或远端 API 变化。新增用户级 Modern settings 与 Run 项、最小会话 metadata、可编辑 Documents 脚本。原 .ahk 关联和 editor 设置不自动重写；Edit fallback 不写注册表。新增 SharpCompress 0.50.3（MIT）并记录上游 notices，保持 .NET 8 / x64 / Windows App SDK 框架。

## Backup / Source package / Rollback

施工前文档与基线 source ZIP：`modern-shell/backups/2026-10-07-core/`；用户 RelayPrompt 副本也备份于此。

源代码打包脚本支持完整包和基于明确 Git baseline 的增量包，保持目录层级，排除 test、bin/obj、runtime 下载、node_modules、旧备份和私钥。增量包同步到：`C:\DESKTOP\srcpack_Area\AutoHotkeyUX\`；manifest 包含 base/head commit 和删除清单。

回滚先在 Settings 关闭 Start with Windows / Explorer shortcuts，再退出管理器；停止其他脚本由用户在 Scripts 明确选择。随后 revert 本次功能提交或恢复基线源码。保留 Documents 用户脚本、原始压缩包及已解压成功的目录；不 broad reset、不覆盖用户 RelayPrompt。仅回退源码不会自动删除 Run 项，因此必须先关闭启动开关。

## Remaining work / 下一阶段

先进行实际 Explorer 快捷键和 Windows 登录验收；根据具体失败视图调整命中识别。完成这一步后再讨论新的快捷动作或脚本体验，不提前扩展 marketplace、云同步、编辑器或插件系统。
