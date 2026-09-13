using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using ZL.Gear.Core.Devices;
using ZL.Gear.Core.Devices.Abstractions;
using ZL.Gear.Core.Infrastructure;
using ZL.Gear.Core.Models;
using ZL.Gear.Core.StepHandler;
using ZL.Gear.Engine;
using ZL.Gear.Engine.Infrastructure;
using ZL.Gear.Engine.Runner;

namespace ZL.Gear.Engine.Tests
{
    [TestFixture]
    public class StepVerdictAndQuarantineTests
    {
        private sealed class IgnoringCancellationHandler : IStepHandler
        {
            public async Task<ExecutionResultBase> ExecuteAsync(StepConfig step, StepContext context)
            {
                await Task.Delay(TimeSpan.FromMinutes(10)).ConfigureAwait(false);
                return ExecutionResult<List<Measurement>>.Succeeded(new List<Measurement>(), 1, "never");
            }
        }

        [Test]
        public async Task DispatchSingleAsync_超时设置VerdictKind为TimedOut()
        {
            var lookup = new RegistryStepHandlerLookup(new DefaultStepHandlerFactory());
            var registry = new SimpleActionRegistry();
            var pipeline = new StepPipelineBuilder().Build();
            var dispatcher = new StepDispatcher(
                lookup,
                new DefaultStepHandlerFactory(),
                registry,
                pipeline,
                _ => { },
                enableUnknownCommandWarning: false,
                defaultTimeoutMs: 30_000,
                BuiltInModules.Core);

            dispatcher.RegisterHandler("HangForever", new IgnoringCancellationHandler());

            var step = new StepConfig
            {
                StepKey = "H1",
                Command = "HangForever",
                TimeoutMs = 200,
                Enable = true,
                Target = "dev_plc_1"
            };

            var services = new ServiceCollection();
            services.AddSingleton<Action<string>>(_ => { });
            var sp = services.BuildServiceProvider();

            var context = new StepContext(
                step.StepKey,
                step,
                new ReadOnlyDictionary<string, IDevice>(new Dictionary<string, IDevice>()),
                sp,
                CancellationToken.None,
                RunTestMode.Auto,
                new ContextVariableStore());

            var result = await dispatcher.DispatchSingleAsync(step, context).ConfigureAwait(false);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(ExecutionStatus.TimedOut, result.Status);
        }

        [Test]
        public async Task ExecuteAsync_步骤超时后隔离设备_第二次Run租约失败()
        {
            using var executor = SequenceExecutorBuilder.Create()
                .AsInstrumentedHost(new QuarantineTestFakeDeviceService())
                .WithStrictPlanCompile(false)
                .WithHandlers("HangForever", new QuarantineTestHangHandler())
                .Build();

            var steps = new List<StepConfig>
            {
                new StepConfig
                {
                    StepKey = "S1",
                    Command = "HangForever",
                    TimeoutMs = 150,
                    Enable = true,
                    Target = "plc_1",
                    EvaluateResult = false
                }
            };

            var first = await executor.ExecuteAsync(steps, "M", "B1", new Dictionary<string, object>(), CancellationToken.None);
            Assert.That(first.StepResults[0].VerdictKind, Is.EqualTo(StepVerdictKind.TimedOut));
            Assert.That(first.RunVerdictKind, Is.EqualTo(StepVerdictKind.TimedOut));
            Assert.That(first.QuarantinedDeviceKeys, Contains.Item("plc_1"));

            var second = await executor.ExecuteAsync(steps, "M", "B2", new Dictionary<string, object>(), CancellationToken.None);
            Assert.That(second.OverallSuccess, Is.False);
            Assert.That(second.RunVerdictKind, Is.EqualTo(StepVerdictKind.Error));
            Assert.That(second.Summary, Does.Contain("隔离"));
        }
    }
}
