using System;
using System.Collections.Generic;

namespace ZL.Gear.Core.Devices.Abstractions
{
    /// <summary>
    /// 设备隔离服务（T-P0-01b）：步骤超时后标记设备，下一 Run 租约前 fail-closed。
    /// </summary>
    /// <remarks>默认按 Runtime 独立 DI 实例隔离；仪器宿主可替换为跨进程持久化实现。</remarks>
    public interface IDeviceQuarantineService
    {
        void Quarantine(string deviceKey, string reason, Guid runId);

        bool IsQuarantined(string deviceKey, out string reason);

        void Release(string deviceKey);

        /// <summary>清空本 Runtime 全部隔离（运维复检 / 人工复位）。</summary>
        void ClearAll();

        /// <summary>当前隔离中的设备键（只读快照）。</summary>
        IReadOnlyCollection<string> GetQuarantinedKeys();
    }
}
