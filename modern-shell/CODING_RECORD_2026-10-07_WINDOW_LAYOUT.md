# 窗口尺寸与布局修复记录

## 目标与完成范围

启动主窗口更大且与屏幕宽高比一致；修复 New Script 三个模板按钮间距不等；移除 APP 顶部全局搜索栏。

上述改动已构建、验证、替换本机单 EXE，并恢复主窗口运行。Window Spy、Compile、Documentation 内嵌现代界面属于后续新增工具子系统，本次只完成其交互预览和设计记录，待用户评审，尚未改动工具功能入口。

## 关键假设与业务决定

- 采用已展示窗口方案的 80% 均衡尺寸：先以显示器完整屏幕比例拟合可用工作区，再等比缩放并居中。小屏幕优先保证可用最小尺寸；空间不足时限制在该显示器内。
- 比例要求作用于启动尺寸。之后用户仍可自由缩放，托盘恢复也保留当前窗口大小，不持续强制长宽比。
- 模板只修布局：三等宽卡片、16 DIP 间距；容器小于 632 DIP 时纵向排列。模板内容、文件名碰撞处理和创建脚本语义保持现有实现。
- 仅移除顶部全局搜索与其查询处理函数；Scripts 内的脚本名搜索继续正常工作。
- 上一任务已经完成关机，此新任务不重复执行关机。

## 问题分析与实现

旧主窗口固定为 1280×820 物理像素，在 4K/150% 显示环境中显得过小且比例与屏幕不同。改用 Windows AppWindow/DisplayArea：以 OuterBounds 取得屏幕比例，WorkArea 排除停靠区域，MoveAndResize 的显示器相对坐标重载处理多显示器偏移。

模板原来已有等宽列与 ColumnSpacing=16，但 Button 未设 HorizontalAlignment=Stretch，其宽度随内容变化，造成视觉间距不等。现分别拉伸三个按钮，依据 TemplateGrid 实际布局宽度切换行列，避免全局窗口阈值与内容宽度/显示缩放不一致。

尺寸计算/显示器放置由 MainWindow.Geometry.cs 承担，延续既有 partial 模式；原 MainWindow 不继续增长。布局诊断同样延续已有显式 --verify-ui 模式，不引入新测试框架或后台定时器。

## 修改与新增文件

| 文件 | 作用 |
| --- | --- |
| MainWindow.xaml.cs | 调用屏幕比例启动放置，删除顶部搜索处理函数 |
| MainWindow.xaml | 删除顶部 AutoSuggestBox，保留原生标题栏与拖动区域 |
| MainWindow.Geometry.cs | 启动尺寸、居中、人工缩放下限及显示器边界诊断 |
| MainWindow.Diagnostics.cs | 实际窗口放置、宽窄模板布局与顶部搜索删除检查 |
| Pages/NewScriptPage.xaml | 模板卡片拉伸、纵向行定义与局部尺寸事件 |
| Pages/NewScriptPage.xaml.cs | 根据实际模板区域 DIP 宽度更新行列 |
| Pages/NewScriptPage.Diagnostics.cs | 测量三种选择状态下真实卡片边界、间距与内容 |
| TOOLS_UI_DESIGN_2026-10-07.md | 后续内嵌工具页面设计，待评审 |

没有删除源码文件；删除的是顶部搜索控件及其专用事件函数。用户已有未跟踪的 RelayPrompt.md 未改动或加入提交。

## 最小充分验证

```powershell
dotnet build .\modern-shell\AutoHotkeyUX.Modern.csproj -c Release -p:Platform=x64 --no-restore
dotnet publish .\modern-shell\AutoHotkeyUX.Modern.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:EnableMsixTooling=true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:IncludeNativeLibrariesForSelfExtract=true --no-restore -o .\modern-shell\artifacts\window-template-release
.\modern-shell\artifacts\win-x64\AutoHotkeyUX.Modern.exe --verify-ui C:\DESKTOP\AutoHotkeyUX\.verification\window-template-single-exe-ui.json
git diff --check
```

