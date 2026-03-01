using System;
using System.Threading.Tasks;
using ZL.Gear.Core.Infrastructure;

namespace ZL.Gear.Core.Events
{
    /// <summary>
    /// [已过时] 全局事件静态代理。
    /// 请在构造函数中注入 IEventBus，并使用 EventBusExtensions 中的扩展方法。
    /// </summary>
    [Obsolete("请使用 IEventBus 依赖注入替代静态访问", false)]
    public static class GlobalEvents
    {
        private static readonly IEventBus _bus = DefaultEventBus.Instance;

        /// <summary>
        /// 公开的事件总线实例
        /// </summary>
        public static IEventBus Bus => _bus;

        /// <summary>
        /// 扫码请求（保留委托，后期建议重构为 Request/Response 模式）
        /// </summary>
        public static Func<string, int, Task<string>> BarcodeCheckRequestScanAsync;

        // --- 核心发布助手 (转发至 IEventBus) ---

        /// <summary>
        /// [已过时] 发布系统级提示或日志。请使用 bus.PostUiTip(...)
        /// </summary>
        [Obsolete("请使用 IEventBus.PostUiTip 扩展方法", false)]
        public static void PostUiTip(string message, bool isSuccess = false) 
            => _bus.PostUiTip(message, isSuccess);

        /// <summary>
        /// [已过时] 发布实时数据更新。请使用 bus.PostRealTimeUpdate(...)
        /// </summary>
        [Obsolete("请使用 IEventBus.PostRealTimeUpdate 扩展方法", false)]
        public static void PostRealTimeUpdate(string key, object value)
            => _bus.PostRealTimeUpdate(key, value);

        /// <summary>
        /// [已过时] 发布扫码结果。请使用 bus.PostScanResult(...)
        /// </summary>
        [Obsolete("请使用 IEventBus.PostScanResult 扩展方法", false)]
        public static void PostScanResult(string barcode) 
            => _bus.PostScanResult(barcode);

        // --- 订阅快捷入口 ---

        [Obsolete("请直接使用 IEventBus.Subscribe", false)]
        public static IDisposable Subscribe<T>(Action<T> handler) where T : IEvent 
            => _bus.Subscribe(handler);

        // --- 兼容性保留 (Marked as Obsolete) ---

        [Obsolete("请改用 GlobalEvents.Bus.Publish(new UiFeedbackEvent(...))", false)]
        public static Action<string, bool> OnUiMainTipChanged 
        { 
            set { if (value != null) _bus.Subscribe<UiFeedbackEvent>(e => value(e.Message, e.Level == UiLogLevel.Success)); }
        }
    }
}
