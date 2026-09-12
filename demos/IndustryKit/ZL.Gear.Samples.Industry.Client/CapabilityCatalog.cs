using System.Collections.Generic;

namespace ZL.Gear.Samples.Industry.Client
{
    /// <summary>
    /// Gear.NET 产品能力 ↔ 场景映射（教学/橱窗用）。
    /// </summary>
    public sealed class CapabilityEntry
    {
        public string Capability { get; }
        public string Layer { get; }
        public string ScenarioFile { get; }
        public string Proof { get; }

        public CapabilityEntry(string capability, string layer, string scenarioFile, string proof)
        {
            Capability = capability;
            Layer = layer;
            ScenarioFile = scenarioFile;
            Proof = proof;
        }
    }

    public static class CapabilityCatalog
    {
        /// <summary>按 L-DSL → L-Test → L-Adapter 分层的能力矩阵。</summary>
        public static IReadOnlyList<CapabilityEntry> All { get; } = new[]
        {
            new CapabilityEntry(
                "微流程 DSL：Calculate + Assert L1/L0/L2",
                "L-DSL / Engine",
                "Gear_Core_Showcase.json + Gear_Assert_L2_Condition.json",
                "Check / Left+Op+Right / Condition 三轨（docs/006）"),
            new CapabilityEntry(
                "GlobalContext（Model/Barcode）",
                "L-Test / Engine",
                "Station_HappyPath.json",
                "ExecuteAsync 注入；MarkComplete 日志回显"),
            new CapabilityEntry(
                "控制流：Parallel / Group / Sequence / Retry / Delay / 节点级 Condition / 节点级 Finally",
                "L-DSL / Engine",
                "Gear_Core_Showcase.json",
                "并行支路、分组、重试、双 Delay 形态、节点级条件守卫与节点级清理"),
            new CapabilityEntry(
                "WaitUntil 轮询 + SetVariable",
                "L-DSL / Engine",
                "Gear_Core_Showcase.json",
                "Ready 变量 + 条件轮询到位"),
            new CapabilityEntry(
                "Finally 清理 + ${插值} 日志",
                "L-DSL / Engine",
                "Gear_Core_Showcase.json",
                "WorkflowDefinition.Finalizers"),
            new CapabilityEntry(
                "行业扩展：ApplyRecipe → Probe → Calculate → Assert",
                "L-Adapter",
                "Station_HappyPath.json",
                "RegisterHandlerWithAction + SetShared 写流程变量"),
            new CapabilityEntry(
                "Args 覆盖（fail-closed）",
                "L-Adapter",
                "Station_ProbeOverride.json",
                "ProbeChannel MeasuredOhm 显式覆盖 SimulatedOhm"),
            new CapabilityEntry(
                "行业 + 控制流一体化工位",
                "L-Test + L-Adapter",
                "Station_Integrated_Ate.json",
                "Parallel 准备 + WaitUntil(ChannelReady) + 双 Assert + MarkComplete"),
            new CapabilityEntry(
                "执行≠评估：故意不合格",
                "L-Test",
                "Station_AssertFail.json",
                "Assert 失败 → OverallSuccess=false"),
            new CapabilityEntry(
                "流程级超时契约",
                "L-Test",
                "Station_TimeoutContract.json",
                "WorkflowTimeoutMs 短于 DelayMs"),
            new CapabilityEntry(
                "换行业先换 JSON",
                "L-Adapter",
                "Fork_Seatbelt_Like.json",
                "同扩展 DLL，不同 RecipeId/限值叙事")
        };
    }
}
