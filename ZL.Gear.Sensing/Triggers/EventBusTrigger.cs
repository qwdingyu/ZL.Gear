using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using ZL.Gear.Core.Infrastructure;

namespace ZL.Gear.Sensing.Triggers
{
    /// <summary>
    /// 基于 EventBus 的触发器。
    /// 例如：监听特定的 PlcAutoManualEvent 来开始或停止采样。
    /// </summary>
    public class EventBusTrigger<T> : ISamplingTrigger where T : IEvent
    {
        private readonly IEventBus _bus;
        private readonly Func<T, bool> _predicate;
        private readonly Subject<bool> _triggered = new();
        private IDisposable _subscription;

        public IObservable<bool> Triggered => _triggered.AsObservable();

        public EventBusTrigger(IEventBus bus, Func<T, bool> predicate = null)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _predicate = predicate ?? (_ => true);
        }

        public void Activate()
        {
            if (_subscription != null) return;
            _subscription = _bus.Subscribe<T>(e =>
            {
                if (_predicate(e))
                {
                    _triggered.OnNext(true);
                }
            });
        }

        public void Deactivate()
        {
            _subscription?.Dispose();
            _subscription = null;
        }

        public void Dispose()
        {
            Deactivate();
            _triggered.OnCompleted();
            _triggered.Dispose();
        }
    }
}
