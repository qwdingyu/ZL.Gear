using System.Threading;
using System.Threading.Tasks;

namespace ZL.Gear.Core.Infrastructure
{
    /// <summary>
    /// 默认健康检查实现：如果 Handler 实现了 <see cref="IStepHandlerHealthCheck"/> 则委托给它，
    /// 否则直接返回健康结果。
    /// </summary>
    public class DefaultStepHandlerHealthCheck : IStepHandlerHealthCheck
    {
        private readonly IStepHandler _handler;

        /// <summary>
        /// 初始化默认健康检查。
        /// </summary>
        /// <param name="handler">被检查的 Handler。</param>
        public DefaultStepHandlerHealthCheck(IStepHandler handler)
        {
            _handler = handler;
        }

        /// <summary>
        /// 执行健康检查。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>健康检查结果。</returns>
        public Task<HealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default)
        {
            if (_handler is IStepHandlerHealthCheck typed)
            {
                return typed.CheckHealthAsync(cancellationToken);
            }

            return Task.FromResult(new HealthCheckResult
            {
                IsHealthy = true,
                Message = "Handler 未实现 IStepHandlerHealthCheck，默认返回健康。"
            });
        }
    }
}
