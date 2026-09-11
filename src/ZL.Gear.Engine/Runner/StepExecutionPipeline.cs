using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Infrastructure;

namespace ZL.Gear.Engine.Runner
{
    public class StepExecutionPipeline
    {
        private readonly List<IStepMiddleware> _middlewares = new();

        public void Use(IStepMiddleware middleware)
        {
            _middlewares.Add(middleware);
        }

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
