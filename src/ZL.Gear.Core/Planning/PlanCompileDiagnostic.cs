namespace ZL.Gear.Core.Planning
{
    /// <summary>
    /// 编译诊断等级。
    /// </summary>
    public enum PlanCompileDiagnosticLevel
    {
        Warning = 0,
        Error = 1
    }

    /// <summary>
    /// 结构化编译诊断（写入 <see cref="Runner.TestRunResult.CompileWarnings"/> 与 Errors 聚合）。
    /// </summary>
    public sealed class PlanCompileDiagnostic
    {
        public PlanCompileDiagnosticLevel Level { get; set; }

        /// <summary>稳定机器码，如 DUPLICATE_STEP_KEY、MISSING_HANDLER、INVALID_CONDITION。</summary>
        public string Code { get; set; }

        public string Message { get; set; }

        /// <summary>关联 StepKey；计划级错误可为 null。</summary>
        public string StepKey { get; set; }

        public static PlanCompileDiagnostic Error(string code, string message, string stepKey = null)
            => new PlanCompileDiagnostic { Level = PlanCompileDiagnosticLevel.Error, Code = code, Message = message, StepKey = stepKey };

        public static PlanCompileDiagnostic Warning(string code, string message, string stepKey = null)
            => new PlanCompileDiagnostic { Level = PlanCompileDiagnosticLevel.Warning, Code = code, Message = message, StepKey = stepKey };
    }
}