- Release 构建：0 警告、0 错误；单 EXE publish 退出码 0。
- 真 WinUI UI 诊断与单 EXE 诊断：Passed=true，退出码 0。
- 本机显示器 3840×2160，窗口实测 3072×1728，位置 (384,216)，80% 同比例居中。
- 宽模板容器 936 DIP：三个按钮均宽 301.333 DIP；两处间距均 16 DIP。
- 窄模板容器 309.333 DIP：三个按钮纵向同宽，Y 为 0/196/392，两处间距均 16 DIP。
- Blank、Hotkeys、Automation 三种选中状态都通过宽窄几何与预览内容检查。诊断比较时仅规范 WinUI TextBox 的 CR/LF 表示差异。
- Home/New Script/Settings/Scripts 页面加载、互斥导航、Scripts 搜索及最小窗口脚本操作行通过现有诊断。
- 6 组代表性尺寸算法边界通过：当前显示器、负坐标显示器、16:10/侧边任务栏、超宽屏、竖屏、小工作区。
- 工具交互预览：真实浏览器中检查 Home/工具返回、暂停、编译输出示例、文档主题切换；窄屏模板/文档无水平溢出，控制台无错误。此结果仅证明预览交互，不代表工具服务已实现。
- 应用后管理器 PID 46192、主窗口可见；开机启动与 Explorer Shortcuts 均启用，快捷键脚本保持原 PID 38124，未重复启动。

## 发布与备份

- 已应用 EXE：`C:\DESKTOP\AutoHotkeyUX\modern-shell\artifacts\win-x64\AutoHotkeyUX.Modern.exe`
- 大小：235,758,956 字节。
- SHA-256：`15DD6C83121B3A4BF20B6E9BE7328D3B9FFF642F866DC1452C1CEEF8E0F4B7C2`。
- 施工前 Git 基线：`a0d995cd1a678fc73db0b17bee70e0acaa45ecd9`。
- 源码备份：`modern-shell\backups\2026-10-07-window-template\source-a0d995c.zip`。
- 上一版 EXE：`modern-shell\backups\2026-10-07-window-template\AutoHotkeyUX.Modern.before-window-template.exe`。
- 使用项目已有 `package-source.ps1 -Incremental -BaseRef a0d995cd1a678fc73db0b17bee70e0acaa45ecd9` 打包，输出到 `C:\DESKTOP\srcpack_Area\AutoHotkeyUX`；排除构建产物、runtime ZIP、依赖、测试目录和备份，manifest 记录提交与变更文件。

## 风险、未验证事项与后续

无数据库/API/配置迁移，无新依赖，保留内置运行时 v2.0.29 的冻结架构与快捷键执行逻辑。新增局部布局事件仅跨阈值时改变行列，没有轮询开销。

多显示器、竖屏、侧边任务栏完成尺寸算法边界检查，未在对应真实硬件逐一运行。读取显示器失败会写现有 manager.log 并退回旧启动尺寸。此次没有重复上一任务已完成的解压/终端性能测试。

下一 Gate：用户评审 TOOLS_UI_DESIGN_2026-10-07.md 后，完成原生采集页、后台编译器和离线文档页；旧工具入口当前仍保留，不能宣称已完成工具内嵌。

## 回滚

先用管理器的 --exit-manager 正常退出，再把备份 EXE 复制回上述 artifacts\win-x64 路径并启动。源码可从 source-a0d995c.zip 单独恢复本记录列出的生产代码；不要覆盖其它用户修改。用户脚本、设置和运行中的脚本进程无需回滚。

## 参考资料

- [DisplayArea.WorkArea](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.windowing.displayarea.workarea?view=windows-app-sdk-2.0)
- [AppWindow.MoveAndResize](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.windowing.appwindow.moveandresize?view=windows-app-sdk-2.0)
- [FrameworkElement.SizeChanged](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.frameworkelement.sizechanged?view=windows-app-sdk-2.0)
