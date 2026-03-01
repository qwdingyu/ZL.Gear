这是一个为您定制的产品白皮书草案。我们将您的框架命名为 "Gear.NET"（寓意：工业齿轮，精密传动），定位为 .NET 工业自动化微编排引擎。
产品白皮书：Gear.NET 工业自动化引擎

### 1. 产品简介

Gear.NET 是一款专为 .NET 开发者打造的代码优先（Code-First）工业自动化微编排框架。
它旨在解决工业现场（EOL 电检、MES 采集终端、非标自动化控制）中，**“手写代码难以维护”与“组态软件不够灵活”**之间的矛盾。它将复杂的工业逻辑（如工位状态管理、设备并发控制、网络重试、条件分支）封装为流畅的 C# DSL（领域特定语言），让开发者像写剧本一样编写工业控制逻辑。

### 2. 核心亮点与优势

#### 2.1 产品亮点

MicroWorkflow DSL：独创的流式编排语法。通过 .Then(), .WaitUntil(), .Retry(), .Parallel()，将复杂的异步业务逻辑压缩为线性、可读的代码。
混合驱动模式：
开发态：支持 Attribute 元数据定义，代码即文档，保证参数类型安全。
运行态：支持 JSON 动态配置，允许现场工程师在不重新编译的情况下调整参数阈值、文案和默认值。
工业级健壮性：
内置设备资源池与并发锁，防止硬件资源冲突。
支持 急停（Emergency Stop） 与 自动复位 机制。
完善的上下文（Context）变量作用域管理（Global/Shared/Local）。

#### 2.2 相比传统方案的优势

