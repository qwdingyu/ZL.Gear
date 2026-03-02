using System.Threading;
using System.Threading.Tasks;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Core.Abstractions
{
    /// <summary>
    /// AI 决策策略接口。
    /// 定义了智能体（Agent）如何根据当前上下文做出决策。
    /// </summary>
    public interface IAiDecisionPolicy
    {
        /// <summary>
        /// 根据当前上下文做出决策。
        /// </summary>
        /// <param name="context">步骤执行上下文，包含观测状态。</param>
        /// <param name="token">取消令牌。</param>
        /// <returns>AI 决策结果。</returns>
        Task<AiDecision> DecideAsync(StepContext context, CancellationToken token);
    }
}
