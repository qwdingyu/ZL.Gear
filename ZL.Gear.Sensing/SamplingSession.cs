using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Sensing
{
    /// <summary>
    /// 表示一次采样任务。
    /// 它协调启动触发、数据采集、停止触发及最终结果计算。
    /// </summary>
    [Obsolete("SamplingSession 模式已被 UniversalMeasurer 和 DeviceSampler 取代，即使在 Extension 中也应逐步迁移。")]
    public class SamplingSession : IDisposable
    {
        private readonly ISensorSampler _sampler;
        private readonly ISamplingTrigger _startTrigger;
        private readonly ISamplingTrigger _stopTrigger;
        private readonly List<ZL.Gear.Core.Models.Measurement> _collectedData = new();
        private readonly TaskCompletionSource<List<ZL.Gear.Core.Models.Measurement>> _sessionTcs = new();
        private IDisposable _dataSubscription;
        private IDisposable _startSubscription;
        private IDisposable _stopSubscription;

        public string SessionId { get; } = Guid.NewGuid().ToString("N");

        public SamplingSession(ISensorSampler sampler, ISamplingTrigger start, ISamplingTrigger stop)
        {
            _sampler = sampler ?? throw new ArgumentNullException(nameof(sampler));
            _startTrigger = start ?? throw new ArgumentNullException(nameof(start));
            _stopTrigger = stop ?? throw new ArgumentNullException(nameof(stop));
        }

        public async Task<List<ZL.Gear.Core.Models.Measurement>> RunAsync(CancellationToken token)
        {
            using (token.Register(() => _sessionTcs.TrySetCanceled()))
            {
                // 1. 等待启动触发
                _startTrigger.Activate();
                _startSubscription = _startTrigger.Triggered.Take(1).Subscribe(_ => StartCollecting());

                // 2. 等待采样完成
                return await _sessionTcs.Task;
            }
        }

        private void StartCollecting()
        {
            _startTrigger.Deactivate();
            
            // 开始从 Sampler 接收流数据
            _dataSubscription = _sampler.DataStream.Subscribe(m => 
            {
                lock (_collectedData) _collectedData.Add(m);
            });
            _sampler.Start();

            // 激活停止触发
            _stopTrigger.Activate();
            _stopSubscription = _stopTrigger.Triggered.Take(1).Subscribe(_ => StopCollecting());
        }

        private void StopCollecting()
        {
            _sampler.Stop();
            _dataSubscription?.Dispose();
            _stopTrigger.Deactivate();
            _sessionTcs.TrySetResult(new List<ZL.Gear.Core.Models.Measurement>(_collectedData));
        }

        public void Dispose()
        {
            _dataSubscription?.Dispose();
            _startSubscription?.Dispose();
            _stopSubscription?.Dispose();
            _startTrigger.Dispose();
            _stopTrigger.Dispose();
        }
    }
}
