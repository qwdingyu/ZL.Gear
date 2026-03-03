using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.Gear.Core.SampleSignal
{
    /// <summary>
    /// 提供基于命名的采样信号触发机制。
    /// </summary>
    /// <summary>
    /// 提供一个全局的、线程安全的机制，用于发出和等待特定通道的样本到达信号。
    /// 这是实现生产者(数据采集)与消费者(测试逻辑)之间高效通信的核心。
    /// </summary>
    public static class SampleSignals
    {
        // 使用线程安全的字典为每个唯一的通道("设备名:通道名")保存一个等待句柄。
        private static readonly ConcurrentDictionary<string, AutoResetEvent> _channelSignals
            = new ConcurrentDictionary<string, AutoResetEvent>();
        
        // 内部辅助方法，生成唯一的字典键
        private static string GetKey(string deviceName, string channel) => $"{deviceName}:{channel}";

        /// <summary>
        /// (生产者调用) 发出指定通道已有新样本到达的信号。
        /// 每当有新数据喂给 MeasurementHub 时，就应该调用此方法。
        /// </summary>
        public static void Signal(string deviceName, string channel)
        {
            var key = GetKey(deviceName, channel);
            // 尝试获取该通道的信号机，如果存在（即有代码正在等待它），则触发它。
            if (_channelSignals.TryGetValue(key, out var signal))
            {
                // Set() 会唤醒一个正在等待的线程。
                // AutoResetEvent 的特性是唤醒后会自动复位，无需手动操作。
                signal.Set();
            }
        }

        /// <summary>
        /// (消费者调用) 异步地等待任何一个指定通道的样本到达。
        /// </summary>
        /// <param name="deviceName">拥有这些通道的设备名。</param>
        /// <param name="channels">需要监控的通道名称集合。</param>
        /// <param name="timeoutMs">最长等待时间（毫秒）。</param>
        /// <param name="token">用于从外部中止等待的取消令牌。</param>
        /// <returns>
        /// 一个 Task，其结果是收到了样本的通道名称；如果等待超时或被取消，则结果为 null。
        /// </returns>
        public static Task<string> WaitForAnySampleAsync(string deviceName, IEnumerable<string> channels, int timeoutMs, CancellationToken token)
        {
            // 将阻塞式的等待操作放到后台线程执行，避免阻塞调用线程。
            return Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                var channelList = channels.ToList();
                if (!channelList.Any()) return null; // 如果没有要等待的通道，直接返回
                
                var waitHandles = new List<WaitHandle>();
                var handleToChannelMap = new Dictionary<WaitHandle, string>();
                
                // 1. 为每个要监听的通道准备好信号机 (WaitHandle)
                foreach (var channel in channelList)
                {
                    var key = GetKey(deviceName, channel);
                    // GetOrAdd确保了每个通道只有一个信号机实例，并且是线程安全的。
                    var signal = _channelSignals.GetOrAdd(key, _ => new AutoResetEvent(initialState: false));

                    waitHandles.Add(signal);
                    handleToChannelMap[signal] = channel; // 记录句柄和通道名的映射关系
                }
                
                // 2. 将取消令牌也加入等待列表，这样就可以同时等待数据信号或取消信号
                waitHandles.Add(token.WaitHandle);
                
                // 3. 执行核心等待逻辑
                // WaitHandle.WaitAny 等待数组中任意一个句柄被触发，并返回其索引
                int signaledIndex = WaitHandle.WaitAny(waitHandles.ToArray(), timeoutMs);
                
                // 4. 分析等待结果
                if (signaledIndex == WaitHandle.WaitTimeout)
                {
                    return null; // 超时
                }
                
                var signaledHandle = waitHandles[signaledIndex];
                if (signaledHandle == token.WaitHandle)
                {
                    token.ThrowIfCancellationRequested(); // 被取消
                }
                
                // 如果代码能执行到这里，说明是一个通道的信号被触发了
                // 通过映射找回通道名并返回
                return handleToChannelMap.TryGetValue(signaledHandle, out var signaledChannel) ? signaledChannel : null;
            }, token);
        }

        /// <summary>
        /// (辅助函数) 异步地等待单个指定通道的样本到达。
        /// 这是对 WaitForAnySampleAsync 的一个便捷封装，用于向后兼容或简化单通道等待场景。
        /// </summary>
        /// <param name="deviceName">设备名。</param>
        /// <param name="channel">要等待的单个通道名。</param>
        /// <param name="timeoutMs">最长等待时间（毫秒）。</param>
        /// <param name="token">用于中止等待的取消令牌。</param>
        /// <returns>
        /// 一个 Task，其结果为布尔值：如果等到了样本则为 true，超时或取消则为 false。
        /// </returns>
        public static async Task<bool> WaitForSampleAsync(string deviceName, string channel, int timeoutMs, CancellationToken token)
        {
            // 内部直接调用更通用的 WaitForAnySampleAsync 方法
            var signaledChannel = await WaitForAnySampleAsync(deviceName, new[] { channel }, timeoutMs, token)
                .ConfigureAwait(false);

            // 将 string/null 的结果转换为 bool
            return !string.IsNullOrEmpty(signaledChannel);
        }
    }
}
