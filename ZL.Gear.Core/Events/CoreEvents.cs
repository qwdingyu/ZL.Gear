using System;
using ZL.Gear.Core.Infrastructure;

namespace ZL.Gear.Core.Events
{
    public abstract class BaseEvent : IEvent
    {
        public DateTime Timestamp { get; } = DateTime.Now;
    }

    /// <summary>
    /// UI 提示信息事件
    /// </summary>
    public class UiTipEvent : BaseEvent
    {
        public string Message { get; }
        public bool IsSuccess { get; }

        public UiTipEvent(string message, bool isSuccess = false)
        {
            Message = message;
            IsSuccess = isSuccess;
        }
    }

    /// <summary>
    /// 全局扫码事件
    /// </summary>
    public class GlobalScanEvent : BaseEvent
    {
        public string Barcode { get; }

        public GlobalScanEvent(string barcode)
        {
            Barcode = barcode;
        }
    }

    /// <summary>
    /// 测量值实时更新事件
    /// </summary>
    public class RealTimeUpdateEvent : BaseEvent
    {
        public string Key { get; }
        public object Value { get; }

        public RealTimeUpdateEvent(string key, object value)
        {
            Key = key;
            Value = value;
        }
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
