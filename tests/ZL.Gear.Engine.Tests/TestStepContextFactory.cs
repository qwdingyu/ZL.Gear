using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using ZL.Gear.Core.Devices.Abstractions;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.Workflow;

namespace ZL.Gear.Engine.Tests
{
    /// <summary>
    /// 构造带 DI 的 <see cref="StepContext"/>（ActionResolver 等为只读属性，须注入 ServiceProvider）。
    /// </summary>
    internal static class TestStepContextFactory
    {
        public static StepContext Create(
            IActionResolver actionResolver,
            ContextVariableStore? variables = null,
            Action<string>? log = null)
        {
            variables ??= new ContextVariableStore();

            var services = new ServiceCollection();
            services.AddSingleton(actionResolver);
            if (log != null)
            {
                services.AddSingleton(log);
            }

            var provider = services.BuildServiceProvider();
            var step = new StepConfig { StepKey = "TEST", Command = "DynamicFlow" };

            return new StepContext(
                step.StepKey,
                step,
                new Dictionary<string, IDevice>(),
                provider,
                CancellationToken.None,
                RunTestMode.Auto,
                variables);
        }
    }
}
