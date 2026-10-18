# 交接简报

更新时间：2026-10-18

## 当前进度

- 已初始化 Git 仓库和 `ModbusHelper.sln`。
- 已建立 `Core`、`Communication`、`Business`、`WinForms` 四项目分层。
- 已固定 NModbus 3.0.83、NModbus.Serial 3.0.83、WinForms.DataVisualization 1.10.2。
- 已预留 `--headless` 入口，UI 依赖装配与主窗体骨架已建立。
- 已完成架构说明、事实库和任务拆分，业务功能尚未实现。

## 下一步

1. 执行 T01，建立纯 POCO 配置、`ConfigVersion` 和 JSON 持久化。
2. 执行 T02 与 T03，先稳定参数校验、地址基准和异常中文映射。
3. 之后进入 T04 连接生命周期和 T05 主站单次读写。

## 遗留问题

- 原始报文采集需要在实现 T06 前确认 NModbus 3.0.83 可装饰的传输层接口，确保请求和响应字节都能截获。
- 趋势缓冲区在 T14 实现前需确定 SPSC 容量和读写序号策略，默认每曲线 5000 点。
- RTU 自环依赖 com0com 等虚拟串口环境，最终验收前需准备现场测试条件。
