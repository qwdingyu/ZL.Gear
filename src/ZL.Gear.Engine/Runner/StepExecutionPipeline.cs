using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Infrastructure;

namespace ZL.Gear.Engine.Runner
{
    /// <summary>
    /// 步骤执行中间件管道。
    /// 职责：按注册顺序串联 <see cref="IStepMiddleware"/>，最终调用核心分发逻辑。
    /// 设计要点：
    /// 1. 参考 ASP.NET Core 中间件模型，支持洋葱式环绕；
    /// 2. 不限制中间件数量，由 <see cref="StepPipelineBuilder"/> 按场景组合；
    /// 3. 核心逻辑由 <paramref name="coreLogic"/> 传入，保持管道与业务解耦。
    /// </summary>
    public class StepExecutionPipeline
    {
        /// <summary>
        /// 中间件列表，按添加顺序执行。
        /// </summary>
        private readonly List<IStepMiddleware> _middlewares = new();

        /// <summary>
        /// 添加中间件到管道末尾。
        /// </summary>
        /// <param name="middleware">中间件实例。</param>
        public void Use(IStepMiddleware middleware)
        {
            _middlewares.Add(middleware);
        }

        /// <summary>
        /// 执行管道：依次调用中间件，最终落入核心逻辑。
        /// </summary>
        /// <param name="step">当前步骤配置。</param>
        /// <param name="context">执行上下文。</param>
        /// <param name="coreLogic">核心分发逻辑（通常为 <see cref="StepDispatcher.DispatchCoreAsync"/>）。</param>
        /// <returns>执行结果。</returns>
        public async Task<ExecutionResult<List<Measurement>>> ExecuteAsync(
            StepConfig step,
            StepContext context,
            Func<StepConfig, StepContext, Task<ExecutionResult<List<Measurement>>>> coreLogic)
        {
            int index = 0;

            async Task<ExecutionResult<List<Measurement>>> Next(StepConfig s, StepContext c)
            {
                if (index < _middlewares.Count)
                {
                    var middleware = _middlewares[index++];
                    return await middleware.InvokeAsync(s, c, Next);
                }
                return await coreLogic(s, c);
            }

            return await Next(step, context);
        }
    }
}
