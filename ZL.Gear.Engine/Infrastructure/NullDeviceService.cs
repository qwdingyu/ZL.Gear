using System;
using System.Threading;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices.Abstractions;

namespace ZL.Gear.Engine.Infrastructure
{
    /// <summary>
    /// 空操作设备服务。用于无真实设备驱动的场景（纯逻辑编排、行业扩展示例、Mock 流程）。
    /// 执行到需要真实设备的步骤时，<see cref="LeaseAsync{TDevice}"/> 将抛出异常并给出修复指引。
    /// </summary>
    /// <remarks>
    /// Engine 解耦后不再编译引用 ZL.Gear.Drivers；宿主未注入 <see cref="IDeviceService"/> 时使用本类兜底，
    /// 确保 Build 阶段不崩溃（IndustryKit、逻辑类 DynamicFlow 等）。
    /// </remarks>
    public sealed class NullDeviceService : IDeviceService
    {
        /// <inheritdoc />
        public Task<IDeviceLease<TDevice>> LeaseAsync<TDevice>(string deviceKey, CancellationToken token = default)
            where TDevice : class, IDevice
        {
            throw new NotSupportedException(
                $"当前未注册设备服务，无法租用设备: {deviceKey}。" +
                "修复方式：请通过 SequenceExecutorBuilder.WithDeviceService() 注入 IDeviceService，" +
                "或在宿主 DI 中调用 ZL.Gear.Drivers 提供的 AddGearDrivers() / DriversServiceCollectionExtensions.CreateDeviceService()。");
        }

        /// <inheritdoc />
        public Task InitializeAllDevicesAsync(int maxParallelism = 4, CancellationToken token = default)
            => Task.CompletedTask;

        /// <inheritdoc />
        public Task EmergencyStopAsync(CancellationToken token = default)
            => Task.CompletedTask;
    }
}
