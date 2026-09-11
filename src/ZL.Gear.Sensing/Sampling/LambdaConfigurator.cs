using System;
using System.Collections.Generic;
using ZL.Gear.Core.Models;
using ZL.Gear.Sensing.Abstractions;

namespace ZL.Gear.Sensing.Sampling
{
    /// <summary>
    /// 一个辅助类，用于解决在注册时需要访问运行时参数 `args` 的作用域问题。
    /// </summary>
    public class LambdaConfigurator<T> : ISamplingConfigurator<T>
    {
        private readonly Action<SamplingConfigBuilder<T>, Dictionary<string, object>, StepContext> _configAction;
        public LambdaConfigurator(Action<SamplingConfigBuilder<T>, Dictionary<string, object>, StepContext> configAction)
        { _configAction = configAction; }
        public void Configure(SamplingConfigBuilder<T> builder, Dictionary<string, object> args, StepContext context)
        { _configAction(builder, args, context); }
    }
}
