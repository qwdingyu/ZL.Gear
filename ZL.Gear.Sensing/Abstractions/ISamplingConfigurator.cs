using System.Collections.Generic;
using ZL.Gear.Core.Models;

namespace ZL.Gear.Sensing.Abstractions
{

    /// <summary>
    /// 一个通用的接口，用于为 SamplingConfigBuilder 应用特定的配置策略。
    /// </summary>
    /// <typeparam name="T">被采样的数据类型（例如 double, float, int 等）。</typeparam>
    public interface ISamplingConfigurator<T>
    {
        void Configure(SamplingConfigBuilder<T> builder, Dictionary<string, object> args, StepContext context);
    }
}
