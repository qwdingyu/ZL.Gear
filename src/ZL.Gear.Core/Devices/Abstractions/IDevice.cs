using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Core.Devices.Abstractions
{
    public interface IDevice
    {
        string DeviceName { get; }
        Action<string> Log { get; }

        /// <summary>
        /// 检查设备当前是否处于健康可用状态。 包括传输层和设备本身的逻辑状态。
        /// </summary>
        bool IsHealthy { get; }

        /// <summary>
        /// 确保设备已完成初始化（如握手、参数设置）。
        /// 此方法是幂等的，可以安全地多次调用。
        /// </summary>
        Task InitializeAsync(CancellationToken token = default);
        /// <summary>
        /// 核心执行方法：向设备发送一个命令并获取结果。
        /// 这是所有设备能力（Capability）的统一入口点。
        /// </summary>
        /// <param name="command">要执行的命令/能力名称。</param>
        /// <param name="args">命令所需的参数。</param>
        /// <param name="stepContext">当前执行步骤的上下文信息。</param>
        /// <returns>一个代表执行结果的异步任务。</returns>
        Task<DeviceReading> ExecuteAsync(string command, Dictionary<string, object> args, StepContext stepContext);
    }


    public interface IResettableDevice : IDevice
    {
        Task ResetAsync(CancellationToken token);
    }
}
