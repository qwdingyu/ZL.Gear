using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Engine.Runner.Middlewares
{
    public class LoggingMiddleware : IStepMiddleware
    {
        private readonly Action<string> _log;

        public LoggingMiddleware(Action<string> log)
        {
            _log = log ?? (s => { });
        }

        public async Task<ExecutionResult<List<Measurement>>> InvokeAsync(
            StepConfig step, 
            StepContext context, 
            Func<StepConfig, StepContext, Task<ExecutionResult<List<Measurement>>>> next)
        {
            _log($"[Pipeline] 准备执行步骤: {step.StepName} ({step.Command})");
            var sw = Stopwatch.StartNew();

            try
            {
                var result = await next(step, context).ConfigureAwait(false);
                sw.Stop();

                _log($"[Pipeline] 步骤 {step.StepName} 执行完毕. 耗时: {sw.ElapsedMilliseconds}ms, 结果: {(result.Success ? "Pass" : "Fail")}");
                return result;
            }
            catch (Exception ex)
            {
                sw.Stop();
                _log($"[Pipeline] 步骤 {step.StepName} 执行发生异常: {ex.Message}");
                throw;
            }
        }
    }
}
