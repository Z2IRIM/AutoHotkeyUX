# 编码记录：可视化创建页面错位修复

## 目标、分析与决定

用户截图显示 New Script 内容整体右移，Properties 在窗口右边被裁切。基线 `407c630`，当前分支 `codex/script-manager-core`。此前诊断只检查三栏内部相对边界，未覆盖整页和实际滚动视口的关系。

页面内的 `PageBody MaxWidth="1440"` 将大窗口的内容封顶，滚动布局产生额外水平偏移。本机原生诊断测得首次进入时 BodyX=176 DIP，重新布局后的另一次测量为347.5 DIP；小于这个限宽的窗口中 BodyX=0。最小修复是移除该限宽，让编辑区使用完整视口，继续保留原28 DIP左右内边距和三栏响应式规则。没有用补偿坐标、负margin或动态宽度回调掩盖偏移。

先复用已批准的界面制作可交互修正预览并在本机打开；预览2048/1024/320宽度无溢出，添加动作正常、无脚本错误。源码修改前备份三份涉及文件和当前EXE到 `modern-shell/backups/2026-10-08-workflow-layout`；旧EXE SHA256为 `56F1069FA11703B5AADF7BC51ED79F69E72AABA22E69C93FA8BCFFA7A89DA945`。

## 修改文件

- `Pages/NewScriptPage.xaml`：仅移除 PageBody 的最大宽度。
- `Pages/NewScriptPage.Diagnostics.cs`：记录整页位置/视口/属性右边界，并检查整页靠左、不超出实际视口。
- `MainWindow.VisualDiagnostics.cs`：首次导航、任何缩放之前测量；保存启动尺寸截图，并在渲染后再次核对位置。
- 本记录。已有 `RelayPrompt.md` 和 `modern-shell/debug.log` 保留。

## 验证命令与结果

- 原生修复前测量报告：`.verification/workflow-layout/before/report-first.json`。对原BodyX的独立检查明确失败：176 DIP，而预期视口起点为0。
- `dotnet publish modern-shell/AutoHotkeyUX.Modern.csproj -c Release -r win-x64 -o modern-shell/artifacts/workflow-layout-fixed`：通过，无C#/XAML错误。
- EXE `--verify-visual-creation <isolated-report>`：通过；沿用隔离注册表、脚本目录和运行时缓存。初始窗口、1600 DIP、1100 DIP及最小窗口的整页检查通过。启动截图渲染前后 BodyX都为0，没有重布局造成的偏移。

| 实际内容视口宽度 DIP | 编辑区左边距 | 编辑区右边距 | 结果 |
| --- | --- | --- | --- |
| 1792 | 28 | 28 | 通过 |
| 1344 | 28 | 28 | 通过 |
| 844 | 28 | 28 | 通过 |
| 397.33 | 28 | 28 | 通过 |

原生表单创建/保存、草稿导航、应用限定、外部合法版本重开及修订4检查仍通过。本次未修改生成器或存储/执行服务，没有重复其已通过的独立服务套件。最终原生PNG已人工查看，证据与构建源码/EXE哈希保存在 `.verification/workflow-layout`。

## 限制、兼容性与回滚

无数据库、API或流程文件格式变化；既有脚本与设置无需迁移。只改变New Script页面的横向空间分配。未新增多显示器/DPI切换及屏幕阅读器专项验收；原生截图不包含Mica和系统标题按钮。

源码可针对本次文件恢复到基线 `407c630`；应用回滚使用上述备份EXE。部署必须先优雅退出管理器，若私有运行时载荷需刷新，仅停止身份已核对的内置解释器，再启动恢复原配置。增量包使用已有 `package-source.ps1 -Incremental -BaseRef 407c630`，输出到 `C:\DESKTOP\srcpack_Area\AutoHotkeyUX`。

部署和最终包审计结果随交付完成追加。
