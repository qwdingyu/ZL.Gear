using System;
using System.Threading;
using System.Threading.Tasks;

namespace ZL.Gear.Samples.Industry.Client.Bootstrap
{
    /// <summary>
    /// 工位人机交互抽象（对应 legacy UiPlcEvents：座椅姿态 + 双手启动）。
    /// AutoSbrSensorCheckHandler 依赖此端口，非纯电检逻辑。
    /// </summary>
    public interface IStationInteractionPort
    {
        /// <summary>等待座椅/靠背/座垫姿态满足 SBR 测试要求。</summary>
        Task WaitForSbrPoseReadyAsync(CancellationToken cancellationToken = default);

        /// <summary>等待操作员双手启动信号。</summary>
        Task WaitForTwoHandStartAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>headless / CI：立即通过，不阻塞。</summary>
    public sealed class NullStationInteractionPort : IStationInteractionPort
    {
        public Task WaitForSbrPoseReadyAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task WaitForTwoHandStartAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
