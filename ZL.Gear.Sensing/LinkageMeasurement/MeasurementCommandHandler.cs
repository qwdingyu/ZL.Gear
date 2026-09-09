using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Models;
using ZL.Gear.Sensing.Abstractions;
using ZL.Gear.Sensing.Dto;
using ZL.Gear.Sensing.Orchestration;

namespace ZL.Gear.Sensing.LinkageMeasurement
{
    /// <summary>
    /// 专门处理复杂测量场景的命令处理器。
    /// </summary>
    public class MeasurementCommandHandler : ICommandHandler
    {
        private readonly Dictionary<string, (Func<Dictionary<string, object>, StepContext, IMeasurable> Factory, ScenarioType Type)> _scenarioRegistry;
        private readonly MeasurementOrchestrator _orchestrator;
        private readonly Action<string> _log;

        public MeasurementCommandHandler(
            Dictionary<string, (Func<Dictionary<string, object>, StepContext, IMeasurable> Factory, ScenarioType Type)> scenarioRegistry,
            MeasurementOrchestrator orchestrator,
            Action<string> log)
        {
            _scenarioRegistry = scenarioRegistry ?? throw new ArgumentNullException(nameof(scenarioRegistry));
            _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
            _log = log;
        }

        public bool CanHandle(string command)
        {
            return _scenarioRegistry.ContainsKey(command);
        }

        public Task<ExecutionResultBase> HandleAsync(string command, Dictionary<string, object> args, StepContext context)
        {
            if (!_scenarioRegistry.TryGetValue(command, out var registration))
            {
                // 理论上 CanHandle 会阻止这种情况，但作为防御性编程
                return Task.FromResult<ExecutionResultBase>(ExecutionResult.Failed($"内部错误: MeasurementCommandHandler 无法找到命令 '{command}' 的注册信息。"));
            }

            var (factory, scenarioType) = registration;
            var measurable = factory(args, context);

            var config = new ScenarioConfiguration
            {
                Measurable = measurable,
                Type = scenarioType,
                Context = context,
                Args = args,
                Log = _log
            };

            return _orchestrator.RunAsync(config);
        }
    }
}
