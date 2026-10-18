# Modbus 调试工具

面向工业现场调试和自动化测试的 Modbus RTU/TCP 主站与从站模拟工具。项目目标包括手动读写、批量轮询、趋势图、报文日志、通信统计、从站模拟和字节序探测。

当前仓库处于工程初始化阶段，已完成分层解决方案、依赖固定、入口拆分和任务规划，业务功能尚未接入。

## 当前解决方案

| 项目 | 目标框架 | 职责 |
| --- | --- | --- |
| `src/ModbusHelper.Core` | `net8.0` | UI 无关的领域模型、纯 POCO 配置、枚举和公共数据结构 |
| `src/ModbusHelper.Communication` | `net8.0` | UI 无关的 Modbus 通信封装、连接、异常转换和原始帧采集 |
| `src/ModbusHelper.Business` | `net8.0` | UI 无关的连接管理、轮询调度、点位、日志和统计 |
| `src/ModbusHelper.WinForms` | `net8.0-windows` | WinForms 界面、事件订阅、控件刷新和依赖装配 |

依赖方向为 `WinForms -> Business -> Communication -> Core`。通信层和业务层不引用任何 WinForms 类型。

## 依赖版本

- `NModbus` 3.0.83
- `NModbus.Serial` 3.0.83
- `System.IO.Ports` 8.0.0，属于 .NET 平台库
- `WinForms.DataVisualization` 1.10.2

## 构建与启动

需要安装 .NET 8 SDK，或能够构建 `net8.0` 目标的更高版本 SDK。

```powershell
dotnet restore ModbusHelper.sln
dotnet build ModbusHelper.sln
dotnet run --project src/ModbusHelper.WinForms/ModbusHelper.WinForms.csproj
```

无头模式入口已预留：

```powershell
dotnet run --project src/ModbusHelper.WinForms/ModbusHelper.WinForms.csproj -- --headless
```

当前无头入口会抛出 `NotImplementedException`，二期实现。

## 文档

- 当前进度与下一步：`docs/handoff.md`
- 长期技术决策：`docs/facts.md`
- 架构和目录规划：`docs/架构说明.md`
- 开发任务拆分：`docs/任务拆分.md`
