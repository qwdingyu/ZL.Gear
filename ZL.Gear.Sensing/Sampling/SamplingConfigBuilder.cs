using System;
using ZL.Gear.Sensing.Dto;

namespace ZL.Gear.Sensing
{
    public class SamplingConfigBuilder<T>
    {
        private readonly SamplingConfig<T> _config;
        private bool _isTriggerSet = false; // 新增标志位
        private bool _isIntervalSet = false;
        private bool _isTimeoutSet = false;

        public SamplingConfigBuilder()
        {
            _config = new SamplingConfig<T>(); // 此处 _config 已包含默认值
        }

        public SamplingConfigBuilder<T> WithStrategy(ISamplingStrategy<T> strategy)
        {
            _config.Strategy = strategy;
            return this;
        }

        public SamplingConfigBuilder<T> WithTrigger(IExecutionTrigger<T> trigger)
        {
            _config.Trigger = trigger;
            _isTriggerSet = true; // 标记用户已设置
            return this;
        }

        public SamplingConfigBuilder<T> WithInterval(int intervalMs)
        {
            _config.SampleIntervalMs = intervalMs;
            _isIntervalSet = true;
            return this;
        }

        public SamplingConfigBuilder<T> WithTimeout(int timeoutMs)
        {
            _config.TotalTimeoutMs = timeoutMs;
            _isTimeoutSet = true;
            return this;
        }
        /// <summary>
        ///  设置测试激活后的采样时长（仅用于时长策略）。
        /// </summary>
        public SamplingConfigBuilder<T> WithActiveDuration(int durationMs)
        {
            _config.ActiveDurationMs = durationMs;
            _isTimeoutSet = true;
            return this;
        }
        /// <summary>
        /// 设置最终结果的规格检查器。
        /// </summary>
        public SamplingConfigBuilder<T> WithSpecCheck(Func<T, bool> specChecker)
        {
            _config.SpecChecker = specChecker;
            return this;
        }

        /// <summary>
        /// 设置单一样本的验证器。
        /// </summary>
        public SamplingConfigBuilder<T> WithPerSampleValidator(Func<T, bool> validator)
        {
            _config.PerSampleValidator = validator;
            return this;
        }
        // --- 事件通知配置 ---
        public SamplingConfigBuilder<T> OnTestStart(Action<T> callback)
        {
            _config.OnTestStarted = callback;
            return this;
        }
        public SamplingConfigBuilder<T> OnTestFinish(Action<ExeResult<T>> callback)
        {
            _config.OnTestFinished = callback;
            return this;
        }

        public SamplingConfigBuilder<T> OnSampleCollect(Action<T> callback)
        {
            _config.OnSampleCollected = callback;
            return this;
        }

        public SamplingConfigBuilder<T> WithLogger(Action<string> logger)
        {
            _config.Logger = logger;
            return this;
        }
        public SamplingConfig<T> Build()
        {
            if (_config.Strategy == null)
                throw new InvalidOperationException("构建配置失败: 必须通过 WithStrategy() 设置一个采样策略。");

            // [可选增强] 如果想在日志中明确是Builder设置的默认值还是Config自身的默认值
            // 但目前的方案已经足够清晰，此处保持简单
            return _config;
        }
    }

}
