using System;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Engine.Runner
{
    /// <summary>
    /// 场景化管道提供器：根据执行场景返回对应的中间件管道
    /// </summary>
    public interface IScenarioPipelineProvider
    {
        /// <summary>
        /// 根据场景获取中间件管道
        /// </summary>
        StepExecutionPipeline GetPipeline(ExecutionScenario scenario, Action<string> log);
    }
}
