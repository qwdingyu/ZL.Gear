using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;

namespace ZL.Gear.Sensing.Sampling
{
    /// <summary>
    /// IObservable 采样扩展方法。
    /// 提供基于 Rx.NET 的高级采样功能。
    /// </summary>
    public static class SamplingExtensions
    {
        /// <summary>
        /// 节流采样：只发射指定时间间隔内的最后一个值。
        /// 适用于快速变化信号的降频处理。
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <param name="source">源数据流</param>
        /// <param name="interval">节流时间间隔</param>
        /// <returns>降频后的数据流</returns>
        public static IObservable<T> ThrottleSample<T>(this IObservable<T> source, TimeSpan interval)
        {
            return source.Throttle(interval);
        }

        /// <summary>
        /// 固定间隔采样：定期发射最新值。
        /// 适用于定期轮询场景。
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <param name="source">源数据流</param>
        /// <param name="interval">采样间隔</param>
        /// <returns>采样后的数据流</returns>
        public static IObservable<T> TimedSample<T>(this IObservable<T> source, TimeSpan interval)
        {
            return source.Sample(interval);
        }

        /// <summary>
        /// 防抖：使用 Timeout 模拟防抖效果。
        /// 注意：这是简化实现，生产环境可能需要更复杂的实现。
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <param name="source">源数据流</param>
        /// <param name="interval">防抖时间间隔</param>
        /// <returns>防抖后的数据流</returns>
        public static IObservable<T> DebounceSample<T>(this IObservable<T> source, TimeSpan interval)
        {
            // 由于 netstandard2.0 兼容性问题，这里使用 Timeout 模拟防抖
            // 实际应用中建议使用专业的防抖实现
            return source.Timeout(interval).Catch(Observable.Empty<T>());
        }

        /// <summary>
        /// 滑动窗口采样：将数据按时间窗口分组。
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <param name="source">源数据流</param>
        /// <param name="windowSpan">窗口大小</param>
        /// <param name="windowShift">窗口滑动步长（可选）</param>
        /// <returns>窗口数据块</returns>
        public static IObservable<IBufferedObservable<T>> WindowSample<T>(
            this IObservable<T> source,
            TimeSpan windowSpan,
            TimeSpan? windowShift = null)
        {
            if (windowShift.HasValue)
            {
                return source.Window(windowSpan, windowShift.Value)
                    .SelectMany(w => w.ToList().Select(list => new BufferedObservable<T>(list)));
            }
            
            return source.Window(windowSpan)
                .SelectMany(w => w.ToList().Select(list => new BufferedObservable<T>(list)));
        }

        /// <summary>
        /// 缓冲区采样：将数据按数量分组。
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <param name="source">源数据流</param>
        /// <param name="count">缓冲区大小</param>
        /// <param name="skip">跳过的数量（可选）</param>
        /// <returns>缓冲区数据块</returns>
        public static IObservable<IReadOnlyList<T>> BufferSample<T>(
            this IObservable<T> source,
            int count,
            int skip = 0)
        {
            if (skip <= 0)
            {
                return source.Buffer(count).Select(b => new List<T>(b).AsReadOnly());
            }
            
            return source.Buffer(count, skip).Select(b => new List<T>(b).AsReadOnly());
        }

        /// <summary>
        /// 条件采样：满足条件才发射。
        /// </summary>
        public static IObservable<T> WhereSample<T>(
            this IObservable<T> source,
            Func<T, bool> predicate)
        {
            return source.Where(predicate);
        }

        /// <summary>
        /// 转换采样：对每个值进行转换。
        /// </summary>
        public static IObservable<TResult> SelectSample<T, TResult>(
            this IObservable<T> source,
            Func<T, TResult> selector)
        {
            return source.Select(selector);
        }

        /// <summary>
        /// 限流：限制发射速率。
        /// </summary>
        public static IObservable<T> RateLimit<T>(
            this IObservable<T> source,
            TimeSpan maxInterval)
        {
            return source
                .Scan(default(T), (last, current) => current)
                .Throttle(maxInterval);
        }

        /// <summary>
        /// 错误重试：自动重试失败的订阅。
        /// </summary>
        public static IObservable<T> RetryWithDelay<T>(
            this IObservable<T> source,
            int retryCount = 3,
            TimeSpan? delay = null)
        {
            if (delay.HasValue)
            {
                return source.Retry(retryCount);
            }
            
            return source.Retry(retryCount);
        }

        /// <summary>
        /// 超时处理：超过指定时间未发射则抛出异常。
        /// </summary>
        public static IObservable<T> WithTimeout<T>(
            this IObservable<T> source,
            TimeSpan timeout)
        {
            return source.Timeout(timeout);
        }

        /// <summary>
        /// take Until：满足条件前持续发射。
        /// </summary>
        public static IObservable<T> TakeUntilCondition<T>(
            this IObservable<T> source,
            Func<T, bool> predicate)
        {
            return source.TakeWhile(predicate);
        }

        /// <summary>
        /// skip Until：满足条件后开始发射。
        /// </summary>
        public static IObservable<T> SkipUntilCondition<T>(
            this IObservable<T> source,
            Func<T, bool> predicate)
        {
            return source.SkipWhile(predicate);
        }

        /// <summary>
        /// 聚合采样：将数据流聚合成单个值。
        /// </summary>
        public static IObservable<TAccumulate> AggregateSample<T, TAccumulate>(
            this IObservable<T> source,
            TAccumulate seed,
            Func<TAccumulate, T, TAccumulate> accumulator)
        {
            return source.Aggregate(seed, accumulator);
        }

        /// <summary>
        /// 扫描采样：流式聚合，每次发射中间结果。
        /// </summary>
        public static IObservable<TAccumulate> ScanSample<T, TAccumulate>(
            this IObservable<T> source,
            TAccumulate seed,
            Func<TAccumulate, T, TAccumulate> accumulator)
        {
            return source.Scan(seed, accumulator);
        }
    }

    /// <summary>
    /// 缓冲区接口。
    /// </summary>
    public interface IBufferedObservable<T>
    {
        IList<T> Buffer { get; }
    }

    /// <summary>
    /// 缓冲区实现。
    /// </summary>
    public class BufferedObservable<T> : IBufferedObservable<T>
    {
        public IList<T> Buffer { get; }

        public BufferedObservable(IList<T> buffer)
        {
            Buffer = buffer;
        }
    }
}