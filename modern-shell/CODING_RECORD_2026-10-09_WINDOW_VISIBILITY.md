# 候选版窗口不可见修复与当前目录终端使用说明

## 目标、基线与范围

- 修复 `configurable-candidate` 打开时窗口落在所有显示器外、必须从任务栏最大化才能看到的问题。
- 说明如何通过已实现的可视化编辑器创建以资源管理器当前目录为工作目录的终端快捷键。
- 本次源码基线为候选版提交 `5853156`，在现有 `configurable-actions` 工作副本继续开发；主仓库此前基线 `769fa13` 及其已有未跟踪文件保留。
- 沿用已批准的 80% 启动尺寸和屏幕宽高比，无页面布局、动作模型或 AHK 生成逻辑变更。

## 根因与决定

实际 WinUI 三屏诊断显示，右侧显示器 `OuterBounds.X=3840`，`WorkArea.X=3840`；工作区坐标已包含屏幕偏移。原实现按绝对工作区计算出 X=4224 后，又使用带 `DisplayArea` 参数的相对放置重载，再次加上 3840，实际窗口 X=8064，超出右屏边界 7680。左侧显示器也受同一错误影响。此前仅在原点为零的主屏进行真实验收，模拟数据又错误地把副屏工作区设为相对坐标，因此未发现问题。

采用 `AppWindow.MoveAndResize(RectInt32)` 的屏幕绝对坐标重载，计算、放置和诊断使用同一坐标系。启动日志记录窗口、显示器和工作区的坐标与尺寸。

显示/托盘恢复/二次启动共用的 `ShowWindow` 在恢复最小化后检查标题栏可达性。只有无法在任一已连接显示器上取得可操作标题栏的普通窗口才重新放置到最近显示器工作区；保留仍可操作的手工位置和跨屏窗口，以及最大化状态。没有后台轮询或定时器。显示器查询异常时将标题栏移到主桌面 (32,32)，避免只改尺寸却继续留在离屏位置。

## 文件与主要改动

| 文件 | 主要改动 |
| --- | --- |
| `modern-shell/MainWindow.Geometry.cs` | 绝对坐标启动放置、离屏恢复、坐标日志及纠正副屏诊断数据 |
| `modern-shell/App.xaml.cs` | 共用恢复入口检查可见位置；沿用隔离诊断入口增加 `--placement-only` |
| `modern-shell/MainWindow.WindowPlacementDiagnostics.cs` | 原生三屏、离屏、最小化和最大化专项验证，复用正常显示入口 |
| 本记录 | 原因、验证、交付、回滚及当前目录终端操作说明 |

诊断按索引读取 WinRT 显示器集合，避免当前 SDK 集合的 `IEnumerable` 投影抛出 `InvalidCastException`。所有新增/修改函数具有职责注释；未删除文件、添加依赖或修改无关业务。

## 验证与结果

构建复用已验证且未改动的运行时/编译器资源包：

```powershell
dotnet publish modern-shell/AutoHotkeyUX.Modern.csproj -c Release -r win-x64 --no-restore -p:Platform=x64 -p:DirectoryBuildTargetsPath=C:\DESKTOP\AutoHotkeyUX\.verification\action-library-flyout\reuse-runtime-payload.targets -o modern-shell/artifacts/window-placement-fixed
AutoHotkeyUX.Modern.exe --verify-visual-creation C:\DESKTOP\AutoHotkeyUX\.verification\window-visibility\fixed-native.json --placement-only
git diff --check
```

- **FAIL → PASS**：修复前原生副屏定位为 X=8064，专项诊断退出码 1；修复后退出码 0，`Passed=true`。
- **PASS**：真实主屏、右副屏、左副屏均为 3840×2160 物理像素，80% 窗口 3072×1728；位置分别为 `(384,216)`、`(4224,216)`、`(-3456,216)`。
- **PASS**：窗口移至 `(30000,-30000)` 后，通过产品共用显示入口恢复到 `(4224,216)`。
- **PASS**：最小化后正常恢复；已最大化的窗口保持最大化。
- **PASS**：6 组已有尺寸/比例边界及纠正后的负坐标副屏算法检查。
- **PASS**：publish 退出码 0，未输出编译警告/错误；`git diff --check` 无空白错误。
- 诊断采用项目内独立 `native-workspace-<GUID>` 和 `Software\AutoHotkeyUX.Verify` 临时设置，不启动 AHK、终端、解压或用户脚本；临时注册表键由正常退出清理。

