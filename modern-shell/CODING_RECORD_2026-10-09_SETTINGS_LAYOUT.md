# Settings 页面横向偏移修复

## 目标与关键假设

用户报告 Settings 页面在切换不同数量的开关后横向偏移。先提供保持现有居中布局的可交互预览，用户确认“开始吧”后实施。源码基线为 `330ec1c`，使用现有 `codex/configurable-actions` 工作副本；不变更设置的存储、脚本运行及启动语义。

编码前已归档源码和开发文档：`C:\DESKTOP\AutoHotkeyUX\.verification\settings-layout\source-before-330ec1c.zip`。

## 问题分析与业务决定

1. 原生测量表明，四个 ToggleSwitch 的实际宽度都是 154 DIP；因此没有增加固定开关宽度的补丁。初轮直接切换开关的探针未复现截图中的状态间位移，不将这轮检查当成失败证据。
2. 设置页 StackPanel 的实际宽度为 1080 DIP，但根 ScrollViewer 的内容范围按约 980.67 DIP 的自然测量宽度定位。启动窗口中页面 X=405.5 DIP，按实际宽度居中应为 356 DIP，偏右 49.5 DIP。对修复前原生报告执行实际居中断言，16 个宽窗口状态全部失败。内容自然宽度参与定位，使页面位置容易随重新测量变化。
3. 现在明确禁用横向滚动，将正文宽度单向绑定到 ScrollViewer 的 ViewportWidth，同时保留 MaxWidth=1080 和居中布局。字幕显式左对齐；提示信息只能在正文宽度内换行，不再以内容自然宽度决定整页位置。
4. 实际最小窗口渲染还显示旧设置行的固定输入列会挤压标题。按已批准预览补充局部响应式布局：行内容可用宽度小于 640 DIP 时，标签与控件上下排列；变宽时恢复原有列宽。依据现有 Grid 行/列位置判断是否已应用布局，避免重复改写触发测量循环。
5. 底部说明由横向 StackPanel 改为两列 Grid，使说明文字在窄窗口正常换行。

继续使用原生开关、ComboBox、TextBox、现有主题和设置服务。没有引入新依赖、后台轮询或平行设置实现。

## 修改、新增与删除文件

| 文件 | 改动 |
| --- | --- |
| `Pages/SettingsPage.xaml` | 正文视口宽度约束、显式对齐、六个设置行的响应式行定义、底部说明换行 |
| `Pages/SettingsPage.xaml.cs` | 新增带职责注释的设置行布局处理，保留既有业务回调 |
| `Pages/SettingsPage.LayoutDiagnostics.cs` | 在隔离实例中测量实际布局，临时抑制设置写入并在 finally 恢复界面状态 |
| `MainWindow.SettingsDiagnostics.cs` | 三种窗口尺寸的几何断言和实际 WinUI 渲染捕获 |
| `App.xaml.cs` | 复用现有隔离诊断入口，支持 `--settings-layout-only` |
| 本记录 | 原因、决定、证据、交付与回滚 |

没有删除文件。SettingsPage 代码文件仍低于约 300 行；布局与隔离诊断分别保留其单一职责。

## 验证命令与结果

资料目录：`C:\DESKTOP\AutoHotkeyUX\.verification\settings-layout`。

```powershell
dotnet publish modern-shell/AutoHotkeyUX.Modern.csproj -c Release -r win-x64 --no-restore -p:Platform=x64 -p:DirectoryBuildTargetsPath=C:\DESKTOP\AutoHotkeyUX\.verification\action-library-flyout\reuse-runtime-payload.targets -o modern-shell/artifacts/settings-layout-fix
AutoHotkeyUX.Modern.exe --verify-visual-creation C:\DESKTOP\AutoHotkeyUX\.verification\settings-layout\fixed\report.json --settings-layout-only
git diff --check
```

- **FAIL → PASS**：修复前实际居中断言失败 16 项，见 `before/centering-failures.json`；修复后宽窗口正文实际宽度和测量宽度均为 1080 DIP，X=356 DIP，符合可视区域居中位置。
- **PASS**：最终 Release publish 退出码 0，最终构建日志未输出编译警告或错误。UI 变更复用此前已校验的嵌入运行时资源。
- **PASS**：实际 WinUI 实例在 3072×1728、1650×1350、980×680 物理像素窗口、150% 缩放下，分别检查 16 种开关组合，共 48 个布局状态。页面位置、宽度、标题位置保持稳定，正文不超出横向视口，六个设置行的标签及控件边界均通过。
- **PASS**：检查提示条隐藏及不同长度的实际保存消息；同一原生诊断同时捕获 wide、medium、narrow 三份 PNG，并核对宽窗口和最小窗口渲染。
- **PASS**：`git diff --check`。复制交付的三份 EXE 均与已验证产物 SHA-256 相同。

诊断通过既有随机单实例标识、独立文件目录和临时注册表运行。测量时 `_loadedSettings=false`，恢复四个开关和提示信息后才恢复该标志；没有运行解释器、发送实际热键或写入用户的开机启动、文件关联、快捷脚本配置。

未重新执行物理鼠标切换、其他显示器的不同 DPI 矩阵或所有设置的持久化回归。此次仅验证布局；上一可配置动作 Gate 的完整实机验收和正式版替换仍独立待确认。

## 交付与源码

- 用户此前使用的路径已更新：`C:\DESKTOP\AutoHotkeyUX\modern-shell\artifacts\configurable-candidate\AutoHotkeyUX.Modern.exe`。
- 同步更新工作副本中的同名候选路径。
- 独立可识别产物：`C:\DESKTOP\AutoHotkeyUX\modern-shell\artifacts\settings-layout-fix\AutoHotkeyUX.Modern.exe`。
- EXE 大小：237,230,013 字节。
- SHA-256：`D8A9A1DCE5A8B5B77826DB4AD13AAA0EE132D6B49D7C5E0AB93DBC75DB6DAE4E`。
- Git 源码分支：`codex/configurable-actions`，继续维护已有提交记录。
- 使用现有 `package-source.ps1 -Incremental -BaseRef 769fa13`，将累计源码包保存到 `C:\DESKTOP\srcpack_Area\AutoHotkeyUX`；包含此前候选实现、窗口/终端修复和本次设置页修复，排除测试、依赖、日志与二进制。

更新前检查没有正在运行的候选进程，不强制结束应用。重新打开候选文件即可看到更新；如用户随后启动了其他旧版本，应从托盘退出旧管理器后打开上述候选路径，避免单实例重定向。

## 兼容性、风险与回滚

无数据库、API、配置格式或数据迁移。新增布局处理只调整本页原生控件的行、列和位置，不改变开关对应的业务动作。隔离诊断参数须与现有 `--verify-visual-creation <绝对报告路径>` 一起使用。

旧候选 EXE 保存在 `.verification/settings-layout/candidate-before/candidate-0.exe`（主项目）与 `candidate-1.exe`（工作副本），旧指纹为 `2192B13FB261F5FFDEDDED99FE1782F5FF5FED7CAF92E8CC32C37C982BDBDA96`。回滚 EXE 时退出候选管理器，再恢复对应备份。源码回滚仅撤销本次六份文件的提交，不恢复或重置其他工作。

参考：[Microsoft ScrollViewer 文档](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/scroll-controls)。
