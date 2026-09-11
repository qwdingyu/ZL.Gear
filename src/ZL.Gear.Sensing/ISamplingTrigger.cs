using System;

namespace ZL.Gear.Sensing
{
    /// <summary>
    /// 定义了采样触发器。它可以监听 EventBus 或特定的数据流。
    /// </summary>
    public interface ISamplingTrigger : IDisposable
    {
        /// <summary>
        /// 当触发条件满足时发出通知
        /// </summary>
        IObservable<bool> Triggered { get; }
        
        /// <summary>
        /// 激活触发器（开始监听）
        /// </summary>
        void Activate();

        /// <summary>
        /// 停用触发器
        /// </summary>
        void Deactivate();
    }
}
