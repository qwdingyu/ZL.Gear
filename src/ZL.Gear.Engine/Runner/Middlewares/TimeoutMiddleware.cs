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
    /// 遗留：超时控制中间件。步骤超时权威在 <c>StepDispatcher.DispatchCoreAsync</c>。
    /// <para>
    /// 预设管道不得挂载本中间件（双 CTS）。勿「抽公共再回流双路径」。
    /// 仅当自定义管道且完全不走 DispatchCore 超时时，才可显式使用（不推荐）。
    /// </para>
    /// </summary>
    [Obsolete("步骤超时权威在 StepDispatcher.DispatchCoreAsync；预设管道勿挂载，避免双 CTS。参见 docs/128。")]
    public class TimeoutMiddleware : IStepMiddleware
    {
        private readonly Action<string> _log;

        /// <summary>
        /// 创建超时中间件。
        /// </summary>
        /// <param name="log">日志委托。</param>
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
            if (step.TryGetParameter("TimeoutMs", out var toObj))
            {
                int.TryParse(toObj?.ToString(), out timeoutMs);
            }

            string timeoutAction = "Fail";
            if (step.TryGetParameter("TimeoutAction", out var taObj))
            {
                timeoutAction = taObj?.ToString() ?? "Fail";
            }

            using var cts = new CancellationTokenSource(timeoutMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, cts.Token);
            
            var linkedContext = context.WithToken(linkedCts.Token);

            try
            {
                _log($"[Timeout] 步骤 '{step.StepName}' 开始执行，超时限制: {timeoutMs}ms");
                
                var result = await next(step, linkedContext).ConfigureAwait(false);
                
                return result;
            }
            catch (OperationCanceledException) when (
                cts.IsCancellationRequested && !context.CancellationToken.IsCancellationRequested)
            {
                // 仅本中间件步骤超时（与 DispatchCore 对齐：外层取消不得被 TimeoutAction 吞掉）
                _log($"[Timeout] 步骤 '{step.StepName}' 执行超时 ({timeoutMs}ms)!");
                
                if (timeoutAction.Equals("Fail", StringComparison.OrdinalIgnoreCase))
                {
                    return ExecutionResult<List<Measurement>>.Failed(
                        $"步骤执行超时 ({timeoutMs}ms)",
                        new List<Measurement>());
                }
                else
                {
                    // 与 DispatchCore 对齐：Continue 仍返回 Failed（防误 PASS），由执行器结合 StopByFail 决定是否中止序列
                    _log($"[Timeout] 步骤 '{step.StepName}' 超时且 TimeoutAction=Continue（记失败，是否中止由 StopByFail 决定）");
                    return ExecutionResult<List<Measurement>>.Failed(
                        $"[TimeoutContinue] 步骤执行超时 ({timeoutMs}ms)",
                        new List<Measurement>());
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
