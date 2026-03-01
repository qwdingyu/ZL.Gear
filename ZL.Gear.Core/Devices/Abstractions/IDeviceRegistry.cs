using System.Collections.Generic;

namespace ZL.Gear.Core.Devices.Abstractions
{
    /// <summary>
    /// 设备注册表接口，用于管理和检索运行时设备实例。
    /// </summary>
    public interface IDeviceRegistry
    {
        bool TryGet(string deviceId, out IDevice device);
        IEnumerable<IDevice> GetAll();
    }
}
