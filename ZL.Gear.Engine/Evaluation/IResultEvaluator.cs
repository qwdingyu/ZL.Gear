using ZL.Gear.Core.Models;
using ZL.Gear.Core.StepHandler;

namespace ZL.Gear.Engine.Evaluation
{
    /// <summary>
    /// 步骤结果评估器接口。
    /// </summary>
    public interface IResultEvaluator
    {
        /// <summary>
        /// 评估步骤执行结果。
        /// </summary>
        /// <param name="stepResult">步骤运行结果。</param>
        /// <param name="config">步骤配置。</param>
        /// <returns>评估结果。</returns>
        EvaluationResult Evaluate(StepRunResult stepResult, StepConfig config);
    }
}
