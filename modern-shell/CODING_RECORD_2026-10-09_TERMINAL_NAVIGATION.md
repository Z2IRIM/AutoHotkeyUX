# Explorer 快捷终端：前台焦点与连续导航目录修复

## 目标与范围

修复用户现有 `Explorer Shortcuts.ahk` 的两个问题：新终端被目录窗口遮挡；关闭终端、进入同一资源管理器窗口的子目录后，下一次终端仍使用上一次目录。

源码基线为候选版 `c1bde32`，沿用已有 `configurable-actions` 工作副本。本次只修改共享终端动作、Shell 文档缓存及已知辅助文件升级识别；保留上一轮窗口位置修复和已批准的可视化编辑器。编码前已备份源码、开发记录和实际运行的四份脚本文件。

## 根因与业务决定

1. `PositionTerminal` 在确认本次新窗口后仅移动/缩放窗口，没有激活它。资源管理器可在终端创建过程中重新取得焦点，使新终端留在后面。现在在原有窗口归属验证通过后，对该 HWND 调用 `WinActivate`，并以 250 ms 的 `WinWaitActive` 检查前台状态；失败写入既有错误路径。窗口按正常前后台规则切换，不增加永久置顶状态。
2. `GetActiveExplorerDocument` 缓存了 `window.Document`。同一标签导航后，该文档对象仍可正常访问原目录，旧的“访问 Folder 不抛异常”检查不能证明它对应当前页面。现在仅缓存经过 HWND/活动标签验证的浏览器对象，每次请求重新取得其 `Document`。继续复用 Shell COM、UIA、标签缓存，不添加后台轮询。
3. 新版安装服务额外识别上一候选版 `Actions.ahk` 的规范化 SHA-256：`EFE46F26918874B073B9ABB31AC9732F275E9C5685150F5AD5C37A2ADEBD403A`。该值从编码前 Git 归档的精确文件内容计算。沿用现有辅助文件组预检、持久备份及失败回滚；不将未知用户编辑当成旧版本覆盖。

修复位于 v3 的共享实现，供内置快捷工具和新生成的 v3 可视化脚本使用。冻结的 v1/v2 资源不变，以保留既有工作流的字节兼容性。已生成的自包含可视化脚本不会被后台修改；需要采用新逻辑时，应在新 EXE 中重新保存。

## 修改文件

| 文件 | 改动 |
| --- | --- |
| `modern-shell/Resources/ExplorerShortcuts/v3/Shell.ahk` | 缓存浏览器对象，每次获取当前 Document |
| `modern-shell/Resources/ExplorerShortcuts/v3/Actions.ahk` | 仅激活已验证的新终端，记录前台成功和目标目录 |
| `modern-shell/Services/ExplorerShortcutInstaller.cs` | 识别上一候选辅助资源的精确指纹 |
| 本记录 | 原因、决定、验证、应用状态与回滚 |

没有删除文件、修改 UI 布局、增加依赖或改变热键/工作流格式。

## 最小充分验证

验证资料位于 `C:\DESKTOP\AutoHotkeyUX\.verification\terminal-navigation`，所有探针不注册测试热键，测试文件/日志隔离在项目验证目录。

```powershell
AutoHotkey64.exe /ErrorStdOut=UTF-8 .verification/terminal-navigation/probe.ahk .verification/terminal-navigation/fixed-final.txt
dotnet publish modern-shell/AutoHotkeyUX.Modern.csproj -c Release -r win-x64 --no-restore -p:Platform=x64 -p:DirectoryBuildTargetsPath=C:\DESKTOP\AutoHotkeyUX\.verification\action-library-flyout\reuse-runtime-payload.targets -o modern-shell/artifacts/terminal-navigation-fix
pwsh -File .verification/terminal-navigation/upgrade-check.ps1
git diff --check
```

- **FAIL → PASS**：修复前 `before.txt` 为 6 项失败，真实子目录仍被解析为父目录，PowerShell 实际工作目录也为父目录；Windows Terminal 的前台焦点检查失败。
- **PASS**：`fixed-final.txt` 退出码 0，10 项断言通过：同一真实 Explorer 窗口父目录 → 子目录 → 父目录的 3 次解析、3 次 PowerShell `Get-Location` 实际工作目录、3 次 PowerShell 和 1 次 Windows Terminal 的前台 HWND。
- 焦点探针在新窗口出现后模拟 Explorer 取得焦点；测试观察器短暂阻止自己的 COM 查询重入，避免在动作已经完成后再次夺走焦点。读取工作目录的命令仅发送到已确认仍在前台的测试终端，并等待测试 PowerShell 输入就绪；这些等待和观察器均不进入产品代码。
- **PASS**：调用产品实际安装服务，验证已发布旧文件升级、上一候选文件升级、重复安装幂等、一个用户编辑文件阻止整组覆盖，共 4 项断言。
- **PASS**：Release publish 退出码 0，未输出编译警告或错误；`git diff --check` 无空白错误。
- 测试只关闭本次创建的 Explorer 窗口及经过归属检查的新终端，不关闭或移动原有终端。测试逻辑放在 `.verification`，不进入增量源码包；未重复无关模型/UI回归。

