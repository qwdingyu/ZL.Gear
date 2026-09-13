namespace ZL.Gear.Core.Planning
{
    /// <summary>
    /// 计划编译选项（由 SequenceExecutorBuilder 注入 DI，产线默认 Strict）。
    /// </summary>
    public sealed class PlanCompileOptions
    {
        /// <summary>
        /// 未注册且无法经模板解析的命令是否编译失败。产线默认 true（对标 OpenTAP 未知 Step 类型）。
        /// Demo / legacy 动态命令可设为 false。
        /// </summary>
        public bool StrictMissingHandlerCheck { get; set; } = true;

        /// <summary>
        /// 是否对 Parameters.Condition 做 DynamicExpresso 语法预检。
        /// </summary>
        public bool ValidateConditionSyntax { get; set; } = true;
    }
}
