# AGENTS.md - Gear.NET 开发规范

## 1. 项目概述

Gear.NET 是 .NET 工业自动化微编排框架，采用洋葱架构（Onion Architecture）。

**工作区（2026-09-12）**：`/Users/dingyuwang/0-X/ZL.Gear.All/` · **五仓分仓真值源**：私有仓 `ZL.Gear.Docs` / `167_五仓分仓与Gear.All工作区指南` · 本仓边界见 [`REPO_BOUNDARY.md`](./REPO_BOUNDARY.md)

| 仓 | 路径 | 远程 |
|----|------|------|
| 公开框架 | `ZL.Gear/` | GitHub MIT（**暂不 push**） |
| 私有驱动 | `../ZL.Gear.Drivers/` | private |
| 私有行业包 | `../ZL.Gear.Exts/` | private |
| 私有 Demo | `../ZL.Gear.Demos/` | private |
| 私有文档 | `../ZL.Gear.Docs/` | private |

本目录（`ZL.Gear`）核心模块：
- **ZL.Gear.Core** / **ZL.Gear.Engine** / **ZL.Gear.Sensing**：框架 DL
- **demos/IndustryKit**：MIT 教学模板（仅引 Core）
- **Drivers / ConsoleApp / 内部 docs**：在 ** sibling 私有仓**，不在本仓 git 范围

---

## 2. 构建命令

### 2.1 完整构建

```bash
# 在解决方案根目录执行
dotnet build ZL.Gear.sln
dotnet build ZL.Gear.sln -c Release
```

### 2.2 单项目构建

```bash
dotnet build src/ZL.Gear.Core/ZL.Gear.Core.csproj
dotnet build ../ZL.Gear.Drivers/ZL.Gear.Drivers/ZL.Gear.Drivers.csproj
dotnet build src/ZL.Gear.Engine/ZL.Gear.Engine.csproj
dotnet build src/ZL.Gear.Sensing/ZL.Gear.Sensing.csproj
dotnet build ../ZL.Gear.Exts/ZL.Gear.Ext.Seat/ZL.Gear.Ext.Seat.csproj
dotnet build demos/IndustryKit/ZL.Gear.Samples.Industry.Client/ZL.Gear.Samples.Industry.Client.csproj
```

### 2.3 运行测试

```bash
# 运行所有测试
dotnet test ZL.Gear.sln

# 运行单个测试项目
dotnet test ../ZL.Gear.Drivers/ZL.Gear.Drivers.Tests/ZL.Gear.Drivers.Tests.csproj

# 运行单个测试类
dotnet test ../ZL.Gear.Drivers/ZL.Gear.Drivers.Tests/ZL.Gear.Drivers.Tests.csproj --filter "FullyQualifiedName~ModuleLoaderRegressionTests"

# 运行单个测试方法
dotnet test ../ZL.Gear.Drivers/ZL.Gear.Drivers.Tests/ZL.Gear.Drivers.Tests.csproj --filter "FullyQualifiedName~ModuleLoaderRegressionTests.StepDispatcher_仅Core_不应注册GenericMeasure与AiDecision"

# Extensions.Data.Tests（已在 ZL.Gear.sln；check_release 第 7 步 Release 专项仍保留）
dotnet test tests/ZL.Gear.Extensions.Data.Tests/ZL.Gear.Extensions.Data.Tests.csproj
```

### 2.4 发版门禁与场景验证

**公开轨真值源：`check_release_public.sh`（6 步）**——`public-guard.sh` (G0) → build → Data.Tests → IndustryKit verify（7 条）→ ExprDialectProof → dotnet pack（5 NuGet）。  
**私有全栈**：`../ZL.Gear.Demos/check_release.sh`（先跑公开轨，再 Full + Drivers + ConsoleApp）。

```bash
bash check_release_public.sh
bash scripts/public-guard.sh   # 仅 G0 静态检查
```

**ConsoleApp 单场景（Mock，无硬件）：**

```bash
export ZL_GEAR_FORCE_MOCK=true
dotnet build ../ZL.Gear.Demos/ZL.Gear.ConsoleApp/ZL.Gear.ConsoleApp.csproj
dotnet run --project ../ZL.Gear.Demos/ZL.Gear.ConsoleApp/ZL.Gear.ConsoleApp.csproj --no-build -- \
  -s ../ZL.Gear.Demos/ZL.Gear.ConsoleApp/Scenarios/Demo_Sampling_Continuous.json
```

**IndustryKit 闭环：**

```bash
dotnet run --project demos/IndustryKit/ZL.Gear.Samples.Industry.Client -- verify
# 须含 INDUSTRY_KIT_VERIFY_PASS
```

