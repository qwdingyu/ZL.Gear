using System;
using ZL.Gear.Core.Infrastructure;

namespace ZL.Gear.Core.Events
{
    public abstract class BaseEvent : IEvent
    {
        public DateTime Timestamp { get; } = DateTime.Now;
    }

    /// <summary>
    /// 全局扫码事件 (原始数据)
    /// </summary>
    public class GlobalScanEvent : BaseEvent
    {
        public string Barcode { get; }
        public GlobalScanEvent(string barcode) { Barcode = barcode; }
    }

    /// <summary>
    /// 测试总计时变更事件
    /// </summary>
    public class TestTotalTimeChangedEvent : BaseEvent
    {
        public TimeSpan Elapsed { get; }
        public TestTotalTimeChangedEvent(TimeSpan elapsed) { Elapsed = elapsed; }
    }
}
