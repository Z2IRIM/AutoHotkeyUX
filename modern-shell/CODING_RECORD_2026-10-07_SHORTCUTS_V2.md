# 快捷键修复、后台解压及终端定位记录

日期：2026-10-07。Git 基线：`52d477ef7467a45ebff9dbd433a5d1661f86561d`；施工分支：`codex/script-manager-core`。

## 目标与边界

本 Gate 修复资源管理器 Alt + 左键的终端动作、增加桌面压缩包动作、降低快捷键响应成本，并让新终端从点击坐标上方出现。沿用现有 WinUI 3、私有 AHK v2.0.29 和解压服务；界面布局、运行时定位和用户脚本管理边界不变。按用户已有交接说明施工，不新建 agent.md。

用户另行明确要求任务保存完成后关机：完成 Git 和源码包后执行 Windows 正常关机，不强制关闭其他应用。

## 问题分析与决定

- v1 热键条件仅接受活动 `CabinetWClass`，桌面 `Progman/WorkerW` 完全被排除，点击非活动资源管理器也无法触发。
- v1 需要 UIA 命中元素已选中、Shell 单选且名称相同，再等待 80 ms；这些静默返回条件令动作依赖选择更新时序。v2 解析实际点击元素，并使用真实 Shell 路径。保留可见标签匹配，缓存 Shell、UIA、walker 和标签文档；每次导航仍读取文档当前 Folder。
- 桌面采用合并公共/用户桌面的 Shell Namespace(0)，不拼接显示名称猜路径。选中命中可复用真实选择项，其他情况按名称做有时限的唯一匹配。文件隐藏扩展名与文件夹重名时拒绝歧义，不误开目录或解压另一文件。MSAA 回退仅在已经确认属于 Shell 文件区域时使用。
- v1 每次解压 `RunWait` 大 EXE，阻塞脚本等待。v2 通过有大小限制的 WM_COPYDATA 把任务交给现有管理器：一项执行、最多四项等待，相同待处理路径合并。消息指针在回调内复制；UI 回调只验协议和入队，解压与日志在工作线程进行。
- ACK 只代表接收，不代表解压成功。完成/失败由托盘反馈并记录日志；250 ms ACK 超时不会自动重复提交。管理器缺席时异步启动原有单次解压入口。退出管理器先停止接收，再排空已接收工作。
- Windows Terminal 使用独立窗口和初始位置参数；短时计时器只识别本次新窗口，完成后停止。底边优先位于点击点上方 12 px，水平居中并裁剪至工作区；靠屏幕上缘时允许贴边。快速连续打开串行完成窗口识别，已有终端不会被移动。PowerShell 回退使用独立 conhost 和目录工作区。
- 自动升级只接受已知、未编辑 v1 的规范化 SHA-256；备份保留原始字节，写入失败尝试恢复。用户编辑的主脚本/辅助模块不覆盖。模块采用临时文件持久化后无覆盖移动，避免半写入 include。

## 修改文件

| 文件 | 主要职责 |
| --- | --- |
| `Resources/ExplorerShortcuts.ahk` | v2 入口、手势判断、调度与诊断错误处理 |
| `Resources/ExplorerShortcuts/v2/Shell.ahk` | Shell/UIA 缓存、桌面和标签解析、唯一命中 |
| `Resources/ExplorerShortcuts/v2/Actions.ahk` | 终端启动/定位、解压 IPC、独立限长日志 |
| `Services/ShortcutCommandService.cs` | 原生接收、去重、有界后台队列及排空 |
| `Services/ExplorerShortcutInstaller.cs` | 资源安装、未修改 v1 的备份升级 |
| `Services/ScriptStartupService.cs` | 调用安装器，并重启升级后的已拥有内置脚本 |
| `Services/ArchiveExtractionService.cs` | 共享支持的扩展名判断，沿用已有解压安全规则 |
| `Services/TrayIconService.cs` | 复用托盘显示后台结果 |
| `App.xaml.cs` | 接收器生命周期、退出前排空、禁止排空期间关闭窗口 |
| `AutoHotkeyUX.Modern.csproj` | 嵌入两份 versioned include |
| `tools/ModernShell.SmokeTests/{ShortcutChecks.cs,Program.cs,ModernShell.SmokeTests.csproj}` | 针对修改边界的隔离验证 |
| `README.md`、本记录 | 使用方式、决定、验证和回滚 |

AHK 按入口、Shell 解析和动作拆分；新增 C# 服务各自保持单一职责。没有新增第三方依赖，也没有新增/删除业务数据库或对外 API。

## 验证与结果

