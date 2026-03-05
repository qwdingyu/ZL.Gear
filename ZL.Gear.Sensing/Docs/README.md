# ZL.Gear.Sensing 使用指南

## 目录

1. [概述](#概述)
2. [核心概念](#核心概念)
3. [快速开始](#快速开始)
4. [采样配置 (SamplingConfig)](#采样配置-samplingconfig)
5. [触发器 (Triggers)](#触发器-triggers)
6. [策略 (Strategies)](#策略-strategies)
7. [响应式采样器 (Reactive Samplers)](#响应式采样器-reactive-samplers)
8. [扩展方法 (SamplingExtensions)](#扩展方法-samplingextensions)
9. [执行引擎 (Measurement Engine)](#执行引擎-measurement-engine)
10. [完整示例](#完整示例)
11. [最佳实践](#最佳实践)

---

## 概述

ZL.Gear.Sensing 是 ZL.Gear 框架中的检测与采样模块，提供基于响应式流（Rx.NET）的传感器数据采集、过滤和处理的完整解决方案。

### 主要特性

- **响应式数据流**：基于 `IObservable<T>` 的响应式编程模型
- **灵活的配置**：通过 `SamplingConfigBuilder` fluent API 构建采样配置
- **多种触发模式**：支持立即触发、条件触发、连续触发等多种模式
- **智能采样策略**：固定数量、持续时间、快速通过等策略
- **Rx.NET 集成**：提供 Throttling、Debounce、Sample 等高级采样器

---

## 核心概念

### 1. 采样配置 (SamplingConfig)

定义如何采集数据的配置对象，包含：

- 采样策略 (Strategy)
- 触发器 (Trigger)
- 采样间隔
- 超时设置
- 回调函数

### 2. 触发器 (IExecutionTrigger)

控制何时开始和停止采样：

- `ImmediateTrigger<T>` - 立即开始
- `ConditionalTrigger<T>` - 条件触发
- `ConsecutiveTrigger<T>` - 连续触发

### 3. 策略 (ISamplingStrategy)

控制采样何时完成：

- `FixedCountStrategy<T>` - 采集固定数量后完成
- `DurationStrategy<T>` - 持续指定时间后完成
- `QuickPassStrategy<T>` - 快速通过策略

### 4. 响应式采样器 (ISensorSampler)

基于 Rx.NET 的实时数据处理器：

- `ThrottlingSampler` - 节流采样
- `SamplingSampler` - 固定间隔采样
- `DebouncingSampler` - 防抖采样
- `DistinctSampler` - 去重采样

---

## 快速开始

### 最简单的采样示例

```csharp
using ZL.Gear.Sensing;
using ZL.Gear.Sensing.Sampling;

// 1. 创建配置
var config = new SamplingConfig<double>();

// 2. 执行采样（需要实现数据源）
var engine = new DefaultMeasurementEngine<double>();
var result = await engine.ExecuteAsync(config, GetDataSource, CancellationToken.None);

// 3. 处理结果
if (result.IsSuccess)
{
    Console.WriteLine($"采集成功，样本数: {result.Samples.Count}");
    Console.WriteLine($"最终值: {result.FinalValue}");
}

// 数据源委托
async IAsyncEnumerable<double> GetDataSource([EnumeratorCancellation] CancellationToken ct)
{
    for (int i = 0; i < 10; i++)
    {
        yield return Math.random() * 100;
        await Task.Delay(50, ct);
    }
}
```

---

## 采样配置 (SamplingConfig)

### 使用默认配置

```csharp
var config = new SamplingConfig<double>();
// 默认值:
// - 采样间隔: 100ms
// - 总超时: 15000ms
// - 策略: FixedCountStrategy(10)
// - 触发器: ImmediateTrigger
```

### 使用 Fluent API 构建配置

```csharp
var config = new SamplingConfigBuilder<double>()
    .WithStrategy(new FixedCountStrategy<double>(20))  // 采集20个样本
    .WithTrigger(new ConditionalTrigger<double>(sample => sample > 50))  // 条件触发
    .WithInterval(50)                                     // 50ms采样间隔
    .WithTimeout(10000)                                  // 10秒超时
    .WithActiveDuration(5000)                           // 5秒活跃时长
    .WithSpecCheck(value => value >= 0 && value <= 100) // 规格检查
    .WithPerSampleValidator(value => !double.IsNaN(value))  // 单样本验证
    .OnTestStart(sample => Console.WriteLine($"开始: {sample}"))
    .OnTestFinish(result => Console.WriteLine($"完成: {result.Status}"))
    .OnSampleCollect(sample => Console.WriteLine($"采样: {sample}"))
    .WithLogger(message => Console.WriteLine($"[LOG] {message}"))
    .Build();
```

---

## 触发器 (Triggers)

### ImmediateTrigger - 立即触发

```csharp
// 立即开始采样
var trigger = new ImmediateTrigger<double>();
config.Trigger = trigger;
```

### ConditionalTrigger - 条件触发

```csharp
// 当样本满足条件时开始和停止
var trigger = new ConditionalTrigger<double>(
    shouldStart: sample => sample > 10,   // 样本大于10时开始
    shouldStop: sample => sample < 5      // 样本小于5时停止
);

// 或者使用带状态检查的版本
var trigger = new ConditionalTrigger<double>();
trigger.ShouldStart = (sample, isActive) => !isActive && sample > 10;
trigger.ShouldStop = (sample, isActive) => isActive && sample < 5;
```

### ConsecutiveTrigger - 连续触发

```csharp
// 连续N个样本满足条件时开始，连续M个样本不满足条件时停止
var trigger = new ConsecutiveTrigger<double>(
    startCondition: sample => sample > 50,  // 开始条件
    stopCondition: sample => sample < 30,  // 停止条件
    startConsecutiveCount: 3,               // 需要连续3个样本满足开始条件
    stopConsecutiveCount: 5                 // 需要连续5个样本不满足停止条件
);
```

---

## 策略 (Strategies)

### FixedCountStrategy - 固定数量策略

```csharp
// 采集指定数量的样本后完成
var strategy = new FixedCountStrategy<double>(10);  // 采集10个样本
var strategy = new FixedCountStrategy<double>(100); // 采集100个样本
```

### DurationStrategy - 持续时间策略

```csharp
// 持续指定时间后完成
var strategy = new DurationStrategy<double>(TimeSpan.FromSeconds(5));  // 采集5秒
var strategy = new DurationStrategy<double>(TimeSpan.FromMinutes(1));  // 采集1分钟
```

### QuickPassStrategy - 快速通过策略

```csharp
// 前N个样本满足条件则通过，否则继续采集
var strategy = new QuickPassStrategy<double>(
    checkCount: 5,                          // 检查前5个样本
    passCondition: samples => samples.All(s => s > 60)  // 所有值都大于60则通过
);
```

### 自定义策略

```csharp
public class CustomStrategy : SamplingStrategyBase<double>
{
    public override string StrategyName => "自定义策略";

    public override (bool isDone, bool shouldCollect) ProcessSample(
        double sample,
        IReadOnlyList<double> collectedSamples)
    {
        // 自定义逻辑：采集20个样本或平均值大于50时完成
        if (collectedSamples.Count >= 20)
            return (true, false);

        var avg = collectedSamples.Average();
        if (avg > 50)
            return (true, false);

        return (false, true);
    }
}
```

---

## 响应式采样器 (Reactive Samplers)

### 概述

响应式采样器是 `ISensorSampler` 的实现，用于实时处理数据流。它们基于 Rx.NET，提供高级的采样、过滤和转换功能。

### 接口定义

```csharp
public interface ISensorSampler : IDisposable
{
    string Name { get; }
    IObservable<Measurement> DataStream { get; }
    void Start();
    void Stop();
}
```

### ThrottlingSampler - 节流采样器

**功能**：只发射指定时间间隔内的最后一个值，适用于快速变化信号的降频处理。

```csharp
using ZL.Gear.Sensing.Samplers;

// 创建节流采样器，100ms内只发射最后一个值
var sampler = new ThrottlingSampler("MyThrottler", TimeSpan.FromMilliseconds(100));

// 订阅输出数据流
sampler.DataStream.Subscribe(measurement =>
{
    Console.WriteLine($"收到数据: {measurement.Value}");
});

// 启动采样器
sampler.Start();

// 输入数据（模拟传感器数据）
sampler.OnNext(new Measurement { Value = 10, Timestamp = DateTime.Now });
sampler.OnNext(new Measurement { Value = 20, Timestamp = DateTime.Now });
sampler.OnNext(new Measurement { Value = 30, Timestamp = DateTime.Now });

// 停止采样器
sampler.Stop();

// 释放资源
sampler.Dispose();
```

**使用场景**：

- 高频传感器数据降频
- UI 事件节流
- 网络请求防抖

### SamplingSampler - 固定间隔采样器

**功能**：每隔指定时间发射最新的值，适用于定期轮询场景。

```csharp
// 创建固定间隔采样器，每200ms发射一次最新值
var sampler = new SamplingSampler("MySampler", TimeSpan.FromMilliseconds(200));

sampler.DataStream.Subscribe(measurement =>
{
    Console.WriteLine($"定时采样: {measurement.Value}");
});

sampler.Start();

// 持续输入数据
for (int i = 0; i < 100; i++)
{
    sampler.OnNext(new Measurement { Value = i, Timestamp = DateTime.Now });
    await Task.Delay(50);  // 每50ms输入一个数据
}

sampler.Stop();
sampler.Dispose();
```

**使用场景**：

- 定期轮询传感器
- 定时刷新数据
- 固定频率数据采集

### DebouncingSampler - 防抖采样器

**功能**：只有当值停止变化指定时间后才发射，适用于等待输入完成的场景。

```csharp
// 创建防抖采样器，500ms内没有新数据才发射
var sampler = new DebouncingSampler("MyDebouncer", TimeSpan.FromMilliseconds(500));

sampler.DataStream.Subscribe(measurement =>
{
    Console.WriteLine($"防抖输出: {measurement.Value}");
});

sampler.Start();

// 模拟用户输入（快速连续输入）
sampler.OnNext(new Measurement { Value = 1, Timestamp = DateTime.Now });
await Task.Delay(100);
sampler.OnNext(new Measurement { Value = 2, Timestamp = DateTime.Now });
await Task.Delay(100);
sampler.OnNext(new Measurement { Value = 3, Timestamp = DateTime.Now });
await Task.Delay(100);
sampler.OnNext(new Measurement { Value = 4, Timestamp = DateTime.Now });

// 等待600ms没有新数据，输出最后一个值
await Task.Delay(600);

sampler.Stop();
sampler.Dispose();
```

**使用场景**：

- 用户输入防抖
- 搜索框自动完成
- 按钮点击防抖

### DistinctSampler - 去重采样器

**功能**：只有当值发生变化时才发射，减少重复数据。

```csharp
// 创建去重采样器
var sampler = new DistinctSampler("MyDistinct");

sampler.DataStream.Subscribe(measurement =>
{
    Console.WriteLine($"去重输出: {measurement.Value}");
});

sampler.Start();

// 输入数据（包含重复值）
sampler.OnNext(new Measurement { Value = 10, Timestamp = DateTime.Now });
sampler.OnNext(new Measurement { Value = 10, Timestamp = DateTime.Now });  // 重复，不输出
sampler.OnNext(new Measurement { Value = 20, Timestamp = DateTime.Now });
sampler.OnNext(new Measurement { Value = 20, Timestamp = DateTime.Now });  // 重复，不输出
sampler.OnNext(new Measurement { Value = 20, Timestamp = DateTime.Now });  // 重复，不输出
sampler.OnNext(new Measurement { Value = 30, Timestamp = DateTime.Now });

sampler.Stop();
sampler.Dispose();
```

**输出**：

```
去重输出: 10
去重输出: 20
去重输出: 30
```

---

## 扩展方法 (SamplingExtensions)

### 概述

为 `IObservable<T>` 提供的高级采样扩展方法，可以链式调用。

### ThrottleSample - 节流采样

```csharp
IObservable<double> source = ...;

// 节流：100ms内只发射最后一个值
var throttled = source.ThrottleSample(TimeSpan.FromMilliseconds(100));

throttled.Subscribe(value => Console.WriteLine(value));
```

### TimedSample - 定时采样

```csharp
// 每200ms发射一次最新值
var sampled = source.TimedSample(TimeSpan.FromMilliseconds(200));

sampled.Subscribe(value => Console.WriteLine(value));
```

### DebounceSample - 防抖采样

```csharp
// 500ms内没有新值才发射
var debounced = source.DebounceSample(TimeSpan.FromMilliseconds(500));

debounced.Subscribe(value => Console.WriteLine(value));
```

### WindowSample - 滑动窗口采样

```csharp
// 按时间窗口分组，每1秒一个窗口
var windowed = source.WindowSample(TimeSpan.FromSeconds(1));

windowed.Subscribe(window =>
{
    Console.WriteLine($"窗口数据: {window.Buffer.Count} 条");
    foreach (var item in window.Buffer)
    {
        Console.WriteLine($"  - {item}");
    }
});
```

### BufferSample - 缓冲区采样

```csharp
// 按数量分组，每10个一组
var buffered = source.BufferSample(10);

buffered.Subscribe(buffer =>
{
    Console.WriteLine($"缓冲区: {buffer.Count} 条");
    var avg = buffer.Average();
    Console.WriteLine($"平均值: {avg}");
});
```

### WhereSample - 条件过滤

```csharp
// 只发射满足条件的值
var filtered = source.WhereSample(value => value > 50);

filtered.Subscribe(value => Console.WriteLine(value));
```

### SelectSample - 值转换

```csharp
// 转换每个值
var transformed = source.SelectSample(value => value * 2);

transformed.Subscribe(value => Console.WriteLine(value));
```

### 链式调用示例

```csharp
// 完整的处理管道：防抖 -> 过滤 -> 转换 -> 节流
var result = source
    .DebounceSample(TimeSpan.FromMilliseconds(300))     // 防抖
    .WhereSample(value => value > 0)                    // 过滤正值
    .SelectSample(value => value * 1.1)                 // 放大10%
    .ThrottleSample(TimeSpan.FromMilliseconds(100));   // 节流

result.Subscribe(value => Console.WriteLine($"最终结果: {value}"));
```

---

## 执行引擎 (Measurement Engine)

### DefaultMeasurementEngine

默认的测量执行引擎，负责执行采样流程。

```csharp
using ZL.Gear.Sensing.Orchestration;

// 创建引擎
var engine = new DefaultMeasurementEngine<double>();

// 可选：注入日志
var logger = NLog.LogManager.GetCurrentClassLogger();
var engineWithLogger = new DefaultMeasurementEngine<double>(logger);

// 执行采样
var result = await engine.ExecuteAsync(
    config,              // 采样配置
    dataSource,          // 数据源
    cancellationToken    // 取消令牌
);

// 处理结果
if (result.Status == ExeStatus.Completed && result.SpecPassed)
{
    Console.WriteLine($"成功: {result.FinalValue}");
}
else
{
    Console.WriteLine($"失败: {result.Message}");
}
```

### 数据源类型

#### IAsyncEnumerable<T>

```csharp
async IAsyncEnumerable<double> GetDataSource([EnumeratorCancellation] CancellationToken ct)
{
    var random = new Random();
    while (!ct.IsCancellationRequested)
    {
        yield return random.NextDouble() * 100;
        await Task.Delay(50, ct);
    }
}
```

#### Func<IAsyncEnumerable<T>>

```csharp
var dataSource = () => GetDataSource();
```

---

## 完整示例

### 示例1：温度传感器采集

```csharp
using ZL.Gear.Sensing;
using ZL.Gear.Sensing.Sampling;
using ZL.Gear.Sensing.Orchestration;

// 配置采样
var config = new SamplingConfigBuilder<double>()
    .WithStrategy(new FixedCountStrategy<double>(50))  // 采集50个样本
    .WithTrigger(new ConditionalTrigger<double>(
        shouldStart: sample => sample > 20,   // 温度大于20°C开始
        shouldStop: sample => sample < 15    // 温度低于15°C停止
    ))
    .WithInterval(100)                        // 100ms采样间隔
    .WithTimeout(30000)                       // 30秒超时
    .WithSpecCheck(value => value >= 0 && value <= 100)  // 温度范围检查
    .WithLogger(message => Console.WriteLine(message))
    .Build();

// 验证配置
config.ValidateAndLogConfiguration("温度采集");

// 模拟温度传感器数据源
async IAsyncEnumerable<double> TemperatureSensor([EnumeratorCancellation] CancellationToken ct)
{
    var random = new Random();
    double temperature = 18;

    for (int i = 0; i < 100; i++)
    {
        // 模拟温度缓慢上升
        temperature += random.NextDouble() * 2 - 0.5;
        yield return temperature;
        await Task.Delay(100, ct);
    }
}

// 执行采集
var engine = new DefaultMeasurementEngine<double>();
var result = await engine.ExecuteAsync(config, TemperatureSensor, CancellationToken.None);

// 输出结果
Console.WriteLine($"状态: {result.Status}");
Console.WriteLine($"样本数: {result.Samples.Count}");
Console.WriteLine($"最终温度: {result.FinalValue:F2}°C");
Console.WriteLine($"规格通过: {result.SpecPassed}");
```

### 示例2：使用响应式采样器处理高频数据

```csharp
using ZL.Gear.Sensing;
using ZL.Gear.Sensing.Samplers;
using System.Reactive.Linq;

// 创建节流采样器
var throttler = new ThrottlingSampler("HighFreqSampler", TimeSpan.FromMilliseconds(100));

// 订阅处理后的数据
var processed = throttler.DataStream
    .Where(m => m.Value > 0)
    .Select(m => m.Value * 1.1)
    .Buffer(10)
    .Select(buffer => buffer.Average());

processed.Subscribe(avg =>
{
    Console.WriteLine($"平均处理: {avg:F2}");
});

throttler.Start();

// 模拟高频传感器输入（1000Hz）
for (int i = 0; i < 1000; i++)
{
    throttler.OnNext(new Measurement
    {
        Value = Math.Sin(i * 0.1) * 100,
        Timestamp = DateTime.Now
    });
    await Task.Delay(1);  // 1ms 间隔
}

throttler.Stop();
throttler.Dispose();
```

---

## 最佳实践

### 1. 资源配置

```csharp
// 始终在 using 块中使用采样器，确保资源释放
using (var sampler = new ThrottlingSampler("name", TimeSpan.FromMilliseconds(100)))
{
    sampler.DataStream.Subscribe(handler);
    sampler.Start();
    // ...
}  // 自动调用 Dispose()
```

### 2. 取消令牌

```csharp
using var cts = new CancellationTokenSource();

// 5秒后自动取消
cts.CancelAfter(TimeSpan.FromSeconds(5));

try
{
    var result = await engine.ExecuteAsync(config, dataSource, cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("采样已取消");
}
```

### 3. 错误处理

```csharp
config.OnTestFinished = result =>
{
    if (result.Status == ExeStatus.Completed)
    {
        if (result.SpecPassed)
            Console.WriteLine("测试通过");
        else
            Console.WriteLine($"规格不通过: {result.FinalValue}");
    }
    else
    {
        Console.WriteLine($"错误: {result.Message}");
    }
};
```

### 4. 日志记录

```csharp
config.WithLogger(message =>
{
    // 可以集成到项目的日志系统
    Log.Information("[Sensing] {Message}", message);
});
```

### 5. 性能考虑

- 对于高频数据，优先使用 `ThrottlingSampler` 进行降频
- 避免在 `OnSampleCollected` 回调中执行耗时操作
- 使用 `BufferSample` 批量处理数据而不是逐条处理

---

## API 参考

### 核心类

| 类名                          | 说明              |
| ----------------------------- | ----------------- |
| `SamplingConfig<T>`           | 采样配置类        |
| `SamplingConfigBuilder<T>`    | Fluent 配置构建器 |
| `DefaultMeasurementEngine<T>` | 默认执行引擎      |
| `ThrottlingSampler`           | 节流采样器        |
| `SamplingSampler`             | 定时采样器        |
| `DebouncingSampler`           | 防抖采样器        |
| `DistinctSampler`             | 去重采样器        |

### 接口

| 接口名                  | 说明       |
| ----------------------- | ---------- |
| `ISensorSampler`        | 采样器接口 |
| `IExecutionTrigger<T>`  | 触发器接口 |
| `ISamplingStrategy<T>`  | 策略接口   |
| `IMeasurementEngine<T>` | 引擎接口   |

### 扩展方法

| 方法名                     | 说明       |
| -------------------------- | ---------- |
| `ThrottleSample<T>`        | 节流采样   |
| `TimedSample<T>`           | 定时采样   |
| `DebounceSample<T>`        | 防抖采样   |
| `WindowSample<T>`          | 滑动窗口   |
| `BufferSample<T>`          | 缓冲区采样 |
| `WhereSample<T>`           | 条件过滤   |
| `SelectSample<T, TResult>` | 值转换     |
