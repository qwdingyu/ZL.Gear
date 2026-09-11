using System;

namespace ZL.Gear.Engine
{
    /// <summary>
    /// 框架内置模块开关（docs/138 微动）。
    /// <para>
    /// 组合根通过掩码显式勾选要挂载的能力；默认 <see cref="All"/>，
    /// 与历史「StepDispatcher 构造即全注册」行为一致，产线无需改调用方。
    /// </para>
    /// <para>
    /// <b>注意</b>：本枚举只控制「注册哪些内置 Handler/Action」，
    /// <b>不</b>拆除 Engine 对 Sensing/Drivers 的编译期 ProjectReference（拆引用属大动，见 ADR 138）。
    /// </para>
    /// </summary>
    [Flags]
    public enum BuiltInModules
    {
        /// <summary>不注册任何内置模块（极少用；仅诊断/极瘦宿主）。</summary>
        None = 0,

        /// <summary>
        /// 逻辑 DSL 与核心 Handler：
        /// StandardActions（Delay/Log/Assert/Calculate 等，亦含设备向 Write/Read/Query）、
        /// DynamicFlow、MicroWorkflowDemo（含 Mock，非产线配方）。
        /// 不含 GenericMeasure / TriggeredMeasure（见 <see cref="Sensing"/>）。
        /// </summary>
        Core = 1,

        /// <summary>
        /// 采样测量适配：UniversalActionProvider（GenericMeasure）+ TriggeredMeasure。
        /// 编译期依赖 Sensing 程序集。
        /// </summary>
        Sensing = 2,

        /// <summary>
        /// PLC 命令族：Drivers 已加载时反射注册 SetupPlcRelay/WriteToPlc/PlcHandshake 等；
        /// 找不到程序集则静默跳过。
        /// </summary>
        Plc = 4,

        /// <summary>
        /// AI 决策 Handler（AiDecision）。Handler 默认挂上；真正推理依赖 DI 的 IAiDecisionPolicy，
        /// 无策略则该步失败——并非凡跑测必调 LLM。
        /// </summary>
        Ai = 8,

        /// <summary>
        /// 电检整机默认包：Core | Sensing | Plc | Ai（对齐历史一次全挂）。
        /// </summary>
        All = Core | Sensing | Plc | Ai
    }
}
