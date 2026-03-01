using Newtonsoft.Json;
using System;

namespace ZL.Gear.Sensing.Dto
{
    public class SamplingConfigModel
    {
        public string Mode { get; set; } = "Duration"; // Duration, FixedCount, Continuous
        public int SampleCount { get; set; } = 1;
        public int TimeoutMs { get; set; } = 5000;
        public int SampleIntervalMs { get; set; } = 100;

        public string Calculator { get; set; } = "Average"; // Average, Max, Min, Last

        public TriggerConfigModel Trigger { get; set; }

        public ValidatorConfigModel Validator { get; set; }
        
        public string SpecCheck { get; set; } // 表达式，如 "Value > 0.0"
    }

    public class TriggerConfigModel
    {
        public string Type { get; set; } = "Immediate"; // Immediate, Threshold
        public string StartCondition { get; set; } // 如 ">= 0.1"
        public string StopCondition { get; set; } // 如 "< 0.05"
    }

    public class ValidatorConfigModel
    {
        public double? Min { get; set; }
        public double? Max { get; set; }
    }
}
