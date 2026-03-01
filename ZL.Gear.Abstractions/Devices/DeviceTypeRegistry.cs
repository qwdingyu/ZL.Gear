using System;
using System.Collections.Generic;
using ZL.Gear.Core.Devices.Abstractions;

namespace ZL.Gear.Abstractions.Devices
{
    /// <summary>
    /// 设备类型常量定义
    /// 用于在 JSON 配置中标识设备类型
    /// </summary>
    public static class DeviceTypes
    {
        public const string PowerSupply = "PowerSupply";
        public const string Multimeter = "Multimeter";
        public const string ResistanceMeter = "ResistanceMeter";
        public const string ElectronicLoad = "ElectronicLoad";
        public const string Oscilloscope = "Oscilloscope";
        public const string SignalGenerator = "SignalGenerator";
        public const string Plc = "Plc";
        public const string UniversalScpi = "UniversalScpi";
    }

    /// <summary>
    /// 设备能力标记
    /// 用于标记设备支持的操作能力
    /// </summary>
    [Flags]
    public enum DeviceCapabilities
    {
        None = 0,
        SetVoltage = 1 << 0,
        SetCurrent = 1 << 1,
        SetPower = 1 << 2,
        Measure = 1 << 3,
        Reset = 1 << 4,
        OutputControl = 1 << 5,
        QueryIdentity = 1 << 6,
        Trigger = 1 << 7
    }

    /// <summary>
    /// 设备类型扩展方法
    /// 提供设备类型相关的辅助方法
    /// </summary>
    public static class DeviceTypeExtensions
    {
        private static readonly Dictionary<string, DeviceCapabilities> _capabilityMap =
            new Dictionary<string, DeviceCapabilities>(StringComparer.OrdinalIgnoreCase)
            {
                [DeviceTypes.PowerSupply] = DeviceCapabilities.SetVoltage | DeviceCapabilities.SetCurrent |
                                             DeviceCapabilities.OutputControl | DeviceCapabilities.Measure | DeviceCapabilities.Reset,
                [DeviceTypes.Multimeter] = DeviceCapabilities.Measure | DeviceCapabilities.Reset,
                [DeviceTypes.ResistanceMeter] = DeviceCapabilities.Measure | DeviceCapabilities.Reset,
                [DeviceTypes.ElectronicLoad] = DeviceCapabilities.SetCurrent | DeviceCapabilities.SetPower |
                                                DeviceCapabilities.SetVoltage | DeviceCapabilities.OutputControl |
                                                DeviceCapabilities.Measure | DeviceCapabilities.Reset,
                [DeviceTypes.UniversalScpi] = DeviceCapabilities.None
            };

        /// <summary>
        /// 获取设备类型对应的能力标记
        /// </summary>
        public static DeviceCapabilities GetCapabilities(this IDevice device)
        {
            if (device == null) return DeviceCapabilities.None;
            return GetCapabilitiesByType(device.GetType().Name);
        }

        /// <summary>
        /// 根据设备类型名称获取能力标记
        /// </summary>
        public static DeviceCapabilities GetCapabilitiesByType(string deviceType)
        {
            if (_capabilityMap.TryGetValue(deviceType, out var capabilities))
            {
                return capabilities;
            }
            return DeviceCapabilities.None;
        }

        /// <summary>
        /// 检查设备是否支持指定能力
        /// </summary>
        public static bool SupportsCapability(this IDevice device, DeviceCapabilities capability)
        {
            return (device.GetCapabilities() & capability) == capability;
        }
    }
}
