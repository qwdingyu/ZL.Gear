using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Devices.Abstractions;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Abstractions.Extensions
{
    /// <summary>
    /// 电源设备扩展方法
    /// 提供类型安全的电源操作接口
    /// 
    /// 使用方式：
    /// ```csharp
    /// await device.SetVoltageAsync(12.0);
    /// await device.OutputAsync(true);
    /// ```
    /// 
    /// JSON 配置要求（遵循《Gear.NET 设备驱动配置规范》）：
    /// ```json
    /// {
    ///   "Commands": {
    ///     "SetVoltage": { "Template": "VOLT {value}" },
    ///     "SetCurrent": { "Template": "CURR {value}" },
    ///     "OutputOn":   { "Template": "OUTP 1" },
    ///     "OutputOff":  { "Template": "OUTP 0" },
    ///     "Query":      { "Template": "MEAS?", "Parser": "Double" }
    ///   }
    /// }
    /// ```
    /// </summary>
    public static class PowerSupplyExtensions
    {
        /// <summary>
        /// 设置电压
        /// </summary>
        /// <param name="device">电源设备实例</param>
        /// <param name="voltage">电压值 (V)</param>
        public static Task SetVoltageAsync(this IDevice device, double voltage)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            var args = new Dictionary<string, object> { ["value"] = voltage };
            return device.ExecuteAsync("SetVoltage", args, null);
        }

        /// <summary>
        /// 设置电流
        /// </summary>
        /// <param name="device">电源设备实例</param>
        /// <param name="current">电流值 (A)</param>
        public static Task SetCurrentAsync(this IDevice device, double current)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            var args = new Dictionary<string, object> { ["value"] = current };
            return device.ExecuteAsync("SetCurrent", args, null);
        }

        /// <summary>
        /// 控制输出开关
        /// </summary>
        /// <param name="device">电源设备实例</param>
        /// <param name="isOn">true = 开启输出, false = 关闭输出</param>
        public static Task OutputAsync(this IDevice device, bool isOn)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            var command = isOn ? "OutputOn" : "OutputOff";
            return device.ExecuteAsync(command, null, null);
        }

        /// <summary>
        /// 开启电源输出
        /// </summary>
        public static Task OutputOnAsync(this IDevice device)
        {
            return OutputAsync(device, true);
        }

        /// <summary>
        /// 关闭电源输出
        /// </summary>
        public static Task OutputOffAsync(this IDevice device)
        {
            return OutputAsync(device, false);
        }

        /// <summary>
        /// 查询当前电压
        /// </summary>
        /// <param name="device">电源设备实例</param>
        /// <returns>当前电压值 (V)</returns>
        public static async Task<double> QueryVoltageAsync(this IDevice device)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            var result = await device.ExecuteAsync("Query", null, null);
            return Convert.ToDouble(result.Value);
        }

        /// <summary>
        /// 查询当前电流
        /// </summary>
        /// <param name="device">电源设备实例</param>
        /// <returns>当前电流值 (A)</returns>
        public static async Task<double> QueryCurrentAsync(this IDevice device)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            var result = await device.ExecuteAsync("QueryCurrent", null, null);
            return Convert.ToDouble(result.Value);
        }
    }
}
