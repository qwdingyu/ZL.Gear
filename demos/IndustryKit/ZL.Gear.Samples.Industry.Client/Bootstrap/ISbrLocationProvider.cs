using System.Collections.Generic;

namespace ZL.Gear.Samples.Industry.Client.Bootstrap
{
    /// <summary>
    /// SBR 下压目标位置配置（对应 legacy DeviceServices.sbrLocDict）。
    /// 实码：AutoSbrResistanceHandler.cs:90 · GlobalV.sbrLocDict L1-L8
    /// </summary>
    public interface ISbrLocationProvider
    {
        /// <summary>按 SBR 位置 Id（"1".."8"）读取目标坐标；缺失返回 false。</summary>
        bool TryGetLocation(string sbrLocId, out float location);
    }

    /// <summary>内存字典实现，供 headless 宿主注入。</summary>
    public sealed class InMemorySbrLocationProvider : ISbrLocationProvider
    {
        private readonly IReadOnlyDictionary<string, float> _locations;

        public InMemorySbrLocationProvider(IReadOnlyDictionary<string, float> locations)
        {
            _locations = locations ?? new Dictionary<string, float>();
        }

        public bool TryGetLocation(string sbrLocId, out float location)
        {
            if (string.IsNullOrEmpty(sbrLocId))
            {
                location = 0;
                return false;
            }

            return _locations.TryGetValue(sbrLocId, out location);
        }
    }
}
