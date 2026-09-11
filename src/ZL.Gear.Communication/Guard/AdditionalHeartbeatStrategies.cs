using System;

namespace ZL.Gear.Communication.Guard
{
    /// <summary>
    /// 计数器心跳策略，0-255 循环递增。
    /// </summary>
    public sealed class CounterHeartbeat : IHeartbeatStrategy
    {
        private readonly Func<byte, byte[]> _frameBuilder;
        private byte _counter;

        public CounterHeartbeat(Func<byte, byte[]> frameBuilder = null)
        {
            _frameBuilder = frameBuilder ?? (c => new[] { c });
        }

        public byte[] CreateHeartbeat()
        {
            unchecked { _counter++; }
            try
            {
                return _frameBuilder(_counter);
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// 委托型心跳策略，允许每次动态生成心跳数据。
    /// </summary>
    public sealed class DelegateHeartbeat : IHeartbeatStrategy
    {
        private readonly Func<byte[]> _generator;

        public DelegateHeartbeat(Func<byte[]> generator)
        {
            _generator = generator ?? throw new ArgumentNullException(nameof(generator));
        }

        public byte[] CreateHeartbeat()
        {
            try
            {
                return _generator();
            }
            catch
            {
                return null;
            }
        }
    }
}
