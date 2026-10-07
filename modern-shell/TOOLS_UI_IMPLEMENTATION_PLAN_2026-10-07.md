# Home 内嵌工具 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** 从 Home 打开可用的现代 Window Spy、Compile、Documentation 二级页，所有显示与反馈留在 APP 内。

**Architecture:** 延续 MainWindow.PageHost 和共享 ApplicationServices。三个 WinUI 页分别调用只读窗口采集、官方编译器 CLI、离线帮助缓存服务；不把旧窗口塞进主窗口。后台任务有明确所有者，UI 离开后不得写入过期页面状态。

**Tech Stack:** C#/.NET 8、WinUI 3/Windows App SDK 2.5.1、内置 AutoHotkey v2.0.29、固定 Ahk2Exe、系统 HTML Help 解包及 WinUI WebView2。

**Spec:** [TOOLS_UI_DESIGN_2026-10-07.md](./TOOLS_UI_DESIGN_2026-10-07.md)，用户已确认。

**进度：** 完整 Gate 已完成：真实工具页、独立审阅与修复、单 EXE 原生验收、本机部署、Git 及增量源码包。执行证据、取舍与遗留项见 `CODING_RECORD_2026-10-07_TOOLS_UI.md`。

## Global Constraints

- 复用现有 MainWindow 页面承载、Home 入口、主题资源和内置 AutoHotkey v2.0.29。
- 保留单 EXE、私有运行时、静默自启动、托盘及现有脚本所有权逻辑。
- 三个工具属于 Home 的二级页面；侧栏 Home 保持选中，页内提供返回 Home。
- 页面可见时约每 250ms 更新；离开页、暂停、窗口隐藏/最小化时停止采集。
- 不修改已冻结的运行时下载/解压/发现架构，不要求系统安装 AHK，也不弹安装器。
- 不自动运行产物。不增加 UPX、MPRESS 或其它可选压缩器依赖。
- 不静默安装 WebView2 或退回旧帮助弹窗。
- 当前任务不再次执行上一任务已完成的关机。
- 使用一个完整 Gate；以下为 Gate 内部施工清单，不拆成多个用户任务。仅做风险匹配的最小充分验证，不默认全量回归。

## Review Focus

1. 外部窗口失去响应或权限不足：采集必须限时，并清楚显示缺失字段；不阻塞 UI。
2. 快速切页、隐藏窗口、初始化尚未完成：采集/浏览器不得重复启动，过期回调不得复活页面。
3. 中文和空格路径、已存在的产物、编译失败：参数不经过 shell，失败不得改写旧产物。
4. 重复编译、取消、退出管理器：最多一个编译任务，只停止自身创建的子进程，及时清理临时产物。
5. 损坏的文档缓存、缺失 WebView2、外部或 file:// 链接：检测实际内容完整性，错误留在页内，禁止任意文件/协议导航。

## 文件与职责

