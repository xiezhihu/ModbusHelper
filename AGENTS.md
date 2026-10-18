# ModbusHelper 项目规则

文档分工原则：仅保留核心信息；过程历史归入 archive；新建窗口优先读取 handoff，长期固定结论查阅 facts，顶层规则以本文件为准。
文档职责：

1. AGENTS.md：主导文档，维护文件索引、开发注意事项、回复模板、项目顶层规则。
2. handoff（交接简报，新窗口必读）：仅记录当前进度、下一步任务、遗留问题，用于窗口快速同步，保持简短。
3. facts（长期事实库）：存放稳定不变的项目结论；结论过时直接覆盖，不堆积历史，handoff 需要细节时查阅。
4. archive（对话归档，常规不读取）：新建对话自动归档，保存窗口过程细节；仅回溯问题使用，避免上下文膨胀。
读写规则：切换新窗口只读 handoff，按需查 facts，不读 archive；窗口交接收尾时统一更新 handoff + facts；AGENTS/handoff/facts 严控篇幅，过程细节全部放入 archive。
Git 规范：单个功能点完成后小粒度 git commit。
重要提醒：AI 代码必须人工及时审阅；文档内容不混写：现状写 handoff，长期结论写 facts，顶层规则在 AGENTS，过程历史进 archive；facts 直接覆盖过期内容，不保留废弃版本。

## 文件索引

| 文件 | 职责 |
| --- | --- |
| `README.md` | 项目入口、构建方式和当前阶段说明 |
| `docs/handoff.md` | 新窗口必读的当前进度、下一步和遗留问题 |
| `docs/facts.md` | 稳定的技术决策和长期项目结论 |
| `docs/架构说明.md` | 分层边界、依赖方向和后续目录规划 |
| `docs/任务拆分.md` | 分阶段任务、依赖关系和验收追溯 |
| `docs/archive/` | 历史对话和过程记录，常规不读取 |

## 技术基线

- 目标框架为 `.NET 8`，界面为 WinForms。
- Modbus 通信固定使用 `NModbus` 与 `NModbus.Serial` 3.0.83，采用 3.0.x 的主站和从站 API。
- 图表固定使用 `WinForms.DataVisualization` 1.10.2。
- 除上述 NuGet 包和 .NET 内置库外，不引入第三方依赖。
- 解决方案分为 `Core`、`Communication`、`Business`、`WinForms` 四个项目。

## 分层约束

- 依赖方向固定为 `WinForms -> Business -> Communication -> Core`，不得反向引用。
- `Core`、`Communication`、`Business` 不得引用任何 WinForms、Control、Form、MessageBox、Invoke 等 UI 类型。
- `WinForms` 不直接调用 NModbus，也不直接调用通信层读写方法。
- 同一个主站实例必须由唯一串行读写上下文访问，轮询调度不得并发调用。
- 配置模型保持纯 POCO，持久化配置与运行时状态必须分离。

## 开发注意事项

- 修改通信层或业务层后，必须运行项目构建和 UI 引用边界检查。
- 所有 NModbus 调用必须有明确异常转换、超时处理和资源释放路径。
- 时间显示使用本机本地时间，并保留毫秒；串口、Socket、主站实例必须可重复安全释放。
- 新增文档、变更说明和提交信息全部使用简体中文。
- 完成一个可独立验收的功能点后立即提交，提交信息采用规范式前缀。

## 完成回复格式

每次功能完成后按“完成内容、验证结果、当前风险、下一步”四项简要说明，不粘贴大段命令输出。
