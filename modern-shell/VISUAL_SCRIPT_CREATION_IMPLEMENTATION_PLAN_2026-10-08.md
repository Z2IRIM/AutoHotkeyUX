# 可视化脚本创建实施计划

基线：6015bf06623a1b1fee39af6e9abadd8941f4ae65。设计：`VISUAL_SCRIPT_CREATION_DESIGN_2026-10-08.md`；用户已确认交互预览并授权实施。一个完整 Gate，当前会话直接执行。

## Global Constraints

- 复用 WinUI 主壳、脚本目录、运行/重启/启动服务；保留已有图标及窗口尺寸规则。
- 先完成可视化创建与本功能生成文件的回编；任意 AHK 转可视化留给下一 Gate。
- 六种动作、快捷键/启动触发、活动程序条件；不引入自由代码、循环或任意分支。
- 编辑和保存不执行动作；只有用户点击 Run/Restart 才运行。
- 主目录预先备份；隔离实现，验证后并入原分支并部署。保留 RelayPrompt.md。
- 文件成对保存采用暂存、独占锁、校验、恢复标记；两文件不宣称原子事务。意外中断只报告并保留恢复材料，不自动覆盖用户文件。
- 输入界限：24 个动作，50 次撤销，文本4096字符，路径/URL2048字符，JSON256KiB；等待0–60000毫秒；副文件未知字段/类型/版本拒绝。
- 仅运行与本 Gate 影响对应的最小充分验证；已通过且未变的旧功能不重复测试。

## Task 1: 完成原生可视化创建 Gate

### Interfaces

Produces: `VisualFlowDocument` / `VisualFlowCodec` / `VisualFlowGenerator` / `VisualFlowStore` / `VisualEditorSession`。应用拥有一个保存服务，页面只管理编辑状态；Scripts Edit 检查副文件后选择可视化或原外部编辑器。

Consumes: `ApplicationServices`、`ScriptCatalogService`、`ScriptExecutionService`、`AutoHotkeyIntegration.EditScript`、主窗口已有 PageHost。

### Files

- 新增 `Models/VisualFlow*.cs` 和 `Services/VisualFlow*.cs`、`VisualEditorSession.cs`。
- 替换 `Pages/NewScriptPage.xaml` 及按编辑/属性/保存职责拆分的 code-behind。
- 修改 `ApplicationServices.cs`、`MainWindow.xaml.cs`、`ScriptsPage.xaml.cs`、`App.xaml.cs`、`Program.cs` 的构造/路由/保存排空与诊断入口。
- 新增有界服务检查及原生诊断；更新原页面几何诊断为动作库/流程/属性布局。
- 更新创建指南、README、任务记录；复用 package-source.ps1。

### Steps

1. 先写有界行为检查，使用未实现接口桩运行。
   Command: `dotnet run --project tools/ModernShell.SmokeTests -- <runtime> <isolated-root> --visual-only`
   Expected: 因尚未实现流程校验/生成/存储而失败；编译错误不算 RED。
2. 实现类型模型、独立校验、纯生成器：正确转义引号/反引号/换行/Unicode，固定快捷键词法，程序条件分别生成 #HotIf 或 if。
3. 实现成对创建/更新/打开。两个名称都避让；独占写入；更新比较源字节哈希及副文件原字节；失败回滚；恢复材料不猜测执行；未知字段、篡改、缺失副文件明确报错。
4. 实现有界编辑会话及 WinUI 页面，动作库→流程→属性；触发固定首位；选择、参数、拖动/上下移、删除、撤销/重做、折叠代码；导航保留草稿；窄屏属性下移；成功页留在 app 内。
5. Scripts Edit 接入本功能生成文件的原生编辑；手工 AHK 保留现有外部编辑路径。运行文件更新后继续使用 Restart 语义。
6. 验证服务边界与真实解释器。
   Command: 同步骤1，完整 visual-only 检查。
   Expected: 全部 PASS；包含创建/碰撞/更新/修改保护/恢复标记/非法文档/撤销重排；实际 AHK 2.0.29解析六种动作，安全测试脚本仅写隔离标记。
7. 发布并执行原生检查。
   Command: `dotnet publish modern-shell/AutoHotkeyUX.Modern.csproj -c Release -r win-x64 -o modern-shell/artifacts/visual-verification`，随后 EXE `--verify-visual-creation <report>`。
   Expected: 发布成功；真实 WinUI 页面可创建/打开/编辑/排序/撤销，验证失败禁存，宽窄布局无越界，导航保草稿，保存不运行；记录原生报告。
8. 文档、变更检查、提交与单次整体审阅；重要问题用失败复现→修复验证。并入、部署、增量源码包与归档。
   Command: `git diff --check`；`package-source.ps1 -Incremental -BaseRef 6015bf06623a1b1fee39af6e9abadd8941f4ae65`。
   Expected: 无空白错误；ZIP 写入 `C:\DESKTOP\srcpack_Area\AutoHotkeyUX`，仅源码和文档，有基线清单；新EXE能正常启动，原后台脚本归属与设置保留。

## Review Focus

- 恶意/错误副文件、无效热键、保留设备名、非法路径、JSON未知字段/枚举/null、磁盘失败、外部改动、重试和同名并发不会静默覆盖有效文件。
- 两文件中断后的恢复标记与保存回滚语义；恢复材料路径不能任意指向/删除其他文件。
- 模型变动的历史/选择、拖动后的实际保存顺序、页面导航与切换编辑对象；保存中的双击/退出不会丢失已接受写入。
- XAML 响应式布局、键盘替代操作、文案不声称未支持的转译或未执行的动作。
- 原 Scripts 外部编辑、后台脚本、启动设置和主窗口行为兼容。
