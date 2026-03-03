using System;

namespace ZL.Gear.Core.Events
{
    /// <summary>
    /// 全局设备状态通知器静态代理。
    /// 演进：内部通过 GlobalEvents.Bus 进行发布，保持向后兼容。
    /// </summary>
    public static class DeviceNotifier
    {
        public static void Notify(string deviceId, DeviceState state, string message = "")
        {
            GlobalEvents.Bus.Publish(new DeviceStatusEvent(deviceId, state, message));
        }
    }
}