### 2.5 Engine/Drivers 宿主注入（Phase 1 解耦后 · 必读）

`ZL.Gear.Engine` **不再**编译引用 `ZL.Gear.Drivers`。`Build()` **强制要求显式声明宿主**——未声明将抛 `InvalidOperationException`，**没有** `NullDeviceService` 兜底（该类已删除）。

| 宿主类型 | 引用 | 必须调用 |
|----------|------|----------|
| 行业模板 / 纯 DynamicFlow / Demo | 仅 Engine + Core 扩展 | `.AsLogicOnlyDemoHost()`（自动锁 `BuiltInModules.Core`；禁止 `WithDeviceConfig`） |
| ConsoleApp / WinForms / 产线 | Engine + **Drivers** | `.AsInstrumentedHost(deviceService)` 或 `.WithDeviceService(svc)` |

- 设备服务来源：`DriversServiceCollectionExtensions.CreateDeviceService()`，或 DI `AddGearDrivers()`。
- 范例：`demos/IndustryKit/ZL.Gear.Samples.Industry.Client/Program.cs`（LogicOnly）、`../ZL.Gear.Demos/ZL.Gear.ConsoleApp/Program.cs`（Instrumented）。

详见私有仓 `ZL.Gear.Docs` · `144_Engine解耦二次深度审查与落地路线图_2026-09-11.md`。

---

## 3. 代码风格指南

### 3.1 命名约定

- **类/接口**: PascalCase (`DeviceFactory`, `IMeasurementEngine`)
- **方法**: PascalCase (`CreateDevice`, `ExecuteAsync`)
- **属性/字段**: PascalCase (`DeviceName`, `_log`)
- **局部变量**: camelCase (`deviceConfig`, `cancellationToken`)
- **常量**: PascalCase (`MaxBufferSize`)
- **命名空间**: PascalCase (`ZL.Gear.Drivers.Devices`)

### 3.2 导入规范

```csharp
// 标准库
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

// 第三方库
using Newtonsoft.Json;
using NLog;

// 项目内部 - 按层级从外到内排序
using ZL.Gear.Core;
using ZL.Gear.Core.Abstractions;
using ZL.Gear.Drivers.Devices;
```

### 3.3 格式规范

- 使用 4 空格缩进
- 大括号使用 Allman 风格（独立一行）
- 每行不超过 120 字符
- 方法间保留一个空行
- 字段声明与属性之间保留一个空行

### 3.4 注释要求

- **所有公共类和方法必须添加 XML 文档注释**
- 使用中文注释和中文日志（项目规范）
- 日志消息使用中文，变量名使用英文
- 示例：
```csharp
/// <summary>
/// 创建设备实例。
/// </summary>
/// <param name="config">设备配置。</param>
/// <returns>设备实例。</returns>
public IDevice CreateDevice(DeviceConfig config)
{
    _log("[Init] 正在创建设备: " + config.DeviceName);
    // ...
}
```

### 3.5 类型使用

- 优先使用接口类型声明 (`IEnumerable<T>`, `IDisposable`)
- 异步方法使用 `Task`/`Task<T>`，返回 `void` 仅用于事件处理器
- 使用可空引用类型时明确标注 `T?`
- 集合参数优先使用 `IReadOnlyList<T>` 或 `IEnumerable<T>`

### 3.6 错误处理

- 优先使用 try-catch 处理可恢复错误
- 记录异常日志时使用 `_log` 或 `LogKit`
- 异步方法中捕获异常后设置 `TaskCompletionSource`
- 避免吞掉异常，必要时重新抛出
- 资源使用 `using` 语句或 `IAsyncDisposable`

```csharp
try
{
    return await _device.ExecuteAsync(command, args, token);
}
catch (OperationCanceledException)
{
    return ExecutionResult.Failed("操作超时或被取消。");
}
catch (Exception ex)
{
    _log($"执行错误: {ex.Message}");
    return ExecutionResult.Failed($"执行错误: {ex.Message}");
}
```

### 3.7 日志规范

- 使用 `_log` 委托或 `LogKit` 进行日志记录
- 日志级别：INFO 用于关键流程，WARN 用于异常情况，ERROR 用于错误
- 日志消息使用中文，格式：`[模块名] 消息内容`
- 避免在日志中输出敏感信息

```csharp
_log($"[Engine] 测量开始, 总超时: {config.TotalTimeoutMs}ms");
_log($"[Device] 设备连接失败: {ex.Message}");
```

### 3.8 异步编程

- 所有 I/O 操作使用异步方法
- 使用 `ConfigureAwait(false)` 避免不必要的上下文切换
- 使用 `CancellationToken` 取消长时间操作
- 避免在异步方法中阻塞（不使用 `.Result` 或 `.Wait()`）

