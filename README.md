# Modbus 调试工具

面向工业现场调试和自动化测试的 Modbus RTU/TCP 主站与从站模拟工具。当前版本已实现连接管理、八种常用功能码、多任务轮询、点位表、数据解析、从站模拟、异常注入、报文日志、通信统计、趋势图和字节序探测。

## 解决方案

| 项目 | 目标框架 | 职责 |
| --- | --- | --- |
| `src/ModbusHelper.Core` | `net8.0` | UI 无关的模型、纯 POCO 配置、枚举和公共协议规则 |
| `src/ModbusHelper.Communication` | `net8.0` | NModbus 主站、RTU/TCP 从站、完整帧采集和异常转换 |
| `src/ModbusHelper.Business` | `net8.0` | 连接管理、轮询调度、点位、日志、统计、趋势缓冲和探测 |
| `src/ModbusHelper.WinForms` | `net8.0-windows` | WinForms 主界面、点位于探测窗体、依赖装配 |
| `tests/ModbusHelper.Verification` | `net8.0` | 无第三方测试框架的业务与 TCP 自环验证 |
| `tests/ModbusHelper.UiSmoke` | `net8.0-windows` | 五页签渲染、UI 自环和运行时布局检查 |

依赖方向固定为 `WinForms -> Business -> Communication -> Core`。通信层与业务层不引用任何 WinForms 类型。

## 主要功能

- RTU/TCP 主站连接、断开、重连、传输层超时和应用层应答超时。
- 功能码 01、02、03、04、05、06、0F、10。
- 0 基与 1 基地址换算，支持 `40001` 风格输入。
- 多轮询任务独立启停，同一主站串行执行，不同连接可独立并行。
- Int16、UInt16、Int32、UInt32、Float、Double、ASCII、UTF-8 双向解析。
- ABCD、CDAB、BADC、DCBA 四种寄存器数据布局。
- NModbus 3.0.x RTU/TCP 从站模拟、多 UnitID 独立存储和手动改值。
- 指定 UnitID、功能码和地址的 Modbus 异常码注入。
- Master/Slave、TX/RX 完整报文日志，暂停、过滤、搜索、CSV/TXT 导出。
- 固定容量无锁 SPSC 趋势缓冲区，200 毫秒 UI Timer 批量刷新。
- 字节序自动探测、范围高亮、推荐和一键应用。
- 配置文件版本化、字符串枚举、运行时状态隔离和关闭时自动保存。

## 依赖版本

- `NModbus` 3.0.83
- `NModbus.Serial` 3.0.83
- `System.IO.Ports` 8.0.0，属于 .NET 平台库
- `WinForms.DataVisualization` 1.10.2

## 构建与启动

需要安装 .NET 8 SDK，或能够构建 `net8.0` 目标的更高版本 SDK。

```powershell
dotnet restore ModbusHelper.sln
dotnet build ModbusHelper.sln --configuration Release
dotnet run --project src/ModbusHelper.WinForms/ModbusHelper.WinForms.csproj
```

无头模式入口已预留，二期实现：

```powershell
dotnet run --project src/ModbusHelper.WinForms/ModbusHelper.WinForms.csproj -- --headless
```

## 验证

```powershell
dotnet run --project tests/ModbusHelper.Verification/ModbusHelper.Verification.csproj --configuration Release
dotnet run --project tests/ModbusHelper.UiSmoke/ModbusHelper.UiSmoke.csproj --configuration Release
powershell -ExecutionPolicy Bypass -File scripts/verify-layer-boundaries.ps1
```

验证控制台覆盖配置往返、参数边界、四种布局、CRC/CSV 规则、SPSC 并发、五任务轮询、TCP 主从自环、异常码 02、重连和两层超时。UI 冒烟覆盖五个页签、五曲线、手动读取和字节序探测窗体。

RTU 虚拟串口专项验收：

```powershell
dotnet run --project tests/ModbusHelper.Verification/ModbusHelper.Verification.csproj -- --rtu COM1 COM2
dotnet run --project tests/ModbusHelper.UiSmoke/ModbusHelper.UiSmoke.csproj -- --rtu COM1 COM2
```

第一个参数为主站串口，第二个参数为从站串口。

## 文档

- 使用步骤：`docs/使用说明.md`
- 当前进度：`docs/handoff.md`
- 长期技术结论：`docs/facts.md`
- 架构和目录：`docs/架构说明.md`
- 开发任务拆分：`docs/任务拆分.md`
