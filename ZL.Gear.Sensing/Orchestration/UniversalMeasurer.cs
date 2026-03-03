using System;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Models;
using ZL.Gear.Sensing.Abstractions;
using ZL.Gear.Sensing.Dto;
using ZL.Gear.Sensing.Samplers;
using ZL.Gear.Sensing.Sampling;

namespace ZL.Gear.Sensing.Orchestration
{
    /// <summary>
    /// 万能测量执行器。
    /// 能够根据 JSON 配置，动态选择设备、命令，并应用采样策略和触发逻辑。
    /// </summary>
    public class UniversalMeasurer : IMeasurable
    {
        private readonly StepContext _context;
        private readonly SamplingConfigModel _configModel;
        private readonly Action<string> _log;

        public UniversalMeasurer(StepContext context, SamplingConfigModel configModel, Action<string> log = null)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _configModel = configModel ?? new SamplingConfigModel(); // 至少提供默认配置
            _log = log ?? SensingLog.Default;
        }

        public async Task<ExecutionResultBase> MeasureAsync(CancellationToken token)
        {
            string stepName = _context.StepConfig.StepName;
            _log($"[{stepName}] 启动万能测量任务...");

            // 1. 获取目标设备
            var targetId = _context.StepConfig.Target;
            if (string.IsNullOrEmpty(targetId))
                return ExecutionResult.Failed("测量失败：未指定 Target 设备。");

            if (!_context.ActiveDevices.TryGetValue(targetId, out var device))
                return ExecutionResult.Failed($"测量失败：找不到设备 '{targetId}'。");

            // 2. 解析采样配置
            var configurator = DynamicSamplingFactory.Create(_configModel);
            var builder = new SamplingConfigBuilder<double>()
                .WithLogger(_log)
                .WithTimeout(_context.TimeoutMs);

            configurator.Configure(builder, _context.StepConfig.Parameters, _context);
            var samplingConfig = builder.Build();
            samplingConfig.ValidateAndLogConfiguration(stepName);

            // 3. 创建采样器 (数据源)
            // CommandKey 优先从 SamplingConfigModel 读，其次从 StepConfig.Command 读
            // CommandKey 优先从 StepConfig.Parameters["Command"] 读，其次从 StepConfig.Command 读
            string cmd = _context.StepConfig.Command;
            if (_context.StepConfig.Parameters != null && 
                _context.StepConfig.Parameters.TryGetValue("Command", out var cmdObj) && 
                cmdObj != null)
            {
                cmd = cmdObj.ToString();
            }
            cmd = cmd ?? "READ";
            
            using var sampler = new DeviceSampler<double>(
                stepName,
                device,
                cmd,
                _context.StepConfig.Parameters,
                _context,
                TimeSpan.FromMilliseconds(samplingConfig.SampleIntervalMs)
            );

            // 4. 准备数据流
            var dataStream = sampler.DataStream
                .Where(m => m.Success && m.Value != null)
                .Select(m => (double)m.Value);

            // 5. 启动引擎执行判定
            var engine = new DefaultMeasurementEngine<double>(_log);
            sampler.Start();

            try
            {
                var result = await engine.ExecuteAsync(dataStream, samplingConfig, token);

                // 6. 转换结果
                if (result.IsSuccess)
                {
                    if (result.SpecPassed)
                        return ExecutionResult<double>.Succeeded(result.Value, result.AllSamples.Count, result.Message);
                    else
                        return ExecutionResult<double>.Failed($"规格检查失败: {result.Message}", result.Value, result.AllSamples.Count);
                }

                return ExecutionResult<double>.Failed(result.Message, result.Value, result.AllSamples.Count);
            }
            catch (Exception ex)
            {
                return ExecutionResult.Failed($"测量引擎异常: {ex.Message}");
            }
            finally
            {
                sampler.Stop();
            }
        }
    }
}
