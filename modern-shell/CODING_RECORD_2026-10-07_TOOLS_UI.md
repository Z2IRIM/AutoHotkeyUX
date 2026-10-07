# Home 工具现代界面施工记录

日期：2026-10-07。Git 基线：`3780bf20d5a7ccf50e5e952048d6f164a0da1ce3`；施工分支：`codex/script-manager-core`。

## 目标与当前状态

用户批准交互预览后，将 Home 的 Window Spy、Compile、Documentation 改为应用内的 WinUI 二级页面。三个页面已完成真实功能接入并通过原生集成诊断；正在进行独立审阅和最终单 EXE 发布验收。

此前已完成的窗口放大、屏幕比例、模板等距和移除顶部搜索栏继续沿用。绿色 H 图标沿用用户已选择的版本。本任务没有数据库或公开 API 迁移。

## 关键假设和业务决定

- 三个工具由现有 Home 进入，侧栏 Home 保持选中，页内可以返回 Home；系统文件选择器继续使用原生交互。
- Window Spy 是只读窗口分析工具。保留最后有效的外部目标；Ctrl/Shift、Pause、离页、隐藏和最小化都会停止更新。可展开的窗口文本按需采集，受限或未响应控件显示不可读取。
- Compile 复用官方固定版本 Ahk2Exe 的无界面 CLI，使用已有内置 v2 运行时的 32/64 位基底。文件参数不经过 shell；默认拒绝覆盖，页内 Replace 仅授权当次完整目标路径。先在目标目录写临时产物，成功后提交；错误或取消保留原产物。
- 输出选择采用目录选择器及可编辑文件名，避免保存选择器提前创建或截断目标文件。编译成功后用户决定何时运行；应用只提供打开输出目录。官方 FileInstall 和构建指令仍然生效，页面对此给出说明。
- 编译任务由共享服务拥有，切页后继续，返回恢复状态；退出管理器取消并排空自己创建的编译进程。用户原有脚本不随管理器退出而结束。
- 文档复用原始 CHM 内容，后台解包至按版本及 CHM 哈希命名的私有缓存。原始 CHM 不被修改。缓存按文件哈希检查；不完整或损坏时重新生成。
- WebView2 仅映射此文档目录，提供原生返回、前进和目录，并给官方正文及 iframe 注入固定主题。外部 HTTPS 参考需要用户在页内选择后打开；禁止 file、javascript、自定义协议和任意本机目录。缺失运行时或加载失败在页内报告并允许重试。
- 文档导航曾因 WinRT 回调对象与 Core 属性值的引用比较失效而超时。修复为保留实际订阅的 Core 实例，事件绑定闭包同时检查页面代次、浏览器及取消状态；有效回调正常完成，过期回调无法复活页面。

## 文件与模块

修改现有入口/生命周期：`App.xaml.cs`、`MainWindow.xaml.cs`、`Pages/HomePage.xaml.cs`、`Services/ApplicationServices.cs`、`Services/AutoHotkeyIntegration.cs`。移除旧工具 GUI 启动入口，继续复用 PageHost 和共享服务。

新增三个页面：`Pages/WindowSpyPage.xaml(.cs)`、`Pages/CompilePage.xaml(.cs)`、`Pages/DocumentationPage.xaml(.cs)`；文档导航拆为 `DocumentationPage.Navigation.cs`，诊断拆为页面专用 partial 文件。

新增数据与服务：`Models/WorkspaceTool.cs`、`WindowSpySnapshot.cs`、`CompilerRequest.cs`、`DocumentationLocation.cs`；`Services/WindowInspectionNative.cs`、`WindowSpyService.cs`、`EmbeddedCompiler.cs`、`CompilerService.cs`、`DocumentationService.cs`、`DocumentationNavigationPolicy.cs`。

构建/资源：`AutoHotkeyUX.Modern.csproj`、`prepare-embedded-tools.ps1`、`ToolPayload/.gitignore`、`ThirdParty/Ahk2Exe.LICENSE.txt`、`Resources/DocumentationTheme.css`；根目录 `package-source.ps1` 排除工具二进制资源及施工临时记录。

