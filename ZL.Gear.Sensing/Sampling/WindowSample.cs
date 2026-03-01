using System.Diagnostics;

namespace ZL.Gear.Sensing
{
    /// <summary>
    /// 单个样本（值 + 时间戳）
    /// </summary>
    public sealed class WindowSample
    {
        public long Ticks;   // Stopwatch.GetTimestamp()
        public double Value;
        public string SessionId;
    }
    public sealed class WindowSample<T>
    {
        public long Ticks { get; set; }
        public T Value { get; set; }
        public string SessionId { get; set; }
        public WindowSample(T value, string sessionId)
        {
            Value = value;
            Ticks = Stopwatch.GetTimestamp();
            SessionId = sessionId;
        }

    }
}
