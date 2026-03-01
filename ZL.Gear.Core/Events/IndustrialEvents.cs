using System;
using System.Collections.Generic;

namespace ZL.Gear.Core.Events
{
    /// <summary>
    /// PLC 状态变更事件 (通用包装)
    /// </summary>
    public class PlcStateChangedEvent : BaseEvent
    {
        public string DeviceId { get; }
        public string Key { get; }
        public object Value { get; }

        public PlcStateChangedEvent(string deviceId, string key, object value)
        {
            DeviceId = deviceId;
            Key = key;
            Value = value;
        }
    }

    /// <summary>
    /// PLC 手自动状态变更
    /// </summary>
    public class PlcAutoManualEvent : BaseEvent
    {
        public bool IsRunning { get; }
        public bool IsReset { get; }
        public bool IsTest { get; }
        public int Mode { get; }
        public bool IsSafe { get; }
        public bool TwoHandStart { get; }

        public PlcAutoManualEvent(bool isRunning, bool isReset, bool isTest, int mode, bool isSafe, bool twoHandStart)
        {
            IsRunning = isRunning; IsReset = isReset; IsTest = isTest; Mode = mode; IsSafe = isSafe; TwoHandStart = twoHandStart;
        }
    }

    /// <summary>
    /// PLC SBR 位置变更事件
    /// </summary>
    public class PlcSbrLocationEvent : BaseEvent
    {
        public float Location { get; }

        public PlcSbrLocationEvent(float location)
        {
            Location = location;
        }
    }

    /// <summary>
    /// 传感器数据变更事件
    /// </summary>
    public class SensorDataEvent : BaseEvent
    {
        public int SeatPos { get; }
        public int BackPos { get; }
        public int CushionPos { get; }
        public Dictionary<string, object> IOStates { get; }

        public SensorDataEvent(int seatPos, int backPos, int cushionPos, Dictionary<string, object> ioStates)
        {
            SeatPos = seatPos;
            BackPos = backPos;
            CushionPos = cushionPos;
            IOStates = ioStates;
        }
    }

}
