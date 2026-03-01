using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZL.Gear.Sensing.Dto;
using ZL.Gear.Sensing.Sampling;

namespace ZL.Gear.Sensing.Abstractions
{
    /// <summary>
    /// 测量判定引擎。
    /// 负责消费原始采样流，根据预设策略和触发逻辑，产出最终业务判定结果。
    /// </summary>
    public interface IMeasurementEngine<T>
    {
        /// <summary>
        /// 消费数据流并执行判定。
        /// </summary>
        /// <param name="dataStream">原始采样数据流</param>
        /// <param name="config">采样与判定配置</param>
        /// <param name="token">取消令牌</param>
        /// <returns>包含最终值、全样本及其规格检查结果的 ExeResult</returns>
        Task<ExeResult<T>> ExecuteAsync(IObservable<T> dataStream, SamplingConfig<T> config, CancellationToken token);
    }
}
