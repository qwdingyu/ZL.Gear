using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Devices.Abstractions;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Abstractions.Extensions
{
    /// <summary>
    /// 电阻测量仪扩展方法
    /// 提供类型安全的电阻测量操作接口
    /// 
    /// 使用方式：
    /// ```csharp
    /// var resistance = await device.MeasureResistanceAsync();
    /// ```
    /// 
    /// JSON 配置要求（遵循《Gear.NET 设备驱动配置规范》）：
    /// ```json
    /// {
    ///   "Commands": {
    ///     "Measure":        { "Template": "MEAS:RES?", "Parser": "Double" },
    ///     "Measure4Wire":   { "Template": "MEAS:RES4?", "Parser": "Double" },
    ///     "SetRange":       { "Template": "CONF:RES {range}", "Param": "range" },
    ///     "AutoRange":      { "Template": "CONF:RES AUTO" }
    ///   }
    /// }
    /// ```
    /// </summary>
    public static class ResistanceMeterExtensions
    {
        /// <summary>
        /// 测量电阻
        /// </summary>
        /// <param name="device">电阻测量仪实例</param>
        /// <returns>电阻值 (Ω)</returns>
        public static async Task<double> MeasureResistanceAsync(this IDevice device)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            var result = await device.ExecuteAsync("Measure", null, null);
            return Convert.ToDouble(result.Value);
        }

        /// <summary>
        /// 四线法测量电阻（高精度）
        /// </summary>
        /// <param name="device">电阻测量仪实例</param>
        /// <returns>电阻值 (Ω)</returns>
        public static async Task<double> Measure4WireResistanceAsync(this IDevice device)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            var result = await device.ExecuteAsync("Measure4Wire", null, null);
            return Convert.ToDouble(result.Value);
        }

        /// <summary>
        /// 设置测量范围
        /// </summary>
        /// <param name="device">电阻测量仪实例</param>
        /// <param name="range">范围值 (Ω)</param>
        public static Task SetRangeAsync(this IDevice device, double range)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            var args = new Dictionary<string, object> { ["range"] = range };
            return device.ExecuteAsync("SetRange", args, null);
        }

        /// <summary>
        /// 启用自动量程
        /// </summary>
        /// <param name="device">电阻测量仪实例</param>
        public static Task AutoRangeAsync(this IDevice device)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            return device.ExecuteAsync("AutoRange", null, null);
        }
    }
}
