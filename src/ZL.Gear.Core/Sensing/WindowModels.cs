using System;
using System.Collections.Generic;

namespace ZL.Gear.Sensing
{
    /// <summary>
    /// 表示采样窗口中的单次样本数据。
    /// </summary>
    public class WindowSample
    {
        public double Value { get; set; }
        public long Ticks { get; set; }
        public string SessionId { get; set; }
    }

    /// <summary>
    /// 统计结果模型。
    /// </summary>
    public class WindowStats
    {
        public int Count { get; set; }
        public double Sum { get; set; }
        public double Min { get; set; } = double.MaxValue;
        public double Max { get; set; } = double.MinValue;
        public long FirstTicks { get; set; }
        public long LastTicks { get; set; }
        public double Average => Count > 0 ? Sum / Count : 0;
        public List<WindowSample> Samples { get; set; } = new List<WindowSample>();
    }

    /// <summary>
    /// 采样窗口的运行状态。
    /// </summary>
    public class WindowState
    {
        public bool Active { get; set; }
        public List<WindowSample> AllSamples { get; } = new List<WindowSample>();
        public object Sync { get; } = new object();
    }
}
