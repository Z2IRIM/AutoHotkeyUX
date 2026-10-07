# 编码记录：资源管理器快捷键配置与脚本创建指南

## 目标与依据

用户已确认交互预览 `autohotkey-shortcut-settings.html`，本阶段一次完成 Settings 内的资源管理器快捷键设置、真实运行时配置、最近动作及交付。沿用已批准的绿色 H 图标、80% 屏幕尺寸与屏幕宽高比。用户已明确不需要建立 agent.md 文件树。

源码基线：`58822392df5b99e9450de2233fa9cc9aeba8c8fd`；施工分支 `codex/shortcut-preferences`，隔离工作树 `C:\Users\Z\.codex\worktrees\shortcut-preferences\AutoHotkeyUX`。保留主目录未跟踪的 `RelayPrompt.md`。

## 业务语义和决策

- 终端、解压分别有开关与快捷键；共享 Alt + 左键时按目录/压缩包类型区分。提供四种受支持的 Alt 组合，Ctrl + 左键用于资源管理器多选，禁止作为这两个动作的配置。
- 终端可选 Auto、Windows Terminal、Windows PowerShell，支持鼠标上方或屏幕工作区居中。宽高和间距按显示缩放转换为物理像素，再限制到点击位置所在的屏幕工作区。
- 解压可在源文件旁或选择的已有目录创建独立子目录；不删除压缩包、不覆盖已有目录。路径/链接/校验和/解压预算保护保持原有实现。
- 草稿在保存前不改变正在使用的设置；Discard 恢复已保存值。切换页面保留草稿。停止状态也可保存，下次启动时生效，提示不声称已激活。
- 运行中的 v3 AHK 通过一次性令牌和受管 PID/精确启动时间验证接收完整配置，先校验，再注册热键、一次写入快照、更新缓存；确认拒绝时保留原设置。启停和保存复用一个串行入口，保存期间锁定表单，退出等待已接受的配置事务。
- `SendMessageTimeout` 超时不等于接收方回滚。发送方查询 revision 并读回存储；无法确认则持续显示不确定状态，导航、Discard 和再次保存相同值都不能绕过确认。重试先做只读 revision 查询，确认后才允许新的配置事务，不自动重放配置或解压。存储读回也通知界面，避免展示过时值。[微软关于超时的说明](https://devblogs.microsoft.com/oldnewthing/20110915-00/?p=9643)
- 已接受的解压任务冻结输出目录；修改设置或关闭动作只影响新任务，队列仍正常完成。
- 最近动作仅本次管理器会话保留 50 条，由实际完成事件更新，显示成功/失败、路径、耗时和输出/错误；不新增数据库或轮询日志。
- 仅自动升级哈希确认未编辑的 v1/v2；备份根脚本的精确字节，保留 v2 辅助模块。用户修改过的根文件或模块保持原样；不兼容的运行脚本不能被悄悄配置。
- PowerShell 窗口报告的是控制台客户端 PID，启动器返回的是 conhost PID；增加父进程关系判断，只在有限的新窗口发现阶段执行，且排除原有窗口。
- Window Spy 无法在有限枚举内证明 ClassNN 序号时显示 `Unavailable`，不构造看似精确的数字。
- 示例使用 Windows 已知文档目录，不假设 Downloads 的实际位置。新增脚本会进入现有管理流程，不会自动产生专属 WinUI 页面。

## 修改/新增文件

| 范围 | 文件与职责 |
| --- | --- |
| 模型（新增） | `Models/ShortcutPreferences.cs`、`ShortcutActivity.cs`：不可变配置、revision、动作记录 |
| 服务（新增） | `Services/ShortcutPreferenceCodec.cs`、`ShortcutPreferencesRuntime.cs`、`ShortcutPreferencesService.cs`、`ShortcutActivityService.cs`：边界校验、真实 AHK 通道、保存和有限历史 |
| 服务（修改） | `ApplicationServices.cs`、`AutoHotkeySettings.cs`、`ScriptStartupService.cs`、`ExplorerShortcutInstaller.cs`、`ArchiveExtractionService.cs`、`ShortcutCommandService.cs`：既有服务组合、迁移、串行保存和冻结解压参数 |
| 窗口检查（修改） | `WindowInspectionNative.cs`、`WindowSpyService.cs`：ClassNN 证明条件 |
| UI（新增） | `Pages/ShortcutSettingsPage.xaml`、`.xaml.cs`、`.Placement.cs`、`.Activity.cs`、`.Diagnostics.cs`，`MainWindow.PreferenceDiagnostics.cs` |
| UI/启动（修改） | `MainWindow.xaml.cs`、`Pages/SettingsPage.xaml`、`.xaml.cs`、`App.xaml.cs`、`Program.cs`：Settings 二级路由、冷启动解压读配置、退出等待、诊断命令 |
| 诊断隔离（修改） | `SingleInstanceService.cs`、`WindowsStartupService.cs`：仅显式诊断使用私有通道与注册表键，正常启动行为不变 |
| AHK（新增/修改） | 新增 `Resources/ExplorerShortcuts/v3/{Shell,Actions,Preferences}.ahk`；根脚本和 csproj 指向 v3，v2 原文件保留 |
| 文档/示例 | 新增设计、实施计划、本记录、`SCRIPT_CREATION_GUIDE.md`、`examples/OpenDocuments.ahk`；更新 README |
| 定向检查 | 既有 SmokeTests 入口/资源映射、`ShortcutChecks.cs`，新增 `PreferenceChecks.cs`、`PreferenceRuntimeChecks.cs`、`PreferenceFailureChecks.cs`、精确 v2 根脚本 fixture |

未删除源码；未引入新依赖。现有 `package-source.ps1` 已支持全量/增量源码包和排除测试、依赖与构建输出，继续复用。

## 验证命令与结果

所有状态/进程测试使用独立临时目录和生成的 `HKCU\Software\AutoHotkeyUX.Verify\<GUID>`，不修改用户配置或终止用户脚本。

1. 初始 RED：`dotnet run --project tools/ModernShell.SmokeTests/ModernShell.SmokeTests.csproj -c Release -- <runtime> <isolated-root> --preferences-only`，固定输出目录断言失败；实现后最初四项 GREEN。
2. 定向集成：`dotnet run --project tools/ModernShell.SmokeTests/ModernShell.SmokeTests.csproj -c Release --no-restore -- <runtime> <isolated-root> <repo> --preferences-only`：最终 **41 项通过**（`service-final.log`）。覆盖完整 Unicode 协议、重复/未知/错误值拒绝、目的地不存在、碰撞保留、迁移/编辑保护、保存失败保留、排队目录冻结、失败隔离、50 条上限、真实 AHK revision/令牌/身份验证、不兼容脚本保留，以及下述两处审阅回归。
3. WinUI 编译：`dotnet build modern-shell/AutoHotkeyUX.Modern.csproj -c Release -p:Platform=x64 --no-restore`：**0 警告、0 错误**。首轮布局容器 `IsEnabled` 编译失败已改为锁定输入控件。
4. 原生页面：`AutoHotkeyUX.Modern.exe --verify-preferences <report>`：**Passed=true**。验证保存、Discard、禁用 Ctrl + 左键、位置示意、当前会话活动、导航保留草稿、最小窗口重排；既有无顶部搜索栏、模板等距与 80% 窗口检查也通过。
5. 真实 PowerShell：`AutoHotkey64.exe /ErrorStdOut=UTF-8 <script> --verify-terminal <folder> 1920 1800 <report> <preferences>`。首轮发现失败，确认窗口 PID=PowerShell 子进程后修复。最终 **1440×780 物理像素**（960×520 DIP，150%），窗口底部在点击点上方 **27 px**（18 DIP），就绪 **109 ms**；测试窗口自动关闭。
6. 真实 Windows Terminal：同一显式诊断入口，program=wt、position=center：工作区居中检查通过，就绪 **203 ms**。给定负坐标点击位于当前屏幕之外，系统选中最近的主屏，因此不能据此声称物理负坐标副屏已验证。
7. 最终发布：`dotnet publish modern-shell/AutoHotkeyUX.Modern.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:EnableMsixTooling=true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:IncludeNativeLibrariesForSelfExtract=true -o modern-shell/artifacts/win-x64`：成功。发布 EXE 的 `--verify-runtime` 和 `--verify-preferences` 均通过，后者明确使用私有内置 v2.0.29，表单与导航草稿检查通过。EXE 大小 236,583,321 字节，SHA-256：`E10AD253FA891D8F205F553B9EE999374827E6AA7DAC67040AA7E22DAB0178BA`。
8. `examples/OpenDocuments.ahk` 使用真实 v2 运行时仅作语法解析：退出码 0，不执行示例动作。

原生 TextBox 的 TextChanged 会晚于属性 setter 派发，诊断等待事件后验证草稿；此处是诊断时序修正，不改变用户的输入流程。已通过且无相关变更的服务检查不重复执行。

## 兼容性、风险和未验证事项

- 无数据库、网络 API、文件关联或用户脚本语法迁移；新增一个 `HKCU\Software\AutoHotkey\Modern\ShortcutPreferences` REG_SZ（schema=1 + revision + 完整字段）。瞬态端点值由当前进程持有。
- 自定义内置脚本可能无法接受 v3 配置；界面显示具体原因，原文件与进程保留。开关改变的是动作，主 Explorer shortcuts 开关仍控制内置解释器。
- 近期历史不跨管理器退出保留；诊断日志仍按既有大小轮转。
- 未验证：实际重启后的登录流程；不同 DPI 物理副屏当前不可用；未逐一重跑全部压缩格式/密码/分卷用例，解压保护实现未改。未模拟外部恶意注册表写入或系统强制终止时的每种时序。
- 当前环境实测有旧内置运行进程，部署只升级/重启经验证的内置受管脚本。

## 备份和回滚

施工前备份在 `modern-shell/backups/2026-10-07-shortcut-preferences`：源码 ZIP、原 EXE、已批准预览。部署前另保存当前进程/设置快照、内置根脚本和 v2 两个模块。

原 EXE SHA-256：`91A242D933935B726182DFFDB4189B92F0771F005DFE0A210FA4590D6A7678FA`。

回滚时退出新管理器，停止确认属于它的内置脚本；恢复备份 EXE 和备份根脚本（v2 模块仍在），再启动管理器。其他用户脚本不应停止。若需要恢复用户更改的配置，应先保存当前值，再恢复部署前的值；不要删除整个 AutoHotkey 注册表键。源码可通过本功能分支的提交回滚，不使用广泛 reset/clean。

## 审阅与交付状态

一次全分支独立审阅提出两处 Important，无 Critical、无 Minor。两处均已复现 RED 后修复，并通过定向 GREEN 与最终 41 项检查；按施工规则不启动第二轮重复审阅。

1. 自定义解压目录带尾部分隔符时，失败清理路径比较失配，可能掩盖原始错误。保存标准化输出根目录时保留盘符/UNC 根语义，确保清理只针对生成的暂存目录。证据：`review-red-cleanup.log` → `review-green-cleanup.log`（2 项）。
2. 配置写入成功但确认丢失时，相同值保存可能绕过不确定状态，导航也会丢失警告。保存服务现在持有待确认快照；每次重试先只读核对运行时和存储，同值保存仍需确认，界面持续显示警告。真实私有 AHK 接收配置后故意丢失查询确认复现，恢复查询后 revision 不变且不重放配置。证据：`review-red-confirmation.log` → `review-green-confirmation.log`（5 项）。

正式合并、部署和源码包证据在交付后补齐。

## 执行中的裁决

1. 示例从 OpenDownloads 改为 OpenDocuments：文档目录使用 Windows 已知目录，避免错误假设 Downloads 重定向位置；若选择不合适，成本是重命名示例。
2. 不重复已通过且代码未变化的检查：遵守用户的风险驱动验证与停止条件；若证据不足，可直接重跑记录中的命令。
3. 配置确认超时后刷新存储读回值，同时保留不确定提示：防止界面显示旧存储值；若运行时仍无法确认，需要先检查脚本再重试。
4. 物理负坐标/混合 DPI 副屏目前没有设备证据，记录为未验证；计算与主屏实测不能代替副屏验证。判断有误时需在对应设备补测。
5. 沿用现有内置运行时缓存机制；同版本 UX ZIP 哈希变化会刷新缓存，旧解释器占用 EXE 时导致诊断回退系统运行时。部署前核对根脚本、辅助模块、进程路径与精确启动时间后，仅停止受管内置解释器并重新物化缓存，最终发布验证已使用私有运行时。其他用户脚本不受影响；若占用来自其他进程则需另行处理，不能扩大终止范围。
6. 外部恶意注册表/暂存目录写入的完整安全审计不属于本次新增配置功能；现有正常编辑保护和解压边界保留。代价是不能宣称覆盖主动攻击的全部并发时序。
7. 原生 WinUI 检查覆盖布局、保存和草稿，未做像素级截图比对；保留获批交互预览。若需要像素级一致性，需补做同尺寸截图核验。
