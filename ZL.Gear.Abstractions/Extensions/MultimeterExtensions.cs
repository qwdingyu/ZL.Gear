using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Devices.Abstractions;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Abstractions.Extensions
{
    /// <summary>
    /// 万用表设备扩展方法
    /// 提供类型安全的万用表操作接口
    /// 
    /// 使用方式：
    /// ```csharp
    /// var voltage = await device.MeasureVoltageAsync();
    /// var resistance = await device.MeasureResistanceAsync();
    /// ```
    /// 
    /// JSON 配置要求（遵循《Gear.NET 设备驱动配置规范》）：
    /// ```json
    /// {
    ///   "Commands": {
    ///     "MeasureVoltage":   { "Template": "VOLT?", "Parser": "Double" },
    ///     "MeasureCurrent":   { "Template": "CURR?", "Parser": "Double" },
    ///     "MeasureResistance":{ "Template": "RES?", "Parser": "Double" },
    ///     "Measure":          { "Template": "MEAS?", "Parser": "Double" }
    ///   }
    /// }
    /// ```
    /// </summary>
    public static class MultimeterExtensions
    {
        /// <summary>
        /// 测量电压
        /// </summary>
        /// <param name="device">万用表设备实例</param>
        /// <returns>电压值 (V)</returns>
        public static async Task<double> MeasureVoltageAsync(this IDevice device)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            var result = await device.ExecuteAsync("MeasureVoltage", null, null);
            return Convert.ToDouble(result.Value);
        }

        /// <summary>
        /// 测量电流
        /// </summary>
        /// <param name="device">万用表设备实例</param>
        /// <returns>电流值 (A)</returns>
        public static async Task<double> MeasureCurrentAsync(this IDevice device)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            var result = await device.ExecuteAsync("MeasureCurrent", null, null);
            return Convert.ToDouble(result.Value);
        }

        /// <summary>
        /// 测量电阻
        /// </summary>
        /// <param name="device">万用表设备实例</param>
        /// <returns>电阻值 (Ω)</returns>
        public static async Task<double> MeasureResistanceAsync(this IDevice device)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            var result = await device.ExecuteAsync("MeasureResistance", null, null);
            return Convert.ToDouble(result.Value);
        }

        /// <summary>
        /// 通用测量
        /// </summary>
        /// <param name="device">万用表设备实例</param>
        /// <returns>测量值</returns>
        public static async Task<double> MeasureAsync(this IDevice device)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            var result = await device.ExecuteAsync("Measure", null, null);
            return Convert.ToDouble(result.Value);
        }
    }
}