### 3.9 可空引用类型

```csharp
// 可空参数应显式标注
public void Register(string type, Func<DeviceFactory, DeviceConfig, IDevice> factory)
{
    if (string.IsNullOrWhiteSpace(type) || factory == null) return;
    // ...
}

// 安全的属性访问
if (cfg.ConnectionCfg.Parameters.TryGetValue("Protocol", out var pName))
{
    protocolName = pName?.ToString();
}
```

---

## 4. 项目结构规范

### 4.1 目录结构

```
ZL.Gear.*/
├── Abstractions/      # 接口定义
├── Models/            # 数据模型
├── Devices/           # 设备相关
│   ├── Abstractions/  # 设备接口
│   ├── Implementations/
│   └── Transport/     # 传输层
├── Handlers/          # 命令处理器
├── Services/          # 服务实现
└── Utilities/         # 工具类
```

### 4.2 接口与实现分离

- 接口定义在 `Abstractions` 目录
- 实现类在根目录或子目录
- 接口命名以 `I` 为前缀

### 4.3 扩展方法

- 扩展方法放在 `Extensions` 目录或 `Kit` 类中
- 文件名以 `Extensions.cs` 结尾

---

## 5. 架构原则

### 5.1 依赖方向

- Core 层不依赖任何其他项目
- 上层依赖下层，下层不依赖上层
- 使用依赖注入解耦

### 5.2 单一职责

- 每个类/方法只做一件事
- 避免出现"巨无霸"类（参考 `StepHandlerKit.cs` 的拆分要求）

### 5.3 开闭原则

- 对扩展开放，对修改关闭
- 通过接口和抽象类实现扩展点

---

## 6. 测试要求

### 6.1 测试项目

- 使用 NUnit + Moq 框架
- 测试项目使用 .NET 7.0（与其他项目不同）
- 测试文件命名：`*Tests.cs`

### 6.2 测试规范

- 每个公共方法至少有一个测试
- 测试命名：`方法名_测试场景_预期结果`
- 使用 `Assert` 进行断言
- 避免测试内部实现细节

```csharp
[Test]
public void StepDispatcher_仅Core_不应注册GenericMeasure与AiDecision()
{
    // 见 ZL.Gear.Drivers.Tests/ModuleLoaderRegressionTests.cs
}
```

---

## 7. 常见任务

### 7.1 添加新设备驱动

1. 在 `ZL.Gear.Drivers/Devices` 下创建设备类
2. 实现 `IDevice` 或 `IDeviceDriver` 接口
3. 在 `DeviceFactory` 中注册设备类型
4. 行业步骤优先用 `IGearExtension` + `StepArgsReader`，参考 `demos/IndustryKit/`，勿再向私有 `ZL.Gear.Ext.Seat` 堆业务

### 7.2 添加新测试步骤 / 行业扩展

1. **推荐**：复制 `demos/IndustryKit/ZL.Gear.Extension.Station`，实现 `IGearExtension`，`RegisterHandlerWithAction`
2. 配方用 DynamicFlow JSON（docs/134–137 新方言）；宿主 `WithExtension(...).WithBuiltInModules(...)`
3. 用 `demos/IndustryKit` 客户端 `verify`（7 条门禁）或 `showcase`（产品演示）
4. 早期 `ZL.Gear.Ext.Seat`（Exts 私有仓）仅作 PLC 遗产参考，**不要**作为新行业模板拷贝源

**PR 自检（产线相关，详见 §8.2 · docs/140 §六 · docs/141 §九–§十）：**

- [ ] 限值/RecipeId：`StepArgSource.ArgsOnly` + `TryRequire*`（禁止 `All` / `context.Get` 读必填限值）
- [ ] 写流程：`args.SetShared`（禁止 Handler 内 `Variables.Set`）
- [ ] 读前序：`GetFlowString` / `VariablesOnly`（禁止 `All` 误读 Global）
- [ ] 可选 Args 覆盖：键存在则 fail-closed，禁止 `TryGet` 失败后静默回退（见 ProbeChannel 修正）
- [ ] 判据在 JSON `Assert`（规格判定）；Handler 仅做设备/安全硬中断
- [ ] `Assert` L1 `Check` **单条件**：禁止 `&&` / `||` / 括号；多条件拆成多条 Assert（否则 `AssertCheckParser` 报「含多余字符」→ 步骤 Failed，见 docs/141 G-02 实测）
- [ ] `ParameterSchema` 与 TryRequire 字段人工对齐（**无**自动绑定，docs/121 已否决）
- [ ] verify 含故意 FAIL + 超时场景

