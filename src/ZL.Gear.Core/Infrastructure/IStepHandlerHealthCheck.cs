using System;
using System.Threading;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Core.Infrastructure
{
    /// <summary>
    /// 步骤处理器健康检查接口。
    /// 用于在不实际执行步骤的情况下，快速检查 Handler 是否可用。
    /// </summary>
    public interface IStepHandlerHealthCheck
    {
        /// <summary>
        /// 检查 Handler 健康状态。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>健康检查结果。</returns>
        Task<HealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// 健康检查结果。
    /// </summary>
    public class HealthCheckResult
    {
        /// <summary>
        /// 是否健康。
        /// </summary>
        public bool IsHealthy { get; set; }

        /// <summary>
        /// 健康状态描述。
        /// </summary>
        public string Message { get; set; }
    }
}
