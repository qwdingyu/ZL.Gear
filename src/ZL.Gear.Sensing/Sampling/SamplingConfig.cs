using System;
using System.Diagnostics;
using System.Threading;
using ZL.Gear.Sensing.Dto;

namespace ZL.Gear.Sensing
{

    public class SamplingConfig<T>
    {
        // --- 默认值常量 ---
        private const int DEFAULT_INTERVAL_MS = 100;
        private const int DEFAULT_TOTAL_TIMEOUT_MS = 15000;

        // --- 属性 ---
        public ISamplingStrategy<T> Strategy { get; set; }
        public IExecutionTrigger<T> Trigger { get; set; }
        public int SampleIntervalMs { get; set; }
        public Func<int> DynamicIntervalProvider { get; set; }
        public int TotalTimeoutMs { get; set; }
        public int ActiveDurationMs { get; set; }
        public bool SaveAllSamples { get; set; } = true;

        public Func<T, bool> SpecChecker { get; set; }
        public Func<T, bool> PerSampleValidator { get; set; }
        public Action<T> OnTestStarted { get; set; }
        public Action<ExeResult<T>> OnTestFinished { get; set; }
        public Action<T> OnSampleCollected { get; set; }
        public Action<string> Logger { get; set; }

        /// <summary>
        /// 构造函数，用于设置默认值。
        /// </summary>
        public SamplingConfig()
        {
            Trigger = new ImmediateTrigger<T>();
            SampleIntervalMs = DEFAULT_INTERVAL_MS;
            TotalTimeoutMs = DEFAULT_TOTAL_TIMEOUT_MS;
            // 默认使用 FixedCountStrategy(10) - 采集10个样本后完成
            Strategy = new FixedCountStrategy<T>(10);
        }

        /// <summary>
        /// 验证配置的逻辑正确性，并生成一份详细的配置日志。
        /// 如果配置不合法，则会抛出 InvalidOperationException。
        /// </summary>
        /// <param name="stepName">当前测试步骤的名称，用于日志记录。</param>
        public void ValidateAndLogConfiguration(string stepName)
        {
            var logger = Logger ?? SensingLog.Default;
            var logBuilder = new System.Text.StringBuilder();

            logBuilder.AppendLine($"--- [{stepName}] 配置审查与生效参数 ---");

            // 1. 关键组件验证
            if (Strategy == null)
                throw new InvalidOperationException($"[{stepName}] 配置错误: 必须通过 WithStrategy() 提供一个采样策略。");

            logBuilder.AppendLine($" - 策略 (Strategy): {Strategy.StrategyName}");

            if (Trigger == null)
                throw new InvalidOperationException($"[{stepName}] 配置错误: 执行触发器(Trigger)不能为 null。");

            logBuilder.AppendLine($" - 触发器 (Trigger): {Trigger.GetType().Name}");

            // 2. 时间参数验证与日志记录
            if (SampleIntervalMs <= 0)
                throw new InvalidOperationException($"[{stepName}] 配置错误: 采样间隔 (SampleIntervalMs) 必须大于 0。");
            LogParameter(logBuilder, "采样间隔 (SampleIntervalMs)", SampleIntervalMs, "ms", DEFAULT_INTERVAL_MS);

            if (TotalTimeoutMs != Timeout.Infinite && TotalTimeoutMs <= 0)
                throw new InvalidOperationException($"[{stepName}] 配置错误: 总超时 (TotalTimeoutMs) 必须大于 0 或为 Infinite。");

            string timeoutStr = TotalTimeoutMs == Timeout.Infinite ? "Infinite (手动模式)" : $"{TotalTimeoutMs}ms";
            logBuilder.AppendLine($" - 总超时 (TotalTimeoutMs): {timeoutStr}");

            if (TotalTimeoutMs != Timeout.Infinite && TotalTimeoutMs <= SampleIntervalMs)
                throw new InvalidOperationException($"[{stepName}] 配置错误: 总超时({TotalTimeoutMs}ms) 必须大于采样间隔({SampleIntervalMs}ms)。");

            logBuilder.AppendLine($" - 动态参数调整 (DynamicInterval): {(DynamicIntervalProvider == null ? "禁用" : "启用")}");
            logBuilder.AppendLine($" - 内存流式保护 (SaveAllSamples): {SaveAllSamples}");

            // 3. 策略特定验证 (DurationStrategy 的时长是在策略内部，构造时已验证)
            // 此处可以添加更多未来策略的特定验证逻辑
            if (Strategy is DurationStrategy<T> durationStrategy)
            {
                logBuilder.AppendLine($"   - 策略详情: 期望持续时长 {durationStrategy.StrategyName}");
            }

            // 4. 可选委托的日志记录
            logBuilder.AppendLine($" - 规格检查 (SpecChecker): {(SpecChecker == null ? "未提供" : "已提供")}");
            logBuilder.AppendLine($" - 单样本验证器 (PerSampleValidator): {(PerSampleValidator == null ? "未提供" : "已提供")}");
            logBuilder.AppendLine($" - 事件:OnTestStarted: {(OnTestStarted == null ? "未订阅" : "已订阅")}");
            logBuilder.AppendLine($" - 事件:OnTestFinished: {(OnTestFinished == null ? "未订阅" : "已订阅")}");
            logBuilder.AppendLine($" - 事件:OnSampleCollected: {(OnSampleCollected == null ? "未订阅" : "已订阅")}");

            logBuilder.AppendLine("--------------------------------------------------");

            logger(logBuilder.ToString());
        }

        /// <summary>
        /// 辅助方法，用于格式化参数日志，并高亮显示默认值。
        /// </summary>
        private void LogParameter<TValue>(System.Text.StringBuilder builder, string name, TValue value, string unit, TValue defaultValue) where TValue : IEquatable<TValue>
        {
            string defaultValueMarker = value.Equals(defaultValue) ? " (默认值)" : "";
            builder.AppendLine($" - {name}: {value}{unit}{defaultValueMarker}");
        }
    }
}
