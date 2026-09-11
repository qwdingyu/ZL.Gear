using System.Collections.Generic;

namespace ZL.Gear.Samples.Industry.Client
{
    /// <summary>
    /// 单条验证用例：场景文件 + 期望的 OverallSuccess。
    /// </summary>
    public sealed class VerificationCase
    {
        /// <summary>用例标识。</summary>
        public string Id { get; }

        /// <summary>相对 Scenarios 的文件名。</summary>
        public string ScenarioFile { get; }

        /// <summary>是否期望 OverallSuccess=true。</summary>
        public bool ExpectSuccess { get; }

        /// <summary>可选：Summary 须包含的子串（用于契约类场景）。</summary>
        public string? SummaryContains { get; }

        /// <summary>
        /// 创建验证用例。
        /// </summary>
        public VerificationCase(string id, string scenarioFile, bool expectSuccess, string? summaryContains = null)
        {
            Id = id;
            ScenarioFile = scenarioFile;
            ExpectSuccess = expectSuccess;
            SummaryContains = summaryContains;
        }
    }

    /// <summary>
    /// 闭环验证清单（故意包含失败与超时，证明不是「全绿假通过」）。
    /// </summary>
    /// <remarks>
    /// 工业 ATE 惯例：门禁须同时覆盖合格、不合格、超时契约；仅跑 HappyPath 无法发现 Assert 漏检或超时误 PASS。
    /// </remarks>
    public static class VerificationCatalog
    {
        /// <summary>全部用例（顺序：先正后负，便于日志阅读）。</summary>
        public static IReadOnlyList<VerificationCase> All { get; } = new[]
        {
            // 扩展 ActionKey + Calculate + Assert L1 + Finally + MarkComplete 全链路
            new VerificationCase(
                "happy-path",
                "Station_HappyPath.json",
                expectSuccess: true),

            // SimulatedOhm &gt; LimitOhm → Margin≤0 → Assert Failed → OverallSuccess 必须为 false（防漏检）
            new VerificationCase(
                "assert-fail",
                "Station_AssertFail.json",
                expectSuccess: false),

            // WorkflowTimeoutMs &lt; DelayMs → 流程级超时失败（对齐 Demo_Timeout_Contract，防超时误 PASS）
            new VerificationCase(
                "timeout-contract",
                "Station_TimeoutContract.json",
                expectSuccess: false),

            // 换行业先改 JSON：同一扩展 DLL，不同 RecipeId/限值（安全带叙事分叉）
            new VerificationCase(
                "fork-seatbelt-json",
                "Fork_Seatbelt_Like.json",
                expectSuccess: true)
        };
    }
}
