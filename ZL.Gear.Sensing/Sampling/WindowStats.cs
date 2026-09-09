using System;
using System.Collections.Generic;

namespace ZL.Gear.Sensing
{
    public class SensingWindowStats
    {
        public long Count { get; set; }
        public double Sum { get; set; }
        public double Min { get; set; } = double.MinValue;
        public double Max { get; set; } = double.MinValue;
        public List<SensingWindowSample> Samples { get; set; } = new List<SensingWindowSample>();
        public long LastTicks { get; set; }  // ★ 新增：最后一个样本的时间戳
        public long FirstTicks { get; set; } // ★ 可选：第一个样本的时间戳

        public int DefaultPrecision { get; set; } = 2;

        public double Avg { get { return Count > 0 ? Math.Round(Sum / Count, DefaultPrecision) : 0.0; } }

        public double PkPk => Count > 0 ? Max - Min : 0;
        public SensingWindowStats Clone(bool includeSamples)
        {
            var copy = new SensingWindowStats();
            copy.Count = this.Count;
            copy.Sum = this.Sum;
            copy.Min = this.Min;
            copy.Max = this.Max;
            copy.LastTicks = this.LastTicks;
            copy.FirstTicks = this.FirstTicks;
            DefaultPrecision = this.DefaultPrecision;
            copy.Samples = includeSamples ? new List<SensingWindowSample>(this.Samples) : new List<SensingWindowSample>();
            return copy;
        }
        public void ResetCounters()
        {
            Count = 0;
            Sum = 0;
            Max = double.MinValue;
            Min = double.MaxValue;
            FirstTicks = 0;
            LastTicks = 0;
        }
    }

    public sealed class SensingWindowState
    {
        public readonly object Sync = new();
        public bool Active;
        public List<SensingWindowSample> AllSamples { get; } = new List<SensingWindowSample>();
    }

}