显式原生诊断：`MainWindow.ToolDiagnostics.cs`、`MainWindow.SpyDiagnostics.cs`、`MainWindow.CompilerDiagnostics.cs`、`MainWindow.DocumentationDiagnostics.cs`、`Pages/HomePage.Diagnostics.cs`、`Pages/WindowSpyPage.Diagnostics.cs`、`Pages/DocumentationPage.Diagnostics.cs`。诊断只在用户显式传入 `--verify-tools <报告路径>` 时执行，不属于普通启动流程。

必要边界回归：`tools/tests/DocumentationRegression/DocumentationRegression.csproj`、`Program.cs`、`Stubs.cs`。直接链接生产文档服务代码，受控延迟 extractor 和隔离 junction 测试用于复现审阅发现；测试目录不进入源代码增量包。

文档：本记录、`TOOLS_UI_DESIGN_2026-10-07.md`、`TOOLS_UI_IMPLEMENTATION_PLAN_2026-10-07.md`。冻结的 RuntimeLocator / EmbeddedAutoHotkeyRuntime 及快捷键脚本未修改。用户未跟踪的 `RelayPrompt.md` 保留。

## 验证命令与已取得证据

```powershell
dotnet build modern-shell/AutoHotkeyUX.Modern.csproj -c Release -p:Platform=x64 --no-restore
AutoHotkeyUX.Modern.exe --verify-tools <绝对报告路径>
powershell -NoProfile -ExecutionPolicy Bypass -File modern-shell/prepare-embedded-tools.ps1 -ProjectDir <干净验证目录>
```

- Release 构建：0 warning / 0 error，日志 `.verification/tools-final-gate-build.log`。
- 导航 RED：未接入前实际 Home 入口仍返回 HomePage，诊断退出码 1；接入后三个入口均加载各自真实页面并可返回 Home。
- 原生 GREEN：`.verification/tools-final-native-ui.json` 为 Passed=true。真实 Explorer 窗口 PID/Class/坐标有效；暂停、隐藏、最小化及三次离页/重进通过。受控未响应窗口文本读取约 64ms，明确报告不可读取，没有阻塞 UI。
- 编译：中文/空格路径真实生成 AMD64 和 I386 PE；缺失 Include 退出码 50；默认拒绝覆盖和失败替换均保留原文件 SHA；重复任务拒绝；取消后自己拥有的进程、临时输出和 scratch 目录清理完成。未自动运行生成的 EXE。
- 文档：483 个解包文件通过缓存校验；受控损坏缓存可重建；原 CHM 的 SHA/大小保持不变。真实 WebView2 首页和 Run 正文/iframe 已应用主题，内部链接、返回、切页释放、隐藏释放和重新进入均通过。危险协议/路径被导航策略拒绝。
- 源码准备：Windows PowerShell 5 在干净验证目录下载固定官方工具包成功，SHA-256 与固定值一致。构建不会依赖开发机事先存在该包；最终用户打开 Compile 时不会下载它。
- 当前验证只覆盖本 Gate 影响边界，未重复无关快捷键全面回归或物理终端操作。
- 单 EXE 首轮：`.verification/tools-published-tools.json` 与 `tools-published-ui.json` 均 Passed=true。内嵌编译器 ZIP 哈希正确；原窗口为屏幕的 80%、宽高比一致，模板两侧间距 16 DIP，搜索移除、图标及原有页面导航通过。
- 审阅修复回归：`dotnet run --project tools/tests/DocumentationRegression/DocumentationRegression.csproj -c Release`，8/8 通过。修复前中间目录 junction、清单外 junction/文件、祖先 junction 及退出进程/暂存清理/停止接收全部失败，正常缓存通过；修复后全部通过。RED / GREEN / 归档位置日志为 `.verification/tools-review-regression-{red,green,final}.log`。

