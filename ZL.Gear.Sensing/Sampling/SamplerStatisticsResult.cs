using System.Collections.Generic;

namespace ZL.Gear.Sensing
{
    public class SamplerStatisticsResult<T>
    {
        public string Channel { get; set; }
        public T Max { get; set; }
        public T Min { get; set; }
        public double Average { get; set; }
        public List<WindowSample<T>> Samples { get; set; }
        public int Count => Samples?.Count ?? 0;
        public bool Success { get; set; }
    }
}