### 7.3 修复编译错误

- Core 和 Sensing 项目可能存在编译错误
- 优先修复 Core 层，再修复依赖层
- 参考测试项目中的 MockInterfaces.cs 获取接口定义
- 行业样例构建：`dotnet build demos/IndustryKit/ZL.Gear.Samples.Industry.Client/ZL.Gear.Samples.Industry.Client.csproj`
---

## 8. Agent 验证最佳实践与踩坑实录

> 详述见 [docs/141 §十](./docs/141_ATE_OpenTAP_TestStand对标基准_2026-09-10.md) · Handler 规则 [docs/140 §六](./docs/140_StepArgsReader语法糖总表与作用域踩坑_2026-09-10.md) · 方向禁止 [docs/141 §九](./docs/141_ATE_OpenTAP_TestStand对标基准_2026-09-10.md)

### 8.1 跑 `dotnet` 时（Agent / CI 通用）

| 踩坑 | 正确做法 |
|------|----------|
| 沙箱内 `dotnet build` NuGet 失败或极慢 | 需要网络时申请 **full_network**；或让用户本机跑 `check_release_public.sh` |
| ~10 分钟 `Build FAILED` 且 **0 Error(s)** | MSBuild 子进程超时；缩小为单 csproj build，必要时清理 MSBuild/dotnet 进程 |
| `dotnet run ... \| tail -n 5` 显示 exit 0 但 build 失败 | **管道 exit code 来自 tail**；看 dotnet 退出码或日志关键字 `PASS`/`FAILED` |
| 每次 `dotnet run` 隐式全量编译 | 先 `dotnet build`，再 `--no-build` 跑场景/verify |
| ConsoleApp 测量场景无硬件失败 | `export ZL_GEAR_FORCE_MOCK=true` |
| ConsoleApp FATAL `feature=basic` | Drivers 未授权 | 公开 Engine 无 LicenseGuard；Instrumented 路径在 **ZL.Gear.Drivers** 配置 `ZL_LICENSE_*` |
| IndustryKit 不应再遇授权错误 | 公开轨已去 Engine 门禁 | `verify.sh` **零 bypass**；LogicOnly 直接 PASS |
| 场景覆盖误判 | 以为只有 ConsoleApp 4 场景跑 PASS | 第 2 步 `ScenarioDemoLibraryTests` **in-process** 已覆盖 Core Showcase / Timeout Contract 等（docs/141 §10.5(3) 双轨） |

### 8.2 产线安全（行业 Handler / JSON 配方）

**禁止方向（按错即产线灾难，勿立项）：**

- ParameterSchema **自动运行时绑定**（docs/121 已否决；Schema=文档 + StepArgsReader 手写）
- 产线限值用 `context.Get` / `StepArgSource.All`
- 行业 Handler 用 `Variables.Set`（须 `args.SetShared`）
- verify **只跑 HappyPath**（须含故意 FAIL + 超时）
- Assert L1 `Check` 写 `&&`（须拆成多条单条件 Assert）

**已验证模板：** `demos/IndustryKit/` · 全栈场景库 `../ZL.Gear.Demos/ZL.Gear.ConsoleApp/Scenarios/`（私有 Demos 仓）。

**合规单测（非全仓 bot）：** `IndustryKitHandlerComplianceTests` 扫描模板三 Handler，禁止 `Variables.Set` / `context.Get` / 限值 `All`。

### 8.3 审查 141 号文档时的方法

1. 结论须 `rg`/读实码可复现；冲突 **实码 > docs/137 > docs/121 §十二**。  
2. 「已完成」须实测 PASS，不能只看文件存在（例：Continuous 场景初版 `&&` Assert 实为 FAIL）。  
3. 新 Gap 不得与 docs/130/121 **明确不做** 清单冲突。

---

## 9. 注意事项

- 项目使用 .NET Standard 2.0，需注意 API 兼容性
- 部分项目引用了 `libs/` 目录下的外部 DLL（**禁止提交**；见 `libs/README.md`）
- 某些设备驱动需要硬件支持才能完整测试
- 关注 `NotImplementedException` 和 `TODO` 注释
- 修改公共 API 时需更新 XML 文档注释
- 修改 Handler 参数/作用域/采样场景时，同步 [docs/140](./docs/140_StepArgsReader语法糖总表与作用域踩坑_2026-09-10.md) 与 [docs/141](./docs/141_ATE_OpenTAP_TestStand对标基准_2026-09-10.md)
- Sampling JSON 勿用废弃字段 `Quantity`（须 `SampleCount`/`SampleIntervalMs`）；`ScenarioDemoLibraryTests` 会门禁