维度 传统手写 (C# Task/Thread) 商业软件 (NI TestStand) Gear.NET
开发效率 低，需处理线程死锁、异常捕获 高，拖拉拽 极高，智能提示 + 极简语法
运行开销 低 高，需安装庞大 Runtime 极低，原生 DLL，适合工控机
逻辑表达 灵活但乱 僵硬，循环/分支配置繁琐 灵活且清晰，图灵完备
部署成本 0 元 数万元/节点 0 元 (开源/自研)
适用场景 简单小工具 实验室、标准化测试 产线、MES 终端、非标自动化

### 3. 系统架构 (The Architecture)

采用 洋葱架构 (Onion Architecture) 思想，层级依赖单向流动。

```Mermaid
graph TD
    subgraph "Level 4: Business (业务扩展)"
    Extension[ZL.Gear.Extension.*] -->|实现业务Handler| Core
    end

    subgraph "Level 3: Infrastructure (驱动与实现)"
    Drivers[ZL.Gear.Drivers] -->|实现IDevice/ActionProvider| Core
    Engine[ZL.Gear.Engine] -->|调度与管理| Core
    end

    subgraph "Level 2: Core (核心契约)"
    Core[ZL.Gear.Core]
    Context[StepContext]
    Workflow[MicroWorkflow]
    Interfaces[IDevice/IStepHandler]
    end
```

Core (核心层)：定义了“世界观”。包括 MicroWorkflow 逻辑流、StepContext 上下文、IDevice 接口。无任何业务逻辑。
Drivers (能力层)：定义了“手和脚”。PLC 驱动、CAN 卡驱动、仪器仪表驱动。包含原子动作（ActionProvider）。
Engine (调度层)：定义了“大脑”。SequenceExecutor（执行序列）、StationManager（工位状态机）、DeviceManager（资源管家）。
Extensions (业务层)：定义了“任务”。如座椅测试、电池包上线逻辑。包含具体的 StepHandler。

### 4. 核心类功能与最佳实践

#### 4.1 MicroWorkflow (微工作流构建器)

功能：通过链式调用编排原子动作。

```c#
Then("描述", "ActionName")：执行动作。
WaitUntil(...)：阻塞等待信号（如气缸到位）。
Retry(...)：失败重试（如扫码）。
Parallel(...)：并行执行（如双通道测试）。
Switch/While：逻辑分支与循环。
```

最佳实践：不要在 MicroWorkflow 里写巨型 Lambda 表达式，尽量调用注册好的 ActionName，保持代码整洁。

#### 4.2 StepContext (执行上下文)

功能：贯穿整个测试流程的数据容器。
特性：
三级变量查找：GetValue<T> 自动在 Local -> Shared -> Global 中查找。
类型安全：支持 GetSeatConfig() 等扩展方法。
隔离性：通过 CreateChildContext 保证并行任务的数据隔离。

#### 4.3 StationManager (工位管理器)

功能：管理工位的生命周期（Idle -> Working -> Error）。
职责：监听触发信号（如 PLC D100=1），加载配置，调用 Executor，处理急停复位。

#### 4.4 MetadataDiscoveryService (元数据服务)

功能：扫描代码中的 [WorkflowAction] 特性，生成 JSON 供前端 UI 使用；加载外部 JSON 补丁覆盖默认值。

### 5. 核心场景实战：PackInboundHandler 与 StationManager 的结合

这是一个非常典型的问题：业务逻辑（Handler）如何挂载到状态机（StationManager）上运行？

#### 5.1 概念模型

StationManager 是容器（Container）。它不知道具体业务是“电池包上线”还是“座椅加热”，它只知道“触发 -> 跑个流程 -> 结束”。
PackInboundHandler 是内容（Content）。它是一个封装好的 IStepHandler。

#### 5.2 结合步骤

第一步：定义业务入口 (Root Step Configuration)
我们需要一个配置（可以是文件，也可以是代码硬编码），告诉 StationManager：“一旦触发，就执行 PackInbound 这个命令”。
Config.json (示例):

```JSON
{
  "StationName": "Battery_Inbound_OP10",
  "TriggerSignal": "PLC.D100",
  "RootSequence": [
    {
      "StepName": "电池包上线主流程",
      "Command": "PackInbound",  // <--- 对应 PackInboundHandler
      "StopByFail": true,
      "Parameters": { "TimeOut": 60000 }
    }
  ]
}
```

第二步：StationManager 的加载逻辑
在 StationManager 的 Working 状态处理逻辑中，加载这个配置并交给 SequenceExecutor。

````C#
// ZL.Gear.Engine.Management.StationManager
private async Task AutoRunLoop(CancellationToken token)
{
    while (!token.IsCancellationRequested)
    {
        // 1. IDLE 状态：等待 PLC 触发
        if (_currentState == StationState.Idle)
        {
            if (await _triggerCondition(token)) // 例如 PLC D100 == 1
            {
                await SetStateAsync(StationState.Working);
            }
        }

        // 2. WORKING 状态：执行业务
        if (_currentState == StationState.Working)
        {
            try
            {
                _log("工位触发，加载业务流程...");

                // A. 准备上下文 (全局变量)
                var globalContext = new Dictionary<string, object>
                {
                    { "StationId", "OP10" },
                    { "StartTime", DateTime.Now }
                };

                // B. 加载步骤配置 (这里加载的就是上面 Config.json 里的 RootSequence)
                // 这里 PackInboundHandler 只是 Sequence 中的第一个（也是唯一一个）主步骤
                List<StepConfig> steps = _configService.LoadStationSteps();

                // C. 交给通用执行器跑
                // Executor 会找到 "PackInbound" 对应的 Handler 并执行
                var result = await _executor.ExecuteAsync(steps, globalContext, token);

                // D. 处理结果 (比如写回 PLC 放行信号)
                if (result.OverallSuccess)
                    await _deviceService.WritePlcAsync("D200", 1); // OK
                else
                    await _deviceService.WritePlcAsync("D200", 2); // NG

                // E. 等待产品离开，复位状态
                await WaitProductLeaveAsync();
                await SetStateAsync(StationState.Idle);
            }
            catch (Exception ex)
            {
                _log($"严重故障: {ex.Message}");
                await SetStateAsync(StationState.Error);
            }
        }
    }
}
第三步：PackInboundHandler 的实现 (业务闭环)
```C#
// ZL.Gear.Extension.Battery.Handlers.PackInboundHandler
public class PackInboundHandler : IStepHandler
{
    public async Task<ExecutionResultBase> ExecuteAsync(StepConfig step, StepContext context)
    {
        // 这里的代码完全不用关心什么时候被触发，只关心业务本身
        await using (var flow = MicroWorkflow.Start(step, context))
        {
            flow.Finally("复位阻挡器", "ResetStopper");

            flow.WaitUntil("等待到位", async (s, c) => c.GetDevice<IPlc>("Main").Read("X0"));
            flow.Then("扫码", "ScanBarcode");
            flow.ThenMeasure("MES校验", "MesCheck");
            // ... 更多复杂逻辑 ...

            return await flow.GetResultAsync();
        }
    }
}
````

#### 5.3 结合的关键点总结

解耦：StationManager 不直接引用 PackInboundHandler 类。它通过 Command 字符串 ("PackInbound") 进行关联。
桥梁：SequenceExecutor 和 StepDispatcher 是桥梁。StationManager 只要把配置扔给 SequenceExecutor，执行器就会去 StepDispatcher 里查表，找到 Handler 执行。
配置驱动：如果明天这个工位改成了“电机上线”，你只需要修改配置文件中的 Command 为 "MotorInbound"，并加载对应的 Extension DLL，无需修改 StationManager 的核心代码。
这正是 “瑞士军刀” 的精髓：刀架（StationManager）是通用的，刀片（Handler）是可以随意更换的。
