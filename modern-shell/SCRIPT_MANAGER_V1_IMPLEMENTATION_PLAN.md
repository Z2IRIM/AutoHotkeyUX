# SCRIPT MANAGER V1 IMPLEMENTATION PLAN

日期：2026-10-07。施工基线：`88647ee`，本地 `modern-wpf-shell`；保留用户未跟踪的 `RelayPrompt.md`。

**Goal:** 完成可实际运行的脚本管理，并实现本次新增的 Windows 登录静默启动和资源管理器 Alt + 左键操作。

**Architecture:** 保留 WinUI 3、Mica、集成标题栏和现有嵌入式 AutoHotkey 架构。应用级服务持有目录监控和进程会话，页面只展示状态和调用服务；脚本文件仍以 Documents/AutoHotkey 为事实来源。

**Tech Stack:** C# / .NET 8 / WinUI 3 / Windows App SDK / AutoHotkey v2 / SharpCompress 0.50.3。

**Spec:** 用户交接说明（仓库根目录 `RelayPrompt.md`）及本次聊天中新增的登录启动、终端和解压需求。用户已要求审计后直接实现，本计划按两个 Phase 在本会话执行，不额外逐项申请确认。

## 冻结约束与本次决定

- 不改框架、嵌入式 runtime materialization 或系统 AutoHotkey 安装方式；不依赖 PATH 和 .ahk 关联。
- 保留当前 shell/style、125% 桌面视觉尺度；新增 Scripts 导航及设置区域，先提供交互预览。
- 文件系统是 script source of truth；不持久化脚本内容、不引入数据库。
- 仅管理本应用启动或通过严格持久化身份恢复的脚本。Stop 不结束子进程。
- 交接说明原先推迟 Start with Windows，本次明确请求将其纳入当前范围。
- Alt + 左键：点文件夹打开该目录的终端；点支持的压缩包解压至同级同名新目录；点文件列表空白打开当前目录终端。其他应用、导航树、地址栏和虚拟目录不触发。
- Windows 登录时 `--background` 启动，不激活主窗口；托盘可打开窗口/退出管理器。关闭窗口隐藏到托盘；退出管理器保留已运行脚本并保存会话。
- 脚本逐个选择 Run at sign-in；Explorer Shortcuts 独立开关。安装内置脚本时不覆盖已存在的用户文件。
- ZIP / 7z / RAR / TAR / TAR.GZ / TGZ：托管解压库随 EXE 发布；本期不做密码输入和分卷交互。
- 解压不删除源文件，不合并/覆盖已有目录；先写独立临时目录，成功再重命名，失败清理本操作产物。拒绝越界路径、链接、Windows 特殊路径；限制 100000 条目和 20 GiB 解压数据。
- 用户明确不需要新增 agent.md，使用当前交接说明施工。

## 源码审计与可复用链路

| 责任 | 当前位置 | 本次使用方式 |
| --- | --- | --- |
| 主窗口/导航 | MainWindow.xaml(.cs) | 保留 chrome，增加 Scripts，注入应用级服务 |
| 当前最近脚本扫描 | Pages/HomePage.xaml.cs / RefreshRecentScripts | 移至共享 ScriptCatalogService；Home 取前三条 |
| runtime 获取 | AutoHotkeyIntegration.FindRuntime → AutoHotkeyRuntimeLocator.FindPreferred → EmbeddedAutoHotkeyRuntime.EnsureReady | Execution 复用此链路，不写死 runtime 版本 |
| 注册表设置 | Services/AutoHotkeySettings.cs | 复用现有 HKCU 设置层级，Modern 区存新设置 |
| 编辑器 | AutoHotkeySettings.ReadEditorCommand / SettingsPage | 沿用已有 shell edit 命令；空配置仅 fallback Notepad，不改关联 |
| 打开目录 | AutoHotkeyIntegration.RevealFile | Scripts 直接复用 |
| 创建脚本 | AutoHotkeyIntegration.CreateScript / NewScriptPage | 保持行为；监控刷新共享目录 |
| 发布 | csproj / build-single-exe.cmd | 新 AHK 脚本单独嵌入，不触碰冻结 runtime ZIP |

## 数据模型与生命周期

