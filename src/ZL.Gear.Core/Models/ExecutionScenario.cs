namespace ZL.Gear.Core.Models
{
    /// <summary>
    /// 执行场景枚举
    /// </summary>
    public enum ExecutionScenario
    {
        /// <summary>
        /// 产线生产场景：启用全量中间件链（日志、审计、熔断、安全、诊断等）
        /// </summary>
        Production = 0,

        /// <summary>
        /// 仿真场景：跳过熔断、快照、审计等硬件相关中间件
        /// </summary>
        Simulation = 1,

        /// <summary>
        /// 实验室场景：保留诊断和安全检查，跳过熔断
        /// </summary>
        Lab = 2
    }
}
