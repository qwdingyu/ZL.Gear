using System;

namespace ZL.Gear.Core.Models
{
    /// <summary>
    /// 采样模式
    /// </summary>
    public enum SamplingMode
    {
        /// <summary>
        /// 单次即时采样（默认）
        /// </summary>
        Single,

        /// <summary>
        /// 定时采样（采集指定时间段）
        /// </summary>
        Duration,

        /// <summary>
        /// 定数采样（采集指定数量的点）
        /// </summary>
        FixedCount,

        /// <summary>
        /// 持续采样（通常由触发器控制开关）
        /// </summary>
        Continuous,

        /// <summary>
        /// 条件等待采集：采样直到满足特定条件停止
        /// </summary>
        WaitCondition,

        /// <summary>
        /// 边沿/状态切换检测：捕捉 0->1 或 1->0 的瞬间
        /// </summary>
        Toggle,

        /// <summary>
        /// 稳定性监控：验证数值在指定时间内是否保持在门限内
        /// </summary>
        Stability
    }

    /// <summary>
    /// 采样数据处理算子
    /// </summary>
    public enum SamplingCalculator
    {
        None,
        Last,
        First,
        Max,
        Min,
        Average,
        PkPk,       // 峰峰值
        Count,
        Sum,
        Delta,      // 差值 (Last - First)
        Presence    // 是否曾出现过 (布尔)
    }

    /// <summary>
    /// 采样配置对象，用于定义瑞士军刀的“如何采样”。
    /// </summary>
    public class SamplingConfig
    {
        public SamplingMode Mode { get; set; } = SamplingMode.Single;
        
        public SamplingCalculator Calculator { get; set; } = SamplingCalculator.Last;

        /// <summary>
        /// 采样间隔 (毫秒)
        /// </summary>
        public int IntervalMs { get; set; } = 100;

        /// <summary>
        /// 采样总量 (秒或个数，取决于 Mode)
        /// </summary>
        public double Quantity { get; set; } = 0;

        /// <summary>
        /// 采样触发配置（可选）
        /// </summary>
        public TriggerConfig Trigger { get; set; }

        /// <summary>
        /// 过滤门限/验证配置（可选）
        /// </summary>
        public ValidationConfig Validation { get; set; }

        /// <summary>
        /// 【实时推送】每采集到一个有效点时的回调
        /// </summary>
        public Action<double> OnSampleCollected { get; set; }
    }

    public class TriggerConfig
    {
        public string Type { get; set; } // "Threshold", "Signal"
        public string StartCondition { get; set; } // "> 0.5"
        public string StopCondition { get; set; } // "< 0.1"
    }

    public class ValidationConfig
    {
        public double? MinValue { get; set; }
        public double? MaxValue { get; set; }
        public bool IgnoreInvalid { get; set; } = true;
    }
}
