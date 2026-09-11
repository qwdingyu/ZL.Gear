using System.Threading;
using System.Threading.Tasks;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Core.Infrastructure
{
    /// <summary>
    /// 步骤处理器健康检查扩展方法。
    /// </summary>
    public static class StepHandlerHealthCheckExtensions
    {
        /// <summary>
        /// 尝试对指定步骤的 Handler 执行健康检查。
        /// 如果 Handler 实现了 <see cref="IStepHandlerHealthCheck"/>，则委托给它；
        /// 否则使用 <see cref="DefaultStepHandlerHealthCheck"/> 返回默认健康结果。
        /// </summary>
        /// <param name="lookup">Handler 查找策略。</param>
        /// <param name="step">当前步骤配置。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>健康检查结果，若未找到 Handler 则返回 null。</returns>
        public static async Task<HealthCheckResult> TryCheckHealthAsync(
            this IStepHandlerLookup lookup,
            StepConfig step,
            CancellationToken cancellationToken = default)
        {
            if (lookup == null) return null;
            if (step == null) return null;

            if (lookup.TryGetHandler(step, out var handler))
            {
                var checker = handler as IStepHandlerHealthCheck ?? new DefaultStepHandlerHealthCheck(handler);
                return await checker.CheckHealthAsync(cancellationToken).ConfigureAwait(false);
            }

            if (lookup.TryGetTemplateHandler(step, out var templateHandler))
            {
                var checker = templateHandler as IStepHandlerHealthCheck ?? new DefaultStepHandlerHealthCheck(templateHandler);
                return await checker.CheckHealthAsync(cancellationToken).ConfigureAwait(false);
            }

            var fallbackHandler = lookup.GetFallbackHandler(step);
            var fallbackChecker = fallbackHandler as IStepHandlerHealthCheck ?? new DefaultStepHandlerHealthCheck(fallbackHandler);
            return await fallbackChecker.CheckHealthAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
