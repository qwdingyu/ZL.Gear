using ZL.Gear.Core.Infrastructure;

namespace ZL.Gear.Core.Events
{
    /// <summary>
    /// 全局事件总线入口。legacy static UI 回调已迁至 <c>ZL.Gear.LegacyBridge.SeatLegacyEventBridge</c>（G1b-08）。
    /// </summary>
    public static class GlobalEvents
    {
        private static readonly IEventBus _bus = DefaultEventBus.Instance;

        /// <summary>公开的事件总线实例。</summary>
        public static IEventBus Bus => _bus;
    }
}
