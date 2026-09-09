
*************************************************************************************************
1、StepCatalog.json中的Target和 AdditionalTargets 统一使用SeatProfile.json中的逻辑名；
*************************************************************************************************


你好这是一个测试框架的关键实现类库，具体包括如下功能。
0、这是一个使用.net framework4.8开发的类库；我需要将其改造成.NET Standard 2.0 兼容net framework 及 .net core8.0 的工具类，可以使用DependencyInjection；
1、设备通讯、协议、分隔符处理；
2、设备包括串口仪器、PLC、CAN、LIN等；
3、设备工厂模式；
4、执行器；
5、众多的测试模式；
6、评估模式；

请你作为一个专业的设备通讯领域的专家、检测领域专家、架构师，效率专家，编程顾问，认真的分析目前的项目结构及存在的问题。
给出建设性的意见和建议，请记住一下几点：
1、请使用中文进行交流，包括代码的中的注释和日志也需要使用中文；
2、不要将此代码全部推翻，请在此代码的基础上进行局部优化； 
3、需要通读所有的代码，将所有的类和功能放在合适的类库中；此外，重点是针对之前电检程序特有的，个性化的内容识别出来，为通用性做好架构迁移；
4、目前这个项目是从电检项目演化而来，是希望能够适配除了电检之外更多的工控领域。
5、需要的是小而美的瑞士军刀，请不要过度设计，过度开发，过度工程化；
6、目前支持api直接调用（入口为ZL.Gear.Engine.Runner.SequenceExecutor中的ExecuteAsync方法），希望能够简化调用方式，单独一个类库及支持api调用又支持webapi方式；无论哪种方式都需要向调用端反馈进度和日志信息；



我觉得目前的重构还太肤浅了，没有深入到代码的每个细节。
这样把请你根据我给出的步骤进行代码梳理：
1、ZL.Gear.Core 是一个接口定义、实体类，公共代码处理，但是其中的事件处理机制有待商榷，虽然功能可以实现，但是架构并不是最优的。
尤其是ZL.Gear.Core/Events/GlobalEvents.cs 及相关的实体类；请你以事件为主线逐个分析实体类的合理性，架构的合理性；
2、ZL.Gear.Engine 是一个执行器引擎，根据步骤执行测试，评估，等任务。
3、ZL.Gear.Drivers 是一个设备驱动引擎，这个类是需要改造的重点（包括了各种驱动、消息分隔、协议、解析、还有对应command和action），职责较为混乱，需要站在一个较高的角度，从复用、扩展性、兼容性等多个方面进行合理的规划和拆分  。
4、ZL.Gear.Sensing 是一个采样器，是在设备驱动引擎之上完成采样任务，这个类强相关的是检测、电检类型的业务，里面的类和规划也需要做调整和优化。
5、ZL.Gear.Extension.Seat 是座椅项目中独特的测量微流程。
请认真分析每个类库的合理性，架构的合理性，给出专业的意见和建议，通过评审后，再改动代码。相关的评审文档请输出到根目录下的docs目录中。


@architecture_review.md 请在这个文件的基础上再细致的展开这个需要改造的方案。现有的这些太粗了，无法满足改造，优化，评审需求。 
请务必认真仔细，不要放过任何细节，不要有遗漏，要有全局思想，站在架构师，调用这个类库的使用者的角度来思考问题。 



请按照计划逐个进行改造，如果有不明确或者拿不准的地方，请与我对齐。改造过程中始终秉承 不过度设计，不过度开发，坚持现有代码为参考，保持简洁，高效；保持架构稳定可靠，有扩展性和兼容性；确保是小而美的瑞士军刀。


