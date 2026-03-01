using System;
using ZL.Gear.Core.Infrastructure;

namespace ZL.Gear.Core.Events
{
    /// <summary>
    /// 提供常用事件发布的便捷扩展方法。
    /// 推荐使用这些方法替代 GlobalEvents 的静态调用。
    /// </summary>
    public static class EventBusExtensions
    {
        public static void PostUiTip(this IEventBus bus, string message, bool isSuccess = false)
        {
            bus.Publish(new UiFeedbackEvent(message, isSuccess ? UiLogLevel.Success : UiLogLevel.Info));
        }

        public static void PostRealTimeUpdate(this IEventBus bus, string key, object value)
        {
            bus.Publish(new UiRealTimeUpdateEvent(key, value));
        }

        public static void PostScanResult(this IEventBus bus, string barcode)
        {
            bus.Publish(new ProductBarcodeScannedEvent(barcode));
        }
    }
}
