using System;
using ZL.Gear.Core.Infrastructure;

namespace ZL.Gear.Core.Events
{
    /// <summary>
    /// 全局设备状态通知器静态代理。
    /// </summary>
    public static class DeviceNotifier
    {
        private static IEventBus _bus;
        
        /// <summary>
        /// 设置自定义事件总线（用于依赖注入）
        /// </summary>
        public static void SetBus(IEventBus bus) => _bus = bus;
        
        public static void Notify(string deviceId, DeviceState state, string message = "")
        {
            var bus = _bus;
            bus.Publish(new DeviceStatusEvent(deviceId, state, message));
        }
    }
}
