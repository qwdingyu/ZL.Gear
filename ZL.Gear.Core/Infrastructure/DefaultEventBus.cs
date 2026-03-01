using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace ZL.Gear.Core.Infrastructure
{
    /// <summary>
    /// 事件总线的默认简单实现
    /// </summary>
    public class DefaultEventBus : IEventBus
    {
        private static readonly Lazy<DefaultEventBus> _instance = new Lazy<DefaultEventBus>(() => new DefaultEventBus());
        public static DefaultEventBus Instance => _instance.Value;

        private readonly ConcurrentDictionary<Type, List<object>> _subscriptions = new();

        public void Publish<T>(T @event) where T : IEvent
        {
            if (@event == null) return;

            var type = typeof(T);
            if (_subscriptions.TryGetValue(type, out var handlers))
            {
                List<object> handlersCopy;
                lock (handlers)
                {
                    handlersCopy = handlers.ToList();
                }

                foreach (var handler in handlersCopy)
                {
                    try
                    {
                        ((Action<T>)handler)(@event);
                    }
                    catch (Exception)
                    {
                        // 记录日志，但不让单个订阅者的异常中止整个发布流程
                        // 工业场景下通常建议异步执行或强制捕获异常
                    }
                }
            }
        }

        public IDisposable Subscribe<T>(Action<T> handler) where T : IEvent
        {
            var type = typeof(T);
            var handlers = _subscriptions.GetOrAdd(type, _ => new List<object>());

            lock (handlers)
            {
                handlers.Add(handler);
            }

            return new Unsubscriber(handlers, handler);
        }

        private class Unsubscriber : IDisposable
        {
            private readonly List<object> _handlers;
            private readonly object _handler;

            public Unsubscriber(List<object> handlers, object handler)
            {
                _handlers = handlers;
                _handler = handler;
            }

            public void Dispose()
            {
                lock (_handlers)
                {
                    _handlers.Remove(_handler);
                }
            }
        }
    }
}
