using System;
using ZL.Gear.Core.Infrastructure;

namespace ZL.Gear.Core.Events
{
    /// <summary>
    /// 系统启动/初始化完成事件
    /// </summary>
    public class SystemStartedEvent : BaseEvent
    {
        public string Version { get; }
        public SystemStartedEvent(string version) => Version = version;
    }

    public enum DeviceState
    {
        Unknown,
        Offline,
        Connecting,
        Online,
        Busy,
        Error
    }

    /// <summary>
    /// 设备状态变更事件
    /// </summary>
    public class DeviceStatusEvent : BaseEvent
    {
        public string DeviceId { get; }
        public DeviceState State { get; }
        public string Message { get; }

        public DeviceStatusEvent(string deviceId, DeviceState state, string message = "")
        {
            DeviceId = deviceId;
            State = state;
            Message = message;
        }
    }

    /// <summary>
    /// 租约/授权变更事件
    /// </summary>
    public class LeaseChangedEvent : BaseEvent
    {
        public string LeaseKey { get; }
        public DateTime ExpiryDate { get; }
        public LeaseChangedEvent(string leaseKey, DateTime expiryDate)
        {
            LeaseKey = leaseKey;
            ExpiryDate = expiryDate;
        }
    }
}
