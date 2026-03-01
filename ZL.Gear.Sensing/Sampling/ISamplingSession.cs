using System;

namespace ZL.Gear.Sensing
{

    /// <summary>
    /// 代表一个独立的、有始有终的采样会话。
    /// 必须通过 using 语句或手动调用 Dispose() 来结束。
    /// </summary>
    [Obsolete("Use IMeasurable or UniversalMeasurer instead.")]
    public interface ISamplingSession<T> : IDisposable where T : IComparable<T>
    {
        /// <summary>
        /// 获取本次采样会话的统计结果。
        /// 只有在会话结束后（Dispose被调用后），这个结果才被最终计算和确定。
        /// </summary>
        SamplerStatisticsResult<T> Result { get; }
    }
}
