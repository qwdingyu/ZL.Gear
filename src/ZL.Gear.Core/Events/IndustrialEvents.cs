using System;
using System.Collections.Generic;

namespace ZL.Gear.Core.Events
{
    // G1d-04: line-specific PLC event types moved to Ext.Seat.Events; only generic types remain here.

    /// <summary>
    /// PLC 状态变更事件 (通用 key/value 包装)
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
