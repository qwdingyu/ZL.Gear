using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ZL.Gear.Sensing
{
    public static class SamplingConstants
    {
        public static int Precision = 3; // 变量名遵循.NET规范，首字母大写
    }

    #region 结果计算器接口与实现
    /*
     // 以前 (只能平均值或最后一个)
    builder.WithStrategy(new DurationStrategy<double>(TimeSpan.FromSeconds(5)));

    // 现在 (可以自由组合)

    // 场景1：在5秒内持续采样，最终结果取所有样本的【最大值】
    builder.WithStrategy(new DurationStrategy<double>(
        TimeSpan.FromSeconds(5), 
        new MaxCalculator<double>()
    ));

    // 场景2：采集10个有效样本，最终结果取【最小值】
    builder.WithStrategy(new FixedCountStrategy<double>(
        10, 
        new MinCalculator<double>()
    ));

    // 场景3：在10秒内采样，最终结果取【平均值】 (这是默认行为，可以不传)
    builder.WithStrategy(new DurationStrategy<double>(
        TimeSpan.FromSeconds(10), 
        new AverageCalculator<double>() // 或者直接传 null，也会默认使用 AverageCalculator
    ));

    // 场景4: 采集20个样本，最终结果取【最后一个值】
    builder.WithStrategy(new FixedCountStrategy<double>(
        20, 
        new LastValueCalculator<double>()
    ));
 
     */
    /// <summary>
    /// 【新接口】定义了如何从样本集合中计算最终结果。
    /// </summary>
    public interface IResultCalculator<T>
    {
        /// <summary>
        /// 获取计算器的名称，用于日志记录。
        /// </summary>
        string Name { get; }
        /// <summary>
        /// 从样本集合中计算最终结果。
        /// </summary>
        T Calculate(IReadOnlyList<T> samples);
    }

    /// <summary>
    /// 计算数值类型样本的平均值。
    /// </summary>
    public class AverageCalculator<T> : IResultCalculator<T>
    {
        public string Name => "平均值";
        public T Calculate(IReadOnlyList<T> samples)
        {
            if (samples == null || !samples.Any()) return default;
            if (typeof(T) == typeof(double) || typeof(T) == typeof(float) || typeof(T) == typeof(decimal) || typeof(T) == typeof(int))
            {
                var values = samples.Select(x => Convert.ToDouble(x, CultureInfo.InvariantCulture));
                return (T)Convert.ChangeType(Math.Round(values.Average(), SamplingConstants.Precision), typeof(T));
            }
            throw new InvalidOperationException("AverageCalculator 仅支持数值类型。对于其他类型，请使用 LastValueCalculator 或自定义实现。");
        }
    }

    /// <summary>
    /// 获取数值类型样本的最大值。
    /// </summary>
    public class MaxCalculator<T> : IResultCalculator<T> where T : IComparable<T>
    {
        public string Name => "最大值";
        public T Calculate(IReadOnlyList<T> samples)
        {
            if (samples == null || !samples.Any()) return default;
            return samples.Max();
        }
    }

    /// <summary>
    /// 获取数值类型样本的最小值。
    /// </summary>
    public class MinCalculator<T> : IResultCalculator<T> where T : IComparable<T>
    {
        public string Name => "最小值";
        public T Calculate(IReadOnlyList<T> samples)
        {
            if (samples == null || !samples.Any()) return default;
            return samples.Min();
        }
    }

    /// <summary>
    /// 获取样本集合中的最后一个值。
    /// </summary>
    public class LastValueCalculator<T> : IResultCalculator<T>
    {
        public string Name => "最后一个值";
        public T Calculate(IReadOnlyList<T> samples)
        {
            if (samples == null || !samples.Any()) return default;
            return samples.Last();
        }
    }
    /// <summary>
    /// 获取样本集合中的中位数。针对工业干扰大的环境非常有效。
    /// </summary>
    public class MedianCalculator<T> : IResultCalculator<T> where T : IComparable<T>
    {
        public string Name => "中位数";
        public T Calculate(IReadOnlyList<T> samples)
        {
            if (samples == null || !samples.Any()) return default;
            var sorted = samples.OrderBy(x => x).ToList();
            int count = sorted.Count;
            if (count % 2 == 0)
            {
                // 简化处理：偶数个取中间左侧
                return sorted[count / 2 - 1];
            }
            return sorted[count / 2];
        }
    }

    /// <summary>
    /// 计算样本的标准差。用于评估产线稳定性 (GR&R)。
    /// </summary>
    public class StdDevCalculator<T> : IResultCalculator<T>
    {
        public string Name => "标准差";
        public T Calculate(IReadOnlyList<T> samples)
        {
            if (samples == null || samples.Count < 2) return default;
            var values = samples.Select(x => Convert.ToDouble(x, CultureInfo.InvariantCulture)).ToList();
            double avg = values.Average();
            double sum = values.Sum(v => Math.Pow(v - avg, 2));
            double stdDev = Math.Sqrt(sum / (values.Count - 1));
            return (T)Convert.ChangeType(Math.Round(stdDev, SamplingConstants.Precision + 2), typeof(T));
        }
    }
    #endregion

    /// <summary>
    /// 【已修改】定义采样策略的接口，决定如何收集样本以及何时完成。
    /// </summary>
    public interface ISamplingStrategy<T>
    {
        string StrategyName { get; }
        (bool isDone, bool shouldCollect) ProcessSample(T sample, IReadOnlyList<T> collectedSamples);

        /// <summary>
        /// 【已修改】现在它依赖 IResultCalculator 来完成计算。
        /// </summary>
        T CalculateResult(IReadOnlyList<T> samples);
    }

    /// <summary>
    /// 抽象基类，封装对 IResultCalculator 的通用依赖。
    /// </summary>
    public abstract class SamplingStrategyBase<T> : ISamplingStrategy<T>
    {
        protected readonly IResultCalculator<T> ResultCalculator;
        public abstract string StrategyName { get; }

        protected SamplingStrategyBase(IResultCalculator<T> resultCalculator)
        {
            // 如果未提供计算器，则根据类型T选择一个智能的默认值
            if (resultCalculator == null)
            {
                if (typeof(IComparable<T>).IsAssignableFrom(typeof(T)))
                {
                    // 对于数值和可比较类型，默认使用平均值
                    this.ResultCalculator = new AverageCalculator<T>();
                }
                else
                {
                    // 对于其他类型（如字符串、自定义对象），默认返回最后一个
                    this.ResultCalculator = new LastValueCalculator<T>();
                }
            }
            else
            {
                this.ResultCalculator = resultCalculator;
            }
        }

        public abstract (bool isDone, bool shouldCollect) ProcessSample(T sample, IReadOnlyList<T> collectedSamples);

        public T CalculateResult(IReadOnlyList<T> samples) => ResultCalculator.Calculate(samples);
    }


    /// <summary>
    /// 快速通过策略：只要单个样本满足通过条件，采样立即完成。
    /// </summary>
    public class QuickPassStrategy<T> : SamplingStrategyBase<T>
    {
        private readonly Func<T, bool> _passCondition;
        public override string StrategyName => $"快速通过(计算:{ResultCalculator.Name})";
        public QuickPassStrategy(Func<T, bool> passCondition) : base(new LastValueCalculator<T>()) // 快速通过通常只关心那个通过的值
        {
            _passCondition = passCondition ?? throw new ArgumentNullException(nameof(passCondition));
        }
        public override (bool isDone, bool shouldCollect) ProcessSample(T sample, IReadOnlyList<T> collectedSamples)
        {
            bool passed = _passCondition(sample);
            return (passed, passed);
        }
    }

    /// <summary>
    /// 【已修改】固定次数策略：采集到指定数量的有效样本后完成。
    /// </summary>
    public class FixedCountStrategy<T> : SamplingStrategyBase<T>
    {
        private readonly int _requiredCount;
        public override string StrategyName => $"固定次数({_requiredCount}, 计算:{ResultCalculator.Name})";

        public FixedCountStrategy(int count, IResultCalculator<T> resultCalculator = null)
            : base(resultCalculator) // 将 resultCalculator 传递给基类
        {
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count), "采样次数必须大于0。");
            _requiredCount = count;
        }

        public override (bool isDone, bool shouldCollect) ProcessSample(T sample, IReadOnlyList<T> collectedSamples)
        {
            bool shouldCollect = true;
            bool isDone = collectedSamples.Count + 1 >= _requiredCount;
            return (isDone, shouldCollect);
        }
    }

    /// <summary>
    /// 【已修改】时长策略：在测试激活后，持续采样直到达到指定时长。
    /// </summary>
    public class DurationStrategy<T> : SamplingStrategyBase<T>
    {
        private readonly TimeSpan _duration;
        private DateTime? _startTime;
        public double DurationMs { get { return _duration.TotalMilliseconds; } }
        public override string StrategyName => $"时长({_duration.TotalSeconds:F1}s, 计算:{ResultCalculator.Name})";

        public DurationStrategy(TimeSpan duration, IResultCalculator<T> resultCalculator = null)
            : base(resultCalculator) // 将 resultCalculator 传递给基类
        {
            if (duration.TotalMilliseconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(duration), "时长必须为正。");
            _duration = duration;
        }

        public override (bool isDone, bool shouldCollect) ProcessSample(T sample, IReadOnlyList<T> collectedSamples)
        {
            _startTime ??= DateTime.UtcNow;
            bool isDone = (DateTime.UtcNow - _startTime.Value) >= _duration;
            return (isDone, !isDone); // 当未完成时收集样本
        }
    }
}
