using System;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.Gear.Core.Devices.Abstractions
{
    // 1. 定义租约的抽象接口，也就是 Engine 拿到的“凭证”
    // 继承 IDisposable 是为了让 Engine 能够用 using 或手动 Dispose 归还设备
    public interface IDeviceLease<out TDevice> : IDisposable where TDevice : IDevice
    {
        TDevice Device { get; }
    }

    // 2. 定义设备服务的抽象接口，Engine 只依赖这个接口
    public interface IDeviceService
    {
        // 这里返回接口 IDeviceLease，而不是具体的 DeviceLease 类
        Task<IDeviceLease<TDevice>> LeaseAsync<TDevice>(string deviceKey, CancellationToken token = default)
            where TDevice : class, IDevice;
        Task InitializeAllDevicesAsync(int maxParallelism = 4, CancellationToken token = default);
        
        /// <summary>
        /// 紧急停止 - 复位所有支持复位的设备
        /// </summary>
        Task EmergencyStopAsync(CancellationToken token = default);

    }
}

