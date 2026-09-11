using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ZL.Gear.Communication.Guard
{
    /// <summary>
    /// 连接状态枚举
    /// </summary>
    public enum GuardState
    {
        Disconnected,
        Connecting,
        Connected,
        Reconnecting,
        Faulted
    }

    /// <summary>
    /// 发送优先级
    /// </summary>
    public enum SendPriority
    {
        Low,
        Normal,
        High
    }

    /// <summary>
    /// 状态回调模式
    /// </summary>
    public enum StateCallbackMode
    {
        Synchronous,
        Async
    }

    /// <summary>
    /// 心跳策略接口
    /// </summary>
    public interface IHeartbeatStrategy
    {
        /// <summary>
        /// 创建心跳数据
        /// </summary>
        byte[] CreateHeartbeat();
    }

    /// <summary>
    /// 静态心跳策略
    /// </summary>
    public class StaticHeartbeat : IHeartbeatStrategy
    {
        private readonly byte[] _heartbeatData;

        public StaticHeartbeat(byte[] heartbeatData)
        {
            _heartbeatData = heartbeatData ?? throw new ArgumentNullException(nameof(heartbeatData));
        }

        public byte[] CreateHeartbeat() => _heartbeatData;
    }

    /// <summary>
    /// 切换心跳策略（交替发送两种数据）
    /// </summary>
    public class ToggleHeartbeat : IHeartbeatStrategy
    {
        private readonly byte[] _data1;
        private readonly byte[] _data2;
        private bool _toggle;

        public ToggleHeartbeat(byte[] data1, byte[] data2)
        {
            _data1 = data1 ?? throw new ArgumentNullException(nameof(data1));
            _data2 = data2 ?? throw new ArgumentNullException(nameof(data2));
        }

        public byte[] CreateHeartbeat()
        {
            _toggle = !_toggle;
            return _toggle ? _data2 : _data1;
        }
    }

    /// <summary>
    /// 连接守护选项
    /// </summary>
    public class ConnectionGuardOptions
    {
        public int ReconnectMinDelayMs { get; set; } = 1000;
        public int ReconnectMaxDelayMs { get; set; } = 30000;
        public double ReconnectJitterFactor { get; set; } = 0.2;
        public int HeartbeatIntervalMs { get; set; } = 3000;
        public int DeviceDeadTimeoutMs { get; set; } = 8000;
        public IHeartbeatStrategy HeartbeatStrategy { get; set; }
        public int SendLockTimeoutMs { get; set; } = 2000;
        public int SendTimeoutMs { get; set; } = 3000;
        public int MaintenanceLoopDelayMs { get; set; } = 100;
        public StateCallbackMode StateCallbackMode { get; set; } = StateCallbackMode.Async;
        public bool WatchdogRequiresSend { get; set; } = true;
        public int WatchdogWarmupMs { get; set; } = 0;
    }
}
