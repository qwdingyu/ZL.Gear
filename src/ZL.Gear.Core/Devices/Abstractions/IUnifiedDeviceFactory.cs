using System.Collections.Generic;

namespace ZL.Gear.Core.Devices.Abstractions
{
    /// <summary>
    /// 统一设备工厂抽象
    /// </summary>
    public interface IUnifiedDeviceFactory
    {
        /// <summary>
        /// 根据设备代码创建设备实例
        /// </summary>
        /// <param name="deviceCode">设备代码</param>
        /// <returns>设备实例</returns>
        IDevice CreateDevice(string deviceCode);

        /// <summary>
        /// 获取所有已创建设备
        /// </summary>
        /// <returns>设备字典</returns>
        IReadOnlyDictionary<string, IDevice> GetAllDevices();

        /// <summary>
        /// 预创建所有配置的设备
        /// </summary>
        void CreateAll();
    }
}
