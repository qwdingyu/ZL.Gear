using System;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Engine.Runner
{
    /// <summary>
    /// 默认场景化管道提供器
    /// </summary>
    public class DefaultScenarioPipelineProvider : IScenarioPipelineProvider
    {
        private readonly IStepHandlerFactory _handlerFactory;

        public DefaultScenarioPipelineProvider(IStepHandlerFactory handlerFactory)
        {
            _handlerFactory = handlerFactory ?? throw new ArgumentNullException(nameof(handlerFactory));
        }

        public StepExecutionPipeline GetPipeline(ExecutionScenario scenario, Action<string> log)
        {
            var builder = new StepPipelineBuilder().UseLogger(log ?? (_ => { }));

            switch (scenario)
            {
                case ExecutionScenario.Production:
                    return builder.UseDefaultPipeline().Build();

                case ExecutionScenario.Simulation:
                    return builder.UseSimulationPipeline().Build();

                case ExecutionScenario.Lab:
                    return builder.UseLabPipeline().Build();

                default:
                    return builder.UseDefaultPipeline().Build();
            }
        }
    }
}
