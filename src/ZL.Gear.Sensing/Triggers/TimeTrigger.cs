using System;
using System.Reactive.Linq;

namespace ZL.Gear.Sensing.Triggers
{
    /// <summary>
    /// 基于时间的触发器。
    /// </summary>
    public class TimeTrigger : ISamplingTrigger
    {
        private readonly TimeSpan _delay;
        private IDisposable _timer;
        private readonly System.Reactive.Subjects.Subject<bool> _triggered = new();

        public IObservable<bool> Triggered => _triggered.AsObservable();

        public TimeTrigger(TimeSpan delay)
        {
            _delay = delay;
        }

        public void Activate()
        {
            _timer = Observable.Timer(_delay).Subscribe(_ => _triggered.OnNext(true));
        }

        public void Deactivate()
        {
            _timer?.Dispose();
        }

        public void Dispose()
        {
            Deactivate();
            _triggered.Dispose();
        }
    }
}
