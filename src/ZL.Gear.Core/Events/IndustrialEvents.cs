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
    /// PLC 坐标/位置变更事件
    /// </summary>
    public class PlcLocationEvent : BaseEvent
    {
        public float Location { get; }

        public PlcLocationEvent(float location)
        {
            Location = location;
        }
    }

    /// <summary>
    /// 通用传感器数据变更事件
    /// </summary>
    public class SensorDataEvent : BaseEvent
    {
        /// <summary>
        /// 模拟量或其他量化数值 (如位置、压力、电压)
        /// </summary>
        public Dictionary<string, object> AnalogValues { get; }
        
        /// <summary>
        /// 状态量或 IO 信号
        /// </summary>
        public Dictionary<string, object> IOStates { get; }

        public SensorDataEvent(Dictionary<string, object> analogValues, Dictionary<string, object> ioStates)
        {
            AnalogValues = analogValues ?? new Dictionary<string, object>();
            IOStates = ioStates ?? new Dictionary<string, object>();
        }
    }

}
