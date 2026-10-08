# 可配置可视化动作：施工与验收记录

## 目标与当前状态

落实已批准的 `VISUAL_EXPLORER_WORKFLOW_PLAN_2026-10-08.md` 和交互预览。一个 Gate：用可视化动作、参数和结果引用创建打开终端、解压和 Alt＋左键分流流程。界面、生成器、运行协议及候选 EXE 已完成；原生验收和正式版本替换尚待明确授权。本文件会随验收更新。

基线：`769fa13`。开发在托管工作区 `C:\Users\Z\.codex\worktrees\configurable-actions\AutoHotkeyUX`；实施前源码和 EXE 保存于主项目 `.verification\configurable-actions`。未改写用户既有脚本；不继续任意 AHK 反向导入，不延续旧关机安排。

## 达成的业务能力

- 动作库由 16 项增加至 21 项，补齐读取路径属性、拼接路径、创建目录、停止本次流程及通知。分类继续使用原生浮动菜单，保持紧凑流程行。
- 终端节点可继承应用设置或独立选择程序、五种位置、DIP 宽高和间距；等待返回此次启动的 HWND。未等待时不能引用窗口就绪结果。
- 解压节点可配置输入、根目录、文字与前序字段组成的名称，以及自动编号／重名报错。结果为提交完成的实际目录，供打开文件夹等后续节点使用。
- 点击／选择／当前目录只捕获一次，运行结果按本次调用隔离。无有效目标可静默结束；源引用受顺序和分支边界约束。
- 配置好的单节点可以命名、复制、保存为“我的动作”，支持重命名和删除。预设去掉原流程的 GUID；插入时重选动态来源。
- 提供空白、终端、解压和智能分流入口。属性区内部滚动，代码仅在展开预览时生成；运行步骤和实际输出进入现有有界活动记录。
- Scripts 中对已知内置快捷键冲突提供明确的替换选择。先停用管理器已拥有的内置脚本，等新 v3 脚本确认解析完成，再继承启动选择；失败停止本次尝试并恢复原选择。普通外部脚本仍不被认领。

## 工程决定与理由

从空白搭建推荐分流：选择 Alt＋左键和 Explorer／桌面作用域 → 获取点击对象 → If「Is folder」的真分支添加终端（来源为点击对象的 Usable directory）→ Else 内再判断「Is archive」，真分支添加解压（来源为点击对象的 Path）和打开文件夹（来源为解压的 Usable directory），其余分支添加停止本次流程。终端和解压分别在 Configuration 中选 Custom 后设置参数。保存后在 Scripts 启动；同键冲突时明确改键或替换内置。只做独立脚本也可选终端／解压示例，并使用不同快捷键。

保存为“我的动作”只保存选定节点的配置，不把整个流程封装成动作组；动态输入在新流程插入时重新选择。此边界使预设可跨流程使用，避免绑定到已删除的旧步骤。

1. 新能力使用 schema v3。v1/v2 生成规则保持不变，v2 的四份辅助资源固化为快照；旧流程不会因当前终端资源更新被误判为手改。首次保存升级前，在相邻 `.flow.backups` 目录保留原始文件对。
2. 保留原动作枚举顺序，按节点增加独立参数记录。文本组合只包含固定文字或类型明确的前序字段，不接受代码表达式。
3. 复用原终端队列、窗口归属和 DPI／工作区裁剪；唯一窗口名避免复用旧终端。没有触发时不安装调度轮询，只在当前任务就绪／完成等待期间短时检查。
4. 显式解压复用 `ShortcutCommandService` 的同一有界队列和 `ArchiveExtractionService`。身份与参数快照共同去重；原子结果文件只使用管理器声明的目录。收到或可能收到的请求不再冷启动重做。
5. 无暖管理器时，使用同一 EXE 的无界面入口。父管理器的注册表上下文通过子进程环境传递，暖／冷路径使用同一结果目录；隔离验收不会使用正式用户设置。
6. 对已交付 v3 的内置辅助资源，仅当哈希匹配已知原版时备份升级。先检查所有模块，再成组更新；发现自定义内容则保留，失败恢复原字节。
7. 对替换使用启动确认收据，而不是把 Process.Start 成功当作解析完成。界面选择和运行／启动所有者分开，锁顺序与既有偏好事务一致。

资源版本属于生成契约。后续修改 v3 生成内容时，应提供新的生成版本或保留本次资源快照，不能直接让旧 v3 文件失去重开能力。

## 修改文件范围