| 文件（均在 modern-shell 下，另有明确标注） | 职责 |
| --- | --- |
| Models/WorkspaceTool.cs | WindowSpy、Compile、Documentation 导航枚举 |
| Models/WindowSpySnapshot.cs | 采集选项与窗口/坐标/控件快照，纯数据 |
| Services/WindowInspectionNative.cs | Win32 只读调用与限时文本读取 |
| Services/WindowSpyService.cs | 组合快照、排除 APP 自身、控件枚举预算 |
| Pages/WindowSpyPage.xaml / .cs | 已批准的采集界面、暂停/复制、可见性生命周期 |
| Models/CompilerRequest.cs | 架构枚举、编译请求和结果 |
| Services/EmbeddedCompiler.cs | 固定资源校验、私有版本缓存 |
| Services/CompilerService.cs | 单任务编译、取消、输出原子提交 |
| Pages/CompilePage.xaml / .cs | 文件选择、已有产物确认、编译状态和日志 |
| prepare-embedded-tools.ps1 / ToolPayload/.gitignore | 固定官方工具资源准备；独立于运行时流水线 |
| ThirdParty/Ahk2Exe.LICENSE.txt | 官方编译器许可记录 |
| Services/DocumentationService.cs | 官方 CHM 后台解包及完整缓存 |
| Services/DocumentationNavigationPolicy.cs | 页面内导航与文件/协议边界 |
| Resources/DocumentationTheme.css | 文档字体、配色、代码块与主项目主题衔接 |
| Pages/DocumentationPage.xaml / .cs | 原生工具栏、WebView2 加载/重试/关闭 |
| MainWindow.xaml.cs、Pages/HomePage.xaml.cs | 统一路由和原入口改接应用内页面 |
| Services/ApplicationServices.cs、App.xaml.cs | 共享服务所有权与退出编译清理 |
| Services/AutoHotkeyIntegration.cs | 删除已由内部页面取代的三个旧 GUI 启动函数 |
| AutoHotkeyUX.Modern.csproj | 编译器及 CSS 嵌入，排除工具下载缓存的重复打包 |
| MainWindow.ToolDiagnostics.cs、MainWindow.Diagnostics.cs | 实际工具导航及采集/编译/帮助集成验证 |
| CODING_RECORD_2026-10-07_TOOLS_UI.md | 实施结果、决定、验证、风险和回滚记录 |

修改与新增函数添加简短职责注释。预计超过约 300 行的多职责页面，按生命周期/显示、采集/进程、导航策略拆分；不复制通用运行时或脚本执行逻辑。

## Gate：三个工具全部可用后发布

### A. 备份与导航接入

**接口：** `enum WorkspaceTool { WindowSpy, Compile, Documentation }`；Home 增加 `Action<WorkspaceTool> openTool`；MainWindow 将枚举映射到现有 `NavigateTo` 页路由。

- [x] 在 `modern-shell/backups/2026-10-07-tools-ui/` 保存当前源码、设计/计划及已工作 EXE，记录 Git 基线；保留未跟踪 `RelayPrompt.md`。
- [x] 扩展现有显式 UI 诊断：对三个页面类型、Loaded 状态、Home 选中状态及返回 Home 做断言。先确认当前旧入口不满足内部页面断言。
- [x] 在既有 PageHost 内实现三页路由；全阶段完成前每个中间状态可构建，最终不保留空页或旧 GUI 兜底。
- [x] 页面复用 SurfaceCard、PageTitle、SectionTitle、主题刷子和原生控件。已批准的交互预览为样式/信息层级依据，不重新征求同一视觉方案。

### B. Window Spy

**接口：** `WindowSpySnapshot? WindowSpyService.Capture(WindowSpyOptions options, nint excludedWindow, nint previousTarget, CancellationToken cancellationToken)`。快照包含 TargetWindow、Title、Class、ProcessName、ProcessId、MouseScreen/Window/Client、PixelColor、ControlClassNN/Text/Bounds、WindowBounds/ClientBounds、StatusText、VisibleText、AllText、ReadWarning。

- [x] 使用 GetCursorPos、WindowFromPoint/GetAncestor、GetForegroundWindow、GetWindowThreadProcessId、窗口/客户区矩形及 GetPixel。读取控件文本使用 SendMessageTimeout；每项限时 20ms，总枚举预算约 100ms、最多 512 个控件；每次调用检查取消。
- [x] 默认 250ms 周期、最多一项采集在途，在后台 Capture；主线程仅绑定结果。目标是本 APP 时沿用最后有效外部目标，初始没有目标时显示引导文本。
- [x] Follow mouse 关闭时使用活动外部窗口；Ctrl/Shift 与 Pause 冻结更新。可展开的窗口文本只在展开时采集，避免无意义的大范围文本读取。
- [x] Loaded 绑定宿主窗口可见性事件并启动；Unloaded/隐藏/最小化/暂停停止定时器、取消本代任务。代次检查阻止晚到结果更新，恢复时只启动一次。
- [x] 复制选择器包括 ahk_class、ahk_exe、ahk_pid、ahk_id；异常用原有 InfoBar 展示，日志不记窗口内容。
- [x] 实际诊断：读取真实 Explorer 窗口；若没有资源管理器窗口则用真实桌面窗口并注明。断言有有效 HWND/PID/边界、屏幕坐标转换正确、暂停/离页/隐藏后采集停止，重复进入只有一个采集器。未响应/无法读取字段在受控采集验证中保持 20ms/100ms 预算。

