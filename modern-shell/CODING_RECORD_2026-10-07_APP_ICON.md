# AutoHotkeyUX：绿色 H 应用图标

日期：2026-10-07（Asia/Manila）。基线：`f15023e`。本次为一个完整的图标接入 Gate。

## 目标与业务决定

用户确认原有核心功能可以正常使用，并从三个可交互预览中选择第三个「快捷键 H」。最终图案采用绿色键帽、白色 H、浅绿到深绿渐变，用于 EXE、原生窗口/任务栏及托盘。

图标使用可编辑 SVG 母稿和九个尺寸的 ICO（16、20、24、32、40、48、64、128、256 px）。保留透明边缘。ICO 同时作为编译图标和托管 embedded resource，运行时直接从内存创建 HICON，继续交付单 EXE。

## 分析与主要改动

- `ApplicationIconService` 按系统 DPI 选择足够大的帧，创建并持有窗口和托盘的原生句柄。App 在移除托盘、关闭窗口后释放句柄；重复 Dispose 安全。
- `MainWindow` 使用 Windows App SDK 的 SetIcon 接口，`TrayIconService` 使用相同图案的独立句柄。图标加载失败记录诊断并采用 Windows 默认图标，管理器仍可启动。
- `manager-startup.json` 增加 `CustomWindowIconApplied`、`CustomTrayIconRegistered`，`manager.log` 记录尺寸和 DPI。现有 `--verify-ui` 同时检查窗口图标接入。
- 首轮发布发现默认项目项将旧 EXE 和开发备份也收集为 publish inputs，导致包增至 707,549,984 字节。核对真实 FilesToBundle 后，在 csproj 排除 `artifacts/**`、`backups/**`，并移除图标母稿及已嵌入 ICO 的重复 Content/None 项。修正后旧产物和 sidecar 图标均为 0，发布包恢复至 235,684,951 字节。
- 图标再生成脚本使用开发环境的 Node.js + sharp，由同一 SVG 渲染 PNG/ICO；普通 .NET 构建使用已提交 ICO，应用运行无需 Node.js 或外置图标。

## 修改 / 新增 / 删除文件

修改：`AutoHotkeyUX.Modern.csproj`、`App.xaml.cs`、`MainWindow.xaml.cs`、`MainWindow.Diagnostics.cs`、`Services/TrayIconService.cs`、`README.md`。

新增：`Assets/AutoHotkey.svg`、`Assets/AutoHotkey.ico`、`Assets/AutoHotkey.png`、`build-icon.mjs`、`Services/ApplicationIconService.cs`、本记录。

删除：无。原窗口文件包含既有导航和原生 chrome，本次只添加图标接入；句柄解析与生命周期放在独立服务，避免扩大窗口职责。

## 验证命令与结果

| 验证 | 命令 / 证据 | 结果 |
| --- | --- | --- |
| 图标生成 | `node modern-shell/build-icon.mjs <bundled-node_modules>` | PASS，九个尺寸 |
| Release 编译 | `dotnet build modern-shell/AutoHotkeyUX.Modern.csproj -c Release -p:Platform=x64 --no-restore` | PASS，0 warning / 0 error |
| 单 EXE 与 bundle 检查 | `dotnet msbuild modern-shell/AutoHotkeyUX.Modern.csproj -t:Publish -p:Configuration=Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64 -p:PublishDir=<staging> -getItem:FilesToBundle` | PASS，旧输出/备份和 sidecar 图标均为 0 |
| ICO 帧 | Pillow 读取九个真实帧，检查尺寸、透明角落、白色 H 横梁 | PASS，9/9 |
| EXE Shell 图标 | Windows PowerShell 5.1 `System.Drawing.Icon.ExtractAssociatedIcon`，读取实际 EXE 并验证绿色键帽/白 H/透明边缘 | PASS，32 px；`.verification/exe-h-icon.png` |
| 实际 WinUI | 发布包 `--verify-ui <report>` | PASS，窗口图标已应用，6 次页面加载、搜索与最小窗口布局通过 |
| 实际托盘 / runtime | 原生 tray 注册成功状态、发布包 `--verify-runtime <report>` | PASS，托盘自定义图标成功注册，AHK 2.0.29 |
| 当前程序更新 | 退出旧管理器、复制经验证 EXE、恢复原显示状态 | PASS，新管理器 PID 52872；原脚本 PID 34180 及开始时间保留，启动设置一致 |
| 源码包 | Windows PowerShell 5.1 `package-source.ps1 -Incremental -BaseRef f15023e` | 本次增量交付到 `C:\DESKTOP\srcpack_Area\AutoHotkeyUX`，包含图标源资产和本记录 |

首轮像素探针误取整个画布中心，随后修正为 H 横梁区域与对应像素中心；图形本身未因此改动，也没有降低颜色检查阈值。

本次验证覆盖图标资源、发布边界、实际 native 加载和管理器重启，不重复运行已完成且未受修改影响的 45 项脚本/解压检查。

## 未验证事项与风险

- 未通过自动化截图逐个核对 Windows 桌面、任务栏、托盘的最终视觉。真实 EXE 图标提取与 native API 加载成功已验证。
- 多显示器间移动后的托盘 DPI 重新适配未专项验证；首次加载按系统 DPI 创建句柄，窗口与 Shell 可继续缩放图标。
- Windows 重新登录/重启未在本次重复执行。

## 数据库 / API / 兼容性 / 迁移

没有数据库或远端 API 变化。新增两项本地诊断字段。快捷脚本、解压、普通脚本进程语义和启动选项沿用原实现；本次更新实际检查了现有脚本身份和设置的保留。Windows App SDK / .NET / 内嵌 AHK runtime 架构保持原有部署方式。

## 备份与回滚

施工前源代码、README 与当前可运行 EXE 位于 `backups/2026-10-07-icon/`。如需回滚，先通过 `--exit-manager` 退出管理器，再恢复 `AutoHotkeyUX.Modern.before-icon.exe` 到原发布路径并启动。原脚本继续运行。源码可 revert 本次图标提交；保留用户的 `RelayPrompt.md`。

## 参考依据

- [Microsoft：AppWindow.SetIcon](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.windowing.appwindow.seticon)：使用 HICON 对应的 IconId 设置原生窗口图标。
- [Microsoft：CreateIconFromResourceEx](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-createiconfromresourceex)：从资源数据创建原生图标，非共享句柄由应用调用 DestroyIcon 释放。

遗留问题：本 Gate 无阻塞事项。
