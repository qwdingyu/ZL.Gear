using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Models;
using ZL.Gear.Sensing.Abstractions;

namespace ZL.Gear.Sensing.LinkageMeasurement
{
    /// <summary>
    /// 定义一个可以启动和停止后台轮询的设备能力接口。
    /// </summary>
    public interface IPollingCapable
    {
        Task<ExecutionResult> StartListeningAsync(Dictionary<string, object> args);
        Task StopListeningAsync();
    }

    /// <summary>
    /// 专门处理后台轮询命令的处理器。
    /// </summary>
    public class PollingCommandHandler : ICommandHandler
    {
        private readonly IPollingCapable _device;

        public PollingCommandHandler(IPollingCapable device)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
        }

        public bool CanHandle(string command)
        {
            return command.Equals("StartListening", StringComparison.OrdinalIgnoreCase) ||
                   command.Equals("StopListening", StringComparison.OrdinalIgnoreCase);
        }

        public async Task<ExecutionResultBase> HandleAsync(string command, Dictionary<string, object> args, StepContext context)
        {
            if (command.Equals("StartListening", StringComparison.OrdinalIgnoreCase))
            {
                return await _device.StartListeningAsync(args).ConfigureAwait(false);
            }

            if (command.Equals("StopListening", StringComparison.OrdinalIgnoreCase))
            {
                await _device.StopListeningAsync().ConfigureAwait(false);
                return ExecutionResult.Succeeded();
            }

            return ExecutionResult.Failed($"PollingCommandHandler 无法处理命令: {command}");
        }
    }
}