- `ScriptEntry`：Name / Path / LastModifiedUtc / Size，扫描 metadata 时容忍文件瞬间消失。
- `RunningScriptSession`：ScriptPath / ProcessId / ProcessStartUtc / StartedUtc / State / Error。状态 Starting → Running → Stopping → Stopped，失败为 Failed。
- `ScriptExecutionService`：串行化 Run/Stop/Restart；ArgumentList 传路径；`/ErrorStdOut=UTF-8` 捕获错误；绑定 Exited 后主动检查，处理立即退出。Stop 只 Kill 被追踪且开始时间仍匹配的单个进程。
- `ScriptSessionStore`：state/managed-sessions.json 仅 scriptPath、pid、processStartUtc；临时文件 + 原子替换；腐败 JSON 记录诊断并返回空。恢复同时检查文件、PID、开始时间、runtime executable；未知身份拒绝操作。
- `ScriptCatalogService`：应用级单实例 watcher，关注创建/删除/重命名/修改；300ms debounce；丢失根目录时监控现存父目录，错误后重建；页面 Loaded/Unloaded 成对订阅，所有控件变更 DispatcherQueue 投递。
- `ScriptStartupService`：选择的脚本路径与内置快捷脚本开关保存在现有 Modern settings；会话恢复后按选择启动，重复 Run 不重复进程。
- `WindowsStartupService`：当前用户 Run 项指向当前 EXE + --background；不提权、不修改系统级启动项；开关与失败反馈可见。
- `TrayIconService` / `SingleInstanceService`：窗口隐藏后仍可恢复；新实例向已有实例请求显示窗口，后台重复启动不弹窗。
- `ArchiveExtractionService`：独立 `--extract <path>` 命令，不启动管理器 UI，不污染脚本会话；失败返回非零并写诊断。

## Phase 1：脚本管理垂直切片

新增 Models/ScriptEntry.cs、Models/RunningScriptSession.cs；Services/ScriptCatalogService.cs、ScriptExecutionService.cs、ScriptSessionStore.cs、ServiceDiagnostics.cs；Pages/ScriptsPage.xaml(.cs)。修改 HomePage、MainWindow 和 AutoHotkeyIntegration（复用编辑器）。

- [ ] 编码前保存文档和源代码基线备份，并展示 Scripts/Settings 交互预览。
- [ ] 共享 catalog、会话存储、运行/停止/重启，处理重复启动、立即退出、失效 PID 和坏 JSON。
- [ ] 接入真实 Scripts 列表、搜索、状态、操作；Home 不保留第二套扫描。
- [ ] 最小集成验证使用临时目录和本应用启动的 AutoHotkey 测试进程：空目录、create/modify/rename/delete、debounce、缺文件/缺 runtime、Run/Stop/Restart/自然退出、恢复和错误 PID。

## Phase 2：静默启动与资源管理器快捷操作

新增应用服务组合、Startup/Tray/SingleInstance/ArchiveExtraction 服务，以及 Resources/ExplorerShortcuts.ahk；修改 App、Settings、csproj；新增源代码备份打包脚本和编码记录。

- [ ] 资源管理器用 UI Automation 点击命中 + Shell 当前活动 tab 匹配确认对象，禁止根据旧 selection 猜路径。
- [ ] 实现当前用户登录启动、逐脚本启动选择、托盘隐藏/恢复和单实例。
- [ ] 解压验证常用格式、中文/空格路径、已有目录不覆盖、穿越条目、损坏包和失败产物清理。
- [ ] `dotnet build .\modern-shell\AutoHotkeyUX.Modern.csproj -c Release -p:Platform=x64`：退出码 0。
- [ ] `dotnet run --project .\modern-shell\AutoHotkeyUX.Modern.csproj -c Release -p:Platform=x64`：进程存活并无新增启动错误。
- [ ] `modern-shell\build-single-exe.cmd`：生成可启动 single EXE，运行时为内嵌 v2，所有新资源随包发布。
- [ ] 验证 `--background` 无可见主窗口；手动启动恢复同一进程。真实鼠标点击、托盘手势和 Windows 重新登录若无法实测，明确记为 UNVALIDATED。

## Review Focus / 风险

- PID 重用、退出事件与启动注册交错：以开始时间和仍持有的 Process 身份检查保护；测试伪造开始时间不得恢复。
- 页面导航重复订阅：应用级 watcher，Loaded/Unloaded 配对；实际 WinUI 导航需独立验收。
- Win11 Explorer 多标签页、文件扩展名隐藏：匹配活动 tab 和命中名称；不确认则不执行；真实 Explorer 各视图待真机验收。
- 解压包路径穿越、链接、大小写碰撞：目标约束、禁止链接、CreateNew；针对穿越和已有目标执行失败测试。
- 开机命令路径变动与启动开关被 Windows 禁用：运行时同步当前 EXE 路径，设置读取实际 Run 注册值；说明移动 EXE 后需重开设置。
- 重建 runtime payload 会影响运行中的 runtime：本次快捷脚本采用独立资源，不改已有 runtime materialization。

## Git、备份与回滚

文档及基线 zip 已备份到 modern-shell/backups/2026-10-07-core；新增 package-source.ps1 只打包源码/文档，不打包 bin/obj/runtime 下载缓存、测试和备份。完成后保留本地 Git 记录，不 push、不 merge 共享分支。回滚先关闭登录启动及 Explorer 快捷开关，停止相关脚本，然后回退本次明确提交；保留 Documents 用户脚本和原始压缩包。