固定 Ahk2Exe 版本 `1.1.37.02a2`，ZIP SHA-256 `C29B8C3A5124850D79FC9E66E2CA79677C377D7F31631AD3022BA159C5D9E3BE`，EXE SHA-256 `E54A599B19BAA5C1688849BBAE7A9CF049EEFCCD4F704C67941B40DA13A625B2`。来源：[官方固定发布](https://github.com/AutoHotkey/Ahk2Exe/releases/tag/Ahk2Exe1.1.37.02a2)。许可证随源码提供。

## 风险、未验证事项与兼容性

- 受保护进程、自绘控件、UIA 深层结构的全部文本不保证可读；本阶段不引入 UIA 分析器。状态栏采用安全的限时文本读取，特定第三方状态栏可能不返回文字。
- 已验证最小脚本、错误、覆盖保护、取消和并发；未枚举所有复杂脚本/构建指令/第三方依赖。用户脚本的构建指令可能产生自己的副作用，取消只能保证应用拥有的编译进程和临时目录清理。
- 本机已有 WebView2。缺失运行时的错误处理代码已核查，未卸载本机运行时来做破坏性验证。更老 Windows、特殊安全策略和其它 DPI 显示器未做完整矩阵测试。
- 新增缓存位于 `%LOCALAPPDATA%\AutoHotkeyUX.Modern\tools\compiler`、`documentation` 和私有 WebView2 目录。它们可重建，不迁移用户脚本、数据库或配置。

## 执行裁定

1. 继续使用现有专用 `codex/script-manager-core` 分支：用户授权当前工作区，源码与可用 EXE 已备份且没有既有生产代码修改。判断错误的代价：仅回滚此 Gate，并保留 `RelayPrompt.md`。
2. 使用原生文件工具维护技能规定位置的施工 ledger：Git Bash 在普通和提升权限执行时都挂起。判断错误的代价：手工维护施工记录，不影响产品功能。
3. 使用真实 WinUI 导航 RED 及按风险选取的工具集成诊断：遵循用户不要非必要逐函数或全量回归的指令。判断错误的代价：无关模块回归不被穷举覆盖。
4. 审阅暂不判断的 UIA/自绘深层控件能力保持在 Win32 范围内：与已批准 Gate 一致。判断错误的代价：高级控件可能只有部分文本，需要后续扩展。
5. 脚本构建指令与第三方组合保持官方语义：用户显式开始编译且已有页内说明；不承诺通用沙箱。判断错误的代价：脚本自己的外部副作用或依赖需单独处理。
6. 不物理卸载 WebView2 或执行完整 Windows/DPI 矩阵：已核查缺失运行时的错误映射，真实证据覆盖当前机器。判断错误的代价：其它机器可能仍需兼容性修复。
7. 此 Gate 之前的窗口、模板、图标沿用其发布诊断证据：实现没有改动，不重复独立审阅。判断错误的代价：既有实现问题可能仍在本范围之外。

## 审阅、发布与源码包

独立只读审阅范围：`3780bf20..8da23e0`，fresh reviewer 使用最有能力模型；没有第二轮 reviewer。发现 0 Critical、2 Important、1 Minor。

已在同一次 fix pass 修复两个 Important：

1. 文档服务跟踪已接受的准备任务，退出时停止接收、取消并等待自身 extractor 和暂存目录清理；正常及启动异常退出均在 UI 释放前等待。受控延迟进程及目录清理 RED→GREEN。
2. 文档缓存校验整个实际文件树与 manifest 一致，拒绝祖先链、所有中间目录、文件及 manifest 上的 reparse point；清单外文件或链接均失败，避免 WebView2 映射未经验证的本机目录。隔离目录 RED→GREEN。

暂缓的 Minor：鼠标控件处于超过 512 项的子控件枚举截断位置之后时，ClassNN 仍可能显示错误编号。该罕见情况应后续改为“无法确定”，不影响本 Gate 的窗口选择器复制或常规窗口信息；本次按技能要求记录而未扩展修复。

最终发布验收、Git 修复提交和增量包信息待补充。

## 回滚

施工前源码和运行版本保存在 `modern-shell/backups/2026-10-07-tools-ui/`：`AutoHotkeyUX-source-20261007-164512.zip`、`AutoHotkeyUX.Modern.before-tools-ui.exe`、基线 JSON 和已批准预览。

正常退出管理器，将备份 EXE 复制到 `modern-shell/artifacts/win-x64/AutoHotkeyUX.Modern.exe` 后重新启动即可回退运行版本。源码通过本 Gate 的 Git 提交按需反向应用；不要整体 reset 脏工作区。工具/文档缓存可保留或只删除本应用相应私有缓存，原脚本、设置和启动配置不回滚。
