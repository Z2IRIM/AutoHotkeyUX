# 实用流程能力实施计划

Spec: modern-shell/VISUAL_WORKFLOW_CAPABILITIES_DESIGN_2026-10-08.md

按一个完整 Gate 内联实施；现有同意授权隔离工作区、预览、实现、验证和本机交付。不另设逐项批准节点。

## Task 1: 完成流程语言、编辑器和运行闭环

Interfaces: VisualFlowDocument/Codec/Generator → Store、Session、NewScriptPage、Execution。复用现有原子保存、Shell 解析、终端定位和解压服务。旧 v1 生成器不改变字节。

1. 备份基线与正式 EXE，创建工作区；提供可交互 UI 预览。
   Expected: Git 基线 0eb68d2；正式 EXE SHA256 5C8FD7AAFF2B37ED65A8B402635C02FAEE0A4462CB9A0F4515E2C1E78FB269DC；预览三个示例、分支选择和引用操作无错误/横向溢出。
2. 编写新能力拒绝无效引用和 v1 兼容的验证，观察未实现失败，再实现版本化模型/验证/生成及递归编辑服务。
   Expected: 缺少 v2 能力时 FAIL；实现后合法示例通过，前向/分支越界/删除引用失败且原文件保留。
3. 原生分类库、参数来源、分支入口、例子、撤销、兼容编辑及快捷键冲突检查；真实 AHK 生成运行、超时与提取结果验证。
   Expected: 三个示例保存、重开；生成 AHK 可解析；模拟输入/窗口运行只用隔离验证对象；禁止触发用户真实热键或改变真实剪贴板。
4. 风险匹配验证、发布构建、编码记录、使用说明与提交。原生宽中窄布局保持视口填满。
   Expected: 针对验证和构建 PASS，原生报告没有裁切，源码已提交且记录可审阅。

## Final integration within this Gate

一次 fresh-context 全分支审阅；有影响发现一轮 RED/GREEN 修复。随后主工作区合并、带回滚备份的本机部署、增量源码 ZIP 字节审计、归档工作区。
   Expected: 正式程序可启动、原有受管核心运行正常、源码包与最终源文件一致、不包含测试/依赖/用户临时文件；输出记录路径、限制与回滚方法。

Review Focus: 非活动 Explorer 第一次点击、错误引用和不同分支作用域、空剪贴板与多选、带引号/Unicode 的参数、窗口超时/焦点改变、快捷键冲突、v1 原文件、升级后重开、临时解压报告失败、含嵌套条件的排序/撤销和原生布局。
