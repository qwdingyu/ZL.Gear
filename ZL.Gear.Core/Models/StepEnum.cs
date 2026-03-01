namespace ZL.Gear.Core.Models
{
    /// <summary>
    /// 步骤执行模式
    /// </summary>
    public enum StepExecutionMode
    {
        /// <summary>
        /// 串行执行：子步骤将按照列表中的顺序，一个接一个地执行。
        /// </summary>
        Serial,

        /// <summary>
        /// 并行执行：所有子步骤将同时开始执行（考虑StartDelayMs延迟后）。
        /// </summary>
        Parallel
    }
    /// <summary>
    /// 
    /// </summary>
    public enum StepExecutionType
    {
        /// <summary>
        /// 纯执行：只要代码不报错即视为成功，忽略 ExpectedResults（除非发生系统级异常）。
        /// 适用：气缸动作、复位操作、蜂鸣器提示。
        /// </summary>
        Execute = 0,

        /// <summary>
        /// 强校验：必须包含测量数据且必须符合 ExpectedResults，否则失败。评估器模式
        /// 适用：电检测量、到位信号检查、扫码比对。
        /// </summary>
        Verify = 1,

        /// <summary>
        /// 数据采集：必须产生数据，但对数据的数值范围不做强制判定（除非显式配置了 Spec）。
        /// 适用：MES数据采集、过程值记录。
        /// </summary>
        DataCollection = 2
    }

    /// <summary>
    ///  定义一个步骤在运行时的状态。
    /// </summary>
    public enum StepExecutionStatus
    {
        Pending, // 等待执行
        Running, // 正在执行
        Completed // 执行完成 (无论成功或失败)
    }

    /// <summary>
    ///  定义一个步骤完成后的最终判定结果。
    /// </summary>
    public enum StepOutcome
    {
        NotEvaluated, // 尚未评估
        Error,
        Passed,       // 通过
        Failed,       // 失败
        Skipped       // 已跳过
    }

}
