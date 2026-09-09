using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Engine.Runner.Middlewares
{
    /// <summary>
    /// 自动重试中间件。
    /// 当步骤执行失败时，根据配置参数进行自动重试。
    /// 这在硬件环境不稳定的 ATE 测试中是非常关键的“护城河”特性。
    /// </summary>
    public class RetryMiddleware : IStepMiddleware
    {
        public async Task<ExecutionResult<List<Measurement>>> InvokeAsync(StepConfig step, StepContext context, Func<StepConfig, StepContext, Task<ExecutionResult<List<Measurement>>>> next)
        {
            // 从步骤参数中获取重试配置，默认为 0 (不重试)
            int retryCount = 0;
            if (step.Parameters != null && step.Parameters.TryGetValue("RetryCount", out var rcObj))
            {
                int.TryParse(rcObj.ToString(), out retryCount);
            }

            int retryDelayMs = 500;
            if (step.Parameters != null && step.Parameters.TryGetValue("RetryDelayMs", out var rdObj))
            {
                int.TryParse(rdObj.ToString(), out retryDelayMs);
            }

            int attempt = 0;
            ExecutionResult<List<Measurement>> result = null;

            do
            {
                result = await next(step, context).ConfigureAwait(false);

                if (result.Success || attempt >= retryCount)
                {
                    break;
                }

                attempt++;
                context.Log($"[Retry] 步骤 '{step.StepName}' 环境不稳定导致失败。正在进行第 {attempt}/{retryCount} 次重试，等待 {retryDelayMs}ms...");

                try
                {
                    await Task.Delay(retryDelayMs, context.CancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

            } while (attempt <= retryCount);

            return result;
        }
    }
}
