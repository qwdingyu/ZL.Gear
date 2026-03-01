using System;

namespace ZL.Gear.Sensing
{
    /// <summary>
    /// 定义执行流程触发器的接口，决定测试何时开始和结束。
    /// </summary>
    /// <typeparam name="T">数据类型</typeparam>
    public interface IExecutionTrigger<T>
    {
        /// <summary>
        /// 根据当前样本值和测试状态，判断是否应该启动测试。
        /// </summary>
        /// <param name="sample">当前读取到的样本。</param>
        /// <param name="isTestActive">当前测试是否已经激活。</param>
        /// <returns>如果应该启动，则返回 true。</returns>
        bool ShouldStart(T sample, bool isTestActive);

        /// <summary>
        /// 根据当前样本值和测试状态，判断是否应该停止测试。
        /// </summary>
        /// <param name="sample">当前读取到的样本。</param>
        /// <param name="isTestActive">当前测试是否已经激活。</param>
        /// <returns>如果应该停止，则返回 true。</returns>
        bool ShouldStop(T sample, bool isTestActive);

        void Reset(); // 所有触发器都应支持重置
    }

    /// <summary>
    /// 立即触发器：测试一开始就处于激活状态。
    /// </summary>
    public class ImmediateTrigger<T> : IExecutionTrigger<T>
    {
        public bool ShouldStart(T sample, bool isTestActive) => !isTestActive;
        public bool ShouldStop(T sample, bool isTestActive) => false; // 从不主动停止
        public void Reset() { /* 无状态，无需操作 */ }
    }
    /// <summary>
    /// 单一条件满足即可，用于场景：安全带锁扣插入
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class ConditionalTrigger<T> : IExecutionTrigger<T>
    {
        private readonly Func<T, bool> _startCondition;
        private readonly Func<T, bool> _stopCondition;
        private bool _startConditionMet = false;

        public ConditionalTrigger(Func<T, bool> startCondition, Func<T, bool> stopCondition = null)
        {
            _startCondition = startCondition ?? throw new ArgumentNullException(nameof(startCondition));
            // 停止条件是可选的
            _stopCondition = stopCondition;
        }

        public bool ShouldStart(T sample, bool isTestActive)
        {
            // 如果测试未激活且启动条件首次满足
            if (!isTestActive && !_startConditionMet && _startCondition(sample))
            {
                _startConditionMet = true; // 标记条件已满足
                return true;
            }
            return false;
        }

        public bool ShouldStop(T sample, bool isTestActive)
        {
            // 只有在测试已激活且定义了停止条件时，才检查
            return isTestActive && _stopCondition?.Invoke(sample) == true;
        }

        // 用于在外部取消或重置时，复位触发器状态
        public void Reset()
        {
            _startConditionMet = false;
        }
    }

    /// <summary>
    /// 一个更强大的触发器，要求条件连续满足指定次数后才启动或停止。
    /// 这对于过滤掉瞬时噪声或意外信号非常有效。
    /// </summary>
    public class ConsecutiveTrigger<T> : IExecutionTrigger<T>
    {
        private readonly Func<T, bool> _startCondition;
        private readonly Func<T, bool> _stopCondition;
        private readonly int _samplesToStart;
        private readonly int _samplesToStop;

        private int _consecutiveStartCount = 0;
        private int _consecutiveStopCount = 0;

        /// <summary>
        /// 创建一个连续条件触发器。
        /// </summary>
        /// <param name="startCondition">启动测试必须满足的条件。</param>
        /// <param name="stopCondition">停止测试必须满足的条件（可选）。</param>
        /// <param name="samplesToStart">需要连续多少个样本满足 startCondition 才启动测试。</param>
        /// <param name="samplesToStop">需要连续多少个样本满足 stopCondition 才停止测试（可选）。</param>
        public ConsecutiveTrigger(
            Func<T, bool> startCondition,
            Func<T, bool> stopCondition = null,
            int samplesToStart = 1,
            int samplesToStop = 1)
        {
            _startCondition = startCondition ?? throw new ArgumentNullException(nameof(startCondition));
            _stopCondition = stopCondition; // 停止条件可以为 null
            _samplesToStart = Math.Max(1, samplesToStart);
            _samplesToStop = Math.Max(1, samplesToStop);
        }

        public bool ShouldStart(T sample, bool isTestActive)
        {
            if (isTestActive) { return false; }

            if (_startCondition(sample))
            {
                _consecutiveStartCount++;
            }
            else
            {
                // 任何一次不满足，计数器都清零
                _consecutiveStartCount = 0;
            }
            return _consecutiveStartCount >= _samplesToStart;
        }

        public bool ShouldStop(T sample, bool isTestActive)
        {
            // 测试未激活或未定义停止条件，则不判断停止
            if (!isTestActive || _stopCondition == null) { return false; }

            if (_stopCondition(sample))
            {
                _consecutiveStopCount++;
            }
            else
            {
                _consecutiveStopCount = 0;
            }

            return _consecutiveStopCount >= _samplesToStop;
        }

        public void Reset()
        {
            _consecutiveStartCount = 0;
            _consecutiveStopCount = 0;
        }
    }
}
