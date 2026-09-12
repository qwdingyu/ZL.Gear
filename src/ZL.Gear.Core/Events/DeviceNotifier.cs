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

        /// <summary>可选 static 钩子（设备连接/断开）；长期收敛至 IEventBus typed event。</summary>
        public static Action<string, DeviceState> DeviceStateChangedEvent { get; set; }
        public static Action<string, string> DeviceInfoChangedEvent { get; set; }

        public static void SetBus(IEventBus bus) => _bus = bus;

        public static void Notify(string deviceId, DeviceState state, string message = "")
        {
            DeviceStateChangedEvent?.Invoke(deviceId, state);
            _bus?.Publish(new DeviceStatusEvent(deviceId, state, message));
        }
    }
}
