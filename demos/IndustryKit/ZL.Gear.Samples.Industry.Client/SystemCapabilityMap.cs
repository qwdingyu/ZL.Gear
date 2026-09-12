using System.Collections.Generic;

namespace ZL.Gear.Samples.Industry.Client
{
    /// <summary>
    /// Gear.NET 全系统能力地图：IndustryKit 已演示 vs 需 Instrumented/私有轨。
    /// </summary>
    /// <remarks>
    /// 打印入口：<c>capabilities</c> 命令。详述见 <c>demos/IndustryKit/CAPABILITIES.md</c>。
    /// </remarks>
    public sealed class DeferredCapability
    {
        public string Capability { get; }
        public string Where { get; }
        public string WhyNotInIndustryKit { get; }

        public DeferredCapability(string capability, string where, string whyNotInIndustryKit)
        {
            Capability = capability;
            Where = where;
            WhyNotInIndustryKit = whyNotInIndustryKit;
        }
    }

    public static class SystemCapabilityMap
    {
        /// <summary>IndustryKit（LogicOnly + Core）未挂载但框架已具备的能力。</summary>
        public static IReadOnlyList<DeferredCapability> InstrumentedOrPrivate { get; } = new[]
        {
            new DeferredCapability(
                "GenericMeasure / TriggeredMeasure（Continuous·Threshold·Duration）",
                "ZL.Gear.Sensing + BuiltInModules.Sensing",
                "LogicOnly 禁止挂 Sensing；见私有 ConsoleApp Demo_Sampling_Continuous"),
            new DeferredCapability(
                "设备元语 Write / Read / Query",
                "StandardActionsProvider + IDevice",
                "需 AsInstrumentedHost + DeviceService/Mock"),
            new DeferredCapability(
                "Sync.SignalStart / Sync.StopMaster（主从信令）",
                "Engine BuiltIn + ContextVariableStore",
                "需多步骤 DependsOn 与 Instrumented 全栈场景"),
            new DeferredCapability(
                "PLC / AI 内置模块",
                "BuiltInModules.Plc / .Ai",
                "LogicOnly Build 门禁禁止；Drivers 私有仓"),
            new DeferredCapability(
                "步骤级 ExpectedResults + EvaluateResult:true",
                "ResultEvaluator + ExecutionType.Verify",
                "IndustryKit 用 DynamicFlow 内 Assert；顶层 Verify 在私有 legacy 树"),
            new DeferredCapability(
                "结果持久化 CSV/SQLite",
                "ZL.Gear.Extensions.Data",
                "可选 NuGet；IndustryKit 未引用"),
            new DeferredCapability(
                "Instrumented 产线扩展 + Noise 采样",
                "ZL.Gear.Exts + ZL.Gear.Demos",
                "私有仓；见 ConsoleApp SeatHostBootstrap（非公开 IndustryKit）")
        };
    }
}
