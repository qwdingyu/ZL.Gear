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
    /// 通用仪器设备扩展方法
    /// 提供设备通用操作接口
    /// 
    /// 使用方式：
    /// ```csharp
    /// await device.ResetAsync();
    /// var identity = await device.QueryIdentityAsync();
    /// ```
    /// 
    /// JSON 配置要求（遵循《Gear.NET 设备驱动配置规范》）：
    /// ```json
    /// {
    ///   "Commands": {
    ///     "Reset":      { "Template": "*RST" },
    ///     "Identity":   { "Template": "*IDN?", "Parser": "String" },
    ///     "Clear":      { "Template": "*CLS" },
    ///     "SelfTest":   { "Template": "*TST?", "Parser": "Int" }
    ///   }
    /// }
    /// ```
    /// </summary>
    public static class GenericInstrumentExtensions
    {
        /// <summary>
        /// 重置设备到默认状态
        /// </summary>
        /// <param name="device">设备实例</param>
        public static Task ResetAsync(this IDevice device)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            return device.ExecuteAsync("Reset", null, null);
        }

        /// <summary>
        /// 查询设备标识
        /// </summary>
        /// <param name="device">设备实例</param>
        /// <returns>设备标识字符串</returns>
        public static async Task<string> QueryIdentityAsync(this IDevice device)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            var result = await device.ExecuteAsync("Identity", null, null);
            return result.Value?.ToString();
        }

        /// <summary>
        /// 清除设备状态
        /// </summary>
        /// <param name="device">设备实例</param>
        public static Task ClearAsync(this IDevice device)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            return device.ExecuteAsync("Clear", null, null);
        }

        /// <summary>
        /// 执行自检
        /// </summary>
        /// <param name="device">设备实例</param>
        /// <returns>0 = 通过, 非0 = 失败</returns>
        public static async Task<int> SelfTestAsync(this IDevice device)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            var result = await device.ExecuteAsync("SelfTest", null, null);
            return Convert.ToInt32(result.Value);
        }

        /// <summary>
        /// 等待设备就绪
        /// </summary>
        /// <param name="device">设备实例</param>
        /// <param name="timeoutMs">超时时间 (毫秒)</param>
        public static async Task WaitForReadyAsync(this IDevice device, int timeoutMs = 5000)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            var startTime = DateTime.UtcNow;
            while (DateTime.UtcNow - startTime < TimeSpan.FromMilliseconds(timeoutMs))
            {
                try
                {
                    await device.ExecuteAsync("Status?", null, null);
                    return;
                }
                catch
                {
                    await Task.Delay(100);
                }
            }
            throw new TimeoutException("设备等待就绪超时");
        }
    }
}
