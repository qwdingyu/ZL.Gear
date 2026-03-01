using System;
using System.Collections.Generic;

namespace ZL.Gear.Core.Models
{
    /// <summary>
    /// 为 StepContext 提供常用工业参数的强类型访问扩展。
    /// 内核演进：通过扩展方法提供“语义化”读取，内置类型转换和异常分级处理。
    /// </summary>
    public static class StepContextExtensions
    {
        public static double GetLcl(this StepContext context, double defaultValue = 0)
            => context.Get<double>("LCL", defaultValue);

        public static double GetUcl(this StepContext context, double defaultValue = double.MaxValue)
            => context.Get<double>("UCL", defaultValue);

        public static string GetUnit(this StepContext context, string defaultValue = "")
            => context.Get<string>("Unit", defaultValue);

        public static double GetOffset(this StepContext context, double defaultValue = 0)
            => context.Get<double>("Offset", defaultValue);

        public static int GetDurationMs(this StepContext context, int defaultValue = 5000)
            => context.Get<int>("DurationMs", defaultValue);

        /// <summary>
        /// 获取当前步骤的主测量键
        /// </summary>
        public static string GetMeasurementKey(this StepContext context)
            => context.Get<string>("MeasurementKey", context.StepConfig.StepName);

        /// <summary>
        /// 核心：强类型获取信号值或变量。
        /// 这里的“信号值”可能来自本步参数、运行时变量池或全局上下文。
        /// </summary>
        public static T GetSignalValue<T>(this StepContext context, string key, T defaultValue = default)
            => context.Get<T>(key, defaultValue);

        /// <summary>
        /// 便捷获取 Double 类型的信号值，内置异常兼容。
        /// </summary>
        public static double GetDouble(this StepContext context, string key, double defaultValue = 0)
        {
            var val = context.Get<object>(key);
            if (val == null || string.IsNullOrEmpty(val.ToString())) return defaultValue;
            return double.TryParse(val.ToString(), out var result) ? result : defaultValue;
        }

        public static bool GetBool(this StepContext context, string key, bool defaultValue = false)
            => context.Get<bool>(key, defaultValue);

        public static int GetInt(this StepContext context, string key, int defaultValue = 0)
            => context.Get<int>(key, defaultValue);
    }
}
