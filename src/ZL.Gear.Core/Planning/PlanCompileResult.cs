using System.Collections.Generic;
using System.Linq;

namespace ZL.Gear.Core.Planning
{
    /// <summary>
    /// 计划编译结果（对标 OpenTAP 编译诊断：Errors 非空即不可运行）。
    /// </summary>
    public sealed class PlanCompileResult
    {
        public PlanCompileResult(
            CompiledTestPlan plan,
            IList<PlanCompileDiagnostic> diagnostics)
        {
            Plan = plan;
            Diagnostics = diagnostics ?? new List<PlanCompileDiagnostic>();
            Errors = Diagnostics
                .Where(d => d.Level == PlanCompileDiagnosticLevel.Error)
                .Select(d => d.Message)
                .ToList();
            Warnings = Diagnostics
                .Where(d => d.Level == PlanCompileDiagnosticLevel.Warning)
                .Select(d => d.Message)
                .ToList();
        }

        public CompiledTestPlan Plan { get; }

        /// <summary>结构化诊断（Errors + Warnings 的权威来源）。</summary>
        public IList<PlanCompileDiagnostic> Diagnostics { get; }

        public IList<string> Errors { get; }

        public IList<string> Warnings { get; }

        public bool Success => Errors.Count == 0;

        public static PlanCompileResult Failed(IEnumerable<PlanCompileDiagnostic> diagnostics)
        {
            return new PlanCompileResult(null, diagnostics?.ToList() ?? new List<PlanCompileDiagnostic>());
        }
    }
}
