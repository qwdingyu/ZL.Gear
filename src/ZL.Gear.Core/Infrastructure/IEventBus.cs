using System;

namespace ZL.Gear.Core.Infrastructure
{
    /// <summary>
    /// 所有系统事件的基类接口
    /// </summary>
    public interface IEvent
    {
        DateTime Timestamp { get; }
    }

    /// <summary>
    /// 事件总线接口，用于解耦组件间通信
    /// </summary>
    public interface IEventBus
    {
        /// <summary>
        /// 发布一个事件
        /// </summary>
        void Publish<T>(T @event) where T : IEvent;

        /// <summary>
        /// 订阅特定类型的事件
        /// </summary>
        IDisposable Subscribe<T>(Action<T> handler) where T : IEvent;
    }
}