1. `dotnet build modern-shell/AutoHotkeyUX.Modern.csproj -c Release -p:Platform=x64 --no-restore`：通过，0 警告、0 错误。
2. `dotnet run --project tools/ModernShell.SmokeTests -c Release -- <runtime> <isolated-root> <v1-backup> --shortcuts-only`：14 项通过。覆盖真实 AHK 升级重启、原始字节备份、重复启用、用户修改保留、冲突拒绝、非法协议、非阻塞入队、去重、队列容量、错误隔离及退出排空。
3. 真正 AHK 解释器 `--validate`：无输出、无错误；`--diagnose`：当前资源管理器文档可解析，首次约 15 ms，20 次缓存解析共约 32 ms，桌面 53 项。
4. 实际只读 UIA/MSAA 命中：桌面 `Temuee_Env.zip` 解析为 `C:\DESKTOP\Temuee_Env.zip`；文件夹、快捷方式也返回各自真实路径。三个独立名称夹具验证隐藏扩展名的唯一项、完整文件名和重名拒绝，全部通过。
5. 终端真实创建验证：中文、空格、分号路径启动成功。点击 `(950,900)` 后约 219 ms 完成定位，窗口 `(350,228,1200,660)`，底边在点击点上方 12 px。边缘 `(20,20)` 约 188 ms 就绪，窗口裁剪至 `(0,0)`。诊断仅关闭自己创建的终端。
6. 同类临时小 ZIP 的单次样本：旧 EXE 冷启动解压总耗时 2721 ms；新版管理器接收 ACK 16 ms，包含测试 AHK 启动和 ACK 共 67 ms，观察到输出共 185 ms。实际工作线程解压约 120 ms，源文件保留、输出一致、临时目录清理，管理器仍只有一个。这是该机器/该夹具的样本，不能代表大压缩包吞吐率或通用性能倍率。
7. `dotnet publish ... --no-restore -o modern-shell/artifacts/shortcuts-v2`：通过，单个 EXE 235726107 bytes。首次常规 restore 受 NuGet SSL 凭据限制失败，使用既有、未改变依赖的恢复缓存完成发布；未绕过任何源码编译错误。
8. 最终 EXE 已替换到原使用路径；启动状态：Windows 启动开启、快捷键开启、内置解释器 Running、窗口/托盘 H 图标正常、无 StartupWarning。最终 SHA-256：`0960FB2269BFE59A86A0642BD2481C0F51CF43E73975E1EA91AFDB46A4800258`。最终主脚本及两个 include 与验证源码一致。

资源管理器的只读测试坐标被 `Chrome_WidgetWin_1` 覆盖，因此不能把这些点的 `hit=none` 判作 Shell 故障。人工 Alt + 左键端到端结果仍待用户反馈；已经发起异步验收提示。没有模拟鼠标点击或移动无关窗口。未测试多显示器负坐标、PowerShell 回退真机、登录重启、大包吞吐或全部归档格式回归；已有解压实现未变，不重复其旧全量测试。

## 发布、备份、回滚

- 运行文件：`C:\DESKTOP\AutoHotkeyUX\modern-shell\artifacts\win-x64\AutoHotkeyUX.Modern.exe`。
- 编码前备份：`modern-shell/backups/2026-10-07-shortcuts-v2/`，含旧 EXE、原始 v1 脚本、基线 Git 源码包、原启动状态；自动升级另在 Documents 的 `AutoHotkey/backups` 保存 `.bak`。
- 部署过程中再次核验本任务安装的 include 哈希，备份后补充同名歧义处理；只在管理器正常退出后，以 PID、精确启动时间、脚本命令行确认内置解释器再重启。其他应用与用户脚本未被终止。
- 增量源码包以 `52d477e` 为基线，沿用根目录 `package-source.ps1 -Incremental -BaseRef 52d477e`，输出至 `C:\DESKTOP\srcpack_Area\AutoHotkeyUX`。测试、运行时下载和构建产物不打入源码包。
- 回滚时先用 `--exit-manager` 正常排空退出，在 Scripts 中停止内置脚本，再恢复备份 EXE 和 v1 文件。新增 include 可保留；启动/用户脚本选项无需重置。只恢复本任务文件，保留原未跟踪 `RelayPrompt.md`。
- 诊断：`%LOCALAPPDATA%\AutoHotkeyUX.Modern\state\shortcuts.log` 与 `manager.log`。路径和计时会被记录；不记录脚本内容。

## 官方资料

- [Windows Terminal 参数](https://learn.microsoft.com/en-us/windows/terminal/command-line-arguments)：新窗口、初始位置/大小和目录。
- [Terminal 命令解析规范](https://github.com/microsoft/terminal/blob/main/doc/specs/%23607%20-%20Commandline%20Arguments%20for%20the%20Windows%20Terminal.md)：分号需在 Terminal 参数层转义。
- [WM_COPYDATA](https://learn.microsoft.com/en-us/windows/win32/dataxchg/wm-copydata)：回调内复制、消息返回后不保留原指针。
- [AccessibleObjectFromPoint](https://learn.microsoft.com/en-us/windows/win32/api/oleacc/nf-oleacc-accessibleobjectfrompoint)：原生屏幕坐标命中及 VARIANT/COM 生命周期。
- [Windows shutdown](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/shutdown)：最终采用 `/s /t 0`，不添加 `/f`；超时大于零会隐含强制关闭，因此不用倒计时关机。
