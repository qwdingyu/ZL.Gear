# AGENTS.md - Gear.NET 开发规范

## 1. 项目概述

Gear.NET 是 .NET 工业自动化微编排框架，采用洋葱架构（Onion Architecture），包含以下核心模块：
- **ZL.Gear.Core**: 核心契约与抽象层
- **ZL.Gear.Drivers**: 设备驱动层
- **ZL.Gear.Engine**: 执行引擎层
- **ZL.Gear.Sensing**: 采样与测量层
- **ZL.Gear.Extension.***: 业务扩展层

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
dotnet build ZL.Gear.Core/ZL.Gear.Core.csproj
dotnet build ZL.Gear.Drivers/ZL.Gear.Drivers.csproj
dotnet build ZL.Gear.Engine/ZL.Gear.Engine.csproj
dotnet build ZL.Gear.Sensing/ZL.Gear.Sensing.csproj
dotnet build ZL.Gear.Extension.Seat/ZL.Gear.Extension.Seat.csproj
```

### 2.3 运行测试

```bash
# 运行所有测试
dotnet test ZL.Gear.sln

# 运行单个测试项目
dotnet test ZL.Gear.Drivers.Tests/ZL.Gear.Drivers.Tests.csproj

# 运行单个测试类
dotnet test ZL.Gear.Drivers.Tests/ZL.Gear.Drivers.Tests.csproj --filter "FullyQualifiedName~FrameSplitterTests"

# 运行单个测试方法
dotnet test ZL.Gear.Drivers.Tests/ZL.Gear.Drivers.Tests.csproj --filter "FullyQualifiedName~FrameSplitterTests.FixedLengthSplitter_正常分帧测试"
```

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
public void FixedLengthSplitter_正常分帧测试()
{
    var splitter = CreateFixedLengthSplitter(8);
    byte[] data = Encoding.ASCII.GetBytes("12345678ABCDEFGH");
    splitter.Append(data, 0, data.Length);
    
    var frames = splitter.ExtractFrames();
    Assert.AreEqual(2, frames.Count);
}
```

---

## 7. 常见任务

### 7.1 添加新设备驱动

1. 在 `ZL.Gear.Drivers/Devices` 下创建设备类
2. 实现 `IDevice` 或 `IDeviceDriver` 接口
3. 在 `DeviceFactory` 中注册设备类型
4. 如有必要，在 `ZL.Gear.Extension.Seat` 中添加对应的 StepHandler

### 7.2 添加新测试步骤

1. 在对应 Extension 项目中创建 `*Handler` 类
2. 实现 `IStepHandler` 接口
3. 在扩展入口类（如 `SeatExtension`）中注册处理器
4. 在 JSON 配置文件中添加步骤定义

### 7.3 修复编译错误

- Core 和 Sensing 项目可能存在编译错误
- 优先修复 Core 层，再修复依赖层
- 参考测试项目中的 MockInterfaces.cs 获取接口定义

---

## 8. 注意事项

- 项目使用 .NET Standard 2.0，需注意 API 兼容性
- 部分项目引用了 `libs/` 目录下的外部 DLL
- 某些设备驱动需要硬件支持才能完整测试
- 关注 `NotImplementedException` 和 `TODO` 注释
- 修改公共 API 时需更新 XML 文档注释
