using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using ZL.Gear.Core.Devices.Abstractions;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Workflow;

namespace ZL.Gear.Testing.Common
{
    /// <summary>
    /// 构造带 DI 的 <see cref="StepContext"/>（ActionResolver 等为只读属性，须注入 ServiceProvider）。
    /// </summary>
    public static class StepContextFactory
    {
        /// <summary>最小 LogicOnly 步骤上下文（无 ActionResolver）。</summary>
        public static StepContext CreateLogicOnly(
            StepConfig step,
            ContextVariableStore? variables = null,
            IReadOnlyDictionary<string, object>? globalContext = null)
        {
            variables ??= new ContextVariableStore();
            return new StepContext(
                step.StepKey,
                step,
                new Dictionary<string, IDevice>(),
                EmptyServiceProvider.Instance,
                CancellationToken.None,
                RunTestMode.Auto,
                variables,
                globalContext);
        }

        /// <summary>带 <see cref="IActionResolver"/> 的 DynamicFlow 测试上下文。</summary>
        public static StepContext CreateWithActionResolver(
            IActionResolver actionResolver,
            ContextVariableStore? variables = null,
            StepConfig? step = null)
        {
            variables ??= new ContextVariableStore();
            step ??= new StepConfig { StepKey = "TEST", Command = "DynamicFlow" };

            var services = new ServiceCollection();
            services.AddSingleton(actionResolver);
            var provider = services.BuildServiceProvider();

            return new StepContext(
                step.StepKey,
                step,
                new Dictionary<string, IDevice>(),
                provider,
                CancellationToken.None,
                RunTestMode.Auto,
                variables);
        }

        private sealed class EmptyServiceProvider : IServiceProvider
        {
            public static readonly EmptyServiceProvider Instance = new();

            public object? GetService(Type serviceType) => null;
        }
    }
}
