using System;
using ZL.Gear.Core.Infrastructure;

namespace ZL.Gear.Core.Events
{
    /// <summary>
    /// UI 提示反馈级别
    /// </summary>
    public enum FeedbackLogLevel
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
    public class UserFeedbackEvent : BaseEvent
    {
        public string Message { get; }
        public FeedbackLogLevel Level { get; }
        public bool ShowAsDialog { get; }

        public UserFeedbackEvent(string message, FeedbackLogLevel level = FeedbackLogLevel.Info, bool showAsDialog = false)
        {
            Message = message;
            Level = level;
            ShowAsDialog = showAsDialog;
        }
    }

    /// <summary>
    /// UI 实时数据更新事件
    /// </summary>
    public class MetricUpdateEvent : BaseEvent
    {
        public string Key { get; }
        public object Value { get; }
        public MetricUpdateEvent(string key, object value)
        {
            Key = key;
            Value = value;
        }
    }

    /// <summary>
    /// UI 条码扫描请求事件 (双向/交互式事件)
    /// </summary>
    public class BarcodeScanRequestEvent : BaseEvent
    {
        public string Prompt { get; }
        public int TimeoutMs { get; }
        public System.Threading.Tasks.TaskCompletionSource<string> Tcs { get; }

        public BarcodeScanRequestEvent(string prompt, int timeoutMs)
        {
            Prompt = prompt;
            TimeoutMs = timeoutMs;
            Tcs = new System.Threading.Tasks.TaskCompletionSource<string>();
        }
    }
}
