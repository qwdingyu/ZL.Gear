using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Core.Devices.Abstractions
{
    /// <summary>
    /// 标准设备驱动接口，暴露业务层面的操作
    /// </summary>
    public interface IDeviceDriver : IAsyncDisposable
    {
        string DriverName { get; }

        /// <summary>
        /// 初始化驱动（握手、配置等）
        /// </summary>
        Task InitializeAsync(CancellationToken token);

        /// <summary>
        /// 执行特定的驱动指令
        /// </summary>
        //Task<DeviceReading> ExecuteAsync(string command, Dictionary<string, object> args, CancellationToken token);
        Task<DeviceReading> ExecuteAsync(string command, Dictionary<string, object> args, StepContext context);
    }
}
