using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.Gear.Core.SampleSignal
{
    /// <summary>
    /// 生产者(数据采集)与消费者(测试逻辑)之间的通道样本到达信令。
    /// </summary>
    public static class SampleSignals
    {
        private static readonly ConcurrentDictionary<string, AutoResetEvent> _channelSignals
            = new ConcurrentDictionary<string, AutoResetEvent>();

        private static string GetKey(string deviceName, string channel) => $"{deviceName}:{channel}";

        public static void Signal(string deviceName, string channel)
        {
            var key = GetKey(deviceName, channel);
            if (_channelSignals.TryGetValue(key, out var signal))
            {
                signal.Set();
            }
        }

        public static Task<string> WaitForAnySampleAsync(string deviceName, IEnumerable<string> channels, int timeoutMs, CancellationToken token)
        {
            return Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                var channelList = channels.ToList();
                if (!channelList.Any()) return null;

                var waitHandles = new List<WaitHandle>();
                var handleToChannelMap = new Dictionary<WaitHandle, string>();

                foreach (var channel in channelList)
                {
                    var key = GetKey(deviceName, channel);
                    var signal = _channelSignals.GetOrAdd(key, _ => new AutoResetEvent(initialState: false));
                    waitHandles.Add(signal);
                    handleToChannelMap[signal] = channel;
                }

                waitHandles.Add(token.WaitHandle);
                int signaledIndex = WaitHandle.WaitAny(waitHandles.ToArray(), timeoutMs);

                if (signaledIndex == WaitHandle.WaitTimeout)
                {
                    return null;
                }

                var signaledHandle = waitHandles[signaledIndex];
                if (signaledHandle == token.WaitHandle)
                {
                    token.ThrowIfCancellationRequested();
                }

                return handleToChannelMap.TryGetValue(signaledHandle, out var signaledChannel) ? signaledChannel : null;
            }, token);
        }

        public static async Task<bool> WaitForSampleAsync(string deviceName, string channel, int timeoutMs, CancellationToken token)
        {
            var signaledChannel = await WaitForAnySampleAsync(deviceName, new[] { channel }, timeoutMs, token)
                .ConfigureAwait(false);
            return !string.IsNullOrEmpty(signaledChannel);
        }
    }
}
