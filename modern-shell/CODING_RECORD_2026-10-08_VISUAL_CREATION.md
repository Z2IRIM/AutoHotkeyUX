# 编码记录：原生可视化脚本创建

## 目标与依据

用户确认交互预览和设计后实施一个完整 Gate：用 WinUI 动作卡片替换 New Script 的纯代码模板流程，并允许本功能生成的脚本在应用内继续编辑。源码基线 `6015bf06623a1b1fee39af6e9abadd8941f4ae65`。主目录 `RelayPrompt.md` 为已有未跟踪文件，保留。

实施前备份：`modern-shell/backups/2026-10-08-visual-creation/AutoHotkeyUX-source-20261008-011832.zip`、`AutoHotkeyUX.Modern.before-visual-creation.exe`、`approved-preview.html`。旧EXE SHA256：`E10AD253FA891D8F205F553B9EE999374827E6AA7DAC67040AA7E22DAB0178BA`。

## 业务语义与决定

- 流程→AHK 是当前创建能力的组成部分；用户要求后做的转译解释为“已有 AHK→可视化”。本 Gate 不宣称解析任意脚本。暂不加入自由代码、循环或任意分支。
- 热键/脚本启动为两个触发类型；应用条件限制整个流程。六种动作按显示顺序生成；固定触发器不进入删除/排序列表。所有动作参数作为字面值处理，Send keys 仅接受受支持组合。
- 24个动作、50次撤销、文本4096字符、路径/URL2048字符、流程JSON256KiB、等待0–60000毫秒整数。限额在服务端独立校验；无效草稿可继续编辑但不能保存。右侧属性在中等宽度下移，窄窗口全部堆叠。
- `.ahk.flow.json` 是有版本的流程数据，记录身份、修订和源字节 SHA256。打开还将源文件与当前生成器输出比较；更新核对两个原文件的精确字节。未知/缺失/损坏数据和手工修改都不静默覆盖。
- 复用原脚本命名和目录/运行/启动服务。原普通 AHK 的外部编辑器不变；生成文件的 Edit 转入原生页面，受保护文件提供显式 Edit code。默认目录之外的文件不会进入现有 Scripts 顶层目录列表，成功提示说明这一点。
- 保存不会运行或重启；已有运行状态应用新内容仍需 Restart。保存排队由应用持有，退出排空已接受写入；页面导航保留草稿，进程退出前需用户保存草稿。
- 文件对不是原子事务。更新在两文件独占打开期间写入，第二文件写入失败时恢复两份旧字节。中断/恢复失败保留固定名称的 pending 恢复材料；运行和重启共用保存声明锁，在未完成状态拒绝操作并保留已运行的解释器。
- 新建失败若已触碰目标，保留部分文件及完整预期字节，避免关闭锁后删除时误删外部修改。重试另取名称。代价：失败目录可能需要手动比对/清理。
- 诊断实例也隔离运行时缓存，避免新构建的载荷哈希触发正在使用的用户运行时目录刷新。正常运行时路径和已有设置布局不变。

## 修改文件及职责

- `Models/VisualFlowDocument.cs`：流程、触发、动作及打开快照。
- `Services/VisualFlowCodec.cs`、`VisualFlowGenerator.cs`：严格版本化校验、纯v2生成和字节哈希；重复JSON字段、未知字段/枚举、非法参数拒绝。
- `Services/VisualFlowStore.cs`、`VisualFlowPending.cs`：成对创建/更新/打开、并发避让、写入排空、恢复材料与字节回滚。
- `Services/VisualEditorSession.cs`：有界历史和稳定动作排序；`ScriptFileName.cs`：复用的Windows命名规则。
- `Pages/NewScriptPage.xaml` 及 Editing/Properties/Saving 分部：原生库、流程、属性、保存和未保存草稿保护；局部卡片更新避免每次输入重建完整列表。
- `MainWindow.xaml.cs`、`ScriptsPage.xaml.cs`、`ApplicationServices.cs`、`App.xaml.cs`、`Program.cs`：共享服务、页面路由、保存排空和显式诊断命令。
- `EmbeddedAutoHotkeyRuntime.cs`：仅增加显式诊断缓存位置，默认位置保持原有规则。`ScriptExecutionService.cs`：声明锁下启动/重启，未完成保存不终止已有脚本。
- 新建页面及主窗口诊断、SmokeTests 增量检查、创建指南/README/实施计划/本记录。保留原源码打包脚本。

## 验证与证据

1. 行为 RED：接口桩运行，7组明确失败；测试代码编译错误修正后才记录 RED。
2. 隔离服务检查：`dotnet run --project tools/ModernShell.SmokeTests -- <AutoHotkey64.exe> <isolated-root> --visual-only`。当前11/11组通过，涵盖参数/转义/严格JSON、两文件名称冲突及并发、手工源和副文件修改、锁定失败、pending保护、真实部分写入回滚、历史/排序、退出排空。
3. 实际AutoHotkey2.0.29解释器解析所有动作和两种带程序范围的触发器；测试前置 ExitApp 防止动作执行。生成的启动流程通过现有 Run/Restart 在隔离目录写入测试标记，最终停止仅测试拥有的进程。
4. `dotnet publish modern-shell/AutoHotkeyUX.Modern.csproj -c Release -r win-x64 -o modern-shell/artifacts/visual-verification` 成功；最新编译无C#或XAML错误。
5. EXE `--verify-visual-creation <report>`：真实WinUI六种参数编辑、非法URL/小数等待禁存、上下移/撤销重做/重排完成、保存回编、目录集成、导航保草稿及修订2保存通过。编辑区域宽度1288/788/341.33 DIP分别验证三种布局，无可见控件横向越界。主窗口80%屏幕比例、图标、无顶栏搜索和侧栏路由通过。
6. 布局检查曾发现 ListView 回收容器停放于X≈-15000；已限定测量当前数据项的容器，仍检查实际显示的控件。PNG采用不透明主题底色，Mica/原生标题按钮不在XAML捕获范围内。

证据保存在主目录 `.verification/visual-creation`（交付时同步）；独立审阅和最终部署/增量包结果在交付完成后追加。

## 未验证、限制和风险

- 实际鼠标拖动只验证了WinUI重排完成后的数据边界；物理拖动手势及屏幕阅读器朗读未做真机人工验收。已有向上/向下按钮可完成同一排序。
- 任意旧AHK反向转译、分支/循环和复杂Shell目标动作属于下一阶段。六类动作的真实外部网站/程序交互没有逐一执行，已验证生成语法；编辑/保存不执行外部动作。
- 未进行断电实验；通过真实部分写入失败验证普通回滚，并通过保留pending目录验证恢复拒绝和运行保护。
- 未重新测试真实重启/登录和未改动的解压/终端硬件交互；已有后台脚本将在部署时核对身份和内容。

## API、兼容性与回滚

无数据库、无对外网络API或新增安装依赖。增加版本1流程副文件和内部服务接口；普通AHK及设置保持兼容。已有可视化副文件不要单独删除；既有模板创建入口由流程替换。

程序回滚：退出管理器，用备份EXE覆盖正式EXE后重新启动；用户脚本不删除。源码回滚以基线6015bf0为参考，仅回退本任务提交，保留其他修改。当前生成的AHK仍可运行，旧管理器会以外部编辑器编辑它；流程副文件可保留供恢复。

## 审阅裁定和遗留

待单次整体审阅，重要问题将追加失败复现及修复结果；低风险建议单列，不夹带扩展功能。
