using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Engine.Runner.Middlewares
{
    /// <summary>
    /// 超时控制中间件。
    /// 为每个步骤提供独立的超时控制，防止单步骤卡死导致整线停产。
    /// 
    /// 使用方式：在步骤的 Parameters 中配置
    /// - TimeoutMs: 超时时间（毫秒），默认使用 step.TimeoutMs
    /// - TimeoutAction: 超时后的动作（Fail/Continue），默认 Fail
    /// </summary>
    public class TimeoutMiddleware : IStepMiddleware
    {
        private readonly Action<string> _log;

        public TimeoutMiddleware(Action<string> log)
        {
            _log = log ?? (s => { });
        }

        public async Task<ExecutionResult<List<Measurement>>> InvokeAsync(
            StepConfig step,
            StepContext context,
            Func<StepConfig, StepContext, Task<ExecutionResult<List<Measurement>>>> next)
        {
            // 获取超时配置，优先使用中间件参数，否则使用步骤配置
            int timeoutMs = step.TimeoutMs > 0 ? step.TimeoutMs : 30000;
            if (step.Parameters != null && step.Parameters.TryGetValue("TimeoutMs", out var toObj))
            {
                int.TryParse(toObj?.ToString(), out timeoutMs);
            }

            string timeoutAction = "Fail";
            if (step.Parameters != null && step.Parameters.TryGetValue("TimeoutAction", out var taObj))
            {
                timeoutAction = taObj?.ToString() ?? "Fail";
            }

            using var cts = new CancellationTokenSource(timeoutMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, cts.Token);
            
            var linkedContext = context.WithToken(linkedCts.Token);

            try
            {
                _log($"[Timeout] 步骤 '{step.StepName}' 开始执行，超时限制: {timeoutMs}ms");
                
                var result = await next(step, linkedContext);
                
                return result;
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                // 超时触发
                _log($"[Timeout] 步骤 '{step.StepName}' 执行超时 ({timeoutMs}ms)!");
                
                if (timeoutAction.Equals("Fail", StringComparison.OrdinalIgnoreCase))
                {
                    return ExecutionResult<List<Measurement>>.Failed(
                        $"步骤执行超时 ({timeoutMs}ms)",
                        new List<Measurement>());
                }
                else
                {
                    // Continue 模式下记录警告但继续执行
                    _log($"[Timeout] 步骤 '{step.StepName}' 超时但配置为继续执行");
                    return ExecutionResult<List<Measurement>>.Succeeded(
                        new List<Measurement>(),
                        0,
                        $"步骤执行超时 ({timeoutMs}ms)");
                }
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
                // 外部取消
                throw;
            }
        }
    }
}
