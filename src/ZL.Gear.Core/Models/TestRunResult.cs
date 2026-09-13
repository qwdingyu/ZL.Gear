using System;
using System.Collections.Generic;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Planning;
using ZL.Gear.Core.StepHandler;

namespace ZL.Gear.Core.Runner
{
    public class TestRunResult
    {
        /// <summary>
        /// 本次 Run 的唯一标识（对标行业运行时 PlanRun Id，用于追溯与 Stop 定位）。
        /// </summary>
        public Guid RunId { get; set; }

        /// <summary>
        /// 编译快照哈希（<see cref="Planning.CompiledTestPlan.PlanHash"/>），用于审计与 MES 追溯。
        /// </summary>
        public string PlanHash { get; set; }

        /// <summary>编译期非阻断警告（结构化，供 MES / 运维审计）。</summary>
        public List<PlanCompileDiagnostic> CompileWarnings { get; set; } = new List<PlanCompileDiagnostic>();

        /// <summary>编译期阻断错误；非空表示未进入执行阶段。</summary>
        public List<PlanCompileDiagnostic> CompileErrors { get; set; } = new List<PlanCompileDiagnostic>();

        public string Model { get; set; }
        public string Barcode { get; set; }
        /// <summary>
        /// 指示整个测试序列是否所有步骤都成功通过。
        /// </summary>
        public bool OverallSuccess { get; set; }

        /// <summary>Run 级终局语义（T-P0-01c），由步骤 Verdict 聚合得出。</summary>
        public StepVerdictKind RunVerdictKind { get; set; } = StepVerdictKind.None;

        /// <summary>Run 结束时仍处于隔离状态的设备键（Runtime 快照，供 MES/运维）。</summary>
        public List<string> QuarantinedDeviceKeys { get; set; } = new List<string>();

        /// <summary>
        /// 本次测试运行的总体摘要信息。
        /// </summary>
        public string Summary { get; set; }
        /// <summary>
        /// 测试开始时间。
        /// </summary>
        public DateTime StartTime { get; set; }
        /// <summary>
        /// 测试结束时间。
        /// </summary>
        public DateTime EndTime { get; set; }
        /// <summary>
        /// 总耗时（秒）。
        /// </summary>
        public string TotalDurationSeconds => ((EndTime - StartTime).TotalSeconds).ToString("F2");
        /// <summary>
        /// 包含所有步骤执行结果的树状结构，用于最终保存和详细分析
        /// </summary>
        public List<StepRunResult> StepResults { get; set; } = new List<StepRunResult>();

    }
}