1、ZL.Gear.Core/ProjectLibraryManager.cs 这个包含了太多的内容，需要进行拆分；在拆分之前做一个非常重要的决定：
目前的电检都是采用配置文件来进行步骤流程配置的，是否需要兼容在数据库中定义的模式，请从电检、工控开发人员习惯，使用便利性方面进行分析，给出专业的建议。
2、ZL.Gear.Core/SelfConstants.cs在目前的架构里没有使用，是否有存在的必要。 
3、ZL.Gear.Core/Parser/CommonParsers.cs 这个存在的位置需要探讨；
4、在整个测试生命周期中 ExecutionResultBase、ExecutionResult 及测试过程中众多的 Result的冗余是否有精简和统一的必要，在保证灵活性的同时，需要考虑到转换过程中的开销。 
5、针对ZL.Gear.Core/Events及ZL.Gear.Core/Infrastructure下所有事件的使用做逐个的梳理，并给出使用建议。
6、针对GearProfileServices加载没有问题，但是json文件中的定义存在不友好或容易混淆的可能，根据如下的使用过程进行梳理整个流程，并给出最优的方案；
Target 一定要使用逻辑名，不能使用物理名，需要同 AdditionalTargets 保持一致。
7、ZL.Gear.Core.Models.StepSignalPair 同 ZL.Gear.Sensing.Dto.SignalPair 是否可以合并？
8、由于网络中断这个没有实现完毕，ZL.Gear.Core.Devices.Abstractions中定义的IDeviceRegistry并没有被实体类实现，此外，在ZL.Gear.Drivers.Sensing.Orchestration中 SensingOrchestrator却使用了这个接口。
9、针对噪音仪 使用的是 NoiseSampler Reactive模式；
10、ZL.Gear.Drivers/StepHandler/StepHandlerKit.cs 是一个巨无霸类，请根据现有的架构，帮我合理的拆分这个类，使其后续的维护更加方便；
11、ZL.Gear.Drivers/Devices/Transport/NiVisaTransport.cs 和 ZL.Gear.Drivers/Devices/Transport/UsbTmcTransport.cs 中有很多没有实现的方法，请核查一下；
12、 DeviceConfig中的Commands 使用情况核查，这要（ 允许通过配置文件(devices.json)直接定义简单的 SCPI 命令，无需编写额外的 CommandHandler。）如何配置？
13、NotImplementedException  检查
14、UiPlcEvents.OnSbrLocationChanged 这个事件丢失没有定义；
15、   bytesRead = await _transport.ReceiveAsync(new ArraySegment<byte>(discardBuffer), linkedCts.Token).ConfigureAwait(false); 同接口 ITransport的定义存在差异；
   Task<int> ReceiveAsync(byte[] buffer, int offset, int count, CancellationToken token = default);
16、严重性	代码	说明	项目	文件	行	禁止显示状态
错误	CS0117	“MeasurementHub”未包含“Mark”的定义	ZL.Gear.Drivers	C:\1-Code\0-电检\ZL.Gear\ZL.Gear.Drivers\Devices\Sp\NoiseSerialTriggerDevice.cs	160	活动

17、
18、
19、
20、



        {
          "Id": "0",
          "StepKey": "滑轨向前电流",
          "StepName": "滑轨向前电流",
          "Description": "滑轨向前电流",
          "ExecutionMode": 1,
          "ExecutionType": 1,
          "StepType": "Standard",
          "Target": "TestPowerSupply",
          "Command": "CurrentManualMotorEvent",
          "AdditionalTargets": ["PLC"],
          "Parameters": {
            "CustomerParam": "",
            "KtdyCmd": "FETC:CURR?\n",
            "plc.id": 502,
            "plc.value": 1,
            "LCL": "1",
            "UCL": "7",
            "Unit": "A"
          },
          "ExpectedResults": [],
          "TimeoutMs": 20000,
          "Enable": true,
          "PromptString1": "请把滑轨前进",
          "PromptString2": "滑轨前进检测完成",
          "PicturePath": "滑轨向前.png",
          "StopByFail": false,
          "StartDelayMs": 0,
          "SubSteps": [],
          "CanSingleTest": false
        },