- `Models`：动作选项、文本组合、可移植预设、请求／事件、执行活动。
- `Services`：版本／编解码／校验／树／生成器、预设、成对保存、终端启动确认、解压配置与共享协议、冲突替换及辅助资源升级。
- `Resources/VisualFlow`：冻结 v2，新增 v3 调度、操作和协议；扩展原 `ExplorerShortcuts/v3` 终端及设置上下文。
- `Pages/NewScriptPage.*`：参数、来源、组合字段、浏览、预设、示例、运行记录及原生诊断；`ScriptsPage` 增加明确替换对话框。
- `ApplicationServices`、`App`、`MainWindow`、项目资源声明及精简检查入口。

沿用现有目录和职责 partial。源代码打包复用项目根目录的 `package-source.ps1`，不打包测试、依赖和构建产物。

## 已执行验证

- `dotnet run --project tools/ModernShell.SmokeTests/ModernShell.SmokeTests.csproj -c Release --no-restore -- --configurable-model <任务目录>/model`：整体审阅修复后 45 项 PASS。覆盖旧 v2 生成字节、首次升级语义、新模型、结果可见性、历史、协议边界、预设重绑定／持久化／复制、运行候选保护与启动补偿、跨所有者结果保护、队列去重／容量／排空及结果发布失败。使用隔离文件和注入工作；未启动解释器、窗口或真实解压。
- `dotnet publish modern-shell/AutoHotkeyUX.Modern.csproj -c Release -r win-x64 --no-restore -p:Platform=x64 -p:DirectoryBuildTargetsPath=<已核验资源复用 targets> -o modern-shell/artifacts/configurable-candidate`：PASS。复用基线同字节的解释器、UX ZIP 和编译器资源。
- 修复编译发现的 WinUI 字体命名空间和诊断默认参数遗漏。最终候选构建成功。

一次 fresh-context 整体审阅发现 5 项 Important 与 1 项 Minor，统一修复：首次升级保留归档旁输出、旧错误通知及异步终端顺序；claim 原子发布且未取得所有权者禁止写结果；运行中的候选流程要求用户先明确停止，失败补偿按本次 PID／启动时间操作；暖／冷进程退出时优先读取已发布结果；原生探针包含实际终端资源；空路径输入先走公共校验。旧升级和跨所有者污染分别先由定向断言检出 FAIL，再修复为 PASS。新 v3 空白和示例保留默认静默策略。

## 待授权的实机步骤

候选 EXE 的 `--verify-visual-creation <任务报告> --configurable-only --with-runtime` 会：

1. 打开一份隔离 WinUI 实例；从空白通过原生控件搭建分流流程，保存／重开，检查宽／窄布局和浮动菜单，写渲染截图。
2. 运行任务拥有的 AHK v2 探针，打开并关闭两份新 Windows PowerShell 终端，检查就绪 HWND、上／下位置和工作区边界。
3. 在隔离目录生成一个小 ZIP，分别验收暖 IPC 和冷无界面 EXE 的实际解压、重名保护，以及路径／文本准备动作。
4. 在隔离设置中短暂使用 Ctrl＋Alt＋中键检查内置脚本替换：模拟一次运行时缺失，再检查真实 v3 就绪确认和启动转移；随后停止测试拥有的解释器。若该组合已被正式内置工具使用，跳过并报告未验证。

工作区为 `.verification/configurable-actions/native-workspace-<GUID>`；注册表仅用 `HKCU\Software\AutoHotkeyUX.Verify\<GUID>`，结束删除该测试键。保留测试报告和样本，正式 EXE 和用户脚本不在这些步骤内替换。

## 未验证、风险与回滚

原生窗口、AHK 解析、真实文件操作、Explorer／桌面物理快捷手势、各归档格式和同机闲置 CPU 对比当前均 UNVALIDATED。前阶段证据不算本阶段执行结果。密码包、分卷包没有新增支持；无结果或模糊确认只报错，不重做。

无数据库迁移；JSON 新增 v3 与单节点预设文件。旧命令和旧流程保留。旧应用不能编辑 v3；回滚前保留新的文件对和预设。应用回滚使用实施前 EXE；首次升级的旧脚本使用 `.flow.backups` 原始文件对。正式版本交付、增量包及本 Gate 完整实机证据仍待完成。

参考：[Windows Terminal 命令行](https://learn.microsoft.com/windows/terminal/command-line-arguments)、[AutoHotkey 官方 SetTimer 文档源码](https://raw.githubusercontent.com/AutoHotkey/AutoHotkeyDocs/v2/docs/lib/SetTimer.htm)。
