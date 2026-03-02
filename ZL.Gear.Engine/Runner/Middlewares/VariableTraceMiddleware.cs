using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Engine.Runner.Middlewares
{
    /// <summary>
    /// 变量追踪中间件 (调试护城河)。
    /// 在步骤执行后，对比变量库的变化并记录日志。
    /// 这对于诊断复杂的“变量在哪个步骤被篡改了”的问题非常有帮助。
    /// </summary>
    public class VariableTraceMiddleware : IStepMiddleware
    {
        public async Task<ExecutionResult<List<Measurement>>> InvokeAsync(StepConfig step, StepContext context, Func<StepConfig, StepContext, Task<ExecutionResult<List<Measurement>>>> next)
        {
            // 拍摄执行前的变量快照
            var before = context.Variables.AsDictionary().ToDictionary(k => k.Key, v => v.Value);

            var result = await next(step, context);

            // 拍摄执行后的变量快照
            var after = context.Variables.AsDictionary();

            foreach (var kvp in after)
            {
                if (!before.TryGetValue(kvp.Key, out var oldVal) || !Equals(oldVal, kvp.Value))
                {
                    context.Log($"[Variable] '{kvp.Key}' 映射变更: {oldVal ?? "null"} -> {kvp.Value}");
                }
            }

            return result;
        }
    }
}
