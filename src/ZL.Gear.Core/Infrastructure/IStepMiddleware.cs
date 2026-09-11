using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Core.Infrastructure
{
    /// <summary>
    /// 步骤执行中间件接口，允许在步骤执行前后插入逻辑（如日志、耗时统计、异常处理）
    /// </summary>
    public interface IStepMiddleware
    {
        Task<ExecutionResult<List<Measurement>>> InvokeAsync(StepConfig step, StepContext context, Func<StepConfig, StepContext, Task<ExecutionResult<List<Measurement>>>> next);
    }
}
