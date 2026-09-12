using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using ZL.Gear.Core.Infrastructure;

namespace ZL.Gear.Sensing.Triggers
{
    /// <summary>
    /// 基于 EventBus 的触发器：监听宿主发布的 <typeparamref name="T"/> 类型事件来决定采样开始/停止。
    /// 抽象层中立（179 §1.1）：只依赖 <see cref="IEvent"/> 契约，不绑定任何行业事件类型。
    /// 行业侧（如「设备就绪」「安全互锁到位」）由宿主自定义 <see cref="IEvent"/> 实现并在运行期注入。
    /// 例如：监听宿主定义的「通道就绪」事件（<see cref="IEvent"/> 实现）来开始或停止采样。
    /// </summary>
    /// <typeparam name="T">要监听的事件类型（须实现 <see cref="IEvent"/>）。</typeparam>
    public class EventBusTrigger<T> : ISamplingTrigger where T : IEvent
    {
        private readonly IEventBus _bus;
        private readonly Func<T, bool> _predicate;
        private readonly Subject<bool> _triggered = new();
        private IDisposable _subscription;

        /// <summary>触发信号流：每次满足判定条件时发出 true（供采样引擎订阅）。</summary>
        public IObservable<bool> Triggered => _triggered.AsObservable();

        /// <summary>
        /// 创建 EventBus 触发器。
        /// </summary>
        /// <param name="bus">事件总线（必填）。</param>
        /// <param name="predicate">事件判定条件；为 null 时默认任何该类型事件都触发。</param>
        public EventBusTrigger(IEventBus bus, Func<T, bool> predicate = null)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _predicate = predicate ?? (_ => true);
        }

        /// <summary>
        /// 激活订阅：开始监听 <typeparamref name="T"/> 事件。
        /// 幂等：重复激活不会重复订阅（先行检查 <see cref="_subscription"/>）。
        /// </summary>
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

        /// <summary>
        /// 停用订阅：释放事件监听；触发流保持不变，可再次 <see cref="Activate"/> 重新监听。
        /// </summary>
        public void Deactivate()
        {
            _subscription?.Dispose();
            _subscription = null;
        }

        /// <summary>销毁：停用订阅并完成/释放触发流。</summary>
        public void Dispose()
        {
            Deactivate();
            _triggered.OnCompleted();
            _triggered.Dispose();
        }
    }
}