### C. Compile

**接口：** `Task<string> EmbeddedCompiler.EnsureReadyAsync(CancellationToken cancellationToken)`；`Task<CompilerResult> CompilerService.CompileAsync(CompilerRequest request, IProgress<string>? output, CancellationToken cancellationToken)`；`Task CompilerService.CancelAndDrainAsync()`。

- [x] 固定 `Ahk2Exe1.1.37.02a2/Ahk2Exe1.1.37.02a2.zip`，ZIP SHA-256 `c29b8c3a5124850d79fc9e66e2ca79677c377d7f31631ad3022ba159c5d9e3be`，EXE SHA-256 `e54a599b19baa5c1688849bbae7a9cf049eefccd4f704c67941b40da13a625b2`。资源准备脚本使用固定官方 URL，验证后嵌入；私有缓存位于应用本地工具目录，不写入 AHK runtime 目录。
- [x] 解压到唯一暂存目录，验证 EXE 后提交缓存；检测越界文件名/路径，不信任未校验的旧缓存。资源下载不发生在最终用户打开 Compile 时。
- [x] 请求使用完整 .ahk/.exe 路径及可选 .ico，明确 32/64 位；使用现有 `Integration.FindRuntime("32-bit"/"64-bit")` 选择基底。校验文件存在、扩展名、不同源/目标及输出目录。
- [x] 命令通过 ProcessStartInfo.ArgumentList 构造，UseShellExecute=false、CreateNoWindow=true；`/silent verbose` 放在首位，再指定 `/in`、`/out`、`/base`、可选 `/icon`、`/compress 0`、`/cp UTF-8`。先启用 silent，防止参数解析过程中出现旧消息框。
- [x] 同一服务只接受一项构建；日志有长度上限。输出写目标目录内唯一临时 .exe，检查退出码和产物后移动；默认拒绝覆盖。页内 Replace 确认仅对当前完整目标路径生效，路径改变后清除确认。
- [x] 页内保留已批准的提示：编译可能处理 FileInstall 和脚本构建指令，产物由用户决定何时运行。默认建议一个未占用的输出文件名。
- [x] 离开编译页允许任务继续，返回恢复状态；Cancel/退出管理器取消并等待自身进程清理，失败保留旧产物。不得运行新产物。
- [x] 实际诊断仅用项目验证目录：中文/空格路径的最小 v2 脚本产物；缺失 Include 错误；已有目标拒绝覆盖并保持原字节；重复提交拒绝；取消/退出清理临时文件。断言 stdout/stderr 或退出码进入页内反馈，不出现编译器 GUI。

### D. Documentation

**接口：** `Task<DocumentationLocation> DocumentationService.EnsureReadyAsync(CancellationToken cancellationToken)`，返回 RootDirectory、HomeRelativePath=`docs/index.htm`、Version；导航策略仅接受当前离线虚拟主机或用户明确选择的 HTTPS 外部参考。