证据位于 `C:\DESKTOP\AutoHotkeyUX\.verification\window-visibility`，通过报告为 `fixed-native.json`；失败和通过实例均保留自己的 `state\manager.log`。这次没有重复与改动无关的动作模型 45 项检查。

## 当前目录终端：在编辑器中的创建方式

1. 打开 **New Script → Examples → Open terminal in current directory**。
2. 点击 **TRIGGER**，默认是 **Ctrl + Alt + T**，作用域为 **File Explorer and desktop**。按需修改；如要 Alt+左键，仅勾选 Alt，Key 选择 Left mouse button。
3. 工作流第一步为 **Get current directory**。第二步 **Open terminal** 的 **Value source** 选择第一步的 **Usable directory** 结果（显示为 `1. Get current directory → Usable directory`），不要选择固定值填写脚本安装目录。
4. 第二步 **Configuration** 选择 **Custom** 时，可以配置终端程序、位置、尺寸和鼠标间距。例如 **Program: Auto**、**Position: Above cursor**。
5. 保存后进入 **Scripts**，选择脚本并 **Run**。保存文件本身不会启动快捷键；需要时再设置自启动。

也可从空白流程依次添加 **Action library → Context → Get current directory** 和 **Files → Open terminal**，再绑定上述 **Usable directory** 结果。键盘触发读取前台资源管理器窗口/桌面；鼠标触发读取点击所在的资源管理器窗口/桌面。Windows Terminal 通过 `new-tab -d <目录>`，PowerShell 通过启动工作目录接收该路径。

业务区别：**Get current directory** 获取资源管理器正在浏览的目录；如果期望 Alt+左键打开“被点中的子文件夹”，第一步改为 **Get clicked object**，再绑定它的 **Usable directory**。快速访问/此电脑等虚拟视图没有唯一文件系统目录，不能作为实际工作目录。若 Alt+左键与运行中的内置快捷工具冲突，使用 APP 的冲突替换确认；独立示例的 Ctrl+Alt+T 可避免该冲突。

上面的操作说明已核对编辑器控件、示例工厂和生成代码；本次窗口专项验收没有实际执行终端工作目录或鼠标快捷键验收。

## 交付、兼容性、风险与回滚

- 修复候选 EXE：`C:\DESKTOP\AutoHotkeyUX\modern-shell\artifacts\configurable-candidate\AutoHotkeyUX.Modern.exe`。
- 已验证 EXE SHA-256：`2192B13FB261F5FFDEDDED99FE1782F5FF5FED7CAF92E8CC32C37C982BDBDA96`。
- 源码备份：`.verification\window-visibility\source-before-5853156.zip`；原候选 EXE 在 `.verification\window-visibility\app-before-window-fix.exe`。
- 开发记录副本放在主项目 `.verification\window-visibility`，便于直接打开。
- 使用现有 `package-source.ps1 -Incremental -BaseRef 769fa13` 输出累计候选版增量包到 `C:\DESKTOP\srcpack_Area\AutoHotkeyUX`。包以主仓库基线为起点，包含上一轮已授权候选功能与本次修复；排除测试、依赖和二进制。
- 无数据库/API/设置/工作流格式迁移；新设置、目录和动作均未改变。
- 未实际验证显示器热插拔、混合 DPI、远程桌面重新连接和特殊任务栏停靠。窗口查询失败有日志和主桌面回退。
- 上一轮终端/解压/冲突恢复的完整实机验收和正式版替换授权仍独立待确认，本次仅交付已验证的候选版窗口修复，不宣称整个可配置动作 Gate 已验收或合并。
- 回滚时先从托盘退出候选实例，再把 `app-before-window-fix.exe` 复制到候选路径。源码可从备份恢复本记录中的生产文件，保留其它用户变更。若旧实例仍在运行，新 EXE 的启动可能被单实例机制重定向到旧窗口，因此使用新版前应先退出旧托盘实例。

参考：Microsoft 官方 [AppWindow.MoveAndResize](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.windowing.appwindow.moveandresize?view=windows-app-sdk-1.8) 区分屏幕坐标与显示器相对坐标重载；当前 SDK 工作区坐标取实际原生结果为准。
