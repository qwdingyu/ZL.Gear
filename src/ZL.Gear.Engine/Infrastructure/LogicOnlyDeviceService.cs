using System;
using System.Threading;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices.Abstractions;

namespace ZL.Gear.Engine.Infrastructure
{
    /// <summary>
    /// LogicOnly 宿主专用设备服务：所有设备 I/O 入口 fail-closed（对齐 docs/145 D-145-7）。
    /// </summary>
    internal sealed class LogicOnlyDeviceService : IDeviceService
    {
        private const string RepairHint =
            "修复方式：纯逻辑场景请使用 AsLogicOnlyDemoHost() 且配方不含 Target；" +
            "仪器场景请 AsInstrumentedHost(IDeviceService) 注入真实设备服务。";

        /// <inheritdoc />
        public Task<IDeviceLease<TDevice>> LeaseAsync<TDevice>(string deviceKey, CancellationToken token = default)
            where TDevice : class, IDevice
        {
            throw new NotSupportedException(
                $"当前为 LogicOnly 宿主，无法租用设备: {deviceKey}。{RepairHint}");
        }

        /// <inheritdoc />
        public Task InitializeAllDevicesAsync(int maxParallelism = 4, CancellationToken token = default)
        {
            throw new InvalidOperationException($"LogicOnly 宿主不支持 InitializeAllDevicesAsync。{RepairHint}");
        }

        /// <inheritdoc />
        public Task EmergencyStopAsync(CancellationToken token = default)
        {
            throw new InvalidOperationException($"LogicOnly 宿主不支持 EmergencyStopAsync。{RepairHint}");
        }
    }
}
