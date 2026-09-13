using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using ZL.Gear.Core.Devices.Abstractions;

namespace ZL.Gear.Engine.Infrastructure
{
    /// <summary>
    /// Runtime 级内存设备隔离表（T-P0-01b 最小实现；每 Builder.Build 独立实例）。
    /// </summary>
    public sealed class RuntimeDeviceQuarantineService : IDeviceQuarantineService
    {
        private readonly ConcurrentDictionary<string, string> _reasons =
            new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public void Quarantine(string deviceKey, string reason, Guid runId)
        {
            if (string.IsNullOrWhiteSpace(deviceKey))
            {
                return;
            }

            var text = string.IsNullOrWhiteSpace(reason)
                ? $"RunId={runId}"
                : $"{reason} (RunId={runId})";
            _reasons[deviceKey] = text;
        }

        public bool IsQuarantined(string deviceKey, out string reason)
        {
            reason = null;
            if (string.IsNullOrWhiteSpace(deviceKey))
            {
                return false;
            }

            return _reasons.TryGetValue(deviceKey, out reason);
        }

        public void Release(string deviceKey)
        {
            if (!string.IsNullOrWhiteSpace(deviceKey))
            {
                _reasons.TryRemove(deviceKey, out _);
            }
        }

        public void ClearAll() => _reasons.Clear();

        public IReadOnlyCollection<string> GetQuarantinedKeys() => _reasons.Keys.ToList();
    }
}
