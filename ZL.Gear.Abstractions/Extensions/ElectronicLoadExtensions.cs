using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Devices.Abstractions;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Abstractions.Extensions
{
    /// <summary>
    /// 电子负载扩展方法
    /// 提供类型安全的电子负载操作接口
    /// 
    /// 使用方式：
    /// ```csharp
    /// await device.SetModeAsync(LoadMode.CC);
    /// await device.SetCurrentAsync(1.0);
    /// await device.LoadOnAsync();
    /// ```
    /// 
    /// JSON 配置要求（遵循《Gear.NET 设备驱动配置规范》）：
    /// ```json
    /// {
    ///   "Commands": {
    ///     "SetMode":      { "Template": "MODE {mode}", "Param": "mode" },
    ///     "SetCurrent":   { "Template": "CURR {value}", "Param": "value" },
    ///     "SetVoltage":   { "Template": "VOLT {value}", "Param": "value" },
    ///     "SetPower":     { "Template": "POW {value}", "Param": "value" },
    ///     "LoadOn":       { "Template": "INP 1" },
    ///     "LoadOff":      { "Template": "INP 0" },
    ///     "Measure":      { "Template": "MEAS?", "Parser": "Double" }
    ///   }
    /// }
    /// ```
    /// </summary>
    public static class ElectronicLoadExtensions
    {
        /// <summary>
        /// 电子负载模式
        /// </summary>
        public enum LoadMode
        {
            CC,
            CV,
            CR,
            CW
        }

        /// <summary>
        /// 设置负载模式
        /// </summary>
        /// <param name="device">电子负载实例</param>
        /// <param name="mode">负载模式</param>
        public static Task SetModeAsync(this IDevice device, LoadMode mode)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            var args = new Dictionary<string, object> { ["mode"] = mode.ToString() };
            return device.ExecuteAsync("SetMode", args, null);
        }

        /// <summary>
        /// 设置恒流值
        /// </summary>
        /// <param name="device">电子负载实例</param>
        /// <param name="current">电流值 (A)</param>
        public static Task SetCurrentAsync(this IDevice device, double current)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            var args = new Dictionary<string, object> { ["value"] = current };
            return device.ExecuteAsync("SetCurrent", args, null);
        }

        /// <summary>
        /// 设置恒压值
        /// </summary>
        /// <param name="device">电子负载实例</param>
        /// <param name="voltage">电压值 (V)</param>
        public static Task SetVoltageAsync(this IDevice device, double voltage)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            var args = new Dictionary<string, object> { ["value"] = voltage };
            return device.ExecuteAsync("SetVoltage", args, null);
        }

        /// <summary>
        /// 设置恒功率值
        /// </summary>
        /// <param name="device">电子负载实例</param>
        /// <param name="power">功率值 (W)</param>
        public static Task SetPowerAsync(this IDevice device, double power)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            var args = new Dictionary<string, object> { ["value"] = power };
            return device.ExecuteAsync("SetPower", args, null);
        }

        /// <summary>
        /// 开启负载
        /// </summary>
        public static Task LoadOnAsync(this IDevice device)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            return device.ExecuteAsync("LoadOn", null, null);
        }

        /// <summary>
        /// 关闭负载
        /// </summary>
        public static Task LoadOffAsync(this IDevice device)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            return device.ExecuteAsync("LoadOff", null, null);
        }

        /// <summary>
        /// 控制负载开关
        /// </summary>
        public static Task LoadAsync(this IDevice device, bool isOn)
        {
            return isOn ? LoadOnAsync(device) : LoadOffAsync(device);
        }

        /// <summary>
        /// 测量当前值
        /// </summary>
        public static async Task<double> MeasureAsync(this IDevice device)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            var result = await device.ExecuteAsync("Measure", null, null);
            return Convert.ToDouble(result.Value);
        }
    }
}
