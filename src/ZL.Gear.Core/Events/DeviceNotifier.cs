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

        public static void SetBus(IEventBus bus) => _bus = bus;

        public static void Notify(string deviceId, DeviceState state, string message = "")
        {
            _bus?.Publish(new DeviceStatusEvent(deviceId, state, message));
        }
    }
}
