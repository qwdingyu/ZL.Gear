using System;
using ZL.Gear.Core.Infrastructure;

namespace ZL.Gear.Core.Events
{
    /// <summary>
    /// UI 提示反馈级别
    /// </summary>
    public enum UiLogLevel
    {
        Debug,
        Info,
        Success,
        Warning,
        Error,
        Fatal
    }

    /// <summary>
    /// UI 提示反馈事件
    /// </summary>
    public class UiFeedbackEvent : BaseEvent
    {
        public string Message { get; }
        public UiLogLevel Level { get; }
        public bool ShowAsDialog { get; }

        public UiFeedbackEvent(string message, UiLogLevel level = UiLogLevel.Info, bool showAsDialog = false)
        {
            Message = message;
            Level = level;
            ShowAsDialog = showAsDialog;
        }
    }

    /// <summary>
    /// UI 实时数据更新事件
    /// </summary>
    public class UiRealTimeUpdateEvent : BaseEvent
    {
        public string Key { get; }
        public object Value { get; }
        public UiRealTimeUpdateEvent(string key, object value)
        {
            Key = key;
            Value = value;
        }
    }
}
