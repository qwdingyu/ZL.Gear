using System;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Sensing.Samplers
{
    /// <summary>
    /// 基于 Throttle 的智能采样器（针对 Measurement 类型）。
    /// 对于快速变化的信号，只输出最后一个值，减少噪声影响。
    /// 适用于需要防抖的场景，如按钮去抖、传感器信号平滑等。
    /// </summary>
    public class ThrottlingSampler : ISensorSampler
    {
        private readonly Subject<Measurement> _input = new();
        private readonly IObservable<Measurement> _throttledStream;
        private readonly Subject<Measurement> _output = new();
        private CancellationTokenSource _cts;
        private IDisposable _subscription;

        public string Name { get; }
        
        /// <inheritdoc />
        public IObservable<Measurement> DataStream => _output.AsObservable();

        /// <summary>
        /// 获取原始数据流（未降频）。
        /// </summary>
        public IObservable<Measurement> RawStream => _input.AsObservable();

        /// <summary>
        /// 创建 ThrottlingSampler 实例。
        /// </summary>
        /// <param name="name">采样器名称</param>
        /// <param name="throttleInterval">节流时间间隔</param>
        public ThrottlingSampler(string name, TimeSpan throttleInterval)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            
            // 使用 Throttle：只发射指定时间内的最后一个值
            _throttledStream = _input.Throttle(throttleInterval, DefaultScheduler.Instance);
        }

        /// <summary>
        /// 创建 ThrottlingSampler 实例（带尾值发射）。
        /// </summary>
        /// <param name="name">采样器名称</param>
        /// <param name="throttleInterval">节流时间间隔</param>
        /// <param name="emitLastOnCompleted">完成时是否发射最后一个值</param>
        public ThrottlingSampler(string name, TimeSpan throttleInterval, bool emitLastOnCompleted)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            
            if (emitLastOnCompleted)
            {
                _throttledStream = _input.Throttle(throttleInterval, DefaultScheduler.Instance);
            }
            else
            {
                _throttledStream = _input
                    .Scan(default(Measurement), (last, current) => current)
                    .Throttle(throttleInterval, DefaultScheduler.Instance);
            }
        }

        public void Start()
        {
            if (_subscription != null) return;
            
            _cts = new CancellationTokenSource();
            _subscription = _throttledStream.Subscribe(
                value => _output.OnNext(value),
                error => _output.OnError(error),
                () => _output.OnCompleted()
            );
        }

        public void Stop()
        {
            _subscription?.Dispose();
            _subscription = null;
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        /// <summary>
        /// 输入数据到采样器。
        /// </summary>
        public void OnNext(Measurement value)
        {
            _input.OnNext(value);
        }

        public void Dispose()
        {
            Stop();
            _input.Dispose();
            _output.Dispose();
        }
    }

    /// <summary>
    /// 基于 Sample 的固定间隔采样器（针对 Measurement 类型）。
    /// 每隔指定时间发射一个最新的值，适用于定期采样场景。
    /// </summary>
    public class SamplingSampler : ISensorSampler
    {
        private readonly Subject<Measurement> _input = new();
        private readonly IObservable<Measurement> _sampledStream;
        private readonly Subject<Measurement> _output = new();
        private IDisposable _subscription;

        public string Name { get; }
        
        /// <inheritdoc />
        public IObservable<Measurement> DataStream => _output.AsObservable();

        /// <summary>
        /// 创建 SamplingSampler 实例。
        /// </summary>
        /// <param name="name">采样器名称</param>
        /// <param name="sampleInterval">采样间隔</param>
        public SamplingSampler(string name, TimeSpan sampleInterval)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            
            _sampledStream = _input.Sample(sampleInterval);
        }

        public void Start()
        {
            if (_subscription != null) return;
            
            _subscription = _sampledStream.Subscribe(
                value => _output.OnNext(value),
                error => _output.OnError(error),
                () => _output.OnCompleted()
            );
        }

        public void Stop()
        {
            _subscription?.Dispose();
            _subscription = null;
        }

        public void OnNext(Measurement value)
        {
            _input.OnNext(value);
        }

        public void Dispose()
        {
            Stop();
            _input.Dispose();
            _output.Dispose();
        }
    }

    /// <summary>
    /// 基于 Debounce 的防抖采样器（针对 Measurement 类型）。
    /// 只有当值停止变化指定时间后才发射，适用于等待用户输入结束的场景。
    /// </summary>
    public class DebouncingSampler : ISensorSampler
    {
        private readonly Subject<Measurement> _input = new();
        private readonly IObservable<Measurement> _debouncedStream;
        private readonly Subject<Measurement> _output = new();
        private IDisposable _subscription;

        public string Name { get; }
        
        /// <inheritdoc />
        public IObservable<Measurement> DataStream => _output.AsObservable();

        /// <summary>
        /// 创建 DebouncingSampler 实例。
        /// </summary>
        /// <param name="name">采样器名称</param>
        /// <param name="debounceInterval">防抖时间间隔</param>
        public DebouncingSampler(string name, TimeSpan debounceInterval)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            
            // 使用 Timeout 模拟防抖效果（由于 netstandard2.0 兼容性问题）
            _debouncedStream = _input
                .Timeout(debounceInterval)
                .Catch<Measurement, TimeoutException>(ex => Observable.Empty<Measurement>());
        }

        public void Start()
        {
            if (_subscription != null) return;
            
            _subscription = _debouncedStream.Subscribe(
                value => _output.OnNext(value),
                error => _output.OnError(error),
                () => _output.OnCompleted()
            );
        }

        public void Stop()
        {
            _subscription?.Dispose();
            _subscription = null;
        }

        public void OnNext(Measurement value)
        {
            _input.OnNext(value);
        }

        public void Dispose()
        {
            Stop();
            _input.Dispose();
            _output.Dispose();
        }
    }

    /// <summary>
    /// 基于 DistinctUntilChanged 的去重采样器。
    /// 只有当值发生变化时才发射，适用于减少重复数据。
    /// </summary>
    public class DistinctSampler : ISensorSampler
    {
        private readonly Subject<Measurement> _input = new();
        private readonly IObservable<Measurement> _distinctStream;
        private readonly Subject<Measurement> _output = new();
        private IDisposable _subscription;

        public string Name { get; }
        
        /// <inheritdoc />
        public IObservable<Measurement> DataStream => _output.AsObservable();

        /// <summary>
        /// 创建 DistinctSampler 实例。
        /// </summary>
        /// <param name="name">采样器名称</param>
        public DistinctSampler(string name)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            
            _distinctStream = _input.DistinctUntilChanged();
        }

        public void Start()
        {
            if (_subscription != null) return;
            
            _subscription = _distinctStream.Subscribe(
                value => _output.OnNext(value),
                error => _output.OnError(error),
                () => _output.OnCompleted()
            );
        }

        public void Stop()
        {
            _subscription?.Dispose();
            _subscription = null;
        }

        public void OnNext(Measurement value)
        {
            _input.OnNext(value);
        }

        public void Dispose()
        {
            Stop();
            _input.Dispose();
            _output.Dispose();
        }
    }
}