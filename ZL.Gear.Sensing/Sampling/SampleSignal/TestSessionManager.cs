using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;

namespace ZL.Gear.Core.SampleSignal
{
    /// <summary>
    /// 管理跨线程的测试会话，确保数据采集的逻辑边界和时间边界。
    /// 使用了带有截止时间戳的会话ID，以防止竞态条件下的数据污染。
    /// </summary>
    public static class TestSessionManager
    {
        // Key: "deviceId/channelName"
        // Value: (sessionId: string, deadlineTicks: long)
        private static readonly ConcurrentDictionary<string, (string sessionId, long deadlineTicks)> _activeSessions = new();
        /// <summary>
        /// 开始一个新的测试会话，并返回一个唯一的会话ID。
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <param name="channel">通道名称</param>
        /// <param name="deadlineTicks">会话的绝对截止时间点 (使用 Stopwatch.GetTimestamp() 计算)</param>
        /// <returns>唯一的会话ID</returns>
        public static string StartSession(string deviceId, string channel, long deadlineTicks)
        {
            var key = $"{deviceId}/{channel}";
            var sessionId = Guid.NewGuid().ToString("N"); // 使用紧凑格式的GUID
            _activeSessions[key] = (sessionId, deadlineTicks);
            return sessionId;
        }
        /// <summary>
        /// 结束一个测试会话。
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <param name="channel">通道名称</param>
        /// <param name="sessionId">要结束的会话ID</param>
        public static void EndSession(string deviceId, string channel, string sessionId)
        {
            var key = $"{deviceId}/{channel}";
            // 使用原子操作：仅在会话ID匹配时才移除，防止错误地关闭了后续启动的新会话。
            if (_activeSessions.TryGetValue(key, out var currentSession) && currentSession.sessionId == sessionId)
            {
                //_activeSessions.TryRemove(key, out _);

                var valueToRemove = new KeyValuePair<string, (string, long)>(key, currentSession);
                ((ICollection<KeyValuePair<string, (string sessionId, long deadlineTicks)>>)_activeSessions).Remove(valueToRemove);
            }
        }
        /// <summary>
        /// 尝试获取当前有效的活动会话ID。
        /// 这个方法是线程安全的，并且会检查会话是否在时间上已过期。
        /// </summary>
        /// <param name="deviceId">设备ID</param>
        /// <param name="channel">通道名称</param>
        /// <param name="sessionId">如果存在有效会话，则返回会话ID</param>
        /// <returns>如果存在一个未过期的活动会话，则返回 true；否则返回 false。</returns>
        public static bool TryGetActiveSession(string deviceId, string channel, out string sessionId)
        {
            sessionId = null;
            var key = $"{deviceId}/{channel}";
            if (_activeSessions.TryGetValue(key, out var sessionInfo))
            {
                // 核心检查：检查会话是否已在时间上过期
                if (Stopwatch.GetTimestamp() <= sessionInfo.deadlineTicks)
                {
                    sessionId = sessionInfo.sessionId;
                    return true;
                }
                else
                {
                    // 会话已过期，但可能还未被主线程的finally块清理。
                    // 在这里尝试清理，可以减少过期会话在字典中停留的时间。
                    // 同样使用原子比较并移除，确保不会误删新会话。
                    var valueToRemove = new KeyValuePair<string, (string, long)>(key, sessionInfo);
                    ((ICollection<KeyValuePair<string, (string sessionId, long deadlineTicks)>>)_activeSessions).Remove(valueToRemove);
                    return false;
                }
            }
            return false;
        }
    }
}