未验证 Windows Terminal 内部默认 profile 的实际 shell 工作目录；其焦点和正确目录参数传递已核对，实际工作目录验证使用 Windows PowerShell。未重新执行物理 Alt+鼠标手势、远程桌面、标签页切换矩阵及空闲 CPU 对比。

## 本机应用状态

通过产品实际 `ExplorerShortcutInstaller` 更新 `C:\Users\Z\Documents\AutoHotkey\ExplorerShortcuts\v3`，返回 `Upgraded=true`、`Warning=null`。根脚本和用户保存的快捷键配置未改动。更新前均已持久备份，更新后的文件 SHA-256 为：

| 文件 | SHA-256 |
| --- | --- |
| `Shell.ahk` | `A7AC6709AFA17EE1165D035F00E08AF0319F880FAA593F1D95B43DE61800AD52` |
| `Actions.ahk` | `2EA13C38A563E4E6594461885F861450D864C0FD2812686A612B88AFD0B091CE` |
| `Preferences.ahk` | `7CBD8729B59EC31584FB22CC8F4A7AE4A9FC8B78CDF3733C0BFD24C9213D8B0A` |

安装服务备份目录为 `C:\Users\Z\Documents\AutoHotkey\backups`，对应本次三份文件的时间戳为 `20261008-175335`（UTC），完整路径记录在 `.verification\terminal-navigation\live-upgrade-log\manager.log`。

准备通过管理器已有 Restart 操作重新加载时，用户的物理 Esc 中止了 Computer Use。此后不再进行界面操作，没有自动点击 Restart，没有退出管理器或绕过管理器另建脚本进程。**若尚未手动重新加载，请在 Scripts 中点击 Explorer Shortcuts.ahk 的 Restart，使当前运行的脚本读取更新后的辅助文件。**

## 交付与回滚

- 新候选 EXE：`C:\DESKTOP\AutoHotkeyUX\modern-shell\artifacts\terminal-navigation-fix\AutoHotkeyUX.Modern.exe`。
- EXE 大小：237,204,924 字节；SHA-256：`F2778DBBFF715CE1964E36ED95A687ACF35280921B6AC7520BE74B583F78A53B`。
- 为保留正在运行的管理器，本次新 EXE 放在独立目录，未替换正在使用的 `configurable-candidate` 或正式 `win-x64` 路径。使用新 EXE 时需先从托盘退出旧管理器，以免被单实例机制重定向到旧程序。
- 编码前源码归档：`.verification\terminal-navigation\source-before-c1bde32.zip`；实际脚本原始备份：同目录 `live-before`。
- 开发记录副本放在主项目 `.verification\terminal-navigation`，便于直接打开。
- 使用现有 `package-source.ps1 -Incremental -BaseRef 769fa13` 将累计候选源码包保存到 `C:\DESKTOP\srcpack_Area\AutoHotkeyUX`，包含此前已授权候选功能和本次修复，排除测试、依赖与二进制。
- 回滚辅助文件时先通过管理器 Stop 该内置脚本，再恢复本次三份对应备份或 `live-before` 中的原始文件，最后通过管理器 Run/Restart。其它用户脚本及其窗口不需停止。源码仅恢复本记录列出的三个生产文件；保留其它用户更改。

无数据库/API/配置迁移。系统可能限制激活前台窗口，此时报告错误并结束本次发现计时器，保留已创建窗口以供用户查看。远程桌面客户端最小化时的特殊激活行为尚未验证。上一可配置动作 Gate 的完整实机验收、正式版本替换与主分支合并仍独立待确认，本次不宣称该 Gate 已完成。

参考：AutoHotkey 官方 [WinActivate](https://raw.githubusercontent.com/AutoHotkey/AutoHotkeyDocs/v2/docs/lib/WinActivate.htm)、[WinWaitActive](https://raw.githubusercontent.com/AutoHotkey/AutoHotkeyDocs/v2/docs/lib/WinWaitActive.htm)；停止界面控制遵循 Computer Use 的 [guidance](C:/Users/Z/.codex/plugins/cache/openai-bundled/computer-use/26.1002.52244/docs/guidance.md)。
