using System;

namespace ZL.Gear.Core.Infrastructure
{
    /// <summary>
    /// 可观测性指标接口（轻量级，.NET Standard 2.0 兼容）。
    /// 用于记录步骤执行时长、成功率、错误数等指标。
    /// </summary>
    public interface IMetrics
    {
        /// <summary>
        /// 记录指标值。
        /// </summary>
        /// <param name="name">指标名称，如 "step.duration"。</param>
        /// <param name="value">指标值。</param>
        /// <param name="tags">标签键值对，如 (command, "PlcWrite")。</param>
        void Record(string name, double value, params (string Key, string Value)[] tags);
    }
}
