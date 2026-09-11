using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using ZL.Gear.Core.Events;
using ZL.Gear.Core.Infrastructure;

namespace ZL.Gear.Sensing.Samplers
{
    /// <summary>
    /// 将 EventBus 中的实时更新流适配为采样器。
    /// 这允许将任何通过 MeasurementHub.Feed 输入的数据作为采样源。
    /// </summary>
    public class HubSampler : ISensorSampler
    {
        private readonly IEventBus _bus;
        private readonly string _targetKey;
        private readonly Subject<ZL.Gear.Core.Models.Measurement> _output = new();
        private IDisposable _subscription;

        public string Name => $"HubSampler:{_targetKey}";
        public IObservable<ZL.Gear.Core.Models.Measurement> DataStream => _output.AsObservable();

        public HubSampler(IEventBus bus, string targetKey)
        {
            _bus = bus;
            _targetKey = targetKey;
        }

        public void Start()
        {
            if (_subscription != null) return;
            _subscription = _bus.Subscribe<MetricUpdateEvent>(e =>
            {
                if (e.Key == _targetKey)
                {
                    _output.OnNext(ZL.Gear.Core.Models.Measurement.Create(e.Key, Convert.ToDouble(e.Value), true));
                }
            });
        }

        public void Stop()
        {
            _subscription?.Dispose();
            _subscription = null;
        }

        public void Dispose()
        {
            Stop();
            _output.Dispose();
        }
    }
}
