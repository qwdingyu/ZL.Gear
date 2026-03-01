using System;
using System.Collections.Generic;
using ZL.Gear.Core.Models;
using ZL.Gear.Sensing.Abstractions;

namespace ZL.Gear.Sensing.Orchestration
{
    /// <summary>
    /// 一个静态工厂类，用于创建通用的、可复用的采样配置器实例。
    /// 这个类是框架的一部分，不应包含任何特定于“电流”或“电压”的业务逻辑。
    /// </summary>
    public static class ConfiguratorFactory
    {
        /// <summary>
        /// 场景一：创建一个独立的、仅靠持续时间控制的采样配置器。
        /// </summary>
        /// <typeparam name="T">测量类型。</typeparam>
        /// <param name="validator">（可选）一个用于验证每个样本的委托。</param>
        /// <returns>一个配置好的 ISamplingConfigurator 实例。</returns>
        public static ISamplingConfigurator<T> StandaloneDuration<T>(Func<T, bool> validator = null, IResultCalculator<T> resultCalculator = null)
        {
            return new StandaloneDurationConfigurator<T>(validator, resultCalculator);
        }

        /// <summary>
        /// 场景二：创建一个从属的、等待主步骤信号后开始测量的配置器。
        /// </summary>
        /// <typeparam name="T">测量类型。</typeparam>
        /// <param name="validator">用于验证每个样本的委托。</param>
        /// <param name="valueTypeName">用于日志记录的测量值的人类可读名称（例如 "电流", "电压"）。</param>
        /// <returns>一个配置好的 ISamplingConfigurator 实例。</returns>
        public static ISamplingConfigurator<T> EventDependent<T>(Func<T, bool> validator, string valueTypeName = "值", ISamplingStrategy<T> strategy = null)
        {
            return new EventDependentConfigurator<T>(validator, valueTypeName, strategy);
        }

        /// <summary>
        /// 场景三：创建一个主步骤的、会发出开始信号的配置器。
        /// 它将信令处理的公共逻辑封装起来，并将具体采样策略的定义权交还给调用者。
        /// </summary>
        /// <typeparam name="T">测量类型。</typeparam>
        /// <param name="strategyProvider">一个委托，用于应用核心的测量策略（例如，持续时间、触发器等）。</param>
        /// <param name="valueTypeName">用于日志记录的测量值的人类可读名称。</param>
        /// <returns>一个配置好的 ISamplingConfigurator 实例。</returns>
        public static ISamplingConfigurator<T> EventMaster<T>(
            Action<SamplingConfigBuilder<T>, Dictionary<string, object>, StepContext> strategyProvider,
            string valueTypeName = "值")
        {
            return new MasterEventConfigurator<T>(strategyProvider, valueTypeName);
        }

        // --- 工厂内部的私有实现类 ---
        // 这些类对外部调用者是隐藏的，它们只是工厂方法的实现细节。

        private class StandaloneDurationConfigurator<T> : ISamplingConfigurator<T>
        {
            private readonly Func<T, bool> _validator;
            private readonly IResultCalculator<T> _resultCalculator;
            public StandaloneDurationConfigurator(Func<T, bool> validator, IResultCalculator<T> resultCalculator = null)
            {
                _validator = validator;
                _resultCalculator = resultCalculator;
            }

            public void Configure(SamplingConfigBuilder<T> builder, Dictionary<string, object> args, StepContext context)
            {
                builder.WithStrategy(new DurationStrategy<T>(TimeSpan.FromMilliseconds(context.TimeoutMs), _resultCalculator));
                if (_validator != null)
                {
                    builder.WithPerSampleValidator(_validator);
                }
            }
        }

        private class EventDependentConfigurator<T> : ISamplingConfigurator<T>
        {
            private readonly Func<T, bool> _validator;
            private readonly string _valueTypeName;
            // 存储注入的策略
            private readonly ISamplingStrategy<T> _strategy;

            public EventDependentConfigurator(Func<T, bool> validator, string valueTypeName, ISamplingStrategy<T> strategy)
            {
                _validator = validator;
                _valueTypeName = valueTypeName;

                // 如果调用者未提供策略，则使用最常见的“捕获第一个有效值”策略。
                _strategy = strategy ?? new FixedCountStrategy<T>(1, new LastValueCalculator<T>());
            }

            public void Configure(SamplingConfigBuilder<T> builder, Dictionary<string, object> args, StepContext context)
            {
                // 直接使用确定好的策略，不再做任何假设
                builder.WithStrategy(_strategy);

                if (_validator != null)
                {
                    builder.WithPerSampleValidator(_validator);
                }

                // 通用的日志模板逻辑保持不变
                string stepName = context.StepConfig.StepName;
                builder.OnTestStart(val => context.Log($"[{stepName}] 收到开始信号，测试启动 ({_valueTypeName}: {val})"))
                       .OnTestFinish(res => context.Log(res.IsSuccess ? $"[{stepName}] 测试完成 ({_valueTypeName}: {res.Value})。" : $"[{stepName}] 测试失败或超时: {res.Message}"));
            }
        }

        private class MasterEventConfigurator<T> : ISamplingConfigurator<T>
        {
            // 核心：不再写死策略，而是接收一个“策略提供者”委托
            private readonly Action<SamplingConfigBuilder<T>, Dictionary<string, object>, StepContext> _strategyProvider;
            private readonly string _valueTypeName;

            public MasterEventConfigurator(Action<SamplingConfigBuilder<T>, Dictionary<string, object>, StepContext> strategyProvider, string valueTypeName)
            {
                _strategyProvider = strategyProvider;
                _valueTypeName = valueTypeName;
            }

            public void Configure(SamplingConfigBuilder<T> builder, Dictionary<string, object> args, StepContext context)
            {
                // 1. 封装公共逻辑：获取信令
                context.Variables.TryGetSignalPair(context.StepConfig.StepKey, out var signals);
                var startSignal = signals?.StartSignal;

                // 2. 委派特定逻辑：调用外部传入的委托来配置采样策略
                _strategyProvider(builder, args, context);

                // 3. 封装公共逻辑：配置事件回调
                string stepName = context.StepConfig.StepName;
                builder.OnTestStart(val =>
                {
                    context.Log($"[{stepName}] [主事件] 条件满足 ({_valueTypeName}: {val})，通知从属步骤...");
                    startSignal?.TrySetResult(true);
                })
                .OnTestFinish(res =>
                {
                    context.Log(res.IsSuccess ? $"[{stepName}] [主事件] 测试完成 ({_valueTypeName}: {res.Value})。" : $"[{stepName}] [主事件] 测试失败或超时: {res.Message}");
                });
            }
        }
    }
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
