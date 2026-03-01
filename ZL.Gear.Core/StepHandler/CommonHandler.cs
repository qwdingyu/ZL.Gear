using System.Collections.Generic;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Devices.Abstractions;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Core.StepHandler
{
    /// <summary>
    /// 通用设备调用处理器。
    /// 用于执行任意设备的任意命令，是默认的回退处理器。
    /// </summary>
    public class CommonHandler : IStepHandler
    {
        public async Task<ExecutionResultBase> ExecuteAsync(StepConfig step, StepContext context)
        {
            if (string.IsNullOrEmpty(step.Target))
                return ExecutionResult.Failed("步骤配置中未指定 'Target'。");
            if (string.IsNullOrEmpty(step.Command))
                return ExecutionResult.Failed("步骤配置中未指定 'Command'。");

            var device = context.GetDevice<IDevice>(step.Target);
            var reading = await device.ExecuteAsync(step.Command, step.Parameters, context);
            var key = step.MeasurementKey;

            var measurement = Measurement.Create(
                key,
                reading.Value ?? 0,
                reading.Success,
                reading.Message,
                "",
                reading.SamplesCollected
            );

            if (!measurement.Success)
            {
                return ExecutionResult<List<Measurement>>.Failed(
                    measurement.Message,
                    new List<Measurement> { measurement },
                    1
                );
            }

            return ExecutionResult<List<Measurement>>.Succeeded(
                new List<Measurement> { measurement }
            );
        }
    }
}