- [x] 从现有运行时目录取得 `AutoHotkey.chm`。首次调用系统 `hh.exe -decompile <staging> <chm>`，隐藏运行、30s 限时；不使用任何显示帮助窗口的参数。解包到私有版本/CHM 哈希缓存，校验 `docs/index.htm`、`docs/static/content.js`、`docs/static/source/data_toc.js` 与 `docs/static/theme.css` 存在后提交。退出码 0 本身不足以证明解包成功。
- [x] 已完成接入条件探查：本机系统命令解包生成 483 个文件，原 CHM 保留；首次受限执行曾返回 0 但没有文件，必须靠上述文件断言识别失败并允许重试。
- [x] WinUI UI 线程异步创建 WebView2 环境，浏览器用户目录使用 APP 私有目录。映射 `https://ahk-docs.invalid/` 至唯一文档目录，DenyCors；不映射其它本机目录，不开放宿主对象或执行任意协议。
- [x] 复用离线官方目录/内容/搜索资产，添加原生 Back/Forward/Contents、加载状态与 Retry。正文注入固定主题 CSS，保留文档内容与链接；主题值通过序列化传递，禁止拼接用户输入为 JavaScript。
- [x] 页面内处理 NewWindowRequested。外部 HTTPS 链接先以页内提示标识并由用户选择在当前面板打开；file://、javascript:、自定义协议及非授权顶层来源取消导航。
- [x] Unloaded 取消该代 UI 初始化、注销事件并关闭 WebView2；重新进入创建新控件。缓存服务可复用同一完成结果；缺失 WebView2 或缓存失败用页内错误与重试，不启动浏览器/旧帮助弹窗。
- [x] 实际诊断：离线初次加载、内部 Run/热键链接与返回、离开/重进；导航策略拒绝 file:// 与其它协议；不完整缓存可重建；缺失浏览器运行时的错误映射可核查。原 CHM SHA/大小不变，没有帮助窗口。

### E. 集成、审查、发布

- [x] 删除 Home 已不再调用的 `OpenWindowSpy/OpenCompiler/OpenDocumentation` 旧 GUI 函数，核查没有剩余引用；不动 RuntimeLocator、EmbeddedAutoHotkeyRuntime 或关联安装器的其它功能。
- [x] 在 ApplicationServices 组合工具服务；App 正常退出先排空已接受的解压，再取消/排空自己的编译，再释放窗口/服务，仍保留用户脚本。异常退出路径也取消本 APP 工具子进程。
- [x] `dotnet build modern-shell/AutoHotkeyUX.Modern.csproj -c Release -p:Platform=x64 --no-restore`：0 错误。显式 `--verify-tools <reportPath>` 使用自身页面和服务完成 B/C/D 验收并返回 Passed=true；原 UI 诊断继续覆盖标题栏/模板/导航。
- [x] 代码审查重点为 Review Focus 五项、异步生命周期和文件所有权。修复阻断问题后停止无信息增益的重复验证，不为样式小改新增全量测试。
- [x] 发布到独立 staging 目录，核对单 EXE 体积/哈希与工具资源；使用新发布包做必要加载检查，再正常退出当前管理器、替换 artifacts/win-x64 EXE 并恢复主窗口。
- [x] 回传 CODING_RECORD 与 Git 提交。用现有 `package-source.ps1 -Incremental -BaseRef <施工前基线>` 输出 `C:\DESKTOP\srcpack_Area\AutoHotkeyUX`，确认 manifest 不包含工具 ZIP、测试或构建产物。原有开机/快捷键设置与脚本 PID 不被重复启动。

## 执行方式与回滚

推荐由当前 agent 在本会话实施（Native），完成整个 Gate 后一次独立审查：三页共享导航、退出和服务所有权，当前执行者已有上下文，减少跨任务接口往返。若用户选择分代理实施，则按上述独立页面/服务边界派发，共享导航/应用生命周期仅一个 owner 修改。

设计、施工方向与更新后的交互预览均已获用户确认。本会话按推荐的 Native 方式完成整个 Gate，最终一次独立审查。无数据库/API 迁移；失败可还原施工前 EXE，私有工具/文档缓存可重建，用户脚本与设置不回滚。

## 官方依据

- [Ahk2Exe 固定发布及校验值](https://github.com/AutoHotkey/Ahk2Exe/releases/tag/Ahk2Exe1.1.37.02a2)
- [官方命令行 silent 行为](https://github.com/AutoHotkey/AutoHotkeyDocs/blob/v1/docs/Scripts.htm#SlashGuiSilent)
- [Windows CHM 无界面解包](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/htmlhelp/to-decompile-a-compiled-help-file-from-the-command-line)
- [WinUI 3 WebView2 初始化与生命周期](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/webview2)
- [WebView2 本地内容目录映射](https://learn.microsoft.com/microsoft-edge/webview2/concepts/working-with-local-content)
