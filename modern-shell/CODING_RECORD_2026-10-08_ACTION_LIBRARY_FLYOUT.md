# Action Library 浮动菜单与 Workflow 密度调整

日期：2026-10-08。基线：`c369df81753604fb22882bac7d2c44c1c504f919`。施工分支：`codex/action-library-flyout`。

## 目标、假设与问题

用户批准“右侧浮出”预览，并要求加高脚本编辑区、压缩 Workflow 条目，以同时查看更多动作。本轮把“脚本编辑区”解释为 New Script 的可视化 Workflow 编辑区。

原分类使用 Expander，展开内容参加 Grid 测量，导致整行三栏随分类展开增高。原动作卡最低 76 DIP、间距 8 DIP，动作列表最高 440 DIP，限制同屏容量。

## 实现与业务决定

- 四个分类改用 Button 附带 WinUI MenuFlyout，优先从右侧弹出，由系统处理边界、键盘和关闭交互。菜单不参加页面布局；呈现宽度 240–320 DIP。
- 菜单继续从原 FlowActionCard 元数据生成 16 种动作，并调用原 AddAction、历史及分支插入逻辑。沿用动作数量、嵌套深度和保存期间的禁用规则。切换分类、页面卸载、禁用编辑或响应式重排时关闭旧菜单。
- 动作列表高度随内容视口高度的 60% 调整，范围 440–800 DIP。条目内容最低 56 DIP、间距 6 DIP、标题 14 DIP、摘要 12 DIP。长内容省略显示，完整内容可通过原属性区和工具提示查看。
- 复用原三档响应式布局及原 ListView 的选择、排序、拖动和撤销职责，不改变工作流模型、AHK 生成器或文件保存规则。
- 最小充分验证只覆盖此次菜单与密度变化。既有创建/能力诊断更新为 MenuFlyoutItem，不重复运行无源码变化的核心、存储和设备回归。

采用原生菜单的理由是保持主项目的 WinUI 主题和无障碍行为，同时避免维护一套独立弹层。扩大列表与紧凑行的代价是小窗口的页面仍需纵向滚动；字体可访问性设置及长参数通过系统排版和原属性编辑器处理。

## 文件范围

- `Pages/NewScriptPage.xaml`、`Pages/NewScriptPage.xaml.cs`：浮动分类、菜单生命周期、动态高度及紧凑卡片。
- `Pages/NewScriptPage.Editing.cs`、`Pages/NewScriptPage.Saving.cs`：保留现有事件逻辑，调整菜单项入口及保存时关闭弹层。
- `Pages/NewScriptPage.Diagnostics.cs`、`Pages/NewScriptPage.CapabilityDiagnostics.cs`：原诊断与新菜单类型一致。
- `Pages/NewScriptPage.FlyoutDiagnostics.cs`：隔离、未保存的 12 个 Wait 动作，核对真实弹层、展开前后几何、原生菜单调用、条目高度及同列表可见条目数。
- `MainWindow.VisualDiagnostics.cs`、`App.xaml.cs`：现有隔离诊断增加 `--layout-only` 分支及单独弹层像素捕获。
- 本记录新增。没有删除源文件。

## 备份、构建与源码核对

施工前已在主目录 `.verification/action-library-flyout` 保存 `source-before.zip`、`app-before.exe` 和原预览记录。源码修改位于独立托管工作区，不覆盖主目录已有临时文件和取消的转译草案。

运行命令：

```powershell
dotnet publish modern-shell/AutoHotkeyUX.Modern.csproj -c Release -r win-x64 -p:Platform=x64 --ignore-failed-sources -o modern-shell/artifacts/action-library-flyout
git diff --check
```

发布构建通过；首轮事件 sender 可空警告已修正，最终构建零警告。`git diff --check` 通过。更新后的交互预览通过 JavaScript 语法和结构检查，SHA256 为 `50F6B750F1D604D32C4BFD31FCDCFDCCF83C12116DB2C305F6F25F06A3761299`；这是源码检查，不是实际浏览器点击验收。

原打包脚本每次重新生成 UX ZIP，其时间戳会改变归档哈希。逐项核对前后 20 个归档条目，文件字节完全相同。此次 UI 发布通过任务证明目录中的 `reuse-runtime-payload.targets` 复用基线已校验归档；未修改通用打包器。具体发布命令及日志保存在同目录。读取发布 DLL 的嵌入资源验证，运行时与 UX 哈希分别保持 `B2D0200724A6B6AD22C965C939C5E5A2C64A35D1CCB455A3CA3F8CE415C5A296` 和 `A1E047592AF609BC2D3ED946FFE1A89968D15DF999A147F3801A9AF6467BB272`，避免一次 UI 更新触发运行时重新物化。这项核对没有启动 App。

## 实机验证与交付状态

待确认并执行：隔离原生诊断，以及正式 EXE 更新和管理器重开。此前协调会话转交了用户“真实 App 操作前先列出步骤、测试数据和影响，并获得事前通知确认”的要求。已给出明确实机清单，未把等待时间或默认选项视为确认。

预定命令：`AutoHotkeyUX.Modern.exe --verify-visual-creation <绝对报告路径> --layout-only`。诊断使用独立目录及临时注册表设置，12 个 Wait 动作仅在内存中展示，不保存或执行，不向用户应用发送键鼠输入。窗口和菜单会在本机短暂显示，结果和截图写入任务证明目录。物理键鼠关闭/选择行为在本轮未验证。

## 兼容性、风险与回滚

无数据库、外部 API、工作流格式或迁移变化。`--layout-only` 只作为已有开发诊断的过滤参数。关闭管理器可能丢失尚未保存的编辑草稿，正式切换前须确认草稿已保存。

可从任务证明目录的原 EXE 恢复程序文件，并以施工基线对照恢复本轮 UI 源码；恢复前保存新的工作，不批量重置仓库或删除用户脚本。源码增量包使用基线 `c369df8`，仅包含本轮源文件与记录，不含依赖、测试、构建产物和取消的转译草案；交付目录为 `C:\DESKTOP\srcpack_Area\AutoHotkeyUX`。

## 参考依据

[Microsoft MenuFlyout](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.menuflyout?view=windows-app-sdk-2.0)、[FlyoutPlacementMode](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.primitives.flyoutplacementmode?view=windows-app-sdk-2.0)、[MenuFlyoutItemAutomationPeer](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.automation.peers.menuflyoutitemautomationpeer?view=windows-app-sdk-2.0)。
